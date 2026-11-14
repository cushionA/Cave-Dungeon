"""Measure the official Needle 3 runtime on a frozen command-to-tool suite.

Only the frozen prompt reaches the model. Tools are schemas, never Python
functions, and complete() never executes an action. Suppressed native calls
remain suppressed. Exact command matching and safe abstention are separate.
"""
import argparse
import datetime
import hashlib
import importlib.metadata
import json
import math
import os
from pathlib import Path
import platform
import threading
import time


MODEL_ID = "Cactus-Compute/needle3"
MODEL_REVISION = "c95d25b4efa1e9601e785d92438e4ade0dbddbf8"
MODEL_SHA256 = "c9d915eca282ed42d1a09b143b592adb4cc6744ffe2d294adf5cfc5548170c38"
RUNTIME_REVISION = "1a028f5a3972b67dd8d8752f882113d475e7ec4c"
RUNTIME_WHEEL_SHA256 = "6b6f1c75acf8eb35c465bfd1f845351448c33804933c806d3406e2be35a40e01"
SDK_WHEEL_SHA256 = "ae70355e8508cf25ea65a61839eeebc17cff81b3930398bbfa605a58fb445bb9"


def sha256(path):
    digest = hashlib.sha256()
    with Path(path).open("rb") as stream:
        while chunk := stream.read(1024 * 1024):
            digest.update(chunk)
    return digest.hexdigest()


def canonical(value):
    return json.dumps(value, sort_keys=True, ensure_ascii=False, separators=(",", ":"), allow_nan=False)


def prepare_tools(tools):
    if not isinstance(tools, list) or not tools:
        raise ValueError("A nonempty frozen tool catalog is required")
    prepared = []
    names = set()
    for wrapper in tools:
        tool = wrapper.get("function") if wrapper.get("type") == "function" else wrapper
        if not isinstance(tool, dict) or not isinstance(tool.get("name"), str) or not tool["name"] or tool["name"] in names:
            raise ValueError("Tool names must be unique nonempty strings")
        parameters = tool.get("parameters")
        if not isinstance(parameters, dict) or parameters.get("type") != "object" or not isinstance(parameters.get("properties", {}), dict):
            raise ValueError("Tools require an object parameters schema")
        if not isinstance(parameters.get("required", []), list) or any(key not in parameters.get("properties", {}) for key in parameters.get("required", [])):
            raise ValueError("Required tool arguments must have property schemas")
        names.add(tool["name"])
        prepared.append(json.loads(canonical(tool)))
    return prepared


def load_suite(path):
    suite = json.loads(Path(path).read_text())
    if "system" in suite and not isinstance(suite["system"], str):
        raise ValueError("Optional system facts/policy must be frozen text")
    tools = prepare_tools(suite.get("tool_schemas"))
    cases = suite.get("cases")
    if not isinstance(cases, list) or not cases:
        raise ValueError("The frozen suite must contain cases")
    seen = set()
    for case in cases:
        if not isinstance(case.get("case_id"), str) or not case["case_id"] or case["case_id"] in seen:
            raise ValueError("Frozen case IDs must be unique")
        seen.add(case["case_id"])
        if not isinstance(case.get("prompt"), str) or not case["prompt"]:
            raise ValueError("Each case requires its exact model prompt")
        expected = case.get("expected_calls")
        if not isinstance(expected, list) or len(expected) > 1:
            raise ValueError("This experiment requires at most one action per case")
        if validate_calls(expected, tools):
            raise ValueError("Expected calls must satisfy the same frozen tool schema")
    return {**suite, "tool_schemas": tools}


def create_agent(cactus_module, suite, weights):
    arguments = {"tools": suite["tool_schemas"], "weights": Path(weights).resolve(),
                 "stateless": True, "auto_date": False}
    if "system" in suite:
        arguments["system"] = suite["system"]
    return cactus_module.Cactus(**arguments)


def validate_value(value, schema):
    kind = schema.get("type")
    valid = {"string": isinstance(value, str),
             "integer": type(value) is int,
             "number": type(value) in (int, float) and math.isfinite(value),
             "boolean": type(value) is bool,
             "object": isinstance(value, dict),
             "array": isinstance(value, list),
             "null": value is None}
    if kind is not None and (kind not in valid or not valid[kind]):
        return False
    if "enum" in schema and canonical(value) not in [canonical(item) for item in schema["enum"]]:
        return False
    if "const" in schema and canonical(value) != canonical(schema["const"]):
        return False
    if kind == "object":
        properties = schema.get("properties", {})
        if any(key not in value for key in schema.get("required", [])):
            return False
        # A game action can only accept declared fields; extra hallucinated
        # arguments are blocked even if a loose upstream schema permits them.
        if any(key not in properties for key in value):
            return False
        return all(validate_value(item, properties[key]) for key, item in value.items())
    if kind == "array" and "items" in schema:
        return all(validate_value(item, schema["items"]) for item in value)
    return True


def validate_calls(calls, tools):
    errors = []
    if not isinstance(calls, list):
        return ["function_calls must be an array"]
    if len(calls) > 1:
        errors.append("multiple calls violate the frozen single-action contract")
    catalog = {tool["name"]: tool for tool in tools}
    for index, call in enumerate(calls):
        if not isinstance(call, dict) or call.get("name") not in catalog:
            errors.append(f"call {index} has an unknown tool")
        elif not isinstance(call.get("arguments"), dict) or not validate_value(call["arguments"], catalog[call["name"]]["parameters"]):
            errors.append(f"call {index} arguments violate the frozen schema")
    return errors


def assess_response(raw, case, tools):
    if not isinstance(raw, dict):
        raw = {"success": False, "error": "Response is not an object", "function_calls": []}
    calls = raw.get("function_calls", [])
    errors = validate_calls(calls, tools)
    validation = raw.get("validation", {})
    if not isinstance(validation, dict):
        errors.append("validation must be an object")
        validation = {}
    blocked = bool(validation.get("ungrounded") or validation.get("negation"))
    response_valid = raw.get("success") is True and raw.get("error") is None and not errors
    accepted = calls if response_valid and not blocked else []
    # Native confidence/grounding suppression is authoritative. The runner does
    # not promote suppressed_calls or change the shipped confidence threshold.
    matched = response_valid and canonical(accepted) == canonical(case["expected_calls"])
    expected_clarification = canonical(case["expected_calls"]) == canonical([{"name": "clarify", "arguments": {}}])
    safe = response_valid and (matched or (expected_clarification and not accepted))
    return {"response_valid": response_valid, "call_validation_errors": errors,
            "blocked_by_validation": blocked, "accepted_calls": accepted,
            "raw_abstained": isinstance(calls, list) and not calls,
            "abstained": not accepted,
            "issued_game_action": any(call["name"] != "clarify" for call in accepted),
            "explicit_clarification": accepted == [{"name": "clarify", "arguments": {}}],
            "expected_clarification": expected_clarification,
            "exact_match": matched, "safe_handling": safe}


def summarize(rows):
    def group(items):
        issued = [row for row in items if row["issued_game_action"]]
        clarify = [row for row in items if row["expected_clarification"]]
        return {"count": len(items), "exact_matches": sum(row["exact_match"] for row in items),
                "valid_response_count": sum(row["response_valid"] for row in items),
                "accepted_call_count": sum(bool(row["accepted_calls"]) for row in items),
                "issued_game_action_count": len(issued),
                "abstained_count": sum(row["abstained"] for row in items),
                "explicit_clarification_count": sum(row["explicit_clarification"] for row in items),
                "conditional_exact_accuracy_among_issued_actions": sum(row["exact_match"] for row in issued) / len(issued) if issued else None,
                "clarification_expected_count": len(clarify),
                "clarification_safely_handled_count": sum(row["safe_handling"] for row in clarify),
                "mean_end_to_end_ms": sum(row["elapsed_ms"] for row in items) / len(items) if items else None}
    return {"overall": group(rows), "by_language": {language: group([row for row in rows if row["language"] == language]) for language in sorted({row["language"] for row in rows})}}


def save(report, output):
    Path(output).parent.mkdir(parents=True, exist_ok=True)
    temporary = Path(output).with_suffix(".json.tmp")
    temporary.write_text(json.dumps(report, ensure_ascii=False, indent=2, allow_nan=False) + "\n")
    temporary.replace(output)


def run_cases(model, suite, report, output, max_new_tokens=128):
    """One separate warmup, then one timed call per frozen case, incremental save."""
    try:
        first = suite["cases"][0]
        started = time.perf_counter()
        raw = model.complete(first["prompt"], max_new_tokens=max_new_tokens)
        report["warmup"] = {"case_id": first["case_id"], "elapsed_ms": (time.perf_counter() - started) * 1000, "raw": raw}
        save(report, output)
        for case in suite["cases"]:
            started = time.perf_counter()
            raw = model.complete(case["prompt"], max_new_tokens=max_new_tokens)
            elapsed = (time.perf_counter() - started) * 1000
            assessed = assess_response(raw, case, suite["tool_schemas"])
            row = {"case_id": case["case_id"], "language": case.get("language", "unspecified"),
                   "category": case.get("category"), "prompt_sha256": hashlib.sha256(case["prompt"].encode()).hexdigest(),
                   "expected_calls": case["expected_calls"], "elapsed_ms": elapsed, **assessed, "raw": raw}
            report["results"].append(row)
            report["summary"] = summarize(report["results"])
            save(report, output)
            print(json.dumps({key: row[key] for key in ("case_id", "elapsed_ms", "exact_match", "abstained", "accepted_calls")}, ensure_ascii=False), flush=True)
        report["status"] = "complete"
    except Exception as error:
        report["status"] = "failed"
        report["error"] = f"{type(error).__name__}: {error}"
    save(report, output)
    return report


class MemorySampler:
    """Separate parent and native-worker observations; no VRAM claims."""
    def __init__(self, interval=0.1):
        import psutil
        self.process = psutil.Process()
        self.interval = interval
        self.done = threading.Event()
        self.rows = []
        self.started = time.perf_counter()
        self.thread = threading.Thread(target=self._loop, daemon=True)

    def _sample(self):
        import psutil
        rows = []
        for process in [self.process] + self.process.children(recursive=True):
            try:
                rows.append({"pid": process.pid, "rss_bytes": process.memory_info().rss,
                             "threads": process.num_threads(), "cpu_seconds": sum(process.cpu_times()[:2])})
            except psutil.Error:
                pass
        self.rows.append({"elapsed_seconds": time.perf_counter() - self.started, "processes": rows})

    def _loop(self):
        while not self.done.is_set():
            self._sample()
            self.done.wait(self.interval)

    def start(self):
        self._sample()
        self.thread.start()

    def stop(self):
        self.done.set()
        self.thread.join()
        self._sample()
        parent = self.process.pid
        return {"interval_seconds": self.interval,
                "scope": "Sampled parent and native worker RSS separately. Summing processes can double-count shared pages; not VRAM or model-only allocation.",
                "peak_parent_rss_bytes": max((row["rss_bytes"] for sample in self.rows for row in sample["processes"] if row["pid"] == parent), default=0),
                "peak_native_worker_rss_bytes": max((row["rss_bytes"] for sample in self.rows for row in sample["processes"] if row["pid"] != parent), default=0),
                "max_native_worker_threads": max((row["threads"] for sample in self.rows for row in sample["processes"] if row["pid"] != parent), default=0),
                "samples": self.rows}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--suite", type=Path, required=True)
    parser.add_argument("--weights", type=Path, required=True)
    parser.add_argument("--runtime-library", type=Path, required=True)
    parser.add_argument("--runtime-wheel", type=Path, required=True)
    parser.add_argument("--sdk-wheel", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--affinity-cores", type=int, default=4)
    parser.add_argument("--max-new-tokens", type=int, default=128)
    parser.add_argument("--case-limit", type=int)
    args = parser.parse_args()
    if args.affinity_cores < 1 or args.max_new_tokens < 1 or (args.case_limit is not None and args.case_limit < 1):
        parser.error("Core, token and case limits must be positive")
    suite = load_suite(args.suite)
    original_count = len(suite["cases"])
    if args.case_limit:
        suite["cases"] = suite["cases"][:args.case_limit]
    if (sha256(args.weights) != MODEL_SHA256 or sha256(args.runtime_wheel) != RUNTIME_WHEEL_SHA256 or
            sha256(args.sdk_wheel) != SDK_WHEEL_SHA256):
        raise ValueError("Pinned official model/runtime/SDK wheel hash mismatch")
    import zipfile
    with zipfile.ZipFile(args.runtime_wheel) as wheel:
        original_library = wheel.read("cactus/libcactus.so")
    if hashlib.sha256(original_library).hexdigest() != sha256(args.runtime_library):
        raise ValueError("Runtime library differs from the verified official wheel")
    if hasattr(os, "sched_setaffinity"):
        os.sched_setaffinity(0, set(sorted(os.sched_getaffinity(0))[:args.affinity_cores]))
    os.environ["CACTUS_LIB_PATH"] = str(args.runtime_library.resolve())
    os.environ["CACTUS_TELEMETRY"] = "0"
    os.environ["HF_HUB_OFFLINE"] = "1"
    report = {"schema_version": 1, "status": "initializing", "created_utc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
              "model_id": MODEL_ID, "revision": MODEL_REVISION, "weights_sha256": MODEL_SHA256,
              "weights_bytes": args.weights.stat().st_size, "parameters": 121021910,
              "runtime_hf_revision": RUNTIME_REVISION, "runtime_version": "4.0.0",
              "runtime_wheel_sha256": RUNTIME_WHEEL_SHA256, "runtime_library_sha256": sha256(args.runtime_library),
              "sdk_wheel_sha256": SDK_WHEEL_SHA256,
              "adapter_sha256": sha256(__file__), "suite_sha256": sha256(args.suite),
              "original_suite_cases": original_count, "requested_cases": len(suite["cases"]),
              "platform": platform.platform(), "python": platform.python_version(),
              "affinity_cpus": sorted(os.sched_getaffinity(0)) if hasattr(os, "sched_getaffinity") else None,
              "requested_affinity_cores": args.affinity_cores, "native_thread_control_available": False,
              "settings": {"stateless": True, "auto_date": False, "max_new_tokens": args.max_new_tokens,
                           "execute_tools": False, "confidence_threshold": "Official shipped native defaults, unchanged; suppressed_calls are never executed.",
                           "ungrounded_and_negation_gate": True,
                           "decoding": "temperature/top_k/top_p/seed are not exposed by the official Python API or C ABI; shipped native defaults unchanged. No claim of general deterministic inference.",
                           "route": "Local pinned libcactus loaded in the official Python worker; no hosted fallback, telemetry disabled, HF downloads offline."},
              "timing_scope": "Official Cactus.complete() including stateless reset, native worker IPC, model inference and output/grounding parsing. Startup, file SHA checks and one warmup are separate. CPU affinity limits allowed cores; it does not imply the runtime uses that many threads.",
              "evaluation_scope": "Frozen synthetic diagnostic command labels, not game win rate. Exact tool+typed arguments are scored. Unknown/ambiguous expected clarify is also safely handled by a clean successful abstention; runtime/parse errors do not count.",
              "system_text": suite.get("system", ""),
              "system_sha256": hashlib.sha256(suite.get("system", "").encode()).hexdigest(),
              "tool_schemas": suite["tool_schemas"], "results": []}
    memory = MemorySampler()
    memory.start()
    save(report, args.output)
    try:
        import cactus
        report["cactus_compute_version"] = importlib.metadata.version("cactus-compute")
        if report["cactus_compute_version"] != "4.0.1":
            raise ValueError("This adapter was reviewed against cactus-compute 4.0.1")
        installed_root = Path(cactus.__file__).parent
        with zipfile.ZipFile(args.sdk_wheel) as wheel:
            for relative in ("agent/text.py", "agent/runtime.py", "agent/fetch.py", "_worker.py"):
                if (installed_root / relative).read_bytes() != wheel.read("cactus/" + relative):
                    raise ValueError("Installed SDK source differs from the verified official wheel: " + relative)
        started = time.perf_counter()
        with create_agent(cactus, suite, args.weights) as model:
            report["initialization_ms"] = (time.perf_counter() - started) * 1000
            report["status"] = "running"
            run_cases(model, suite, report, args.output, args.max_new_tokens)
    except Exception as error:
        report["status"] = "failed"
        report["error"] = f"{type(error).__name__}: {error}"
    finally:
        report["memory"] = memory.stop()
        save(report, args.output)
    print(json.dumps({"status": report["status"], "summary": report.get("summary"), "error": report.get("error")}, ensure_ascii=False), flush=True)
    raise SystemExit(0 if report["status"] == "complete" else 1)


if __name__ == "__main__":
    main()
