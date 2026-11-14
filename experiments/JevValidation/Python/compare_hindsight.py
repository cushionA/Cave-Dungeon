"""CPU diagnostic adapter for the pinned hs-decide-1-flash release.

This imports the release's unchanged renderer, alias reduction and answer code.
Torch supplies LM-head logits at the same colon positions as vLLM STEP pooling.
It is a CPU adapter, not a claim of measured parity with a running vLLM server.
"""

import argparse
from collections import defaultdict
import datetime
import importlib
import importlib.metadata
import json
import math
from pathlib import Path
import platform
import statistics
import sys
import time
import traceback
import urllib.request

from compare_intern_decision import MemorySampler, atomic_json, file_sha256, fingerprint, rss_sample

MODEL_ID = "hindsight-ai/hs-decide-1-flash"
REVISION = "b9a782058dbc81ad59d9397bf2a9fed946778cb3"


def download_checkpoint(directory):
    """Download the fixed public release and check LFS SHA-256 / Git blob SHA-1."""
    import hashlib
    from huggingface_hub import snapshot_download

    directory = Path(directory)
    url = f"https://huggingface.co/api/models/{MODEL_ID}/revision/{REVISION}?blobs=true"
    with urllib.request.urlopen(url, timeout=30) as response:
        metadata = json.load(response)
    if metadata["sha"] != REVISION:
        raise ValueError("Checkpoint revision changed")
    descriptors = [row for row in metadata["siblings"]
                   if row["rfilename"] != ".gitattributes" and not row["rfilename"].startswith("assets/")]
    names = [row["rfilename"] for row in descriptors]
    if any(Path(name).is_absolute() or ".." in Path(name).parts for name in names):
        raise ValueError("Invalid repository path")
    snapshot_download(MODEL_ID, revision=REVISION, local_dir=directory,
                      allow_patterns=names, max_workers=2, token=False)
    files = []
    for row in descriptors:
        path = directory / row["rfilename"]
        size = path.stat().st_size
        if size != row["size"]:
            raise ValueError("Downloaded file size changed: " + row["rfilename"])
        digest = hashlib.sha256()
        blob = hashlib.sha1(b"blob " + str(size).encode() + b"\0")
        with path.open("rb") as source:
            for chunk in iter(lambda: source.read(1024 * 1024), b""):
                digest.update(chunk)
                blob.update(chunk)
        lfs = row.get("lfs", {})
        expected = lfs.get("sha256") or row["blobId"]
        if (digest.hexdigest() if lfs else blob.hexdigest()) != expected:
            raise ValueError("Repository file hash differs: " + row["rfilename"])
        files.append({"path": row["rfilename"], "bytes": size,
                      "sha256": digest.hexdigest(), "repository_hash": expected})
    report = {"model_id": MODEL_ID, "revision": REVISION, "metadata_url": url, "files": files}
    atomic_json(directory / "download-provenance.json", report)
    return report


def load_cases(path):
    value = json.loads(Path(path).read_text())
    cases = value["cases"] if isinstance(value, dict) else value
    if not isinstance(cases, list) or not cases:
        raise ValueError("Cases must be a nonempty list")
    ids = set()
    for case in cases:
        if not isinstance(case, dict) or not isinstance(case.get("case_id"), str) or not case["case_id"].strip() or case["case_id"] in ids:
            raise ValueError("Unique case IDs are required")
        ids.add(case["case_id"])
        request = case.get("request")
        if not isinstance(request, dict) or not {"state", "questions"}.issubset(request) or set(request) - {"model", "id", "state", "questions"}:
            raise ValueError("Only state and typed questions, with optional model/id, may enter inference")
        if not isinstance(request["questions"], dict) or not request["questions"]:
            raise ValueError("A request needs typed questions")
        for question in request["questions"].values():
            if not isinstance(question, dict) or set(question) - {"type", "instructions", "criteria"}:
                raise ValueError("Unexpected question field, potentially a gold-label leak")
        expected = case.get("expected")
        if expected is not None and (not isinstance(expected, dict) or set(expected) != set(request["questions"])):
            raise ValueError("Expected labels must be separate and cover the request's questions")
    return cases


def model_request(case):
    """Explicit projection: metadata, expected labels and rationales never reach the renderer."""
    request = case["request"]
    return {"model": request.get("model", "hs-decide-1-flash"),
            "state": request["state"], "questions": request["questions"]}


def checkpoint_identity(directory):
    directory = Path(directory)
    manifest_path = directory / "download-provenance.json"
    manifest = json.loads(manifest_path.read_text())
    if manifest.get("model_id") != MODEL_ID or manifest.get("revision") != REVISION:
        raise ValueError("Only the pinned official checkpoint is supported")
    required = {"model.safetensors", "config.json", "decision.json", "tokenizer.json", "tokenizer_config.json",
                "serving/hindsight_decision/schema.py", "serving/hindsight_decision/prompt.py",
                "serving/hindsight_decision/typed.py", "serving/hindsight_decision/typed_render.py"}
    files = manifest.get("files", [])
    if not required.issubset({row["path"] for row in files}):
        raise ValueError("Checkpoint or official serving sources are missing")
    for row in files:
        path = directory / row["path"]
        if not path.resolve().is_relative_to(directory.resolve()) or path.stat().st_size != row["bytes"] or file_sha256(path) != row["sha256"]:
            raise ValueError("Pinned artifact changed: " + row["path"])
    return {"manifest_sha256": file_sha256(manifest_path), **manifest}


def load_official(directory):
    serving = str((Path(directory) / "serving").resolve())
    if serving not in sys.path:
        sys.path.insert(0, serving)
    names = ("prompt", "schema", "typed_render", "typed")
    modules = {name: importlib.import_module("hindsight_decision." + name) for name in names}
    for module in modules.values():
        if not Path(module.__file__).resolve().is_relative_to(Path(serving)):
            raise ValueError("Unexpected installed package shadowed the pinned serving source")
    return modules


def reduce_logits(modules, request, rendered, rows, classifier_ids):
    """Apply the official max-over-spellings reduction before its ordinary T=1 softmax."""
    if len(rows) != len(rendered.questions) or any(len(row) != len(classifier_ids) for row in rows):
        raise ValueError("Classifier row shape differs from the rendered question slots")
    if any(not math.isfinite(float(value)) for row in rows for value in row):
        raise ValueError("Nonfinite native logits")
    logits = modules["typed"].question_logits(rows, rendered, classifier_ids)
    answers, details = {}, {}
    for question, values in zip(rendered.questions, logits, strict=True):
        original = request.questions[question.id]
        keys = modules["schema"].option_keys(original)
        probabilities = modules["schema"].softmax(values)
        if len(probabilities) != len(keys) or any(not math.isfinite(p) or not 0 <= p <= 1 for p in probabilities) or abs(sum(probabilities) - 1) > 1e-6:
            raise ValueError("Invalid conditional answer distribution")
        answers[question.id] = modules["schema"].answer(original, probabilities)
        best = max(range(len(keys)), key=lambda index: probabilities[index])
        # Noul is a probability in the official API. The diagnostic's fixed binary threshold is 0.5.
        selected = probabilities[1] >= 0.5 if original.type == "noul" else keys[best]
        details[question.id] = {"type": original.type, "slot_position": question.slot,
                                "option_keys": keys, "alias_token_ids": question.groups,
                                "logits": values, "probabilities": dict(zip(keys, probabilities)),
                                "selected": selected}
    return answers, details


def load_runner(args):
    start = time.perf_counter()
    import torch
    import transformers

    torch.set_num_threads(args.threads)
    torch.set_num_interop_threads(1)
    torch.manual_seed(0)
    modules = load_official(args.checkpoint)
    settings = modules["prompt"].load_settings(args.checkpoint)
    tokenizer = transformers.AutoTokenizer.from_pretrained(args.checkpoint, local_files_only=True, trust_remote_code=False)
    ids = modules["typed"].classifier_ids(tokenizer)
    dtype = {"bfloat16": torch.bfloat16, "float32": torch.float32}[args.dtype]
    model = transformers.AutoModelForCausalLM.from_pretrained(
        args.checkpoint, dtype=dtype, local_files_only=True, trust_remote_code=False,
        attn_implementation="sdpa").eval()
    if model.get_output_embeddings().weight.shape[0] != model.config.vocab_size:
        raise ValueError("Output vocabulary differs from configuration")
    runtime = {"python": platform.python_version(), "platform": platform.platform(),
               "packages": {name: importlib.metadata.version(name) for name in
                            ("torch", "transformers", "tokenizers", "safetensors", "pydantic", "huggingface-hub", "numpy")},
               "transformers_path": transformers.__file__, "torch_path": torch.__file__,
               "device": str(next(model.parameters()).device), "actual_dtype": str(next(model.parameters()).dtype),
               "parameters": sum(p.numel() for p in model.parameters()), "threads": torch.get_num_threads(),
               "interop_threads": torch.get_num_interop_threads(), "seed": 0, "temperature": 1,
               "classifier_token_count": len(ids), "no_text_generation": True,
               "backend": "Torch CPU LM-head rows at official colon slots; official renderer/reduction/answer functions",
               "timing_scope": "Official validation/rendering, causal model forward at all answer slots, LM head and official probability decoding; checkpoint hashing, initialization and warmup excluded.",
               "parity_scope": "Same checkpoint, prompt IDs, positions, alias-max rule and probability formula as released vLLM plugin. vLLM server/kernel numerical parity is not measured."}
    initialization_ms = (time.perf_counter() - start) * 1000

    @torch.inference_mode()
    def predict(payload):
        request = modules["schema"].SystemOneRequest.model_validate(payload)
        rendered = modules["typed_render"].render_request(tokenizer, request, settings)
        positions = torch.tensor([question.slot for question in rendered.questions], dtype=torch.long)
        input_ids = torch.tensor([rendered.ids], dtype=torch.long)
        forward_start = time.perf_counter()
        vocabulary_logits = model(input_ids=input_ids, attention_mask=torch.ones_like(input_ids),
                                  use_cache=False, logits_to_keep=positions).logits[0]
        forward_ms = (time.perf_counter() - forward_start) * 1000
        rows = vocabulary_logits[:, ids].float().tolist()
        answers, details = reduce_logits(modules, request, rendered, rows, ids)
        return {"answers": answers, "details": details, "input_tokens": len(rendered.ids),
                "input_ids_sha256": fingerprint(rendered.ids), "forward_ms": forward_ms,
                "question_count": len(rendered.questions)}

    return predict, runtime, initialization_ms


def evaluate(case, raw):
    expected = case.get("expected")
    if expected is None:
        return {"label_scope": "unlabeled selection comparison", "correct": None, "questions": {}}
    comparisons = {}
    for name, gold in expected.items():
        selected = raw["details"][name]["selected"]
        if raw["details"][name]["type"] == "noul" and not isinstance(gold, bool):
            raise ValueError("Noul diagnostic labels must be actual booleans")
        comparisons[name] = {"expected": gold, "selected": selected, "correct": selected == gold}
    return {"label_scope": "Synthetic authored diagnostic labels, not gameplay outcomes",
            "correct": all(row["correct"] for row in comparisons.values()), "questions": comparisons}


def summarize(results):
    groups = defaultdict(list)
    for row in results:
        groups["all"].append(row)
        for key in ("suite", "language", "category"):
            if row.get(key) is not None:
                groups[key + ":" + str(row[key])].append(row)
    output = {}
    for name, rows in groups.items():
        labeled = [row for row in rows if row["evaluation"]["correct"] is not None]
        output[name] = {"cases": len(rows), "labeled_cases": len(labeled),
                        "correct": sum(row["evaluation"]["correct"] for row in labeled),
                        "mean_predict_ms": statistics.mean(row["end_to_end_predict_ms"] for row in rows),
                        "mean_forward_ms": statistics.mean(row["forward_ms"] for row in rows),
                        "input_token_range": [min(row["input_tokens"] for row in rows), max(row["input_tokens"] for row in rows)]}
    return output


def run(args):
    cases = load_cases(args.cases)
    if args.case_limit is not None:
        cases = cases[:args.case_limit]
    if not cases:
        raise ValueError("No selected cases")
    args.output.mkdir(parents=True, exist_ok=True)
    requests = [{"case_id": case["case_id"], "request": model_request(case)} for case in cases]
    atomic_json(args.output / "requests.json", requests)
    report = {"schema_version": 1, "status": "prepared", "created_utc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
              "model_id": MODEL_ID, "revision": REVISION, "adapter_sha256": file_sha256(__file__),
              "cases_file": str(args.cases.resolve()), "cases_file_sha256": file_sha256(args.cases),
              "requests_sha256": fingerprint(requests), "requested_cases": len(cases), "results": [],
              "evaluation_policy": {"noul_true_threshold": 0.5, "temperature": 1, "calibration_fit": False,
                                    "labels_in_model_input": False, "one_warmup": True, "timed_passes_per_case": 1}}
    path = args.output / "result.json"
    atomic_json(path, report)
    if args.prepare_only:
        return report
    memory = MemorySampler()
    memory.start()

    def persist():
        report["memory"] = memory.snapshot()
        report["summary"] = summarize(report["results"])
        atomic_json(path, report)

    try:
        report["status"] = "validating_checkpoint"
        started = time.perf_counter()
        report["checkpoint"] = checkpoint_identity(args.checkpoint)
        report["validation_ms"] = (time.perf_counter() - started) * 1000
        report["status"] = "initializing"
        persist()
        predict, runtime, initialization_ms = load_runner(args)
        report.update(runtime=runtime, initialization_ms=initialization_ms, status="warming_up")
        persist()
        started = time.perf_counter()
        raw = predict(model_request(cases[0]))
        report["warmup"] = {"case_id": cases[0]["case_id"], "end_to_end_predict_ms": (time.perf_counter() - started) * 1000, **raw}
        report["status"] = "running"
        persist()
        for case in cases:
            started = time.perf_counter()
            raw = predict(model_request(case))
            elapsed = (time.perf_counter() - started) * 1000
            evaluation = evaluate(case, raw)
            row = {"case_id": case["case_id"], "suite": case.get("suite"), "language": case.get("language"),
                   "category": case.get("category"), "request_sha256": fingerprint(model_request(case)),
                   "end_to_end_predict_ms": elapsed, "evaluation": evaluation, "memory_after": rss_sample(), **raw}
            report["results"].append(row)
            persist()
            print(json.dumps({"case_id": case["case_id"], "completed": len(report["results"]),
                              "predict_ms": elapsed, "correct": evaluation["correct"]}), flush=True)
        report["status"] = "complete"
    except BaseException as error:
        report["status"] = "failed"
        report["failure"] = {"type": type(error).__name__, "message": str(error), "traceback": traceback.format_exc()}
        raise
    finally:
        memory.stop()
        persist()
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--checkpoint", type=Path, required=True)
    parser.add_argument("--cases", type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--threads", type=int, default=4)
    parser.add_argument("--dtype", choices=("bfloat16", "float32"), default="bfloat16")
    parser.add_argument("--case-limit", type=int)
    parser.add_argument("--prepare-only", action="store_true")
    parser.add_argument("--download", action="store_true")
    parser.add_argument("--download-only", action="store_true")
    args = parser.parse_args()
    if args.threads < 1 or (args.case_limit is not None and args.case_limit < 1):
        parser.error("Thread and case counts must be positive")
    if args.download or args.download_only:
        report = download_checkpoint(args.checkpoint)
        print(json.dumps({"downloaded_files": len(report["files"]), "revision": REVISION}))
    if args.download_only:
        return
    if args.cases is None or args.output is None:
        parser.error("--cases and --output are required for inference/preparation")
    report = run(args)
    print(json.dumps({"status": report["status"], "cases": len(report["results"]), "summary": report.get("summary")}))


if __name__ == "__main__":
    main()
