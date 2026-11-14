"""Eight frozen post-hoc format/answer-bias controls, never main-score data.

Freeze with --freeze-protocol before any control inference. Root starts each
externally verified four-thread server, then invokes this helper once per model.
There is no warm-up, retry, model download, server launch, confidence interval,
or main-benchmark modification here. Each fixture contains one native question.
"""
import argparse
from collections import Counter
import copy
import datetime
import hashlib
import importlib
import json
from pathlib import Path

from run_additional_candidates import (ADAPTERS, atomic_json, call_adapter,
                                       digest, encoded, file_descriptor,
                                       predictions_match, strict_json)


HERE = Path(__file__).resolve().parent
DEFAULT_MAIN_PROTOCOL = HERE.parent / "Results/additional-candidates-protocol.json"
DEFAULT_PROTOCOL = HERE.parent / "Results/additional-controls-protocol.json"
MODEL_ADAPTERS = {"unee-0.8b": "unee", "lfm25-jp": "chat", "unee-2b": "unee", "lfm3": "chat"}
COLOR_CRITERIA = {"blue": "青", "red": "赤", "yellow": "黄", "green": "緑"}


def fixtures():
    rows = []
    for index, (color, description) in enumerate(COLOR_CRITERIA.items()):
        rows.append({
            "case_id": "control_lamp_" + color + "_ja", "kind": "color", "language": "ja",
            "request": {"state": "見えているランプは" + description + "色です。", "questions": {
                "color": {"type": "choice", "instructions": "見えているランプの色を選んでください。",
                          "criteria": copy.deepcopy(COLOR_CRITERIA)}}},
            "expected": {"color": color},
            "expected_symbol_by_adapter": {"unee": "ABCD"[index], "chat": "ABCD"[index]},
        })
    for state in ("closed", "open"):
        for question in ("closed", "open"):
            truth = state == question
            rows.append({
                "case_id": "control_drawer_" + state + "_ask_" + question + "_ja",
                "kind": "boolean", "language": "ja",
                "request": {"state": "引き出しは" + ("閉じています。" if state == "closed" else "開いています。"),
                            "questions": {"drawer": {"type": "noul", "instructions":
                                "引き出しは" + ("閉じていますか？" if question == "closed" else "開いていますか？"),
                                "criteria": {"false": "いいえ", "true": "はい"}}}},
                "expected": {"drawer": truth},
                "expected_symbol_by_adapter": {"unee": "A" if truth else "B", "chat": "B" if truth else "A"},
            })
    validate_fixtures(rows)
    return rows


def validate_fixtures(rows):
    if len(rows) != 8 or len({row["case_id"] for row in rows}) != 8:
        raise ValueError("Exactly eight distinct controls are required")
    colors = [row for row in rows if row["kind"] == "color"]
    booleans = [row for row in rows if row["kind"] == "boolean"]
    if len(colors) != 4 or len(booleans) != 4:
        raise ValueError("Four color and four boolean controls are required")
    if Counter(next(iter(row["expected"].values())) for row in booleans) != {True: 2, False: 2}:
        raise ValueError("Boolean gold must contain two true and two false controls")
    for adapter in ("unee", "chat"):
        if Counter(row["expected_symbol_by_adapter"][adapter] for row in colors) != dict.fromkeys("ABCD", 1):
            raise ValueError("Color controls must cover A/B/C/D exactly once")
        if Counter(row["expected_symbol_by_adapter"][adapter] for row in booleans) != {"A": 2, "B": 2}:
            raise ValueError("Boolean answer symbols must be balanced for each native mapping")
    for row in rows:
        if set(row["request"]) != {"state", "questions"} or len(row["request"]["questions"]) != 1:
            raise ValueError("Only state plus one question may cross the adapter boundary")
        if len(row["expected"]) != 1 or set(row["expected"]) != set(row["request"]["questions"]):
            raise ValueError("Control gold and question identity disagree")


def projection(adapter_name, rows):
    """Pure message preparation only: no predict(), HTTP, server or weights."""
    adapter = importlib.import_module(ADAPTERS[adapter_name])
    result = []
    for row in rows:
        prepared = adapter.prepare_tasks(copy.deepcopy(row["request"]))
        if len(prepared) != 1:
            raise ValueError("A control must project to exactly one native request")
        if adapter_name == "unee":
            task, body = prepared[0]
            candidates = [item["key"] for item in task.options]
            if task.examples:
                raise ValueError("Control gold must not become native examples")
            user = strict_json(body["messages"][1]["content"])
            if set(user) != {"question", "options", "state"} or user["state"] != row["request"]["state"]:
                raise ValueError("Unexpected Unee control projection")
        else:
            task = prepared[0]
            body, candidates = task["body"], task["candidate_ids"]
        serialized = encoded(body).decode("utf-8")
        if row["case_id"] in serialized or '"expected"' in serialized or '"audit"' in serialized:
            raise ValueError("Control bookkeeping or gold leaked into its model messages")
        value = next(iter(row["expected"].values()))
        expected_id = ("true" if value else "false") if type(value) is bool else value
        if "ABCDEFGH"[candidates.index(expected_id)] != row["expected_symbol_by_adapter"][adapter_name]:
            raise ValueError("Native answer mapping disagrees with the independently fixed gold")
        result.append({"case_id": row["case_id"], "request_sha256": digest(row["request"]),
                       "native_body_sha256": digest(body), "candidate_ids": candidates})
    return result


def verify_descriptor(item, base=None):
    path = Path(item["path"])
    if not path.is_absolute() and base is not None:
        path = base / path
    data = path.read_bytes()
    if hashlib.sha256(data).hexdigest() != item["sha256"] or ("bytes" in item and len(data) != item["bytes"]):
        raise ValueError("Frozen source/data changed: " + str(path))


def freeze_protocol(path=DEFAULT_PROTOCOL, main_path=DEFAULT_MAIN_PROTOCOL):
    path, main_path = Path(path), Path(main_path)
    if path.exists():
        raise FileExistsError("Existing control protocol must never be overwritten")
    main = strict_json(main_path.read_text(encoding="utf-8"))
    rows = fixtures()
    for item in main["frozen_sources"]:
        verify_descriptor(item, HERE.parent)
    verify_descriptor(main["cases"])
    sources = [file_descriptor(HERE / name) for name in (
        "diagnose_candidate_controls.py", "run_additional_candidates.py",
        "compare_unee_candidates.py", "compare_llamacpp_candidates.py")]
    main_models = {model["key"]: model for model in main["models"]}
    if set(main_models) != set(MODEL_ADAPTERS):
        raise ValueError("The controls must include all four preselected main candidates")
    protocol = {
        "schema_version": 1,
        "frozen_utc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        "status": "Post-hoc auxiliary controls frozen before control inference; main quality run already started",
        "main_protocol": file_descriptor(main_path),
        "post_hoc": True,
        "trigger": "Root observed LFM2.5-JP returning True on all 500 Japanese memory cases. Its earlier actual lamp-red and drawer-closed-positive smoke questions both had chat answer symbol B. These controls separate gross symbol/format collapse from task difficulty.",
        "selection_scope": "Chosen after observing main aggregates; includes simple patterns already used in preflight. This is not an independent holdout, primary performance estimate, causal explanation, or confirmatory test.",
        "fixed_rules": [
            "Exactly eight fixed Japanese single-question controls per model, in stored order, for all four candidates.",
            "No extra model warm-up, retries, prompt/threshold/grammar changes, output cleanup or feasibility repair.",
            "Call the already frozen native adapters; only request.state and request.questions are projected.",
            "Gold, case IDs and diagnostic metadata stay outside model messages.",
            "Errors are retained as incorrect; missing outcomes never become inferred answers.",
            "Never pool these controls into the 1200-case main results or the previous primary tests.",
            "No confidence intervals, p-values or general performance claims from these eight selected controls.",
            "Root starts a verified four-CPU-thread server after all main candidate jobs finish; this helper never launches a server or downloads weights.",
        ],
        "models": [{"key": key, "adapter": MODEL_ADAPTERS[key], "artifact": main_models[key]}
                   for key in main["run_order"]],
        "runtime_contract": copy.deepcopy(main["runtime"]),
        "timeout_seconds": 300.0,
        "case_count": 8, "planned_adapter_calls": 32,
        "cases_sha256": digest(rows), "cases": rows,
        "projection_audit": {name: projection(name, rows) for name in ("unee", "chat")},
        "frozen_sources": sources,
    }
    encoded(protocol)
    path.parent.mkdir(parents=True, exist_ok=True)
    atomic_json(path, protocol)
    return protocol


def load_protocol(path):
    path = Path(path)
    protocol = strict_json(path.read_text(encoding="utf-8"))
    if protocol.get("schema_version") != 1 or protocol.get("post_hoc") is not True:
        raise ValueError("Not the frozen post-hoc controls protocol")
    if protocol.get("cases") != fixtures() or protocol.get("cases_sha256") != digest(protocol["cases"]):
        raise ValueError("Frozen controls differ from the reviewed fixture definitions")
    for item in protocol["frozen_sources"]:
        verify_descriptor(item)
    verify_descriptor(protocol["main_protocol"])
    for name in ("unee", "chat"):
        if projection(name, protocol["cases"]) != protocol["projection_audit"][name]:
            raise ValueError("Native control projection changed")
    return protocol


def decision_symbol(row, observation):
    """Report the adapter decision mapping, without parsing/repairing raw text."""
    if observation["status"] != "complete":
        return None
    raw = observation.get("raw", {})
    calls = raw.get("details", {}).get("calls", []) if isinstance(raw, dict) else []
    if len(calls) != 1:
        return None
    value = next(iter(observation["predictions"].values()))
    key = ("true" if value else "false") if type(value) is bool else value
    candidates = calls[0].get("candidate_ids", [])
    return "ABCDEFGH"[candidates.index(key)] if key in candidates else None


def summary(rows):
    colors = [row for row in rows if row["kind"] == "color"]
    booleans = [row for row in rows if row["kind"] == "boolean"]
    return {"completed_controls": len(rows), "correct": sum(row["correct"] for row in rows),
            "failed": sum(row["status"] == "failed" for row in rows),
            "colors": {"count": len(colors), "correct": sum(row["correct"] for row in colors),
                       "returned_ids": dict(Counter(next(iter(row["predictions"].values()))
                                                    for row in colors if row["status"] == "complete"))},
            "booleans": {"count": len(booleans), "correct": sum(row["correct"] for row in booleans),
                         "returned_true": sum(next(iter(row["predictions"].values())) is True for row in booleans if row["status"] == "complete"),
                         "returned_false": sum(next(iter(row["predictions"].values())) is False for row in booleans if row["status"] == "complete")},
            "interpretation": "Counts describe only eight selected post-hoc controls. No CI/p-value, pooled main score, performance estimate or causal diagnosis is supplied."}


def run_controls(args, adapter=None):
    protocol = load_protocol(args.protocol)
    model = next((item for item in protocol["models"] if item["key"] == args.model_key), None)
    if model is None or model["adapter"] != args.adapter:
        raise ValueError("The model key and native adapter disagree with the frozen control protocol")
    output = Path(args.output)
    if output.exists():
        raise FileExistsError("Control evidence exists; never silently overwrite or rerun it")
    adapter = adapter or importlib.import_module(ADAPTERS[args.adapter])
    report = {"schema_version": 1, "created_utc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
              "status": "running", "post_hoc": True, "model_key": args.model_key, "adapter": args.adapter,
              "model_artifact": model["artifact"], "server_url": args.server_url,
              "model_verification": "Server artifact and four-thread settings verified externally by root",
              "protocol": file_descriptor(args.protocol), "cases_sha256": protocol["cases_sha256"],
              "main_results_modified": False, "results": []}
    output.parent.mkdir(parents=True, exist_ok=True)
    atomic_json(output, report)
    for index, case in enumerate(protocol["cases"]):
        observation = call_adapter(adapter, args.server_url, case["request"], protocol["timeout_seconds"])
        record = {"case_index": index, "case_id": case["case_id"], "kind": case["kind"],
                  "case_sha256": digest(case), "request_sha256": digest(case["request"]),
                  "expected": copy.deepcopy(case["expected"]),
                  "expected_decision_symbol": case["expected_symbol_by_adapter"][args.adapter],
                  "correct": observation["status"] == "complete" and predictions_match(observation["predictions"], case["expected"]),
                  **observation}
        record["returned_decision_symbol"] = decision_symbol(case, observation)
        record["record_sha256"] = digest(record)
        report["results"].append(record)
        report["summary"] = summary(report["results"])
        atomic_json(output, report)
        print(json.dumps({"model_key": args.model_key, "completed_controls": index + 1,
                          "case_id": case["case_id"], "status": observation["status"]}), flush=True)
    report["status"] = "complete"
    report["completed_utc"] = datetime.datetime.now(datetime.timezone.utc).isoformat()
    atomic_json(output, report)
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--freeze-protocol", action="store_true")
    parser.add_argument("--protocol", type=Path, default=DEFAULT_PROTOCOL)
    parser.add_argument("--main-protocol", type=Path, default=DEFAULT_MAIN_PROTOCOL)
    parser.add_argument("--adapter", choices=sorted(ADAPTERS))
    parser.add_argument("--server-url")
    parser.add_argument("--model-key", choices=sorted(MODEL_ADAPTERS))
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    if args.freeze_protocol:
        if any(value is not None for value in (args.adapter, args.server_url, args.model_key, args.output)):
            parser.error("Freezing must not be combined with model execution arguments")
        protocol = freeze_protocol(args.protocol, args.main_protocol)
        print(json.dumps({"protocol": file_descriptor(args.protocol), "cases_sha256": protocol["cases_sha256"],
                          "controls": protocol["case_count"], "post_hoc": True}, ensure_ascii=False))
    else:
        if any(value is None for value in (args.adapter, args.server_url, args.model_key, args.output)):
            parser.error("Running controls requires --adapter --server-url --model-key --output")
        result = run_controls(args)
        print(json.dumps(result["summary"], ensure_ascii=False))


if __name__ == "__main__":
    main()
