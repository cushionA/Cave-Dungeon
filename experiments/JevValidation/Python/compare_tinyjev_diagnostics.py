"""Score frozen labeled decision requests with the official TinyJev CPU backend.

Expected answers and diagnostic metadata are kept outside the model request.
Inference uses agent.logits at its native temperature without calibration fits.
"""

import argparse
import copy
import datetime
import importlib.metadata
import json
import math
from pathlib import Path
import platform
import statistics
import traceback
import time

from compare_intern_decision import (MemorySampler, atomic_json, checkpoint_identity,
                                     file_sha256, fingerprint, rss_sample)
from prepare import MODEL_ID, MODEL_REVISION


def validate_cases(cases):
    if not isinstance(cases, list) or not cases:
        raise ValueError("--cases must contain a nonempty JSON list")
    seen = set()
    for case in cases:
        if not isinstance(case, dict) or not {"case_id", "request", "language", "category"} <= set(case):
            raise ValueError("Each case requires case_id, request, language and category")
        case_id = case["case_id"]
        if not isinstance(case_id, str) or not case_id.strip() or case_id in seen:
            raise ValueError("Case IDs must be unique nonempty strings")
        seen.add(case_id)
        if any(not isinstance(case[key], str) or not case[key].strip() for key in ("language", "category")):
            raise ValueError("Language and category must be nonempty strings")
        request = case["request"]
        if not isinstance(request, dict) or set(request) != {"state", "questions"}:
            raise ValueError("Model requests must contain only state and questions; the adapter adds bookkeeping IDs")
        questions = request["questions"]
        if not isinstance(questions, dict) or not questions:
            raise ValueError("Each request requires at least one question")
        for question_id, question in questions.items():
            if not isinstance(question_id, str) or not question_id.strip() or not isinstance(question, dict):
                raise ValueError("Question IDs must be nonempty strings mapping to objects")
            if not {"type", "instructions"} <= set(question) or set(question) - {"type", "instructions", "criteria"}:
                raise ValueError("Questions may contain only type, instructions and criteria")
            if not isinstance(question["instructions"], str) or not question["instructions"].strip():
                raise ValueError("Question instructions must be nonempty text")
            kind, criteria = question["type"], question.get("criteria", {})
            if kind in ("noul", "boolean"):
                if not isinstance(criteria, dict) or set(criteria) - {"false", "true"}:
                    raise ValueError("TinyJev boolean criteria must use only false/true keys")
            elif kind == "choice":
                if not isinstance(criteria, dict) or not 1 <= len(criteria) <= 255 or any(
                        not isinstance(key, str) or not key for key in criteria):
                    raise ValueError("Choice criteria must map 1..255 nonempty candidate IDs to descriptions")
            else:
                raise ValueError("Diagnostics support only native noul/boolean and choice questions")
        if "expected" in case:
            expected = case["expected"]
            if not isinstance(expected, dict) or set(expected) - set(questions):
                raise ValueError("Expected answers must map existing question IDs to boolean or choice labels")
            for question_id, label in expected.items():
                question = questions[question_id]
                if question["type"] in ("noul", "boolean"):
                    if type(label) is not bool:
                        raise ValueError("Expected noul/boolean answers must be JSON booleans")
                elif not isinstance(label, str) or label not in question["criteria"]:
                    raise ValueError("Expected choice labels must be original candidate IDs")
    # Reject non-JSON or nonfinite fixture values before launching a model.
    fingerprint(cases)
    return cases


def model_payload(case):
    # Whitelist the original request; no expected/category/language/rationale.
    payload = copy.deepcopy(case["request"])
    payload["id"] = case["case_id"]
    return payload


def normalize_response(case, raw):
    questions = case["request"]["questions"]
    expected_keys = {case["case_id"] + ":" + question_id for question_id in questions}
    if not isinstance(raw, dict) or set(raw) != expected_keys:
        raise ValueError("TinyJev returned missing or inconsistent state/question IDs")
    predictions = {}
    for question_id, question in questions.items():
        native = raw[case["case_id"] + ":" + question_id]
        if not isinstance(native, dict):
            raise ValueError("TinyJev question results must be objects")
        boolean = question["type"] in ("noul", "boolean")
        choice_ids = ["false", "true"] if boolean else list(question["criteria"])
        if native.get("candidate_ids") != choice_ids:
            raise ValueError("TinyJev candidate IDs/order differ from the original request")
        if native.get("type") != ("boolean" if boolean else "choice"):
            raise ValueError("TinyJev returned an inconsistent question type")
        values = native.get("probabilities")
        if not isinstance(values, list) or len(values) != len(choice_ids) or any(
                isinstance(value, bool) or not isinstance(value, (int, float)) or
                not math.isfinite(value) or not 0 <= value <= 1 for value in values):
            raise ValueError("TinyJev probabilities must be finite numbers for every original candidate")
        if not math.isclose(math.fsum(values), 1.0, rel_tol=0, abs_tol=1e-5):
            raise ValueError("Native probabilities must sum to one; adapter renormalization is forbidden")
        counts = native.get("path_token_counts")
        if not isinstance(counts, list) or len(counts) != 1 or type(counts[0]) is not int or counts[0] <= 0:
            raise ValueError("Every question must report one positive actual causal-row token count")
        probabilities = dict(zip(choice_ids, values))
        if boolean:
            prediction = probabilities["true"] >= 0.5
        else:
            # Native choice ties resolve to the first candidate in request order.
            prediction = max(choice_ids, key=probabilities.__getitem__)
        predictions[question_id] = {"type": "noul" if boolean else "choice", "prediction": prediction,
            "candidate_ids": choice_ids, "probabilities": probabilities, "input_tokens": counts[0]}
        if boolean:
            predictions[question_id]["p_true"] = probabilities["true"]
    return predictions


def score_predictions(case, predictions=None, failure=None):
    expected = case.get("expected", {})
    scored = {}
    for question_id, question in case["request"]["questions"].items():
        row = copy.deepcopy((predictions or {}).get(question_id, {
            "type": "noul" if question["type"] in ("noul", "boolean") else "choice", "prediction": None}))
        row["labeled"] = question_id in expected
        row["correct"] = None
        if question_id in expected:
            row["expected"] = expected[question_id]
            row["correct"] = bool(failure is None and type(row["prediction"]) is type(expected[question_id])
                                  and row["prediction"] == expected[question_id])
        if failure is not None:
            row["failure"] = failure
        scored[question_id] = row
    labeled = [row for row in scored.values() if row["labeled"]]
    return {"questions": scored, "correct": all(row["correct"] for row in labeled) if labeled else None}


def aggregate_rows(rows):
    questions = [question for row in rows for question in row["questions"].values()]
    labeled = [question for question in questions if question["labeled"]]
    labeled_cases = [row for row in rows if row["correct"] is not None]
    correct_questions = sum(question["correct"] for question in labeled)
    correct_cases = sum(row["correct"] for row in labeled_cases)
    boolean = {"true_positive": 0, "true_negative": 0, "false_positive": 0, "false_negative": 0,
               "invalid_expected_true": 0, "invalid_expected_false": 0}
    choices = {}
    for question in labeled:
        expected, prediction = question["expected"], question["prediction"]
        if question["type"] == "noul":
            if type(prediction) is not bool:
                key = "invalid_expected_true" if expected else "invalid_expected_false"
            elif expected:
                key = "true_positive" if prediction else "false_negative"
            else:
                key = "false_positive" if prediction else "true_negative"
            boolean[key] += 1
        else:
            key = (expected, prediction)
            choices[key] = choices.get(key, 0) + 1
    times = [row["end_to_end_logits_ms"] for row in rows if row.get("end_to_end_logits_ms") is not None]
    success_times = [row["end_to_end_logits_ms"] for row in rows if row["status"] == "complete"]
    return {"case_count": len(rows), "failed_cases": sum(row["status"] == "failed" for row in rows),
            "labeled_cases": len(labeled_cases), "correct_cases": correct_cases,
            "case_accuracy": correct_cases / len(labeled_cases) if labeled_cases else None,
            "labeled_questions": len(labeled), "correct_questions": correct_questions,
            "question_accuracy": correct_questions / len(labeled) if labeled else None,
            "unlabeled_questions": len(questions) - len(labeled), "boolean_confusion": boolean,
            "choice_confusion": [{"expected": expected, "prediction": prediction, "count": count}
                                 for (expected, prediction), count in choices.items()],
            "timing": {"timed_requests": len(times), "total_end_to_end_logits_ms": math.fsum(times),
                       "mean_end_to_end_logits_ms": statistics.mean(times) if times else None,
                       "median_end_to_end_logits_ms": statistics.median(times) if times else None,
                       "mean_successful_end_to_end_logits_ms": statistics.mean(success_times) if success_times else None}}


def summarize(rows):
    return {"overall": aggregate_rows(rows),
            "by_language": {language: aggregate_rows([row for row in rows if row["language"] == language])
                            for language in dict.fromkeys(row["language"] for row in rows)},
            "by_category": {category: aggregate_rows([row for row in rows if row["category"] == category])
                            for category in dict.fromkeys(row["category"] for row in rows)},
            "failure_scoring": "Each labeled question in a failed request counts incorrect; invalid predictions are reported separately in confusion counts.",
            "sampling_note": "Translated pairs are related examples, not independent samples; no significance claim or calibration fit."}


def verified_checkpoint(checkpoint):
    from prepare_gguf import PINNED_SOURCE_SHA256

    record = checkpoint_identity(checkpoint)
    for name, expected in PINNED_SOURCE_SHA256.items():
        if record["files"].get(name, {}).get("sha256") != expected:
            raise ValueError("Checkpoint differs from the frozen TinyJev revision: " + name)
    return record


def load_runner(checkpoint, threads):
    import torch
    import tinyjev

    torch.set_num_threads(threads)
    torch.set_num_interop_threads(1)
    torch.manual_seed(0)
    packages = {}
    for name in ("torch", "transformers", "tinyjev", "numpy", "safetensors"):
        packages[name] = importlib.metadata.version(name)
    package_root = Path(tinyjev.__file__).parent
    runtime = {"python": platform.python_version(), "platform": platform.platform(), "packages": packages,
               "backend": "official tinyjev.Agent.logits/torch", "device": "cpu", "temperature_argument": 1.0,
               "intra_op_threads": torch.get_num_threads(), "inter_op_threads": torch.get_num_interop_threads(),
               "seed": 0, "source_sha256": {name: file_sha256(package_root / name) for name in
                   ("agent.py", "families/pointer.py", "backends/torch_backend.py")}}
    started = time.perf_counter()
    agent = tinyjev.load(str(checkpoint.resolve()), backend="torch", device="cpu")
    initialization_ms = (time.perf_counter() - started) * 1000
    runtime["actual_model_dtype"] = str(next(agent.backbone.model.parameters()).dtype)
    runtime["model_parameter_count"] = sum(parameter.numel() for parameter in agent.backbone.model.parameters())
    runtime["pointer_head_temperature"] = agent.family.temperature
    if runtime["actual_model_dtype"] != "torch.float32" or agent.family.name != "pointer":
        raise ValueError("The frozen baseline must use the official FP32 CPU pointer model")
    return agent.logits, runtime, initialization_ms


def evaluate_case(case, predict):
    raw = None
    payload = model_payload(case)
    started = time.perf_counter()
    elapsed = None
    try:
        raw = predict(payload)
        elapsed = (time.perf_counter() - started) * 1000
        predictions = normalize_response(case, raw)
        score = score_predictions(case, predictions)
        status, failure = "complete", None
    except Exception as error:
        if elapsed is None:
            elapsed = (time.perf_counter() - started) * 1000
        failure = {"type": type(error).__name__, "message": str(error)}
        score = score_predictions(case, failure=failure)
        status = "failed"
    return {"case_id": case["case_id"], "language": case["language"], "category": case["category"],
            "pair_id": case.get("pair_id"), "case_sha256": fingerprint(case),
            "request_sha256": fingerprint(case["request"]), "status": status,
            "end_to_end_logits_ms": elapsed, "failure": failure, **score, "raw_response": serializable_raw(raw),
            "memory_after_request": rss_sample()}


def serializable_raw(value):
    """Keep invalid native outputs inspectable without breaking incremental JSON."""
    if isinstance(value, float) and not math.isfinite(value):
        return {"invalid_nonfinite_float": repr(value)}
    if isinstance(value, dict):
        return {key: serializable_raw(item) for key, item in value.items()}
    if isinstance(value, (list, tuple)):
        return [serializable_raw(item) for item in value]
    if value is None or isinstance(value, (str, bool, int, float)):
        return value
    return {"invalid_output_type": type(value).__name__, "representation": repr(value)}


def run(args):
    cases = validate_cases(json.loads(args.cases.read_text(encoding="utf-8")))
    args.output.mkdir(parents=True, exist_ok=True)
    requests = [{"case_id": case["case_id"], "request": copy.deepcopy(case["request"])} for case in cases]
    atomic_json(args.output / "requests.json", requests)
    report = {"schema_version": 1, "experiment": "Frozen TinyJev decision diagnostics", "status": "prepared",
              "created_utc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
              "model_id": MODEL_ID, "revision": MODEL_REVISION, "adapter_sha256": file_sha256(__file__),
              "shared_helpers_sha256": file_sha256(Path(__file__).with_name("compare_intern_decision.py")),
              "cases_path": str(args.cases.resolve()), "cases_file_sha256": file_sha256(args.cases),
              "cases_sha256": fingerprint(cases), "requests_sha256": fingerprint([case["request"] for case in cases]),
              "requests_file_sha256": file_sha256(args.output / "requests.json"), "requested_case_count": len(cases),
              "timing_scope": "Official Agent.logits call, including request normalization, native tokenization, backbone, pointer head and answer decoding; excludes adapter validation/scoring, checkpoint hashes, model initialization and one warmup.",
              "notes": ["Expected answers and case metadata never enter the model request.",
                        "Native request fields, tokenizer, candidate descriptions and order remain unchanged.",
                        "Boolean predictions use the raw true probability >= 0.5; choice ties use original candidate order.",
                        "No calibration fitting, probability renormalization, prompt trimming or game integration."],
              "results": []}
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
        report["checkpoint"] = verified_checkpoint(args.checkpoint)
        persist()
        predict, runtime, initialization_ms = load_runner(args.checkpoint, args.threads)
        report.update(runtime=runtime, initialization_ms=initialization_ms, status="warming_up")
        persist()
        report["warmup"] = {"requests": 1, **evaluate_case(cases[0], predict)}
        report["status"] = "running"
        persist()
        for case in cases:
            row = evaluate_case(case, predict)
            report["results"].append(row)
            persist()
            print(json.dumps({"completed": len(report["results"]), "total": len(cases), "case_id": row["case_id"],
                              "status": row["status"], "correct": row["correct"],
                              "end_to_end_logits_ms": round(row["end_to_end_logits_ms"], 2)}), flush=True)
        report["status"] = "complete"
        report["execution_completed"] = True
    except BaseException as error:
        report["status"] = "failed"
        report["execution_completed"] = False
        report["failure"] = {"type": type(error).__name__, "message": str(error), "traceback": traceback.format_exc()}
        raise
    finally:
        sampler.stop()
        persist()
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cases", type=Path, required=True, help="Frozen JSON list; expected answers are optional")
    parser.add_argument("--checkpoint", type=Path, default=Path(__file__).parent / "Artifacts/split-source/checkpoint")
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--threads", type=int, default=4)
    parser.add_argument("--prepare-only", action="store_true")
    args = parser.parse_args()
    if args.threads <= 0:
        parser.error("--threads must be positive")
    report = run(args)
    print(json.dumps({"status": report["status"], "result_file": str(args.output / "result.json"),
                      "summary": report.get("summary")}, ensure_ascii=False, indent=2), flush=True)


if __name__ == "__main__":
    main()
