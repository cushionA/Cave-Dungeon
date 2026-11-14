"""Append-only, resumable quality runs using the existing native CPU adapters.

This runner never scores against gold. Completed and explicitly failed requests
are both durable observations; missing requests remain incomplete, not failures
that can silently disappear from an analysis denominator.
"""
import argparse
import contextlib
import copy
import datetime
import hashlib
import importlib
import importlib.metadata
import json
import math
import os
from pathlib import Path
import platform
import resource
import sys
import time
from types import SimpleNamespace


SCHEMA_VERSION = 1


def encoded(value):
    return json.dumps(value, ensure_ascii=False, separators=(",", ":"), allow_nan=False).encode("utf-8")


def fingerprint(value):
    # Insertion order, including candidate order, is significant at this boundary.
    return hashlib.sha256(encoded(value)).hexdigest()


def file_sha256(path):
    digest = hashlib.sha256()
    with Path(path).open("rb") as source:
        for block in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def atomic_json(path, value):
    path = Path(path)
    temporary = path.with_suffix(path.suffix + ".partial")
    temporary.write_bytes(encoded(value) + b"\n")
    temporary.replace(path)


def utc_now():
    return datetime.datetime.now(datetime.timezone.utc).isoformat()


def _unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("Duplicate JSON object key: " + key)
        result[key] = value
    return result


def strict_json(text):
    def invalid_constant(value):
        raise ValueError("Nonfinite JSON constant: " + value)
    return json.loads(text, object_pairs_hook=_unique_object, parse_constant=invalid_constant)


def validate_cases(cases):
    if not isinstance(cases, list) or not cases:
        raise ValueError("Cases must be a nonempty list")
    seen = set()
    for case in cases:
        if (not isinstance(case, dict) or not isinstance(case.get("case_id"), str)
                or not case["case_id"].strip() or case["case_id"] in seen):
            raise ValueError("Case IDs must be distinct nonempty strings")
        seen.add(case["case_id"])
        request = case.get("request")
        if not isinstance(request, dict) or set(request) != {"state", "questions"}:
            raise ValueError("Native requests may contain only state and questions")
        questions = request["questions"]
        if not isinstance(questions, dict) or not questions:
            raise ValueError("Every request requires named questions")
        for name, question in questions.items():
            if (not isinstance(name, str) or not name or not isinstance(question, dict)
                    or not {"type", "instructions"} <= set(question)
                    or set(question) - {"type", "instructions", "criteria"}
                    or not isinstance(question["instructions"], str) or not question["instructions"]):
                raise ValueError("Question instructions/type/criteria contract changed")
            criteria = question.get("criteria", {})
            if question["type"] in ("noul", "boolean"):
                if not isinstance(criteria, dict) or set(criteria) - {"false", "true"}:
                    raise ValueError("Boolean criteria require native false/true keys")
            elif question["type"] == "choice":
                if (not isinstance(criteria, dict) or not 2 <= len(criteria) <= 255
                        or any(not isinstance(key, str) or not key for key in criteria)):
                    raise ValueError("Choices require 2..255 original candidate IDs")
            else:
                raise ValueError("Only boolean and choice decisions are supported")
    encoded(cases)  # Reject non-JSON/nonfinite input before loading any weights.
    return cases


def load_cases(path):
    value = strict_json(Path(path).read_text(encoding="utf-8"))
    return validate_cases(value["cases"] if isinstance(value, dict) else value)


def apply_affinity(specification):
    if specification is None:
        return sorted(os.sched_getaffinity(0)) if hasattr(os, "sched_getaffinity") else None
    if not hasattr(os, "sched_setaffinity"):
        raise ValueError("This host cannot apply an explicit CPU affinity")
    try:
        values = [int(value) for value in specification.split(",")]
    except (AttributeError, ValueError) as error:
        raise ValueError("Affinity must be comma-separated CPU numbers") from error
    if (not values or len(set(values)) != len(values) or min(values) < 0
            or not set(values) <= set(os.sched_getaffinity(0))):
        raise ValueError("Affinity CPUs must be distinct and available to this process")
    os.sched_setaffinity(0, set(values))
    return sorted(values)


def valid_predictions(case, predictions):
    questions = case["request"]["questions"]
    if not isinstance(predictions, dict) or set(predictions) != set(questions):
        raise ValueError("Predictions must cover exactly the native questions")
    for name, question in questions.items():
        value = predictions[name]
        if question["type"] in ("noul", "boolean"):
            if type(value) is not bool:
                raise ValueError("A boolean prediction must be an actual JSON boolean")
        elif not isinstance(value, str) or value not in question["criteria"]:
            raise ValueError("A choice prediction must be an original candidate ID")


class NativeAdapter:
    """Delegate inference and normalization to the previously reviewed adapters."""
    def __init__(self, backend, module=None):
        if backend not in {"hindsight", "tinyjev"}:
            raise ValueError("Unknown backend")
        self.backend = backend
        self.module = module or importlib.import_module(
            "compare_hindsight" if backend == "hindsight" else "compare_tinyjev_diagnostics")

    def describe(self):
        module = self.module
        paths = [Path(module.__file__), Path(__file__), Path(__file__).with_name("compare_intern_decision.py")]
        if self.backend == "tinyjev":
            paths += [Path(__file__).with_name("prepare.py"), Path(__file__).with_name("prepare_gguf.py")]
        packages = ("torch", "transformers", "tokenizers", "safetensors", "numpy", "pydantic", "huggingface-hub")
        if self.backend == "tinyjev":
            packages += ("tinyjev",)
        versions = {}
        for name in packages:
            try:
                versions[name] = importlib.metadata.version(name)
            except importlib.metadata.PackageNotFoundError:
                versions[name] = None
        return {"model_id": module.MODEL_ID,
                "revision": getattr(module, "REVISION", getattr(module, "MODEL_REVISION", None)),
                "source_files": {str(path.resolve()): file_sha256(path) for path in paths},
                "packages": versions}

    def verify_checkpoint(self, path):
        return (self.module.checkpoint_identity(path) if self.backend == "hindsight"
                else self.module.verified_checkpoint(path))

    def project(self, case):
        # Whitelist BEFORE invoking another helper: even a future helper cannot
        # accidentally obtain outer gold, audits, family labels or protocol data.
        clean = {"case_id": case["case_id"], "request": copy.deepcopy(case["request"])}
        return (self.module.model_request(clean) if self.backend == "hindsight"
                else self.module.model_payload(clean))

    def load(self, checkpoint, threads):
        if self.backend == "hindsight":
            return self.module.load_runner(SimpleNamespace(checkpoint=checkpoint, threads=threads, dtype="bfloat16"))
        return self.module.load_runner(checkpoint, threads)

    def normalize(self, case, raw):
        if self.backend == "tinyjev":
            clean = {"case_id": case["case_id"], "request": copy.deepcopy(case["request"])}
            rows = self.module.normalize_response(clean, raw)
            predictions = {name: row["prediction"] for name, row in rows.items()}
        else:
            details = raw.get("details") if isinstance(raw, dict) else None
            questions = case["request"]["questions"]
            if not isinstance(details, dict) or set(details) != set(questions):
                raise ValueError("Hindsight returned missing or unexpected questions")
            predictions = {}
            for name, question in questions.items():
                row = details[name]
                boolean = question["type"] in ("noul", "boolean")
                keys = ["false", "true"] if boolean else list(question["criteria"])
                probabilities = row.get("probabilities") if isinstance(row, dict) else None
                if (row.get("type") != ("noul" if boolean else "choice") or row.get("option_keys") != keys
                        or not isinstance(probabilities, dict) or set(probabilities) != set(keys)):
                    raise ValueError("Hindsight question/candidate contract changed")
                values = list(probabilities.values())
                if (any(type(value) not in (int, float) or not math.isfinite(value) or not 0 <= value <= 1 for value in values)
                        or not math.isclose(math.fsum(values), 1, abs_tol=1e-6, rel_tol=0)):
                    raise ValueError("Hindsight probabilities must be finite and normalized")
                prediction = probabilities["true"] >= 0.5 if boolean else max(keys, key=probabilities.__getitem__)
                if type(row.get("selected")) is not type(prediction) or row["selected"] != prediction:
                    raise ValueError("Native selection disagrees with the fixed probability rule")
                predictions[name] = prediction
        valid_predictions(case, predictions)
        return predictions


def json_safe(value):
    if isinstance(value, float) and not math.isfinite(value):
        return {"invalid_nonfinite_float": repr(value)}
    if isinstance(value, dict):
        return {str(key): json_safe(item) for key, item in value.items()}
    if isinstance(value, (tuple, list)):
        return [json_safe(item) for item in value]
    if value is None or type(value) in (str, bool, int, float):
        return value
    return {"invalid_native_type": type(value).__name__, "representation": repr(value)}


def infer_case(case, predict, adapter):
    native = None
    started = time.perf_counter()
    try:
        native = predict(adapter.project(case))
        predictions = adapter.normalize(case, native)
        valid_predictions(case, predictions)
        encoded(native)  # Nonfinite/unserializable replies are explicit failures.
        status, error = "complete", None
    except Exception as failure:
        status, predictions = "failed", {}
        error = {"type": type(failure).__name__, "message": str(failure)}
    return {"case_id": case["case_id"], "status": status, "predictions": predictions,
            "latency_ms": (time.perf_counter() - started) * 1000,
            "error": error, "native": json_safe(native)}


def seal_record(record):
    record = {key: value for key, value in record.items() if key != "record_sha256"}
    record["record_sha256"] = fingerprint(record)
    return record


def read_journal(path, cases):
    path = Path(path)
    if not path.exists():
        return []
    result = []
    with path.open("r", encoding="utf-8", newline="") as source:
        for index, line in enumerate(source):
            if not line.endswith("\n") or not line.strip():
                raise ValueError("Journal contains a partial or empty line; refusing implicit repair")
            try:
                row = strict_json(line)
            except (ValueError, TypeError) as error:
                raise ValueError("Journal contains invalid JSON") from error
            if index >= len(cases) or not isinstance(row, dict):
                raise ValueError("Journal contains extra or malformed records")
            if set(row) != {"case_id", "status", "predictions", "latency_ms", "error", "native",
                            "case_index", "request_sha256", "record_sha256"}:
                raise ValueError("Journal record schema changed")
            case = cases[index]
            if (row.get("case_index") != index or row.get("case_id") != case["case_id"]
                    or row.get("request_sha256") != fingerprint(case["request"])
                    or row.get("record_sha256") != seal_record(row)["record_sha256"]):
                raise ValueError("Journal is not an intact contiguous prefix of this dataset")
            if (row.get("status") not in {"complete", "failed"}
                    or type(row.get("latency_ms")) not in (int, float)
                    or not math.isfinite(row["latency_ms"]) or row["latency_ms"] < 0):
                raise ValueError("Journal has an invalid outcome or timing")
            if row["status"] == "complete":
                valid_predictions(case, row.get("predictions"))
                if row.get("error") is not None:
                    raise ValueError("Completed record contains an error")
            elif row.get("predictions") != {} or not isinstance(row.get("error"), dict):
                raise ValueError("Failed records must be explicit, without invented predictions")
            result.append(row)
    return result


@contextlib.contextmanager
def output_lock(directory):
    # POSIX kernel locks release on process death and do not strand a resume.
    import fcntl
    with (Path(directory) / ".run.lock").open("a") as lock:
        try:
            fcntl.flock(lock.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError as error:
            raise ValueError("Another process owns this output directory") from error
        try:
            yield
        finally:
            fcntl.flock(lock.fileno(), fcntl.LOCK_UN)


def memory_peak():
    value = resource.getrusage(resource.RUSAGE_SELF).ru_maxrss
    return {"lifetime_peak_rss_bytes": int(value if sys.platform == "darwin" else value * 1024),
            "scope": "Process-lifetime resource.ru_maxrss only; no growing sample history, model-only or VRAM claim."}


def run(args, adapter=None):
    if args.threads < 1:
        raise ValueError("Thread count must be positive")
    affinity = apply_affinity(args.affinity_cpus)
    cases = load_cases(args.cases)
    adapter = adapter or NativeAdapter(args.backend)
    output = Path(args.output)
    output.mkdir(parents=True, exist_ok=True)
    with output_lock(output):
        return _run_locked(args, cases, adapter, affinity)


def _run_locked(args, cases, adapter, affinity):
    output = Path(args.output)
    manifest_path, journal_path = output / "manifest.json", output / "results.ndjson"
    if not args.resume and any((output / name).exists() for name in
                               ("manifest.json", "results.ndjson", "progress.json", "result.json", "requests.ndjson")):
        raise FileExistsError("Output already contains a run; use --resume with identical inputs")
    if args.resume and not manifest_path.is_file():
        raise ValueError("Resume requires the original identity manifest")
    checkpoint = adapter.verify_checkpoint(Path(args.checkpoint))
    requests = [{"case_id": case["case_id"], "request": adapter.project(case)} for case in cases]
    protocol = Path(args.protocol) if args.protocol else None
    identity = {"schema_version": SCHEMA_VERSION, "backend": args.backend,
        "adapter": adapter.describe(), "runner_sha256": file_sha256(__file__),
        "cases_file_sha256": file_sha256(args.cases), "cases_sha256": fingerprint(cases),
        "case_order": [case["case_id"] for case in cases], "model_requests_sha256": fingerprint(requests),
        "checkpoint_directory": str(Path(args.checkpoint).resolve()), "checkpoint_sha256": fingerprint(checkpoint),
        "protocol_sha256": file_sha256(protocol) if protocol else None,
        "threads": args.threads, "affinity_cpus": affinity, "python": platform.python_version(),
        "python_executable": str(Path(sys.executable).resolve()),
        "fixed_rules": {"noul_true_threshold": 0.5, "one_warmup_per_process": True,
                        "gold_scoring_in_runner": False, "failed_outcomes_retained": True}}
    requests_path = output / "requests.ndjson"
    if args.resume:
        manifest = strict_json(manifest_path.read_text(encoding="utf-8"))
        if manifest.get("identity") != identity:
            raise ValueError("Resume identity differs: dataset, protocol, source, model, environment or CPU settings changed")
        if (not requests_path.is_file()
                or manifest.get("requests_file_sha256") != file_sha256(requests_path)):
            raise ValueError("Saved native requests were changed or lost")
        records = read_journal(journal_path, cases)
    else:
        requests_path.write_bytes(b"".join(encoded(row) + b"\n" for row in requests))
        manifest = {"schema_version": SCHEMA_VERSION, "created_utc": utc_now(), "identity": identity,
                    "verified_checkpoint": checkpoint, "requests_file_sha256": file_sha256(requests_path),
                    "sessions": [], "quality_only": True,
                    "latency_scope": "Request projection, native call and normalization. Journal/progress I/O excluded. Runs may share CPU resources; these wall times are not a controlled latency comparison."}
        atomic_json(manifest_path, manifest)
        records = []

    active_session = None

    def progress(status, failure=None):
        current_memory = memory_peak()
        if active_session is not None:
            active_session["memory"] = current_memory
            atomic_json(manifest_path, manifest)
        peaks = [session.get("memory", {}).get("lifetime_peak_rss_bytes", 0)
                 for session in manifest["sessions"]]
        value = {"schema_version": SCHEMA_VERSION, "status": status, "updated_utc": utc_now(),
                 "requested_case_count": len(cases), "completed_case_count": len(records),
                 "failed_case_count": sum(row["status"] == "failed" for row in records),
                 "execution_completed": len(records) == len(cases), "failure": failure,
                 "memory": {"lifetime_peak_rss_bytes": max([current_memory["lifetime_peak_rss_bytes"], *peaks]),
                            "scope": "Maximum recorded process-lifetime resource.ru_maxrss across run/resume processes; no growing sample history, model-only or VRAM claim."},
                 "identity_sha256": fingerprint(identity)}
        atomic_json(output / "progress.json", value)
        return value

    if len(records) < len(cases):
        progress("initializing")
        try:
            predict, runtime, initialization_ms = adapter.load(Path(args.checkpoint), args.threads)
            if manifest["sessions"] and runtime != manifest["sessions"][0]["runtime"]:
                raise ValueError("Loaded runtime differs from the original run")
            session = {"started_utc": utc_now(), "resume_from": len(records), "runtime": runtime,
                       "initialization_ms": initialization_ms}
            manifest["sessions"].append(session)
            active_session = session
            atomic_json(manifest_path, manifest)
            progress("warming_up")
            session["warmup"] = infer_case(cases[len(records)], predict, adapter)
            atomic_json(manifest_path, manifest)
            progress("running")
            last_progress = time.monotonic()
            with journal_path.open("a", encoding="utf-8", buffering=1) as journal:
                for index in range(len(records), len(cases)):
                    case = cases[index]
                    row = infer_case(case, predict, adapter)
                    row.update(case_index=index, request_sha256=fingerprint(case["request"]))
                    row = seal_record(row)
                    journal.write(encoded(row).decode("utf-8") + "\n")
                    journal.flush()
                    records.append(row)
                    if len(records) % 10 == 0 or time.monotonic() - last_progress >= 30:
                        progress("running")
                        last_progress = time.monotonic()
                        print(json.dumps({"completed": len(records), "total": len(cases),
                                          "failed": sum(item["status"] == "failed" for item in records)}), flush=True)
        except BaseException as error:
            progress("interrupted", {"type": type(error).__name__, "message": str(error)})
            raise
    # Re-read the durable observations before declaring coverage complete.
    records = read_journal(journal_path, cases)
    status = "complete" if len(records) == len(cases) else "incomplete"
    report = {**progress(status), "manifest_sha256": file_sha256(manifest_path),
              "journal_sha256": file_sha256(journal_path), "identity": identity,
              "sessions": manifest["sessions"], "results": records}
    atomic_json(output / "result.json", report)
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--backend", choices=("hindsight", "tinyjev"), required=True)
    parser.add_argument("--cases", type=Path, required=True)
    parser.add_argument("--checkpoint", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--threads", type=int, default=1)
    parser.add_argument("--affinity-cpus", help="Comma-separated CPUs from this process's allowed set")
    parser.add_argument("--protocol", type=Path)
    parser.add_argument("--resume", action="store_true")
    args = parser.parse_args()
    report = run(args)
    print(json.dumps({key: report[key] for key in
                      ("status", "completed_case_count", "requested_case_count", "failed_case_count")}), flush=True)


if __name__ == "__main__":
    main()
