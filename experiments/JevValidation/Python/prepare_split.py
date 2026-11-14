"""Split the fixed TinyJev ONNX at complete transformer-block boundaries.

This preserves the exported graph and its weights; it does not retrain or prune
the model. Only the three hidden-state boundary names are changed. Shared mask
and positional calculations are retained independently where required.
"""

import argparse
import gc
import hashlib
import json
import math
from pathlib import Path
import re
import shutil
import time


def sha256(path):
    digest = hashlib.sha256()
    with Path(path).open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def _scope(name, layer):
    return re.search(r"(?:^|/)layers[./]" + str(layer) + r"(?:/|$)", name) is not None


def discover_boundaries(model):
    """Find one embedding and the residual values crossing blocks 9/18.

    Names identify module scopes, while actual graph edges identify the cut.
    An unfamiliar or ambiguous export fails rather than silently cutting the
    wrong Add/normalization operation.
    """
    initializers = {value.name: value for value in model.graph.initializer}
    embeddings = [node for node in model.graph.node if node.op_type == "Gather"
                  and node.input and node.input[0] in initializers
                  and ("embed_tokens" in node.name or "embed_tokens" in node.input[0])]
    if len(embeddings) != 1 or len(embeddings[0].output) != 1:
        raise ValueError("Expected exactly one named token-embedding Gather")
    embedding = embeddings[0]
    weight = initializers[embedding.input[0]]
    if len(weight.dims) != 2:
        raise ValueError("The token embedding must have vocabulary x hidden shape")
    layers = {index for index in range(28)
              if any(_scope(node.name, index) for node in model.graph.node)}
    if layers != set(range(28)):
        raise ValueError("Expected the fixed TinyJev export with all 28 transformer blocks")
    cuts = [embedding.output[0]]
    for index in (9, 18):
        produced = {output for node in model.graph.node if _scope(node.name, index)
                    for output in node.output}
        crossing = {value for node in model.graph.node if _scope(node.name, index + 1)
                    for value in node.input if value in produced}
        if len(crossing) != 1:
            raise ValueError(f"Expected one hidden-state edge between blocks {index} and {index + 1}, got {sorted(crossing)}")
        cuts.append(next(iter(crossing)))
    if len(set(cuts)) != 3:
        raise ValueError("The embedding and transformer boundaries must be distinct")
    return cuts, weight


def _rename(model, mapping):
    for node in model.graph.node:
        for values in (node.input, node.output):
            for index, value in enumerate(values):
                values[index] = mapping.get(value, value)
    for collection in (model.graph.input, model.graph.output, model.graph.value_info,
                       model.graph.initializer):
        for value in collection:
            value.name = mapping.get(value.name, value.name)


def _dependencies(model, output_names, stops):
    """Collect actual graph-input dependencies, stopping at the previous cut."""
    producers = {output: node for node in model.graph.node for output in node.output if output}
    initializers = {value.name for value in model.graph.initializer}
    original_inputs = {value.name for value in model.graph.input}
    required_inputs, visited = set(), set()
    pending = list(output_names)
    while pending:
        value = pending.pop()
        if not value or value in visited:
            continue
        visited.add(value)
        if value in stops or value in original_inputs:
            required_inputs.add(value)
        elif value in initializers:
            continue
        elif value in producers:
            pending.extend(producers[value].input)
        else:
            raise ValueError("The graph has an unresolved tensor dependency: " + value)
    return required_inputs


def _tensor_metadata(value):
    import onnx
    tensor = value.type.tensor_type
    shape = [dimension.dim_value if dimension.HasField("dim_value") else dimension.dim_param
             for dimension in tensor.shape.dim]
    return {"name": value.name, "dtype": onnx.TensorProto.DataType.Name(tensor.elem_type), "shape": shape}


def initializer_statistics(model):
    import onnx
    statistics = []
    floats = {onnx.TensorProto.FLOAT, onnx.TensorProto.FLOAT16, onnx.TensorProto.BFLOAT16}
    for tensor in model.graph.initializer:
        elements = math.prod(tensor.dims)
        size = onnx.helper.tensor_dtype_to_np_dtype(tensor.data_type).itemsize
        statistics.append({"name": tensor.name, "shape": list(tensor.dims),
                           "dtype": onnx.TensorProto.DataType.Name(tensor.data_type),
                           "elements": elements, "tensor_bytes": elements * size,
                           "fp32_expanded_bytes": elements * (4 if tensor.data_type in floats else size)})
    return statistics


def partition_model(model, token_count):
    """Yield embedding, blocks 0..9, 10..18, and 19..27 plus the head.

    Internal value metadata is deliberately added only at known Qwen hidden
    boundaries, avoiding full-model shape-inference copies of gigabyte weights.
    """
    import onnx
    from onnx.utils import Extractor

    boundaries, embedding = discover_boundaries(model)
    hidden_size = embedding.dims[1]
    names = ("hidden_0", "hidden_10", "hidden_19")
    if any(value.name in names for value in model.graph.input):
        raise ValueError("Source graph already uses reserved split boundary names")
    _rename(model, dict(zip(boundaries, names)))
    existing = {value.name for value in model.graph.value_info}
    for name in names:
        if name not in existing:
            model.graph.value_info.append(onnx.helper.make_tensor_value_info(
                name, embedding.data_type, [1, token_count, hidden_size]))
    specs = (("embedding", None, names[0]),
             ("blocks_00_09", names[0], names[1]),
             ("blocks_10_18", names[1], names[2]),
             ("blocks_19_27_head", names[2], "probabilities"))
    original_inputs = [value.name for value in model.graph.input]
    extractor = Extractor(model)
    for name, previous, output in specs:
        inputs = _dependencies(model, [output], {previous} if previous else set())
        if previous and previous not in inputs:
            raise ValueError(name + " does not depend on its preceding hidden state")
        ordered_inputs = ([previous] if previous else []) + [value for value in original_inputs if value in inputs]
        chunk = extractor.extract_model(ordered_inputs, [output])
        chunk.ir_version = model.ir_version
        chunk.metadata_props.extend(model.metadata_props)
        if previous and any(value.name == embedding.name for value in chunk.graph.initializer):
            raise ValueError(name + " unexpectedly duplicates the token embedding; split dependency is incomplete")
        onnx.checker.check_model(chunk)
        yield name, chunk


def split(model_path, cases_path, output):
    import onnx

    model_path, cases_path, output = map(Path, (model_path, cases_path, output))
    if not model_path.is_file() or not cases_path.is_file():
        raise ValueError("Both a source ONNX and its prepared cases are required")
    source_hash = sha256(model_path)
    document = json.loads(cases_path.read_text(encoding="utf-8"))
    if document.get("schema_version") != 1 or document.get("model_sha256") != source_hash:
        raise ValueError("The prepared cases must identify the exact source ONNX SHA-256")
    if not document.get("cases") or type(document.get("token_count")) is not int:
        raise ValueError("The source cases and token_count are required")
    output.mkdir(parents=True, exist_ok=True)
    manifest_path = output / "split-manifest.json"
    if manifest_path.exists():
        raise ValueError("Output already has a split manifest; use a fresh directory")
    start = time.perf_counter()
    model = onnx.load(str(model_path), load_external_data=True)
    _, embedding = discover_boundaries(model)
    manifest = {"schema_version": 1, "model_id": document["model_id"],
                "model_revision": document["model_revision"], "source_onnx_sha256": source_hash,
                "token_count": document["token_count"], "hidden_size": embedding.dims[1],
                "case_file": "cases.json", "case_sha256": sha256(cases_path),
                "chunks": [], "source_initializers": initializer_statistics(model)}
    for name, chunk in partition_model(model, document["token_count"]):
        path = output / (name + ".onnx")
        if path.exists():
            raise ValueError("Refusing to overwrite an existing split file: " + str(path))
        onnx.save_model(chunk, str(path), save_as_external_data=False)
        weights = initializer_statistics(chunk)
        entry = {"name": name, "file": path.name, "sha256": sha256(path),
                 "file_bytes": path.stat().st_size,
                 "inputs": [value.name for value in chunk.graph.input],
                 "outputs": [value.name for value in chunk.graph.output],
                 "input_tensors": [_tensor_metadata(value) for value in chunk.graph.input],
                 "output_tensors": [_tensor_metadata(value) for value in chunk.graph.output],
                 "initializer_bytes": sum(value["tensor_bytes"] for value in weights),
                 "fp32_expanded_initializer_bytes": sum(value["fp32_expanded_bytes"] for value in weights),
                 "initializers": weights}
        manifest["chunks"].append(entry)
        print(json.dumps({key: entry[key] for key in ("name", "file_bytes", "inputs", "outputs", "fp32_expanded_initializer_bytes")}), flush=True)
        del chunk
        gc.collect()
    owners = {}
    for chunk in manifest["chunks"]:
        for tensor in chunk["initializers"]:
            owners.setdefault(tensor["name"], []).append(chunk["name"])
    missing = sorted({value["name"] for value in manifest["source_initializers"]} - owners.keys())
    if missing:
        raise ValueError("Splitting dropped source initializers: " + ", ".join(missing))
    manifest["shared_initializers"] = {name: chunks for name, chunks in owners.items() if len(chunks) > 1}
    manifest["split_seconds"] = time.perf_counter() - start
    manifest["source_file_bytes"] = model_path.stat().st_size
    target_cases = output / "cases.json"
    if cases_path.resolve() != target_cases.resolve():
        shutil.copyfile(cases_path, target_cases)
    if sha256(target_cases) != manifest["case_sha256"]:
        raise ValueError("Prepared cases changed during splitting")
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    return manifest


def _relative_file(directory, filename):
    if not isinstance(filename, str) or not filename or Path(filename).name != filename:
        raise ValueError("Artifact paths must be filenames within the manifest directory")
    path = directory / filename
    if not path.is_file():
        raise ValueError("Missing split artifact: " + str(path))
    return path


def verify(manifest_path, *, threads=4, tolerance=0.005, case_limit=0):
    import numpy as np
    import onnxruntime as ort

    if type(threads) is not int or threads < 1 or not math.isfinite(tolerance) or tolerance <= 0:
        raise ValueError("Positive CPU threads and finite positive tolerance are required")
    if type(case_limit) is not int or case_limit < 0:
        raise ValueError("The case limit must be zero (all cases) or a positive integer")
    manifest_path = Path(manifest_path)
    directory = manifest_path.parent
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    if manifest.get("schema_version") != 1 or len(manifest.get("chunks", [])) != 4:
        raise ValueError("A schema_version=1 four-chunk manifest is required")
    cases_path = _relative_file(directory, manifest["case_file"])
    if sha256(cases_path) != manifest["case_sha256"]:
        raise ValueError("Prepared case SHA-256 mismatch")
    document = json.loads(cases_path.read_text(encoding="utf-8"))
    options = ort.SessionOptions()
    options.intra_op_num_threads = threads
    options.inter_op_num_threads = 1
    sessions = []
    start = time.perf_counter()
    for chunk in manifest["chunks"]:
        path = _relative_file(directory, chunk["file"])
        if sha256(path) != chunk["sha256"]:
            raise ValueError("Split ONNX SHA-256 mismatch: " + chunk["name"])
        session = ort.InferenceSession(str(path), sess_options=options, providers=["CPUExecutionProvider"])
        if [value.name for value in session.get_inputs()] != chunk["inputs"] or [value.name for value in session.get_outputs()] != chunk["outputs"]:
            raise ValueError("Actual ONNX inputs/outputs differ from the manifest: " + chunk["name"])
        sessions.append(session)
    report = {"schema_version": 1, "backend": "ONNXRuntimeCPU", "threads": threads,
              "manifest_sha256": sha256(manifest_path), "load_seconds": time.perf_counter() - start,
              "timing_scope": "Sequential inference only, excluding loading and one warmup case",
              "tolerance": tolerance, "cases": []}

    def predict(case):
        values = {name: np.asarray(case[name], dtype=np.int32) for name in
                  ("input_ids", "attention_mask", "option_positions", "decision_position")}
        for name in ("input_ids", "attention_mask"):
            values[name] = values[name].reshape(1, manifest["token_count"])
        for session in sessions:
            outputs = session.run(None, {value.name: values[value.name] for value in session.get_inputs()})
            values.update(zip((value.name for value in session.get_outputs()), outputs))
        return values["probabilities"]

    cases = document["cases"][:case_limit or None]
    if not cases:
        raise ValueError("No actual model cases were selected")
    warmup = time.perf_counter()
    predict(cases[0])
    report["warmup_seconds"] = time.perf_counter() - warmup
    for case in cases:
        start = time.perf_counter()
        actual = predict(case)
        milliseconds = (time.perf_counter() - start) * 1000
        expected = np.asarray(case["reference_probabilities"], dtype=np.float32)
        if actual.shape != expected.shape or not np.isfinite(actual).all():
            raise ValueError(case["case_id"] + ": split output must be a finite vector matching the reference shape")
        numeric_matches = bool(np.allclose(actual, expected, atol=tolerance, rtol=tolerance))
        selected, reference = int(np.argmax(actual)), int(np.argmax(expected))
        report["cases"].append({"case_id": case["case_id"], "probabilities": actual.tolist(),
                                "reference_probabilities": expected.tolist(),
                                "max_reference_error": float(np.max(np.abs(actual - expected))),
                                "chosen_id": case["choice_ids"][selected],
                                "reference_chosen_id": case["choice_ids"][reference],
                                "choice_matches": selected == reference,
                                "numeric_matches": numeric_matches, "inference_ms": milliseconds})
    report["mean_inference_ms"] = sum(case["inference_ms"] for case in report["cases"]) / len(report["cases"])
    report["max_reference_error"] = max(case["max_reference_error"] for case in report["cases"])
    report["choice_matches"] = sum(case["choice_matches"] for case in report["cases"])
    report["success"] = all(case["numeric_matches"] and case["choice_matches"] for case in report["cases"])
    (directory / "split-ort-parity.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    if not report["success"]:
        raise ValueError("Split output failed reference numeric parity or selected-action agreement; see split-ort-parity.json")
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    exporter = commands.add_parser("split")
    exporter.add_argument("--model", type=Path, required=True)
    exporter.add_argument("--cases", type=Path, required=True)
    exporter.add_argument("--output", type=Path, required=True)
    verifier = commands.add_parser("verify")
    verifier.add_argument("--manifest", type=Path, required=True)
    verifier.add_argument("--threads", type=int, default=4)
    verifier.add_argument("--tolerance", type=float, default=0.005)
    verifier.add_argument("--case-limit", type=int, default=0)
    args = parser.parse_args()
    if args.command == "split":
        result = split(args.model, args.cases, args.output)
        print(json.dumps({"chunks": len(result["chunks"]), "split_seconds": result["split_seconds"]}), flush=True)
    else:
        print(json.dumps(verify(args.manifest, threads=args.threads, tolerance=args.tolerance,
                                case_limit=args.case_limit), indent=2), flush=True)


if __name__ == "__main__":
    main()
