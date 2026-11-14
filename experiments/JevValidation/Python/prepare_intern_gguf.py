"""Prepare pinned Intern-Decision text GGUF and exact native-template Unity inputs.

This retains the trained decision marker, the entire assistant skeleton, and the
original A-H output embedding rows. It does not load a Transformers model or
convert the unused vision tower. Run after the native HF benchmark, with its
completed result.json as --reference, to compare the same 18 requests in Unity.
"""

import argparse
import ast
import copy
import hashlib
import importlib.metadata
import importlib.util
import json
import os
from pathlib import Path
import struct
import subprocess
import sys

from compare_intern_decision import (atomic_json, build_suite, checkpoint_identity,
                                    file_sha256, fingerprint, source_identity,
                                    validate_probabilities)


INTERN_COMMIT = "565350244af860e1c276768cd80bdd9b8be93913"
MODEL_PINS = {
    "internlm/Intern-Decision-0.8B": "42457fafd13d10d0b0d9be46cb30a23caa409994",
    "internlm/Intern-Decision-2B": "85c5657708a09884ff9fd8d7f57789b410a97b33",
}
SYMBOLS = tuple("ABCDEFGH")
QWEN35_PRETOKENIZER_HASH = "1444df51289cfa8063b96f0e62b1125440111bc79a52003ea14b6eac7016fd5f"
QWEN35_REGEX = r"(?i:'s|'t|'re|'ve|'m|'ll|'d)|[^\r\n\p{L}\p{N}]?[\p{L}\p{M}]+|\p{N}| ?[^\s\p{L}\p{M}\p{N}]+[\r\n]*|\s*[\r\n]+|\s+(?!\S)|\s+"
QWEN35_PRETOKENIZER = {"type": "Sequence", "pretokenizers": [
    {"type": "Split", "pattern": {"Regex": QWEN35_REGEX}, "behavior": "Isolated", "invert": False},
    {"type": "ByteLevel", "add_prefix_space": False, "trim_offsets": False, "use_regex": False}]}
QWEN35_MAPPING_SOURCE = {
    "repository": "https://github.com/ggml-org/llama.cpp",
    "git_commit": "e14a779719652200cec7ad58dfa4820edc347182",
    "file": "conversion/base.py", "blob": "841efe7299cfd5685f1e65286bb9088bd6b6dee3",
    "url": "https://github.com/ggml-org/llama.cpp/blob/e14a779719652200cec7ad58dfa4820edc347182/conversion/base.py#L2116",
    "registry_url": "https://github.com/ggml-org/llama.cpp/blob/e14a779719652200cec7ad58dfa4820edc347182/convert_hf_to_gguf_update.py#L192",
}


def tokenizer_check_text(source):
    tree = ast.parse((Path(source) / "convert_hf_to_gguf.py").read_text(encoding="utf-8"))
    matches = [node.value.value for node in ast.walk(tree) if isinstance(node, ast.Assign) and
               isinstance(node.value, ast.Constant) and isinstance(node.value.value, str) and
               any(isinstance(target, ast.Name) and target.id == "chktxt" for target in node.targets)]
    if len(matches) != 1:
        raise ValueError("Pinned converter must contain exactly one tokenizer fingerprint string")
    return matches[0]


def qwen35_recognition(tokenizer_json, check_hash):
    """Backport one upstream recognition entry without changing pinned source."""
    if check_hash != QWEN35_PRETOKENIZER_HASH:
        return None
    if (tokenizer_json.get("pre_tokenizer") != QWEN35_PRETOKENIZER or
            tokenizer_json.get("normalizer") != {"type": "NFC"}):
        raise ValueError("Known fingerprint requires the exact Qwen3.5 regex, ByteLevel and NFC configuration")
    return {"adapter": "Exact upstream Qwen3.5 pre-tokenizer recognition backport",
            "check_hash": check_hash, "gguf_pre": "qwen35",
            "pre_tokenizer_sha256": fingerprint(tokenizer_json["pre_tokenizer"]),
            "normalizer": tokenizer_json["normalizer"], "mapping_source": QWEN35_MAPPING_SOURCE,
            "pinned_converter_modified": False,
            "scope": "Recognition only; original tokenizer vocabulary, merges, IDs and trained weights are unchanged."}


def tokenizer_compatibility(checkpoint, tokenizer, source):
    config = json.loads((Path(checkpoint) / "tokenizer.json").read_text(encoding="utf-8"))
    encoded = tokenizer.encode(tokenizer_check_text(source))
    check_hash = hashlib.sha256(str(encoded).encode()).hexdigest()
    record = qwen35_recognition(config, check_hash)
    if record:
        effective = json.loads(tokenizer.backend_tokenizer.to_str())
        record.update(runtime_tokenizer_class=type(tokenizer).__module__ + "." + type(tokenizer).__name__,
                      runtime_pre_tokenizer=effective.get("pre_tokenizer"),
                      runtime_normalizer=effective.get("normalizer"),
                      runtime_pre_tokenizer_matches_checkpoint=effective.get("pre_tokenizer") == config["pre_tokenizer"],
                      unity_input_scope="Only the exported native HF raw token IDs are compared. Text tokenization parity is not asserted.")
        if not record["runtime_pre_tokenizer_matches_checkpoint"]:
            record["loader_difference"] = (
                "Transformers reconstructed a different effective pre-tokenizer from the untouched checkpoint JSON. "
                "The exported input IDs still use this exact native HF loader; GGUF qwen35 metadata describes the checkpoint regex.")
    return record


def _rows(shard, name, token_ids, hidden_size, vocab_size):
    """Read only eight rows, including BF16, without importing/loading Torch."""
    import numpy as np

    with Path(shard).open("rb") as stream:
        prefix = stream.read(8)
        if len(prefix) != 8:
            raise ValueError("Truncated safetensors header")
        header_size = struct.unpack("<Q", prefix)[0]
        if header_size > 64 * 1024 * 1024:
            raise ValueError("Unexpected safetensors header size")
        header = json.loads(stream.read(header_size))
        info = header[name]
        dtypes = {"BF16": ("<u2", 2), "F16": ("<f2", 2), "F32": ("<f4", 4)}
        if info["dtype"] not in dtypes or info["shape"] != [vocab_size, hidden_size]:
            raise ValueError("Output embedding dtype or dimensions disagree with config")
        dtype, item_size = dtypes[info["dtype"]]
        start, end = info["data_offsets"]
        if start < 0 or end - start != vocab_size * hidden_size * item_size or 8 + header_size + end > Path(shard).stat().st_size:
            raise ValueError("Invalid output embedding data offsets")
        rows = []
        for token_id in token_ids:
            if isinstance(token_id, bool) or not isinstance(token_id, int) or not 0 <= token_id < vocab_size:
                raise ValueError("Symbol token ID exceeds embedding vocabulary")
            stream.seek(8 + header_size + start + token_id * hidden_size * item_size)
            data = stream.read(hidden_size * item_size)
            if len(data) != hidden_size * item_size:
                raise ValueError("Truncated output embedding row")
            values = np.frombuffer(data, dtype=dtype)
            if info["dtype"] == "BF16":
                values = (values.astype(np.uint32) << 16).view(np.float32)
            else:
                values = values.astype(np.float32)
            if not np.isfinite(values).all():
                raise ValueError("Output embedding rows must be finite")
            rows.append(values.tolist())
    return rows


def export_head(checkpoint, config, symbol_token_ids, output):
    checkpoint = Path(checkpoint)
    text = config.get("text_config", config)
    mapping = json.loads((checkpoint / "model.safetensors.index.json").read_text(encoding="utf-8"))["weight_map"]
    heads = [name for name in mapping if name.endswith("lm_head.weight")]
    if any(name.endswith("lm_head.bias") for name in mapping) or len(heads) > 1:
        raise ValueError("Unexpected output head or bias")
    if heads:
        name = heads[0]
    elif config.get("tie_word_embeddings", text.get("tie_word_embeddings", False)):
        name = "model.language_model.embed_tokens.weight"
    else:
        raise ValueError("No output head exists and embeddings are not tied")
    if name not in mapping or len(symbol_token_ids) != 8 or len(set(symbol_token_ids)) != 8:
        raise ValueError("Expected eight distinct trained output symbol rows")
    # Checkpoint provenance validation below restricts shard names to downloaded files.
    shard = (checkpoint / mapping[name]).resolve()
    if shard.parent != checkpoint.resolve():
        raise ValueError("Output embedding shard must be inside checkpoint")
    rows = _rows(shard, name, symbol_token_ids, text["hidden_size"], text["vocab_size"])
    head = {"schema_version": 1, "hidden_size": text["hidden_size"], "rows": rows,
            "symbol_token_ids": symbol_token_ids, "symbols": list(SYMBOLS),
            "source_tensor": name, "source_file": shard.name, "bias": False,
            "row_format": "Original checkpoint values converted exactly to float32; A through H."}
    atomic_json(output, head)
    return head


def compile_cases(suite, tokenizer, compile_row, options, marker_id, context_size):
    cases = []
    for case in suite["cases"]:
        request = case["request"]
        if request.get("images"):
            raise ValueError("This Unity experiment covers text-only requests")
        compiled = compile_row(request, include_targets=False)
        field = case["question_id"]
        if compiled.fields != (field,) or compiled.symbols[field] != SYMBOLS:
            raise ValueError("Expected one original eight-choice question")
        text = tokenizer.apply_chat_template(copy.deepcopy(compiled.messages), tokenize=False,
                                            add_generation_prompt=False, enable_thinking=False,
                                            add_vision_id=True)
        input_ids = tokenizer(text, add_special_tokens=False)["input_ids"]
        if not input_ids or len(input_ids) > context_size:
            raise ValueError("Input exceeds context size; truncation is forbidden")
        positions = [index - 1 for index, value in enumerate(input_ids) if value == marker_id]
        if len(positions) != 1 or positions[0] < 0:
            raise ValueError("Decision marker count or position mismatch")
        encoded = [tokenizer.encode(symbol, add_special_tokens=False) for symbol in SYMBOLS]
        if any(len(ids) != 1 for ids in encoded):
            raise ValueError("Answer symbols must each encode as one token")
        symbol_ids = [ids[0] for ids in encoded]
        choice_ids = [value for value, _ in options(request["questions"][field])]
        if len(choice_ids) != 8 or len(set(choice_ids)) != 8:
            raise ValueError("Expected eight unique candidate IDs")
        cases.append({"case_id": case["case_id"], "input_ids": input_ids,
                      "decision_position": positions[0], "choice_ids": choice_ids,
                      "reference_probabilities": [], "symbol_token_ids": symbol_ids,
                      "request_sha256": case["request_sha256"],
                      "templated_text_sha256": fingerprint(text)})
    if not cases or any(case["symbol_token_ids"] != cases[0]["symbol_token_ids"] for case in cases):
        raise ValueError("All cases must use the same fixed A-H output rows")
    return cases


def validate_reference(reference, suite, cases, model_id, revision):
    expected = {"status": "complete", "backend": "intern", "model_id": model_id,
                "revision": revision, "inputs_sha256": suite["inputs_sha256"]}
    if any(reference.get(key) != value for key, value in expected.items()):
        raise ValueError("Reference must be a completed native HF run of the same pinned model and inputs")
    results = reference.get("results", [])
    if len(results) != len(cases) or [row.get("case_id") for row in results] != [row["case_id"] for row in cases]:
        raise ValueError("Reference must contain every prepared case exactly once in order")
    output = copy.deepcopy(cases)
    for case, result in zip(output, results):
        if (result.get("request_sha256") != case["request_sha256"] or
                result.get("candidate_ids") != case["choice_ids"] or
                result.get("input_tokens") != len(case["input_ids"])):
            raise ValueError("Reference request, candidate order or actual token count changed")
        probabilities = validate_probabilities(result.get("probabilities"), case["choice_ids"], result.get("selected_id"))
        case["reference_probabilities"] = [probabilities[choice] for choice in case["choice_ids"]]
    return output


def convert_f16(checkpoint, output, source):
    # A fresh process avoids converter imports altering the case tokenizer. Only
    # the pinned converter decides architecture-specific tensor transformations.
    environment = os.environ.copy()
    environment["PYTHONPATH"] = str(source / "gguf-py") + os.pathsep + environment.get("PYTHONPATH", "")
    subprocess.run([sys.executable, str(Path(__file__).resolve()), "--convert-worker", str(source),
                    str(checkpoint), str(output)],
                   env=environment, check=True)


def convert_worker(source, checkpoint, output):
    """Run pinned converter with one proven recognition entry added in memory."""
    from prepare_gguf import LLAMA_COMMIT

    source, checkpoint, output = Path(source), Path(checkpoint), Path(output)
    converter = source / "convert_hf_to_gguf.py"
    pin = json.loads((source / "source-pin.json").read_text(encoding="utf-8"))
    if pin["commit"] != LLAMA_COMMIT or pin["converter_sha256"] != file_sha256(converter):
        raise ValueError("Converter worker requires the unchanged pinned source")
    sys.path.insert(0, str(source / "gguf-py"))
    spec = importlib.util.spec_from_file_location("intern_pinned_converter", converter)
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    original = module.TextModel.get_vocab_base_pre

    def recognized(self, tokenizer):
        recognition = tokenizer_compatibility(checkpoint, tokenizer, source)
        if recognition:
            module.logger.warning("Using exact upstream pre-tokenizer recognition backport: %s -> qwen35; %s",
                                  recognition["check_hash"], recognition["mapping_source"]["url"])
            return "qwen35"
        return original(self, tokenizer)

    module.TextModel.get_vocab_base_pre = recognized
    sys.argv = [str(converter), str(checkpoint), "--outfile", str(output), "--outtype", "f16", "--use-temp-file"]
    module.main()


def inspect_gguf(model, config, source, marker_id):
    sys.path.insert(0, str(source / "gguf-py"))
    import gguf

    text = config["text_config"]
    reader = gguf.GGUFReader(model)
    expected = {"general.architecture": "qwen35", "qwen35.embedding_length": text["hidden_size"],
                "qwen35.context_length": text["max_position_embeddings"],
                "qwen35.block_count": text["num_hidden_layers"],
                "qwen35.feed_forward_length": text["intermediate_size"],
                "general.file_type": int(gguf.LlamaFileType.MOSTLY_F16)}
    for key, value in expected.items():
        if key not in reader.fields or reader.fields[key].contents() != value:
            raise ValueError("Converted GGUF metadata mismatch: " + key)
    tokens = reader.fields["tokenizer.ggml.tokens"].contents()
    if len(tokens) != text["vocab_size"] or tokens[marker_id] != "<decision>":
        raise ValueError("The converted vocabulary or trained decision token changed")
    names = {tensor.name for tensor in reader.tensors}
    if not {"token_embd.weight", "output_norm.weight"}.issubset(names) or any(
            name.startswith(("v.", "vision.", "mm.")) for name in names):
        raise ValueError("Expected text backbone tensors without a vision tower")
    return {"metadata": expected, "vocab_size": len(tokens), "gguf_tensor_count": len(reader.tensors),
            "tensor_types": {kind: sum(tensor.tensor_type.name == kind for tensor in reader.tensors)
                             for kind in sorted({tensor.tensor_type.name for tensor in reader.tensors})},
            "decision_token_id": marker_id, "vision_included": False}


def prepare(args):
    from prepare_gguf import LLAMA_COMMIT, ensure_artifact, ensure_source
    from transformers import AutoTokenizer

    checkpoint = args.checkpoint.resolve()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    provenance_path = checkpoint / "download-provenance.json"
    download = json.loads(provenance_path.read_text(encoding="utf-8"))
    model_id, revision = download["model_id"], download["revision"]
    if MODEL_PINS.get(model_id) != revision:
        raise ValueError("Checkpoint is not a supported pinned official Intern-Decision release")
    checkpoint_record = checkpoint_identity(checkpoint, provenance_path)
    upstream = source_identity(args.source_repo)
    if upstream["git_commit"] != INTERN_COMMIT:
        raise ValueError("Intern compiler source differs from its pinned commit")
    subprocess.run(["git", "-C", str(args.source_repo), "diff", "--exit-code", "HEAD", "--",
                    *upstream["inference_source_sha256"]], check=True, stdout=subprocess.DEVNULL)
    sys.path.insert(0, str(args.source_repo.resolve()))
    from src.inputs.schema import DECISION_TOKEN, _options, compile_row

    config = json.loads((checkpoint / "config.json").read_text(encoding="utf-8"))
    if config.get("architectures") != ["Qwen3_5ForConditionalGeneration"]:
        raise ValueError("Expected the official Qwen3.5 conditional-generation architecture")
    text_config = config["text_config"]
    if not 0 < args.context_size <= text_config["max_position_embeddings"]:
        raise ValueError("Context size must fit the original model configuration")
    tokenizer = AutoTokenizer.from_pretrained(checkpoint, local_files_only=True)
    if DECISION_TOKEN not in tokenizer.get_added_vocab():
        raise ValueError("Checkpoint lacks its trained decision token")
    marker_id = tokenizer.convert_tokens_to_ids(DECISION_TOKEN)
    suite = build_suite()
    cases = compile_cases(suite, tokenizer, compile_row, _options, marker_id, args.context_size)
    reference = None
    if args.reference:
        reference = json.loads(args.reference.read_text(encoding="utf-8"))
        cases = validate_reference(reference, suite, cases, model_id, revision)
        if reference.get("checkpoint", {}).get("files_sha256") != checkpoint_record["files_sha256"]:
            raise ValueError("Native HF reference used different checkpoint bytes")
        if reference.get("source") != upstream:
            raise ValueError("Native HF reference used a different official compiler or backend source")
    head_path = output / "intern-decision.head.json"
    export_head(checkpoint, config, cases[0]["symbol_token_ids"], head_path)
    preparation = {"schema_version": 1, "model_id": model_id, "revision": revision,
                   "head_file": head_path.name, "head_sha256": file_sha256(head_path),
                   "hidden_size": text_config["hidden_size"], "context_size": args.context_size,
                   "reference_result_file": str(args.reference.resolve()) if args.reference else None,
                   "reference_result_sha256": file_sha256(args.reference) if args.reference else None,
                   "inputs_sha256": suite["inputs_sha256"], "cases": cases,
                   "checkpoint": checkpoint_record, "source": upstream,
                   "decision_protocol": {"template": "Exact HFBackend.encode text path", "temperature": 1.0,
                       "decision_token_id": marker_id, "padding": False, "truncation": False,
                       "apply_chat_template_in_unity": False, "head_symbols": list(SYMBOLS)},
                   "adapter_sha256": file_sha256(__file__)}
    atomic_json(output / "preparation.json", preparation)
    if args.prepare_only:
        return {"status": "prepared_inputs_only", "file": str(output / "preparation.json"),
                "case_count": len(cases), "max_input_tokens": max(len(case["input_ids"]) for case in cases)}
    source = ensure_source(output / "tools")
    python_files = {str(path.relative_to(source / "gguf-py")): file_sha256(path)
                    for path in sorted((source / "gguf-py/gguf").rglob("*.py"))}
    tool = {"llama_cpp_commit": LLAMA_COMMIT,
            "source_pin": json.loads((source / "source-pin.json").read_text(encoding="utf-8")),
            "converter_sha256": file_sha256(source / "convert_hf_to_gguf.py"),
            "gguf_python_sha256": fingerprint(python_files),
            "pretokenizer_compatibility": tokenizer_compatibility(checkpoint, tokenizer, source),
            "python_dependencies": {name: importlib.metadata.version(name) for name in
                                    ("torch", "transformers", "numpy", "safetensors")}}
    model = output / "intern-decision.f16.gguf"
    expected = {"model_id": model_id, "revision": revision, "checkpoint_files": checkpoint_record["files"],
                "download_provenance_sha256": file_sha256(provenance_path), "tools": tool,
                "adapter_sha256": file_sha256(__file__), "outtype": "f16", "use_temp_file": True,
                "text_only": True}
    artifact = ensure_artifact(model, expected, lambda path: convert_f16(checkpoint, path, source))
    inspection = inspect_gguf(model, config, source, marker_id)
    manifest = {**preparation, "gguf_file": model.name, "gguf_sha256": artifact["artifact_sha256"],
                "gguf_bytes": artifact["artifact_bytes"], "gguf_provenance_file": model.name + ".provenance.json",
                "gguf_provenance_sha256": file_sha256(model.with_suffix(model.suffix + ".provenance.json")),
                "converter": tool, "gguf_inspection": inspection}
    atomic_json(output / "cases.json", manifest)
    return {"status": "complete", "file": str(output / "cases.json"), "gguf_bytes": model.stat().st_size,
            "case_count": len(cases), "max_input_tokens": max(len(case["input_ids"]) for case in cases)}


def main():
    if len(sys.argv) == 5 and sys.argv[1] == "--convert-worker":
        convert_worker(*sys.argv[2:])
        return
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--checkpoint", type=Path, required=True)
    parser.add_argument("--source-repo", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--reference", type=Path, help="Completed native HF result.json for the same pinned checkpoint")
    parser.add_argument("--context-size", type=int, default=1024)
    parser.add_argument("--prepare-only", action="store_true", help="Export exact tokens/head/provenance without converting GGUF")
    args = parser.parse_args()
    print(json.dumps(prepare(args), indent=2), flush=True)


if __name__ == "__main__":
    main()
