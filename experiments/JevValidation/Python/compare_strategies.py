import argparse
import gc
import hashlib
import json
import math
from pathlib import Path
import shutil
import time
import urllib.request

import numpy as np

from prepare import MODEL_REVISION, _sha256, validate_case
from prepare_strategies import STRATEGIES, catalog_fingerprint

HYPOTHESIS_TEMPLATE = "The best high-level strategy for the actor's stated goal is: {strategy}"
MODELS = (
    {"name": "deberta_xsmall", "model_id": "MoritzLaurer/deberta-v3-xsmall-zeroshot-v1.1-all-33",
     "revision": "262ae02f29173eec1c250f90804dc7edc677dcff", "onnx": "onnx/model_quantized.onnx",
     "sha256": "1fbf78872ceab4e8003eaa0696896008c019d5a239be734c5d6230f312ad480e"},
    {"name": "minilm2", "model_id": "cross-encoder/nli-MiniLM2-L6-H768",
     "revision": "b95119ce93d3e065de6214e38cd4a97b0f2f2c6d", "onnx": "onnx/model_quint8_avx2.onnx",
     "sha256": "44391a5241a62e0083c1a8899a71e69a092b95aea5ba89e14062925468eceac7"},
)


def entailment_index(config):
    labels = config.get("id2label")
    if not isinstance(labels, dict) or len(labels) not in (2, 3) or set(labels) != {str(i) for i in range(len(labels))}:
        raise ValueError("NLI config must explicitly map contiguous indices to two or three labels")
    normalized = {int(index): label.strip().casefold() for index, label in labels.items() if isinstance(label, str)}
    if len(normalized) != len(labels) or len(set(normalized.values())) != len(labels):
        raise ValueError("NLI labels must be unique strings")
    matches = [index for index, label in normalized.items() if label == "entailment"]
    if len(matches) != 1:
        raise ValueError("Exactly one explicit entailment label is required")
    if "label2id" in config:
        inverse = config["label2id"]
        if not isinstance(inverse, dict) or {str(label).strip().casefold(): index for label, index in inverse.items()} != {label: index for index, label in normalized.items()}:
            raise ValueError("NLI label2id and id2label disagree")
    return matches[0]


def _softmax(values):
    maximum = max(values)
    weights = [math.exp(float(value) - maximum) for value in values]
    total = math.fsum(weights)
    return [weight / total for weight in weights]


def rank_logits(choice_ids, logits, entailment, label_count):
    values = np.asarray(logits, dtype=np.float64)
    if len(choice_ids) != 8 or set(choice_ids) != set(STRATEGIES) or values.shape != (8, label_count) or label_count not in (2, 3) or not 0 <= entailment < label_count or not np.isfinite(values).all():
        raise ValueError("NLI output must be finite logits for eight known strategies and configured classes")
    scores = _softmax(values[:, entailment])
    class_scores = [_softmax(row) for row in values]
    relative = dict(zip(choice_ids, scores))
    return {"selected_strategy": max(relative, key=relative.__getitem__), "relative_scores": relative,
            "entailment_logits": {key: float(row[entailment]) for key, row in zip(choice_ids, values)},
            "raw_logits": {key: row.tolist() for key, row in zip(choice_ids, values)},
            "nli_class_probabilities": dict(zip(choice_ids, class_scores)),
            "entailment_probabilities": {key: row[entailment] for key, row in zip(choice_ids, class_scores)}}


def load_reference(path):
    document = json.loads(Path(path).read_text(encoding="utf-8"))
    if not isinstance(document, dict) or document.get("token_count") != 512 or document.get("reference_source", {}).get("model_revision") != MODEL_REVISION:
        raise ValueError("The preserved 512-token fixed TinyJev reference suite is required")
    source = document["reference_source"]
    catalog = source.get("strategy_catalog")
    if not isinstance(catalog, dict) or catalog != STRATEGIES or source.get("strategy_catalog_sha256") != catalog_fingerprint(catalog):
        raise ValueError("The preserved strategy catalog, its fingerprint, and current candidate descriptions must match")
    cases = document.get("cases")
    if not isinstance(cases, list) or len(cases) != 18:
        raise ValueError("Exactly eighteen preserved strategy cases are required")
    for case in cases:
        validate_case(case, 151936, token_count=512, choice_count=8)
        if set(case["choice_ids"]) != set(STRATEGIES) or not isinstance(case.get("state"), str):
            raise ValueError("Every reference case must preserve its state and eight strategy identities")
    if len({case["case_id"] for case in cases}) != 18:
        raise ValueError("Reference case IDs must be unique")
    return document


def make_pairs(case, catalog):
    return [(case["state"], HYPOTHESIS_TEMPLATE.format(strategy=catalog[key])) for key in case["choice_ids"]]


def _verify_file(path, descriptor):
    size = path.stat().st_size
    if size != descriptor["size"] or not 0 < size <= 330_000_000:
        raise ValueError("Downloaded file length differs from the pinned repository metadata")
    sha256, git = hashlib.sha256(), hashlib.sha1(b"blob " + str(size).encode() + b"\0")
    with path.open("rb") as stream:
        while block := stream.read(1024 * 1024):
            sha256.update(block)
            git.update(block)
    if "lfs" in descriptor:
        valid = sha256.hexdigest() == descriptor["lfs"]["sha256"]
    else:
        valid = git.hexdigest() == descriptor["blobId"]
    if not valid:
        raise ValueError("Downloaded bytes differ from the pinned LFS SHA-256 or Git blob hash")
    return {"bytes": size, "sha256": sha256.hexdigest(), "repository_hash": descriptor.get("lfs", {}).get("sha256", descriptor.get("blobId"))}


def _download_file(url, path, descriptor):
    path = Path(path)
    if path.exists():
        return _verify_file(path, descriptor)
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".partial")
    request = urllib.request.Request(url, headers={"User-Agent": "Strategy-NLI-Comparison"})
    with urllib.request.urlopen(request, timeout=120) as response, temporary.open("wb") as stream:
        shutil.copyfileobj(response, stream, length=1024 * 1024)
    record = _verify_file(temporary, descriptor)
    temporary.replace(path)
    return record


def download_model(specification, root):
    url = f"https://huggingface.co/api/models/{specification['model_id']}/revision/{specification['revision']}?blobs=true"
    with urllib.request.urlopen(url, timeout=60) as response:
        metadata = json.load(response)
    if metadata.get("sha") != specification["revision"]:
        raise ValueError("The model metadata does not match its fixed revision")
    files = {item["rfilename"]: item for item in metadata["siblings"]}
    required = {"config.json", "tokenizer.json", "tokenizer_config.json", "special_tokens_map.json", specification["onnx"]}
    if not required <= set(files) or files[specification["onnx"]].get("lfs", {}).get("sha256") != specification["sha256"]:
        raise ValueError("The fixed model lacks the required files or expected ONNX SHA-256")
    selected = required | ({"added_tokens.json", "spm.model", "vocab.json", "merges.txt"} & set(files))
    directory = Path(root) / specification["name"]
    records = {}
    for filename in sorted(selected):
        file_url = f"https://huggingface.co/{specification['model_id']}/resolve/{specification['revision']}/{filename}"
        records[filename] = _download_file(file_url, directory / filename, files[filename])
    record = {**specification, "license": metadata.get("cardData", {}).get("license"), "files": records}
    (directory / "download-manifest.json").write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
    print(f"Verified {specification['name']}: {records[specification['onnx']]['bytes']} ONNX bytes", flush=True)
    return directory, record


def evaluate_model(directory, record, cases, catalog):
    import onnxruntime as ort
    from transformers import AutoTokenizer

    config = json.loads((directory / "config.json").read_text(encoding="utf-8"))
    entailment = entailment_index(config)
    label_count = len(config["id2label"])
    started = time.perf_counter_ns()
    tokenizer = AutoTokenizer.from_pretrained(directory, local_files_only=True, trust_remote_code=False, use_fast=True)
    tokenizer_load_ms = (time.perf_counter_ns() - started) / 1e6
    options = ort.SessionOptions()
    options.intra_op_num_threads = 4
    options.inter_op_num_threads = 1
    options.execution_mode = ort.ExecutionMode.ORT_SEQUENTIAL
    started = time.perf_counter_ns()
    session = ort.InferenceSession(str(directory / record["onnx"]), sess_options=options, providers=["CPUExecutionProvider"])
    session_load_ms = (time.perf_counter_ns() - started) / 1e6
    if "logits" not in {output.name for output in session.get_outputs()}:
        raise ValueError("The official ONNX classifier must expose raw logits")
    input_types = {item.name: item.type for item in session.get_inputs()}
    dtypes = {"tensor(int64)": np.int64, "tensor(int32)": np.int32}
    if any(kind not in dtypes for kind in input_types.values()):
        raise ValueError("Unexpected ONNX token input dtype")
    prepared = []
    for case in cases:
        pairs = make_pairs(case, catalog)
        started = time.perf_counter_ns()
        encoded = tokenizer([pair[0] for pair in pairs], [pair[1] for pair in pairs],
                            padding=True, truncation=False, return_tensors="np")
        tokenization_ms = (time.perf_counter_ns() - started) / 1e6
        if encoded["input_ids"].shape[1] > min(512, tokenizer.model_max_length):
            raise ValueError("An NLI premise/hypothesis pair exceeds the model token limit; no truncation was applied")
        if not set(input_types) <= set(encoded):
            raise ValueError("Tokenizer output does not supply the official ONNX inputs")
        inputs = {name: np.asarray(encoded[name], dtype=dtypes[kind]) for name, kind in input_types.items()}
        prepared.append((inputs, tokenization_ms, encoded["attention_mask"].sum(axis=1).tolist()))
    started = time.perf_counter_ns()
    session.run(["logits"], prepared[0][0])
    warmup_ms = (time.perf_counter_ns() - started) / 1e6
    results = []
    for case, (inputs, tokenization_ms, counts) in zip(cases, prepared):
        started = time.perf_counter_ns()
        logits = session.run(["logits"], inputs)[0]
        inference_ms = (time.perf_counter_ns() - started) / 1e6
        ranking = rank_logits(case["choice_ids"], logits, entailment, label_count)
        results.append({"case_id": case["case_id"], "premise": case["state"], "choice_ids": case["choice_ids"],
                        "hypotheses": [pair[1] for pair in make_pairs(case, catalog)], "input_token_counts": counts,
                        "tokenization_ms": tokenization_ms, "inference_ms": inference_ms, **ranking})
    report = {"model": record, "template": HYPOTHESIS_TEMPLATE, "strategy_catalog": catalog,
              "strategy_catalog_sha256": catalog_fingerprint(catalog), "id2label": config["id2label"],
              "entailment_index": entailment, "runtime": {"onnxruntime": ort.__version__, "provider": session.get_providers(),
              "intra_op_threads": 4, "inter_op_threads": 1, "execution": "sequential"},
              "timing": {"tokenizer_load_ms": tokenizer_load_ms, "session_load_ms": session_load_ms,
                         "warmup_batches": 1, "warmup_inference_ms": warmup_ms, "candidates_per_batch": 8,
                         "timed_batches": len(cases), "total_inference_ms": sum(row["inference_ms"] for row in results),
                         "mean_inference_ms_per_eight_candidates": math.fsum(row["inference_ms"] for row in results) / len(results)},
              "results": results}
    del session, tokenizer, prepared
    gc.collect()
    return report


def _summaries(results, baseline):
    by_id = {row["case_id"]: row for row in results}
    grid = []
    for metadata in baseline["results"]:
        if metadata["world_id"] == "opening" and metadata["current_strategy"] == "none":
            row = by_id[metadata["case_id"]]
            grid.append({"case_id": row["case_id"], "personality": metadata["personality_id"], "goal": metadata["goal_id"],
                         "selected_strategy": row["selected_strategy"], "relative_score": row["relative_scores"][row["selected_strategy"]],
                         "nli_entailment_probability": row["entailment_probabilities"][row["selected_strategy"]]})
    base, rotated = by_id[baseline["results"][0]["case_id"]], by_id[baseline["results"][12]["case_id"]]
    return {"no_current_grid": grid, "order_control": {
        "case_ids": [base["case_id"], rotated["case_id"]], "selected_strategies": [base["selected_strategy"], rotated["selected_strategy"]],
        "id_aligned_max_relative_score_difference": max(abs(base["relative_scores"][key] - rotated["relative_scores"][key]) for key in STRATEGIES)},
        "current_controls": [{"case_id": baseline["results"][index]["case_id"],
                              "current_strategy": baseline["results"][index]["current_strategy"],
                              "selected_strategy": by_id[baseline["results"][index]["case_id"]]["selected_strategy"]}
                             for index in (0, 9, 10, 11)]}


def _check_core_hashes(baseline):
    hashes = {}
    for quantization, variant in baseline["prepared_cases"].items():
        directory = Path(variant["cases_file"]).parent
        for path, expected in ((directory / f"tinyjev-{quantization}.gguf", variant["model_sha256"]),
                               (directory / "tinyjev.head", variant["head_sha256"])):
            actual = _sha256(path)
            if actual != expected:
                raise ValueError("The original TinyJev model or trained head changed")
            hashes[str(path)] = actual
    for filename, expected in baseline["legacy_sha256"].items():
        if _sha256(Path(filename)) != expected:
            raise ValueError("The original ONNX or six-case suite changed")
        hashes[filename] = expected
    return hashes


def compare(cases_path, baseline_path, output, download_only=False):
    cases_path, baseline_path, output = map(Path, (cases_path, baseline_path, output))
    reference = load_reference(cases_path)
    cases = reference["cases"]
    catalog = reference["reference_source"]["strategy_catalog"]
    baseline = json.loads(baseline_path.read_text(encoding="utf-8"))
    preserved_paths = [cases_path, baseline_path] + [Path(row["cases_file"]) for row in baseline["prepared_cases"].values()]
    before = {str(path): _sha256(path) for path in preserved_paths}
    output.mkdir(parents=True, exist_ok=True)
    reports = []
    for specification in MODELS:
        directory, record = download_model(specification, output / "models")
        if download_only:
            continue
        report = evaluate_model(directory, record, cases, catalog)
        report["source_cases_sha256"] = before[str(cases_path)]
        report["summary"] = _summaries(report["results"], baseline)
        (output / (specification["name"] + ".json")).write_text(json.dumps(report, indent=2, allow_nan=False) + "\n", encoding="utf-8")
        reports.append({"model_id": specification["model_id"], "summary": report["summary"], "timing": report["timing"]})
        print(json.dumps(reports[-1], indent=2), flush=True)
        gc.collect()
    after = {str(path): _sha256(path) for path in preserved_paths}
    if before != after:
        raise ValueError("The preserved eighteen-case TinyJev baseline changed")
    if not download_only:
        summary = {"template": HYPOTHESIS_TEMPLATE, "strategy_catalog": catalog, "strategy_catalog_sha256": catalog_fingerprint(catalog),
                   "source_sha256": after, "original_core_sha256": _check_core_hashes(baseline),
                   "models": reports, "score_meaning": "Relative ranking over entailment logits; NLI class probabilities are not game success probabilities",
                   "timing_method": "One warmup batch, then one CPU batch of eight candidates per preserved case; model load excluded"}
        (output / "comparison.json").write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    return reports


def main():
    parser = argparse.ArgumentParser()
    base = Path(__file__).parent / "Artifacts"
    parser.add_argument("mode", nargs="?", choices=("download", "run"), default="run")
    parser.add_argument("--cases", type=Path, default=base / "strategies/reference.json")
    parser.add_argument("--baseline", type=Path, default=base / "strategies/strategy-validation.json")
    parser.add_argument("--output", type=Path, default=base / "alternatives")
    args = parser.parse_args()
    compare(args.cases, args.baseline, args.output, args.mode == "download")


if __name__ == "__main__":
    main()
