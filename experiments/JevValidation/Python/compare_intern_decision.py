"""Run the preserved game questions through native Intern-Decision or TinyJev.

This measures uncalibrated choice distributions, response controls, CPU time and
process memory. The questions have no gold strategy or gameplay outcome labels.
"""

import argparse
from collections import Counter
import datetime
import hashlib
import importlib.metadata
import json
import math
from pathlib import Path
import platform
import resource
import statistics
import subprocess
import sys
import threading
import time
import traceback


def json_bytes(value):
    # Dict insertion order is part of the input: changing it changes A/B/... .
    return (json.dumps(value, ensure_ascii=False, indent=2, allow_nan=False) + "\n").encode("utf-8")


def fingerprint(value):
    return hashlib.sha256(json_bytes(value)).hexdigest()


def file_sha256(path):
    digest = hashlib.sha256()
    with Path(path).open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def atomic_json(path, value):
    path = Path(path)
    temporary = path.with_suffix(path.suffix + ".partial")
    temporary.write_bytes(json_bytes(value))
    temporary.replace(path)


def build_suite(include_actions=False):
    from prepare_strategies import STRATEGIES, _request, catalog_fingerprint, strategy_cases

    cases = []
    for specification in strategy_cases():
        request = _request(specification)
        cases.append({"case_id": specification["case_id"], "question_id": "strategy",
                      "kind": "strategy", "metadata": specification, "request": request,
                      "request_sha256": fingerprint(request)})
    if include_actions:
        from prepare import _request as action_request, load_states

        for case_id, state in load_states():
            request = action_request(case_id, state)
            cases.append({"case_id": case_id, "question_id": "action", "kind": "action",
                          "metadata": {}, "request": request, "request_sha256": fingerprint(request)})
    if len({case["case_id"] for case in cases}) != len(cases):
        raise ValueError("Case IDs must be unique")
    for case in cases:
        validate_request(case)
    return {"schema_version": 1, "strategy_catalog": dict(STRATEGIES),
            "strategy_catalog_sha256": catalog_fingerprint(STRATEGIES),
            "inputs_sha256": fingerprint([case["request"] for case in cases]),
            "case_count": len(cases), "cases": cases,
            "scope": "Preserved input questions only; no gold labels, targets, padding or truncation."}


def validate_request(case):
    request = case["request"]
    if set(request) != {"id", "state", "questions"} or request["id"] != case["case_id"]:
        raise ValueError("Request must contain only its ID, original state and questions")
    if not isinstance(request["state"], str) or not request["state"].strip():
        raise ValueError("State must be nonempty text")
    question_id = case["question_id"]
    if set(request["questions"]) != {question_id}:
        raise ValueError("Exactly the original single question is required")
    question = request["questions"][question_id]
    if set(question) != {"type", "instructions", "criteria"} or question["type"] != "choice":
        raise ValueError("Question may contain only choice type, instructions and criteria")
    criteria = question["criteria"]
    if not isinstance(criteria, dict) or len(criteria) < 2 or any(
            not isinstance(key, str) or not key or not isinstance(value, str) or not value
            for key, value in criteria.items()):
        raise ValueError("Choice criteria must be an ordered mapping of IDs to descriptions")


def validate_probabilities(probabilities, choice_ids, selected):
    if not isinstance(probabilities, dict) or set(probabilities) != set(choice_ids):
        raise ValueError("Probabilities must contain every original candidate ID exactly once")
    values = list(probabilities.values())
    if any(isinstance(value, bool) or not isinstance(value, (int, float)) or
           not math.isfinite(value) or not 0 <= value <= 1 for value in values):
        raise ValueError("Probabilities must be finite numbers in [0, 1]")
    if not math.isclose(math.fsum(values), 1.0, rel_tol=0, abs_tol=1e-5):
        raise ValueError("Probabilities must sum to one; the adapter does not renormalize outputs")
    if selected not in probabilities or probabilities[selected] != max(values):
        raise ValueError("Returned choice must be an original candidate with maximal probability")
    return {key: probabilities[key] for key in choice_ids}


def validate_intern_result(case, raw):
    question_id = case["question_id"]
    if not isinstance(raw, dict) or set(raw.get("answers", {})) != {question_id}:
        raise ValueError("Intern returned unexpected or missing question IDs")
    answer = raw["answers"][question_id]
    if answer.get("type") != "choice":
        raise ValueError("Intern must return a choice answer")
    selected = answer.get("choice")
    choice_ids = list(case["request"]["questions"][question_id]["criteria"])
    probabilities = validate_probabilities(answer.get("probabilities"), choice_ids, selected)
    tokens = raw.get("usage", {}).get("input_tokens")
    inference_ms = raw.get("timing", {}).get("inference_ms")
    if isinstance(tokens, bool) or not isinstance(tokens, int) or tokens <= 0:
        raise ValueError("Intern must report a positive actual input token count")
    if isinstance(inference_ms, bool) or not isinstance(inference_ms, (int, float)) or not math.isfinite(inference_ms) or inference_ms < 0:
        raise ValueError("Intern must report finite nonnegative upstream model inference time")
    return {"selected_strategy" if case["kind"] == "strategy" else "selected_action": selected,
            "selected_id": selected, "candidate_ids": choice_ids, "probabilities": probabilities,
            "input_tokens": tokens, "upstream_inference_ms": inference_ms}


def validate_tinyjev_result(case, raw):
    key = case["case_id"] + ":" + case["question_id"]
    if not isinstance(raw, dict) or set(raw) != {key}:
        raise ValueError("TinyJev returned unexpected or missing state/question IDs")
    result = raw[key]
    choice_ids = list(case["request"]["questions"][case["question_id"]]["criteria"])
    values = result.get("probabilities")
    if result.get("candidate_ids") != choice_ids or not isinstance(values, list) or len(values) != len(choice_ids):
        raise ValueError("TinyJev must preserve every original candidate and its order")
    selected = result.get("answer", {}).get("choice")
    probabilities = validate_probabilities(dict(zip(choice_ids, values)), choice_ids, selected)
    answer = result.get("answer", {})
    if answer.get("type") != "choice" or answer.get("probabilities") != probabilities:
        raise ValueError("TinyJev raw probabilities and answer probabilities disagree")
    counts = result.get("path_token_counts")
    if not isinstance(counts, list) or len(counts) != 1 or isinstance(counts[0], bool) or not isinstance(counts[0], int) or counts[0] <= 0:
        raise ValueError("TinyJev must report one positive actual causal-row token count")
    return {"selected_strategy" if case["kind"] == "strategy" else "selected_action": selected,
            "selected_id": selected, "candidate_ids": choice_ids, "probabilities": probabilities,
            "input_tokens": counts[0], "upstream_inference_ms": None}


def distribution_delta(first, second):
    if set(first) != set(second):
        raise ValueError("Distribution controls must have identical candidate identities")
    differences = [abs(first[key] - second[key]) for key in first]
    return {"id_aligned_total_variation": math.fsum(differences) / 2,
            "id_aligned_max_probability_difference": max(differences)}


def summarize(results):
    strategy = [row for row in results if row["kind"] == "strategy"]
    by_id = {row["case_id"]: row for row in strategy}
    original = [row for row in strategy if row["metadata"]["current_strategy"] == "lure_ambush"
                and "rotated" not in row["case_id"]]
    kept = sum(row["selected_id"] == row["metadata"]["current_strategy"] for row in original)
    summary = {"completed_cases": len(results), "selected_strategy_counts": dict(Counter(row["selected_id"] for row in strategy)),
               "original_nine": {"completed_cases": len(original), "kept_current_strategy": kept,
                                 "selected_strategy_counts": dict(Counter(row["selected_id"] for row in original))},
               "no_current_grid": [], "current_strategy_controls": [], "personality_goal_current_controls": [],
               "order_control": None,
               "interpretation": "Selection and sensitivity measurements only; no ground truth, accuracy or game win-rate measurement."}
    for row in strategy:
        metadata = row["metadata"]
        if metadata["current_strategy"] == "none" and metadata["world_id"] == "opening":
            summary["no_current_grid"].append({"case_id": row["case_id"], "personality": metadata["personality_id"],
                                              "goal": metadata["goal_id"], "selected_id": row["selected_id"],
                                              "probabilities": row["probabilities"]})
    base_id = "strategy_cunning_protect_ally"
    base = by_id.get(base_id)
    if base:
        for current in ("direct_assault", "observe", "none"):
            control = by_id.get(base_id + "_current_" + current)
            if control:
                summary["current_strategy_controls"].append({"case_id": control["case_id"], "current_strategy": current,
                    "base_selected_id": base["selected_id"], "selected_id": control["selected_id"],
                    "kept_current_strategy": control["selected_id"] == current,
                    **distribution_delta(base["probabilities"], control["probabilities"])})
        rotated = by_id.get(base_id + "_rotated4")
        if rotated:
            summary["order_control"] = {"case_ids": [base_id, rotated["case_id"]],
                "selected_ids": [base["selected_id"], rotated["selected_id"]],
                "same_selected_id": base["selected_id"] == rotated["selected_id"],
                **distribution_delta(base["probabilities"], rotated["probabilities"])}
    for row in original:
        if row["metadata"]["world_id"] != "opening":
            continue
        suffix = "_current_none"
        control = by_id.get(row["case_id"] + suffix)
        if control:
            summary["personality_goal_current_controls"].append({"case_ids": [row["case_id"], control["case_id"]],
                "selected_ids": [row["selected_id"], control["selected_id"]],
                **distribution_delta(row["probabilities"], control["probabilities"])})
    timings = [row["end_to_end_predict_ms"] for row in results]
    model_times = [row["upstream_inference_ms"] for row in results if row["upstream_inference_ms"] is not None]
    summary["timing"] = {"timed_requests": len(timings),
        "total_end_to_end_predict_ms": math.fsum(timings),
        "mean_end_to_end_predict_ms": statistics.mean(timings) if timings else None,
        "median_end_to_end_predict_ms": statistics.median(timings) if timings else None,
        "mean_upstream_inference_ms": statistics.mean(model_times) if model_times else None}
    return summary


def rss_sample():
    try:
        status = Path("/proc/self/status").read_text()
        rss = next(int(line.split()[1]) * 1024 for line in status.splitlines() if line.startswith("VmRSS:"))
    except (OSError, StopIteration, ValueError):
        rss = None
    maximum = resource.getrusage(resource.RUSAGE_SELF).ru_maxrss
    return {"rss_bytes": rss, "lifetime_peak_rss_bytes": int(maximum if sys.platform == "darwin" else maximum * 1024)}


class MemorySampler:
    def __init__(self, interval=0.1):
        self.interval = interval
        self.started = time.perf_counter()
        self.samples = []
        self.lock = threading.Lock()
        self.stopped = threading.Event()
        self.thread = threading.Thread(target=self._run, daemon=True)

    def sample(self):
        row = {"elapsed_seconds": time.perf_counter() - self.started, **rss_sample()}
        with self.lock:
            self.samples.append(row)

    def _run(self):
        while not self.stopped.wait(self.interval):
            self.sample()

    def start(self):
        self.sample()
        self.thread.start()

    def snapshot(self):
        with self.lock:
            samples = list(self.samples)
        return {"scope": "This Python process CPU RSS; sampled RSS plus process-lifetime resource.ru_maxrss, not VRAM.",
                "sample_interval_seconds": self.interval,
                "peak_sampled_rss_bytes": max((row["rss_bytes"] or 0) for row in samples),
                "lifetime_peak_rss_bytes": rss_sample()["lifetime_peak_rss_bytes"], "samples": samples}

    def stop(self):
        self.stopped.set()
        self.thread.join()
        self.sample()


def source_identity(source):
    source = Path(source).resolve()
    result = subprocess.run(["git", "-C", str(source), "rev-parse", "HEAD"], check=True, capture_output=True, text=True)
    files = ["src/inference/engine.py", "src/inference/hf_backend.py", "src/inputs/schema.py"]
    return {"repository": "https://github.com/InternLM/Intern-Decision", "directory": str(source),
            "git_commit": result.stdout.strip(),
            "inference_source_sha256": {name: file_sha256(source / name) for name in files}}


def checkpoint_identity(checkpoint, manifest=None):
    checkpoint = Path(checkpoint).resolve()
    if not checkpoint.is_dir():
        raise FileNotFoundError(checkpoint)
    if manifest is None:
        for name in ("download-provenance.json", "download-manifest.json"):
            if (checkpoint / name).is_file():
                manifest = checkpoint / name
                break
    record = {"directory": str(checkpoint), "files": {}}
    if manifest:
        manifest = Path(manifest).resolve()
        record["download_manifest"] = {"path": str(manifest), "sha256": file_sha256(manifest),
                                       "content": json.loads(manifest.read_text(encoding="utf-8"))}
    # Hash the actual checkpoint bytes, independently of the download manifest.
    for path in sorted(checkpoint.iterdir()):
        if path.is_file() and path.suffix in {".json", ".safetensors", ".bin", ".model", ".jinja", ".txt"}:
            record["files"][path.name] = {"bytes": path.stat().st_size, "sha256": file_sha256(path)}
    if not record["files"]:
        raise ValueError("Checkpoint contains no model or tokenizer files")
    manifest_files = record.get("download_manifest", {}).get("content", {}).get("files", {})
    if isinstance(manifest_files, list):
        manifest_files = {entry["file"]: entry for entry in manifest_files}
    for name, expected in manifest_files.items():
        if "sha256" in expected and (name not in record["files"] or
                                       expected["sha256"] != record["files"][name]["sha256"]):
            raise ValueError("Checkpoint bytes disagree with download provenance: " + name)
    record["files_sha256"] = fingerprint(record["files"])
    return record


def load_runner(args):
    import torch

    torch.set_num_threads(args.threads)
    torch.set_num_interop_threads(1)
    torch.manual_seed(0)
    packages = {}
    for package in ("torch", "transformers", "numpy", "safetensors", "tinyjev"):
        try:
            packages[package] = importlib.metadata.version(package)
        except importlib.metadata.PackageNotFoundError:
            pass
    runtime = {"python": platform.python_version(), "platform": platform.platform(), "packages": packages,
               "device": "cpu", "intra_op_threads": torch.get_num_threads(),
               "inter_op_threads": torch.get_num_interop_threads(), "seed": 0}
    started = time.perf_counter()
    if args.backend == "intern":
        if args.source_repo is None:
            raise ValueError("--source-repo is required for the native Intern backend")
        sys.path.insert(0, str(args.source_repo.resolve()))
        from src.inference.engine import DecisionEngine

        engine = DecisionEngine(str(args.checkpoint.resolve()), backend="hf", device="cpu", dtype=args.dtype,
                                attn_implementation="sdpa", max_length=args.max_length, calibration_path=None)
        predict, validate = engine.predict, validate_intern_result
        model = engine.backend.model
        runtime.update({"backend": "upstream DecisionEngine/HFBackend", "requested_dtype": args.dtype,
                        "temperature": engine.temperature, "calibration_path": engine.calibration_path,
                        "attention": "sdpa", "max_length": args.max_length,
                        "upstream_timing_scope": "HFBackend.score model forward plus batch device transfer; excludes encoding and probability decoding.",
                        "end_to_end_timing_scope": "DecisionEngine.predict: native encode, model forward and answer decoding."})
    else:
        import tinyjev

        agent = tinyjev.load(str(args.checkpoint.resolve()), backend="torch", device="cpu")
        predict, validate = agent.logits, validate_tinyjev_result
        model = agent.backbone.model
        runtime.update({"backend": "tinyjev.logits/torch", "requested_dtype": "upstream default on CPU",
                        "temperature": 1.0, "calibration_path": None,
                        "upstream_timing_scope": None,
                        "end_to_end_timing_scope": "Agent.logits: normalize request, native encode, backbone, pointer head and answer decoding; includes encoding."})
    elapsed = (time.perf_counter() - started) * 1000
    runtime["actual_model_dtype"] = str(next(model.parameters()).dtype)
    runtime["model_parameter_count"] = sum(parameter.numel() for parameter in model.parameters())
    return predict, validate, runtime, elapsed


def run(args):
    args.output.mkdir(parents=True, exist_ok=True)
    suite = build_suite(args.include_actions)
    atomic_json(args.output / "requests.json", suite)
    cases = suite["cases"][:args.case_limit] if args.case_limit is not None else suite["cases"]
    report = {"schema_version": 1, "status": "prepared", "backend": args.backend,
              "created_utc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
              "model_id": args.model_id, "revision": args.revision,
              "adapter_sha256": file_sha256(__file__),
              "inputs_sha256": suite["inputs_sha256"], "requests_file_sha256": file_sha256(args.output / "requests.json"),
              "strategy_catalog_sha256": suite["strategy_catalog_sha256"],
              "strategy_catalog": suite["strategy_catalog"], "available_case_count": len(suite["cases"]),
              "requested_case_count": len(cases), "case_limit": args.case_limit, "results": [],
              "timed_inputs_sha256": fingerprint([case["request"] for case in cases]),
              "notes": ["No gold labels: selection sensitivity only, not policy accuracy or game win rate.",
                        "Raw temperature 1; released XTuner calibration presets are not used for the HF backend.",
                        "Inputs use native tokenizer and templates without padding/truncation or a generation loop.",
                        "Model timing scopes differ; use end-to-end predict time for whole-request comparison."]}
    path = args.output / "result.json"
    atomic_json(path, report)
    if args.prepare_only:
        return report
    sampler = MemorySampler()
    sampler.start()

    def persist():
        report["summary"] = summarize(report["results"])
        report["memory"] = sampler.snapshot()
        atomic_json(path, report)

    try:
        report["status"] = "initializing"
        report["checkpoint"] = checkpoint_identity(args.checkpoint, args.checkpoint_manifest)
        provenance = report["checkpoint"].get("download_manifest", {}).get("content", {})
        for key, expected in (("model_id", args.model_id), ("revision", args.revision)):
            if key in provenance and provenance[key] != expected:
                raise ValueError("Checkpoint download provenance disagrees with requested " + key)
        if args.backend == "intern":
            report["source"] = source_identity(args.source_repo)
        persist()
        predict, validate, runtime, initialization_ms = load_runner(args)
        report.update(runtime=runtime, initialization_ms=initialization_ms, status="warming_up")
        persist()
        started = time.perf_counter()
        raw = predict(cases[0]["request"])
        warmup_ms = (time.perf_counter() - started) * 1000
        warmup = validate(cases[0], raw)
        report["warmup"] = {"requests": 1, "case_id": cases[0]["case_id"], "end_to_end_predict_ms": warmup_ms,
                            **warmup, "raw_response": raw}
        report["status"] = "running"
        persist()
        for case in cases:
            started = time.perf_counter()
            raw = predict(case["request"])
            elapsed = (time.perf_counter() - started) * 1000
            normalized = validate(case, raw)
            report["results"].append({"case_id": case["case_id"], "question_id": case["question_id"],
                "kind": case["kind"], "metadata": case["metadata"], "request_sha256": case["request_sha256"],
                "end_to_end_predict_ms": elapsed, "memory_after_request": rss_sample(),
                **normalized, "raw_response": raw})
            persist()
            print(json.dumps({"completed": len(report["results"]), "total": len(cases), "case_id": case["case_id"],
                              "selected_id": normalized["selected_id"], "input_tokens": normalized["input_tokens"],
                              "end_to_end_predict_ms": round(elapsed, 2)}), flush=True)
        report["status"] = "complete"
    except BaseException as error:
        report["status"] = "failed"
        report["failure"] = {"type": type(error).__name__, "message": str(error), "traceback": traceback.format_exc()}
        raise
    finally:
        sampler.stop()
        persist()
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--backend", choices=("intern", "tinyjev"), default="intern")
    parser.add_argument("--source-repo", type=Path)
    parser.add_argument("--checkpoint", type=Path)
    parser.add_argument("--checkpoint-manifest", type=Path)
    parser.add_argument("--model-id")
    parser.add_argument("--revision")
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--threads", type=int, default=4)
    parser.add_argument("--dtype", choices=("float32", "bfloat16"), default="bfloat16")
    parser.add_argument("--max-length", type=int, default=8192)
    parser.add_argument("--case-limit", type=int)
    parser.add_argument("--include-actions", action="store_true", help="Append the six original four-choice action questions")
    parser.add_argument("--prepare-only", action="store_true", help="Export exact requests without loading any model")
    args = parser.parse_args()
    if args.threads <= 0 or args.max_length <= 0 or (args.case_limit is not None and args.case_limit <= 0):
        parser.error("threads, max-length and case-limit must be positive")
    if not args.prepare_only and (args.checkpoint is None or not args.model_id or not args.revision):
        parser.error("--checkpoint, --model-id and pinned --revision are required for inference")
    if not args.prepare_only and args.backend == "intern" and args.source_repo is None:
        parser.error("--source-repo is required for the native Intern backend")
    report = run(args)
    print(json.dumps({"status": report["status"], "result_file": str(args.output / "result.json"),
                      "summary": report.get("summary")}, indent=2), flush=True)


if __name__ == "__main__":
    main()
