"""Durable exploratory candidate runs; never amend the registered comparison.

Servers and their thread/context/cache settings are managed externally. The
model SHA supplied by the caller identifies that externally verified artifact;
this HTTP client cannot independently attest the server's loaded weights.
"""
import argparse
import contextlib
import copy
import datetime
import hashlib
import importlib
import json
import math
import os
from pathlib import Path
import platform
import signal
import statistics
import sys
import threading
import time
import traceback
from urllib.parse import urlsplit


SCHEMA_VERSION = 1
ADAPTERS = {"unee": "compare_unee_candidates", "chat": "compare_llamacpp_candidates"}
WARMUP_REQUEST = {
    "state": "The indicator is blue.",
    "questions": {"warmup": {"type": "choice", "instructions": "What color is the indicator?",
                              "criteria": {"blue": "Blue", "red": "Red"}}},
}


def encoded(value):
    return json.dumps(value, ensure_ascii=False, separators=(",", ":"), allow_nan=False).encode("utf-8")


def digest(value):
    return hashlib.sha256(encoded(value)).hexdigest()


def file_descriptor(path):
    path = Path(path).resolve()
    data = path.read_bytes()
    return {"path": str(path), "bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()}


def utc_now():
    return datetime.datetime.now(datetime.timezone.utc).isoformat()


def strict_json(text):
    def pairs(items):
        result = {}
        for key, value in items:
            if key in result:
                raise ValueError("Duplicate JSON key: " + key)
            result[key] = value
        return result

    def nonfinite(value):
        raise ValueError("Nonfinite JSON constant: " + value)
    return json.loads(text, object_pairs_hook=pairs, parse_constant=nonfinite)


def safe_raw(value):
    """Retain invalid replies diagnostically without accepting invalid numbers."""
    if isinstance(value, float) and not math.isfinite(value):
        return {"invalid_nonfinite": repr(value)}
    if isinstance(value, dict):
        return {str(key): safe_raw(item) for key, item in value.items()}
    if isinstance(value, (list, tuple)):
        return [safe_raw(item) for item in value]
    if value is None or type(value) in (str, bool, int, float):
        return value
    return {"invalid_type": type(value).__name__, "representation": repr(value)}


def atomic_json(path, value):
    path = Path(path)
    temporary = path.with_suffix(path.suffix + ".partial")
    with temporary.open("wb") as target:
        target.write(encoded(value) + b"\n")
        target.flush()
        os.fsync(target.fileno())
    os.replace(temporary, path)


@contextlib.contextmanager
def output_lock(directory):
    import fcntl
    with (directory / ".run.lock").open("a+b") as lock:
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError as error:
            raise RuntimeError("Another runner owns this output directory") from error
        try:
            yield
        finally:
            fcntl.flock(lock, fcntl.LOCK_UN)


@contextlib.contextmanager
def deadline(seconds):
    if not hasattr(signal, "setitimer") or threading.current_thread() is not threading.main_thread():
        raise RuntimeError("Strict wall deadlines require a POSIX main-thread runner")
    old_handler = signal.getsignal(signal.SIGALRM)
    old_timer = signal.getitimer(signal.ITIMER_REAL)
    if old_timer[0] or old_timer[1]:
        raise RuntimeError("An existing process alarm would conflict with the request deadline")

    def expire(_signum, _frame):
        raise TimeoutError("Adapter exceeded the fixed wall deadline")
    signal.signal(signal.SIGALRM, expire)
    signal.setitimer(signal.ITIMER_REAL, seconds)
    try:
        yield
    finally:
        signal.setitimer(signal.ITIMER_REAL, 0)
        signal.signal(signal.SIGALRM, old_handler)


def valid_predictions(request, predictions):
    questions = request["questions"]
    if not isinstance(predictions, dict) or set(predictions) != set(questions):
        raise ValueError("Predictions must cover exactly the requested questions")
    for name, question in questions.items():
        value = predictions[name]
        if question["type"] in {"noul", "boolean"}:
            if type(value) is not bool:
                raise ValueError("Boolean predictions must be JSON booleans")
        elif type(value) is not str or value not in question["criteria"]:
            raise ValueError("Choice prediction is not an original candidate ID")


def predictions_match(predictions, expected):
    return set(predictions) == set(expected) and all(
        type(predictions[key]) is type(value) and predictions[key] == value for key, value in expected.items())


def load_cases(path):
    value = strict_json(Path(path).read_text(encoding="utf-8"))
    cases = value.get("cases") if isinstance(value, dict) else value
    if not isinstance(cases, list) or not cases:
        raise ValueError("Cases must be a nonempty list")
    seen = set()
    for case in cases:
        if not isinstance(case, dict) or type(case.get("case_id")) is not str or not case["case_id"]:
            raise ValueError("Every case needs a nonempty case_id")
        if case["case_id"] in seen:
            raise ValueError("Duplicate case ID")
        seen.add(case["case_id"])
        request = case.get("request")
        if not isinstance(request, dict) or set(request) != {"state", "questions"}:
            raise ValueError("Only state/questions may reach the adapter")
        questions = request["questions"]
        if not isinstance(questions, dict) or not questions:
            raise ValueError("Questions must be a nonempty object")
        for name, question in questions.items():
            if (type(name) is not str or not name or not isinstance(question, dict)
                    or set(question) - {"type", "instructions", "criteria"}
                    or type(question.get("instructions")) is not str or not question["instructions"]):
                raise ValueError("Question instructions/schema changed")
            criteria = question.get("criteria", {})
            if question.get("type") in {"noul", "boolean"}:
                if not isinstance(criteria, dict) or set(criteria) - {"false", "true"}:
                    raise ValueError("Boolean criteria may contain false/true only")
            elif question.get("type") == "choice":
                if (not isinstance(criteria, dict) or not 2 <= len(criteria) <= 255
                        or any(type(key) is not str or not key for key in criteria)):
                    raise ValueError("Choices need original distinct candidate IDs")
            else:
                raise ValueError("Only boolean and choice questions are supported")
        valid_predictions(request, case.get("expected"))
    encoded(cases)
    return cases


def token_counts(raw):
    details = raw.get("details", {}) if isinstance(raw, dict) else {}
    calls = details.get("calls") if isinstance(details, dict) else None
    calls = calls if isinstance(calls, list) and calls else [details]
    totals = []
    for field, usage_field in (("input_tokens", "prompt_tokens"), ("output_tokens", "completion_tokens")):
        values = []
        for call in calls:
            if not isinstance(call, dict):
                values.append(None)
                continue
            usage = call.get("usage")
            if not isinstance(usage, dict):
                envelope = call.get("raw", {})
                usage = envelope.get("usage", {}) if isinstance(envelope, dict) else {}
            number = call.get(field)
            if number is None and isinstance(usage, dict):
                number = usage.get(usage_field)
            if number is not None and (type(number) is not int or number < 0):
                raise ValueError("Reported token counts must be nonnegative integers or null")
            values.append(number)
        totals.append(sum(values) if all(number is not None for number in values) else None)
    return tuple(totals)


def call_adapter(adapter, server_url, request, timeout_seconds):
    start = time.perf_counter()
    raw, predictions, error = None, {}, None
    inputs, outputs = None, None
    try:
        with deadline(timeout_seconds):
            raw = adapter.predict(server_url, copy.deepcopy(request), timeout_seconds=timeout_seconds)
            if time.perf_counter() - start > timeout_seconds:
                raise TimeoutError("Adapter returned after the fixed wall deadline")
            if not isinstance(raw, dict) or "details" not in raw:
                raise ValueError("Adapter must return predictions and full raw details")
            valid_predictions(request, raw.get("predictions"))
            encoded(raw)
            inputs, outputs = token_counts(raw)
            predictions = copy.deepcopy(raw["predictions"])
    except Exception as exception:
        if raw is None and getattr(exception, "details", None) is not None:
            raw = {"adapter_exception_details": safe_raw(exception.details)}
        error = {"type": type(exception).__name__, "message": str(exception),
                 "traceback": traceback.format_exc()}
    return {"status": "failed" if error else "complete", "predictions": predictions,
            "duration_ms": (time.perf_counter() - start) * 1000,
            "input_tokens": inputs, "output_tokens": outputs, "error": error, "raw": safe_raw(raw)}


def seal_record(record):
    return {**record, "record_sha256": digest(record)}


def read_journal(path, cases):
    if not path.exists():
        return []
    data = path.read_bytes()
    if data and not data.endswith(b"\n"):
        raise ValueError("Incomplete journal tail; refusing to silently truncate an observation")
    rows = []
    for line in data.splitlines():
        if not line:
            raise ValueError("Blank journal record")
        row = strict_json(line)
        if not isinstance(row, dict) or len(rows) >= len(cases):
            raise ValueError("Foreign or excess journal record")
        checksum = row.get("record_sha256")
        plain = {key: value for key, value in row.items() if key != "record_sha256"}
        if checksum != digest(plain):
            raise ValueError("Journal record SHA mismatch")
        case, index = cases[len(rows)], len(rows)
        if (row.get("case_index") != index or row.get("case_id") != case["case_id"]
                or row.get("request_sha256") != digest(case["request"])
                or encoded(row.get("expected")) != encoded(case["expected"])):
            raise ValueError("Journal does not preserve the selected case order, request and gold")
        if row.get("status") == "complete":
            valid_predictions(case["request"], row.get("predictions"))
            expected_correct = predictions_match(row["predictions"], case["expected"])
            if row.get("error") is not None or type(row.get("correct")) is not bool or row["correct"] != expected_correct:
                raise ValueError("Completed journal outcome contradicts its predictions")
        elif row.get("status") == "failed":
            if row.get("predictions") != {} or row.get("correct") is not False or not isinstance(row.get("error"), dict):
                raise ValueError("Failed observations must remain failed and incorrect")
        else:
            raise ValueError("Unknown journal status")
        if type(row.get("duration_ms")) not in (int, float) or not math.isfinite(row["duration_ms"]) or row["duration_ms"] < 0:
            raise ValueError("Invalid journal timing")
        for field in ("input_tokens", "output_tokens"):
            if row.get(field) is not None and (type(row[field]) is not int or row[field] < 0):
                raise ValueError("Invalid journal token count")
        rows.append(row)
    return rows


def summarize(rows, cases):
    def group(selected):
        completed = sum(row["status"] == "complete" for row in selected)
        good = sum(row["correct"] for row in selected)
        known = {field: [row[field] for row in selected if row[field] is not None]
                 for field in ("input_tokens", "output_tokens")}
        return {"count": len(selected), "correct": good, "failed": len(selected) - completed,
                "exact_accuracy": good / len(selected) if selected else None,
                "mean_duration_ms": statistics.mean(row["duration_ms"] for row in selected) if selected else None,
                "token_counts": {field: {"known_rows": len(values), "total": sum(values)}
                                 for field, values in known.items()}}
    result = {"overall": group(rows)}
    for field in ("suite", "language"):
        keys = sorted({str(case.get(field, "unspecified")) for case in cases})
        result["by_" + field] = {key: group([row for row in rows if str(row.get(field, "unspecified")) == key])
                                 for key in keys}
    return result


def run(args, adapter=None):
    if args.adapter not in ADAPTERS or type(args.model_id) is not str or not args.model_id:
        raise ValueError("Unknown adapter or empty model ID")
    if (type(args.model_sha256) is not str or len(args.model_sha256) != 64
            or any(char not in "0123456789abcdefABCDEF" for char in args.model_sha256)):
        raise ValueError("Model artifact SHA-256 must contain 64 hexadecimal characters")
    if (type(args.timeout_seconds) not in (int, float) or not math.isfinite(args.timeout_seconds)
            or args.timeout_seconds <= 0):
        raise ValueError("A finite positive timeout is required")
    endpoint = urlsplit(args.server_url)
    if endpoint.scheme not in {"http", "https"} or not endpoint.hostname or endpoint.username or endpoint.password or endpoint.query or endpoint.fragment:
        raise ValueError("Server URL must be an HTTP(S) endpoint without embedded credentials")
    cases = load_cases(args.cases)
    total_cases = len(cases)
    if args.limit is not None:
        if type(args.limit) is not int or not 1 <= args.limit <= total_cases:
            raise ValueError("Limit must select a positive prefix of this corpus")
        cases = cases[:args.limit]
    adapter = adapter or importlib.import_module(ADAPTERS[args.adapter])
    adapter_path = getattr(adapter, "__file__", None)
    if not adapter_path or not callable(getattr(adapter, "describe", None)):
        raise ValueError("Adapter needs a source file and a describe() provenance function")
    identity = {"schema_version": SCHEMA_VERSION, "adapter": args.adapter,
                "adapter_source": file_descriptor(adapter_path), "adapter_description": adapter.describe(),
                "runner_source": file_descriptor(__file__), "model_id": args.model_id,
                "model_sha256": args.model_sha256.lower(),
                "model_verification": "Caller-verified external server artifact; not independently attested over HTTP",
                "server_url": args.server_url.rstrip("/"), "cases_source": file_descriptor(args.cases),
                "case_order": [case["case_id"] for case in cases], "total_corpus_cases": total_cases,
                "limit": args.limit, "timeout_seconds": args.timeout_seconds,
                "requests_sha256": digest([case["request"] for case in cases]),
                "gold_sha256": digest([case["expected"] for case in cases]),
                "warmup_request_sha256": digest(WARMUP_REQUEST),
                "protocol": file_descriptor(args.protocol) if args.protocol else None,
                "python": platform.python_version(), "python_executable": str(Path(sys.executable).resolve()),
                "execution": "One request at a time; server startup/CPU threads/context/cache controlled externally",
                "scope": "Exploratory reuse of published synthetic cases; no registered primary result is amended"}
    encoded(identity)
    output = Path(args.output_dir)
    output.mkdir(parents=True, exist_ok=True)
    with output_lock(output):
        return _run_locked(args, adapter, cases, identity, output)


def _run_locked(args, adapter, cases, identity, output):
    manifest_path, journal_path = output / "manifest.json", output / "results.ndjson"
    requests_path, progress_path, result_path = output / "requests.ndjson", output / "progress.json", output / "result.json"
    if not args.resume:
        if any(path.exists() for path in (manifest_path, journal_path, requests_path, progress_path, result_path)):
            raise FileExistsError("Existing run; --resume requires identical model, sources, protocol and selected cases")
        with requests_path.open("wb") as target:
            for case in cases:
                target.write(encoded({"case_id": case["case_id"], "request": case["request"]}) + b"\n")
            target.flush()
            os.fsync(target.fileno())
        manifest = {"schema_version": SCHEMA_VERSION, "created_utc": utc_now(), "identity": identity,
                    "requests_file": file_descriptor(requests_path), "sessions": []}
        atomic_json(manifest_path, manifest)
        rows = []
    else:
        manifest = strict_json(manifest_path.read_text(encoding="utf-8"))
        if manifest.get("identity") != identity or manifest.get("requests_file") != file_descriptor(requests_path):
            raise ValueError("Resume identity or saved model requests changed")
        rows = read_journal(journal_path, cases)
        if progress_path.exists() and strict_json(progress_path.read_text())["completed_cases"] > len(rows):
            raise ValueError("Journal lost previously durable observations")
        if result_path.exists():
            previous = strict_json(result_path.read_text())
            if previous.get("status") == "complete" and previous.get("journal_sha256") != file_descriptor(journal_path)["sha256"]:
                raise ValueError("Completed journal was changed")

    def report(status, error=None):
        value = {"schema_version": SCHEMA_VERSION, "created_utc": utc_now(), "status": status,
                 "identity": identity, "completed_cases": len(rows), "planned_cases": len(cases),
                 "summary": summarize(rows, cases), "error": error}
        atomic_json(progress_path, value)
        return value

    try:
        if len(rows) < len(cases):
            session = {"started_utc": utc_now(), "resume_from": len(rows), "warmup_request": WARMUP_REQUEST}
            manifest["sessions"].append(session)
            atomic_json(manifest_path, manifest)
            report("warming_up")
            session["warmup"] = call_adapter(adapter, identity["server_url"], WARMUP_REQUEST, args.timeout_seconds)
            atomic_json(manifest_path, manifest)
            report("running")
            last_log = time.monotonic()
            with journal_path.open("a", encoding="utf-8") as journal:
                for index in range(len(rows), len(cases)):
                    case = cases[index]
                    observation = call_adapter(adapter, identity["server_url"], case["request"], args.timeout_seconds)
                    correct = observation["status"] == "complete" and predictions_match(observation["predictions"], case["expected"])
                    row = seal_record({"schema_version": SCHEMA_VERSION, "case_index": index,
                                       "case_id": case["case_id"], "suite": case.get("suite", case.get("task", "unspecified")),
                                       "language": case.get("language", "unspecified"), "expected": copy.deepcopy(case["expected"]),
                                       "correct": correct, **observation, "request_sha256": digest(case["request"])})
                    journal.write(encoded(row).decode("utf-8") + "\n")
                    journal.flush()
                    os.fsync(journal.fileno())
                    rows.append(row)
                    value = report("running")
                    if len(rows) % 10 == 0 or len(rows) == len(cases) or time.monotonic() - last_log >= 30:
                        print(json.dumps({"completed": len(rows), "total": len(cases),
                                          "failed": value["summary"]["overall"]["failed"]}), flush=True)
                        last_log = time.monotonic()
    except BaseException as exception:
        report("interrupted", {"type": type(exception).__name__, "message": str(exception)})
        raise
    rows = read_journal(journal_path, cases)
    value = report("complete" if len(rows) == len(cases) else "incomplete")
    value.update(manifest_sha256=file_descriptor(manifest_path)["sha256"],
                 journal_sha256=file_descriptor(journal_path)["sha256"], sessions=manifest["sessions"], results=rows)
    atomic_json(result_path, value)
    return value


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adapter", choices=sorted(ADAPTERS), required=True)
    parser.add_argument("--server-url", required=True)
    parser.add_argument("--cases", type=Path, required=True)
    parser.add_argument("--model-id", required=True)
    parser.add_argument("--model-sha256", required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--protocol", type=Path)
    parser.add_argument("--timeout-seconds", type=float, default=300.0)
    parser.add_argument("--limit", type=int)
    parser.add_argument("--resume", action="store_true")
    args = parser.parse_args()
    result = run(args)
    print(json.dumps({"status": result["status"], "completed_cases": result["completed_cases"],
                      "summary": result["summary"]["overall"]}, ensure_ascii=False), flush=True)


if __name__ == "__main__":
    main()
