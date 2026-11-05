import argparse
import copy
import hashlib
import importlib.metadata
import importlib.util
import inspect
import json
import math
from pathlib import Path, PurePosixPath
import shutil
import stat
import struct
import subprocess
import sys
import urllib.request
import zipfile

from prepare import MODEL_ID, MODEL_REVISION, TOKEN_COUNT, _sha256, validate_case

LLAMA_COMMIT = "872646b30c342c5fa98efec002f94fdb4f1f1ea5"
RELEASE = "b8209"
QUANTIZER_ARCHIVE = "llama-b8209-bin-win-cpu-x64.zip"
QUANTIZER_SHA256 = "774177077d4ac9ea88ece1365f18a9a3a7b1e842c002385c80975a1f6c4ec6df"
QUANTIZATIONS = ("F16", "Q8_0", "Q4_K_M")
PINNED_SOURCE_SHA256 = {
    "model.safetensors": "d5cdc518743ba1574455790e637a7917515cb7292b336dd75a36d254aec6b824",
    "head.safetensors": "1acf273a08d453351b6fd2d05827a6f90e8e2dd5e953d398454323fd729dacd7",
    "config.json": "bdfc3d2416429cf227e20c2acb3b6cd3e73944332186fe7f055c11da55470a84",
    "tinyjev.json": "7395d390ff5bea39e5958618ff0b32c930f28f4507704abc44bacb1756d04d56",
    "tokenizer.json": "be75606093db2094d7cd20f3c2f385c212750648bd6ea4fb2bf507a6a4c55506",
    "tokenizer_config.json": "3cca39ab54f024cdc0e40bf9791c7e71535e0fe0a21ac245d8c6f72c5e4f2904",
}


def validate_checkpoint(checkpoint):
    result = {}
    for name, expected in PINNED_SOURCE_SHA256.items():
        path = Path(checkpoint) / name
        if not path.is_file() or _sha256(path) != expected:
            raise ValueError(f"{name} does not match the fixed trained TinyJev revision")
        result[name] = expected
    return result


def export_head(checkpoint, output):
    import numpy as np
    from safetensors.numpy import load_file

    checkpoint, output = Path(checkpoint), Path(output)
    config = json.loads((checkpoint / "config.json").read_text(encoding="utf-8"))
    manifest = json.loads((checkpoint / "tinyjev.json").read_text(encoding="utf-8"))
    hidden, dimension = config["hidden_size"], manifest["head"]["head_dim"]
    temperature = manifest["head"]["temperature"]
    if type(hidden) is not int or type(dimension) is not int or not 0 < dimension <= hidden <= 65536:
        raise ValueError("The head dimensions must be positive supported integers")
    if type(temperature) not in (float, int) or not math.isfinite(temperature) or not 0 < temperature <= np.finfo(np.float32).max:
        raise ValueError("The head temperature must be finite and positive")
    weights = load_file(str(checkpoint / "head.safetensors"))
    shapes = {"q.weight": (dimension, hidden), "q.bias": (dimension,),
              "k.weight": (dimension, hidden), "k.bias": (dimension,)}
    if set(weights) != set(shapes):
        raise ValueError("The checkpoint must contain exactly the trained q/k head tensors")
    payload = []
    for name, shape in shapes.items():
        value = weights[name]
        if value.shape != shape or value.dtype != np.float32 or not np.isfinite(value).all():
            raise ValueError(f"{name} must be finite float32 with shape {shape}")
        payload.append(value.astype("<f4", copy=False).tobytes(order="C"))
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_bytes(struct.pack("<8siif", b"JEVHEAD1", hidden, dimension, temperature) + b"".join(payload))
    return {"hidden_size": hidden, "head_dim": dimension, "temperature": temperature,
            "head_sha256": _sha256(output), "bytes": output.stat().st_size,
            "source_sha256": {name: _sha256(checkpoint / name) for name in
                              ("config.json", "tinyjev.json", "head.safetensors")}}


def _validate_head(path):
    import numpy as np

    with path.open("rb") as stream:
        header = stream.read(20)
    if len(header) != 20:
        raise ValueError("The head header is truncated")
    magic, hidden, dimension, temperature = struct.unpack("<8siif", header)
    if magic != b"JEVHEAD1" or not 0 < dimension <= hidden <= 65536 or not math.isfinite(temperature) or temperature <= 0:
        raise ValueError("The head header is invalid")
    if path.stat().st_size != 20 + 4 * (2 * hidden * dimension + 2 * dimension):
        raise ValueError("The head payload length does not match its dimensions")
    if not np.isfinite(np.fromfile(path, dtype="<f4", offset=20)).all():
        raise ValueError("The head payload contains non-finite values")


def write_cases(source, model, head, quantization, output, trusted_model_sha256):
    source, model, head, output = map(Path, (source, model, head, output))
    if quantization not in QUANTIZATIONS:
        raise ValueError("Unsupported backbone quantization")
    with model.open("rb") as stream:
        if stream.read(4) != b"GGUF":
            raise ValueError("The model must be a GGUF file")
    _validate_head(head)
    document = json.loads(source.read_text(encoding="utf-8"))
    if not isinstance(document, dict) or type(document.get("schema_version")) is not int or document["schema_version"] != 1 or type(document.get("token_count")) is not int or document["token_count"] != TOKEN_COUNT or document.get("model_id") != MODEL_ID or document.get("model_revision") != MODEL_REVISION:
        raise ValueError("The prepared references must come from the fixed TinyJev checkpoint")
    source_hash = document.get("model_sha256")
    if not isinstance(source_hash, str) or len(source_hash) != 64 or any(value not in "0123456789abcdef" for value in source_hash):
        raise ValueError("The reference model SHA-256 is required")
    if not isinstance(document.get("cases"), list) or len(document["cases"]) < 2:
        raise ValueError("At least two prepared reference cases are required")
    for case in document["cases"]:
        validate_case(case, 151936)
    if len({case["case_id"] for case in document["cases"]}) != len(document["cases"]):
        raise ValueError("Prepared case IDs must be unique")
    result = copy.deepcopy(document)
    if _sha256(model) != trusted_model_sha256:
        raise ValueError("The GGUF changed after its generation provenance was verified")
    result.update(model_format="gguf", model_sha256=trusted_model_sha256, head_file=head.name,
                  head_sha256=_sha256(head), backbone_quantization=quantization,
                  reference_source={"model_format": document.get("model_format", "onnx"),
                                    "model_sha256": source_hash, "cases_sha256": _sha256(source)})
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, indent=2, allow_nan=False) + "\n", encoding="utf-8")
    return result


def _download(url, path, expected_hash=None):
    if path.exists() and (expected_hash is None or _sha256(path) == expected_hash):
        return
    path.parent.mkdir(parents=True, exist_ok=True)
    partial = path.with_suffix(path.suffix + ".partial")
    request = urllib.request.Request(url, headers={"User-Agent": "TinyJev-GGUF-Preparation"})
    with urllib.request.urlopen(request, timeout=120) as response, partial.open("wb") as stream:
        shutil.copyfileobj(response, stream, length=1024 * 1024)
    if expected_hash is not None and _sha256(partial) != expected_hash:
        raise ValueError("The downloaded archive SHA-256 does not match the pinned release")
    partial.replace(path)


def _extract(archive, destination, source_prefix=None):
    destination = destination.resolve()
    with zipfile.ZipFile(archive) as package:
        entries = []
        for entry in package.infolist():
            name = PurePosixPath(entry.filename)
            if name.is_absolute() or ".." in name.parts or "\\" in entry.filename or any(":" in part for part in name.parts) or stat.S_ISLNK(entry.external_attr >> 16):
                raise ValueError("The archive contains an unsafe path or symbolic link")
            relative = name
            if source_prefix:
                if not name.parts or name.parts[0] != source_prefix:
                    raise ValueError("The source archive does not match the pinned commit")
                relative = PurePosixPath(*name.parts[1:])
                if str(relative) != "convert_hf_to_gguf.py" and not relative.parts[:1] == ("gguf-py",):
                    continue
            target = (destination / relative).resolve()
            if not target.is_relative_to(destination):
                raise ValueError("Archive extraction would escape the tool directory")
            entries.append((entry, target))
        for entry, target in entries:
            if entry.is_dir():
                target.mkdir(parents=True, exist_ok=True)
            else:
                target.parent.mkdir(parents=True, exist_ok=True)
                with package.open(entry) as source, target.open("wb") as output:
                    shutil.copyfileobj(source, output)


def ensure_source(tools):
    tools = Path(tools)
    source = tools / "llama.cpp"
    marker = source / "source-pin.json"
    if not marker.exists():
        archive = tools / ("llama.cpp-" + LLAMA_COMMIT + ".zip")
        _download("https://github.com/ggml-org/llama.cpp/archive/" + LLAMA_COMMIT + ".zip", archive)
        _extract(archive, source, "llama.cpp-" + LLAMA_COMMIT)
        marker.write_text(json.dumps({"commit": LLAMA_COMMIT, "archive_sha256": _sha256(archive),
                                      "converter_sha256": _sha256(source / "convert_hf_to_gguf.py")}, indent=2), encoding="utf-8")
    pin = json.loads(marker.read_text(encoding="utf-8"))
    if pin["commit"] != LLAMA_COMMIT or pin["converter_sha256"] != _sha256(source / "convert_hf_to_gguf.py"):
        raise ValueError("The cached converter does not match its pinned source")
    return source


def ensure_quantizer(tools):
    if sys.platform != "win32":
        raise ValueError("The pinned automatic quantizer installation currently targets Windows x64")
    tools = Path(tools)
    archive = tools / QUANTIZER_ARCHIVE
    _download(f"https://github.com/ggml-org/llama.cpp/releases/download/{RELEASE}/{QUANTIZER_ARCHIVE}", archive, QUANTIZER_SHA256)
    directory = tools / RELEASE
    _extract(archive, directory)
    matches = list(directory.rglob("llama-quantize.exe"))
    if len(matches) != 1:
        raise ValueError("The pinned release must contain exactly one llama-quantize executable")
    return matches[0]


def convert_f16(checkpoint, output, source):
    converter_path = source / "convert_hf_to_gguf.py"
    spec = importlib.util.spec_from_file_location("tinyjev_pinned_converter", converter_path)
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    module.ModelBase.register("Qwen3Model")(module.Qwen3Model)
    previous = sys.argv
    try:
        sys.argv = [str(converter_path), str(checkpoint), "--outfile", str(output), "--outtype", "f16", "--use-temp-file"]
        module.main()
    finally:
        sys.argv = previous


def inspect_gguf(model, checkpoint, source, quantization):
    sys.path.insert(0, str(source / "gguf-py"))
    import gguf
    from safetensors import safe_open

    config = json.loads((checkpoint / "config.json").read_text(encoding="utf-8"))
    reader = gguf.GGUFReader(model)
    expected = {"general.architecture": "qwen3", "qwen3.embedding_length": config["hidden_size"],
                "qwen3.context_length": config["max_position_embeddings"],
                "qwen3.block_count": config["num_hidden_layers"], "qwen3.feed_forward_length": config["intermediate_size"],
                "qwen3.attention.head_count": config["num_attention_heads"],
                "qwen3.attention.head_count_kv": config["num_key_value_heads"],
                "qwen3.attention.key_length": config["head_dim"], "qwen3.attention.value_length": config["head_dim"],
                "qwen3.rope.freq_base": config["rope_theta"], "tokenizer.ggml.model": "gpt2",
                "tokenizer.ggml.bos_token_id": 151643, "tokenizer.ggml.eos_token_id": 151643,
                "tokenizer.ggml.padding_token_id": 151643}
    for key, value in expected.items():
        if key not in reader.fields or reader.fields[key].contents() != value:
            raise ValueError(f"GGUF metadata mismatch: {key}")
    epsilon = reader.fields["qwen3.attention.layer_norm_rms_epsilon"].contents()
    if not math.isclose(epsilon, config["rms_norm_eps"], rel_tol=1e-6):
        raise ValueError("The GGUF final RMSNorm epsilon changed")
    tokens = reader.fields["tokenizer.ggml.tokens"].contents()
    if len(tokens) != config["vocab_size"]:
        raise ValueError("The GGUF vocabulary size changed")
    delimiters = {"<|fim_prefix|>": 151659, "<|fim_middle|>": 151660, "<|box_start|>": 151648,
                  "<|box_end|>": 151649, "<|fim_suffix|>": 151661}
    if any(tokens[index] != token for token, index in delimiters.items()):
        raise ValueError("The prepared-case delimiter token IDs changed")
    names = {tensor.name for tensor in reader.tensors}
    with safe_open(str(checkpoint / "model.safetensors"), framework="numpy") as weights:
        source_count = len(weights.keys())
    if source_count != 310 or len(reader.tensors) != source_count or "output.weight" in names or "output_norm.weight" not in names:
        raise ValueError("The GGUF must preserve the 310 bare trained backbone tensors without an added LM head")
    expected_type = {"F16": gguf.LlamaFileType.MOSTLY_F16, "Q8_0": gguf.LlamaFileType.MOSTLY_Q8_0,
                     "Q4_K_M": gguf.LlamaFileType.MOSTLY_Q4_K_M}[quantization]
    if reader.fields["general.file_type"].contents() != int(expected_type):
        raise ValueError("The GGUF quantization does not match its prepared-case metadata")
    return {"metadata": {key: reader.fields[key].contents() for key in expected}, "vocab_size": len(tokens),
            "source_tensor_count": source_count, "gguf_tensor_count": len(reader.tensors),
            "tensor_types": {kind: sum(tensor.tensor_type.name == kind for tensor in reader.tensors)
                             for kind in sorted({tensor.tensor_type.name for tensor in reader.tensors})},
            "delimiter_ids": delimiters, "apply_chat_template": False, "append_special_tokens": False}


def _provenance_path(model):
    return model.with_suffix(model.suffix + ".provenance.json")


def _record_artifact(model, expected):
    with model.open("rb") as stream:
        if stream.read(4) != b"GGUF":
            raise ValueError("The generated artifact is not GGUF")
    record = {"schema_version": 1, "provenance": expected, "artifact_sha256": _sha256(model),
              "artifact_bytes": model.stat().st_size}
    path = _provenance_path(model)
    temporary = path.with_suffix(path.suffix + ".partial")
    temporary.write_text(json.dumps(record, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    temporary.replace(path)
    return record


def _validate_artifact(model, expected):
    path = _provenance_path(model)
    if not path.is_file():
        raise ValueError(f"{model.name}: no trusted generation provenance; run verify-existing explicitly")
    record = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(record, dict) or record.get("schema_version") != 1 or record.get("provenance") != expected:
        raise ValueError(f"{model.name}: source, tools, settings, or parent F16 provenance changed")
    if record.get("artifact_bytes") != model.stat().st_size or record.get("artifact_sha256") != _sha256(model):
        raise ValueError(f"{model.name}: GGUF bytes do not match the trusted generation provenance")
    return record


def ensure_artifact(model, expected, generate):
    model = Path(model)
    if model.exists():
        return _validate_artifact(model, expected)
    temporary = model.with_suffix(model.suffix + ".partial")
    if temporary.exists() or temporary.is_symlink():
        raise ValueError(f"Unverified partial artifact already exists: {temporary}")
    generate(temporary)
    temporary.replace(model)
    return _record_artifact(model, expected)


def verify_existing_artifact(model, expected, generate):
    model = Path(model)
    if not model.is_file():
        raise ValueError("Explicit provenance verification requires an existing model")
    original_hash = _sha256(model)
    temporary = model.with_suffix(model.suffix + ".verify.partial")
    if temporary.exists() or temporary.is_symlink():
        raise ValueError(f"A previous verification artifact needs inspection: {temporary}")
    generate(temporary)
    if _sha256(temporary) != original_hash or _sha256(model) != original_hash:
        raise ValueError("Explicit regeneration differs from the existing model; neither its bytes nor provenance were replaced")
    temporary.unlink()
    return _record_artifact(model, expected)


def _tool_identity(source, quantizer=None):
    python_files = {str(path.relative_to(source / "gguf-py")).replace("\\", "/"): _sha256(path)
                    for path in sorted((source / "gguf-py/gguf").rglob("*.py"))}
    result = {"llama_cpp_commit": LLAMA_COMMIT,
              "converter_sha256": _sha256(source / "convert_hf_to_gguf.py"),
              "gguf_python_sha256": hashlib.sha256(json.dumps(python_files, sort_keys=True).encode()).hexdigest(),
              "adapter_sha256": hashlib.sha256(inspect.getsource(convert_f16).encode()).hexdigest(),
              "python_dependencies": {name: importlib.metadata.version(name) for name in
                                      ("torch", "transformers", "numpy", "safetensors")}}
    if quantizer is not None:
        result.update(quantizer_sha256=_sha256(quantizer), quantizer_release=RELEASE,
                      quantizer_archive_sha256=QUANTIZER_SHA256)
    return result


def prepare(checkpoint, references, output, quantizations, threads, verify_existing=False):
    checkpoint, references, output = map(lambda value: Path(value).resolve(), (checkpoint, references, output))
    if not quantizations or any(value not in QUANTIZATIONS for value in quantizations) or len(set(quantizations)) != len(quantizations):
        raise ValueError("Choose unique supported backbone quantizations")
    if not 1 <= threads <= 64:
        raise ValueError("threads must be in [1, 64]")
    output.mkdir(parents=True, exist_ok=True)
    source = ensure_source(output / "tools")
    source_hashes = validate_checkpoint(checkpoint)
    f16_tools = _tool_identity(source)
    f16_expected = {"model_id": MODEL_ID, "model_revision": MODEL_REVISION, "quantization": "F16",
                    "source_sha256": source_hashes, "tools": f16_tools, "parent_f16_sha256": None,
                    "settings": {"source_directory_name": checkpoint.name, "use_temp_file": True}}
    f16 = output / "F16" / "tinyjev-F16.gguf"
    f16.parent.mkdir(exist_ok=True)

    def generate_f16(temporary):
        with (output / ("convert-f16.verify.log" if verify_existing else "convert-f16.log")).open("w", encoding="utf-8") as log:
            subprocess.run([sys.executable, str(Path(__file__).resolve()), "convert", "--checkpoint", str(checkpoint),
                            "--output", str(temporary), "--tools", str(source)], stdout=log, stderr=subprocess.STDOUT, check=True)
        if validate_checkpoint(checkpoint) != source_hashes or _tool_identity(source) != f16_tools:
            raise ValueError("Checkpoint or converter changed during F16 generation")

    f16_record = (verify_existing_artifact(f16, f16_expected, generate_f16) if verify_existing else
                  ensure_artifact(f16, f16_expected, generate_f16))
    variants = {}
    for quantization in quantizations:
        directory = output / quantization
        directory.mkdir(exist_ok=True)
        model = directory / f"tinyjev-{quantization}.gguf"
        record = f16_record
        if quantization != "F16":
            quantizer = ensure_quantizer(output / "tools")
            tools = _tool_identity(source, quantizer)
            expected = {"model_id": MODEL_ID, "model_revision": MODEL_REVISION, "quantization": quantization,
                        "source_sha256": source_hashes, "tools": tools,
                        "parent_f16_sha256": f16_record["artifact_sha256"], "settings": {"threads": threads}}

            def generate_quantized(temporary):
                with (directory / ("quantize.verify.log" if verify_existing else "quantize.log")).open("w", encoding="utf-8") as log:
                    subprocess.run([str(quantizer), str(f16), str(temporary), quantization, str(threads)],
                                   stdout=log, stderr=subprocess.STDOUT, check=True)
                if validate_checkpoint(checkpoint) != source_hashes or _tool_identity(source, quantizer) != tools:
                    raise ValueError("Checkpoint or quantizer changed during generation")
                _validate_artifact(f16, f16_expected)

            record = (verify_existing_artifact(model, expected, generate_quantized) if verify_existing else
                      ensure_artifact(model, expected, generate_quantized))
        print(f"Validating {quantization}", flush=True)
        inspection = inspect_gguf(model, checkpoint, source, quantization)
        head = export_head(checkpoint, directory / "tinyjev.head")
        cases = write_cases(references, model, directory / "tinyjev.head", quantization, directory / "cases.json",
                            record["artifact_sha256"])
        variants[quantization] = {"model_file": str(model), "bytes": model.stat().st_size,
                                  "model_sha256": cases["model_sha256"], "head": head, "inspection": inspection,
                                  "artifact_provenance_file": _provenance_path(model).name}
        (directory / "preparation.json").write_text(json.dumps(variants[quantization], indent=2) + "\n", encoding="utf-8")
    if any(_sha256(checkpoint / name) != value for name, value in source_hashes.items()):
        raise ValueError("The source checkpoint changed during conversion")
    report = {"model_id": MODEL_ID, "model_revision": MODEL_REVISION, "llama_cpp_commit": LLAMA_COMMIT,
              "quantizer_release": RELEASE, "quantizer_archive_sha256": QUANTIZER_SHA256,
              "source_sha256": source_hashes, "variants": variants,
              "status": "Prepared; native hidden-state and decision parity must be measured separately"}
    (output / "preparation-summary.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({key: {"bytes": value["bytes"], "model_sha256": value["model_sha256"]} for key, value in variants.items()}, indent=2), flush=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("mode", nargs="?", choices=("prepare", "convert", "verify-existing"), default="prepare")
    parser.add_argument("--checkpoint", type=Path, default=Path(__file__).parent / "Artifacts/tinyjev/checkpoint")
    parser.add_argument("--references", type=Path, default=Path(__file__).parent / "Artifacts/tinyjev/cases.json")
    parser.add_argument("--output", type=Path, default=Path(__file__).parent / "Artifacts/gguf")
    parser.add_argument("--tools", type=Path)
    parser.add_argument("--quantizations", nargs="+", default=list(QUANTIZATIONS))
    parser.add_argument("--threads", type=int, default=4)
    args = parser.parse_args()
    if args.mode == "convert":
        convert_f16(args.checkpoint, args.output, args.tools)
    else:
        prepare(args.checkpoint, args.references, args.output, args.quantizations, args.threads, args.mode == "verify-existing")


if __name__ == "__main__":
    main()
