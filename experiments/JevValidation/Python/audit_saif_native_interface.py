"""Read-only Saif interface audit; never constructs an engine or loads weights.

Live mode checks a complete journal prefix and reconstructs a small token-layout
sample. Final mode requires completed main/control runs and can reconstruct every
layout with the pinned tokenizer and pure native layouts() function.
"""
import argparse
import ast
import datetime
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import sys


MODEL_ID = "GyrTechnology/saif-0.8b-lightning"
REVISION = "0cc7d0954a7aa48e0ba7302c4839185eb5aa164d"
TEMPERATURES = {"choice": 1.3492052555084229, "noul": 1.6690177917480469, "score": 0.5360531806945801}


def encoded(value):
    return json.dumps(value, ensure_ascii=False, separators=(",", ":"), allow_nan=False).encode()


def digest(value):
    return hashlib.sha256(encoded(value)).hexdigest()


def strict_json(value):
    def pairs(items):
        result = {}
        for key, item in items:
            if key in result:
                raise ValueError("Duplicate JSON key: " + key)
            result[key] = item
        return result
    def nonfinite(value):
        raise ValueError("Nonfinite JSON constant: " + value)
    return json.loads(value, object_pairs_hook=pairs, parse_constant=nonfinite)


def descriptor(path):
    path = Path(path).resolve()
    sha = hashlib.sha256()
    with path.open("rb") as source:
        for chunk in iter(lambda: source.read(1024 * 1024), b""):
            sha.update(chunk)
    return {"path": str(path), "bytes": path.stat().st_size, "sha256": sha.hexdigest()}


def verify_descriptor(row):
    actual = descriptor(row["path"])
    assert actual["sha256"] == row["sha256"] and actual["bytes"] == row["bytes"], row["path"]


def check_native_call(observation, request, temperatures):
    assert set(request) == {"state", "questions"}
    assert observation["status"] in {"complete", "failed"}
    if observation["status"] == "failed":
        assert observation["predictions"] == {} and isinstance(observation["error"], dict)
        return None
    assert observation["error"] is None
    raw = observation["raw"]
    details = raw["details"]
    assert details["initialization_excluded_from_request"] is True
    assert details["raw_logits"] is None
    calls = details["calls"]
    assert len(calls) == 1
    call = calls[0]
    assert encoded(call["request"]) == encoded(request)
    answers = call["raw"]["answers"]
    assert set(answers) == set(request["questions"])
    assert call["native_answers"] == answers
    assert call["probabilities"] == {name: answer["probabilities"] for name, answer in answers.items()}
    assert call["native_temperatures"] == temperatures
    assert type(call["native_elapsed_ms"]) in (int, float) and 0 <= call["native_elapsed_ms"] <= observation["duration_ms"]
    predictions = {}
    for name, question in request["questions"].items():
        answer = answers[name]
        probabilities = answer["probabilities"]
        assert answer["type"] == question["type"]
        keys = ["false", "true"] if question["type"] == "noul" else list(question["criteria"])
        assert list(probabilities) == keys
        assert all(type(value) in (int, float) and math.isfinite(value) and 0 <= value <= 1
                   for value in probabilities.values())
        assert abs(sum(probabilities.values()) - 1) <= 1e-4
        if question["type"] == "noul":
            assert answer["noul"] == probabilities["true"]
            predictions[name] = probabilities["true"] >= 0.5
        else:
            selected = max(keys, key=probabilities.get)
            assert answer["choice"] == selected
            predictions[name] = selected
    assert encoded(predictions) == encoded(observation["predictions"]) == encoded(raw["predictions"])
    layouts = call["layouts"]
    assert len(layouts) == 1  # Frozen native reading is joint, not per-question.
    assert sorted(name for layout in layouts for name in layout["fields"]) == sorted(request["questions"])
    assert observation["input_tokens"] == call["input_tokens"] == sum(layout["input_tokens"] for layout in layouts)
    assert observation["output_tokens"] == call["output_tokens"] == 0
    for layout in layouts:
        assert type(layout["input_tokens"]) is int and layout["input_tokens"] > 0
        assert 0 < layout["prefix_tokens"] < layout["input_tokens"]
        assert len(layout["fields"]) == len(layout["slots"]) == len(layout["blocks"]) == len(layout["label_token_ids"])
        for name, slot, block, tokens in zip(layout["fields"], layout["slots"], layout["blocks"], layout["label_token_ids"], strict=True):
            assert layout["prefix_tokens"] <= block[0] < block[1] <= layout["input_tokens"]
            assert slot == block[1] - 1
            assert len(tokens) == (2 if request["questions"][name]["type"] == "noul" else len(request["questions"][name]["criteria"]))
    return call


def load_layout_reconstructor(identity):
    """Only tokenizer + pure renderer; no native engine, model constructor or forward."""
    from transformers import AutoTokenizer
    core_path = next(row["path"] for row in identity["native_sources"] if Path(row["path"]).name == "saif_v2_core.py")
    spec = importlib.util.spec_from_file_location("saif_interface_audit_core", core_path)
    core = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = core
    spec.loader.exec_module(core)
    model_dir = identity["checkpoint"]["model_dir"]
    tokenizer = AutoTokenizer.from_pretrained(model_dir, local_files_only=True, trust_remote_code=False)
    description = identity["adapter_description"]

    def reconstruct(request):
        layouts = core.layouts(tokenizer, request["state"], request["questions"],
                               description["reading"], description["max_context_tokens"], description["read_twice"])
        return [{"input_tokens": len(layout.ids), "prefix_tokens": layout.prefix,
                 "input_ids_sha256": digest(layout.ids), "fields": layout.fields, "slots": layout.slots,
                 "blocks": [list(block) for block in layout.blocks], "label_token_ids": layout.label_ids}
                for layout in layouts]
    return reconstruct


def audit_run(directory, *, live=False, layouts="sample", verify_weights=False):
    directory = Path(directory).resolve()
    manifest_bytes = (directory / "manifest.json").read_bytes()
    manifest = strict_json(manifest_bytes)
    identity = manifest["identity"]
    assert identity["model_id"] == MODEL_ID and identity["model_revision"] == REVISION
    assert identity["server_url"] == "native://cpu" and identity["threads"] == 4 and identity["affinity"] == [0, 1, 2, 3]
    assert identity["limit"] is None
    checked_sources = []
    for key in ("adapter_source", "runner_source", "entrypoint_source", "affinity_helper_source", "source_manifest", "sdk_manifest", "protocol", "cases_source"):
        verify_descriptor(identity[key])
        checked_sources.append(identity[key])
    for row in identity["native_sources"]:
        verify_descriptor(row)
        checked_sources.append(row)
    verify_descriptor(identity["checkpoint"]["manifest"])
    for row in identity["checkpoint"]["files"]:
        if verify_weights or not row["filename"].endswith(".safetensors"):
            verify_descriptor(row)
    assert digest(identity["checkpoint"]) == identity["model_sha256"]
    description = identity["adapter_description"]
    assert description["native_source_files"] == identity["native_sources"]
    assert description["attention"] == "bidir" and description["reading"] == "joint"
    assert description["read_twice"] is False and description["fast_path"] is True
    assert description["native_temperatures"] == TEMPERATURES
    assert description["decoder_dtype"] == "torch.bfloat16" and description["decoder_parameter_dtypes"] == ["torch.bfloat16"]
    assert description["head_dtype"] == "torch.float32"
    assert description["decoder_device"] == description["head_device"] == "cpu"
    assert description["torch_threads"] == 4
    assert description["packages"]["torch"] == "2.9.1+cpu" and description["packages"]["transformers"] == "5.17.0"
    assert description["calibration_fit_on_this_corpus"] is False
    assert description["native_provenance"]["prefix_caching"] is False
    assert description["native_provenance"]["truncation"] is False
    # Static check backs the recorded one-call evidence without adding instrumentation.
    adapter_ast = ast.parse(Path(identity["adapter_source"]["path"]).read_text())
    engine_calls = [node for node in ast.walk(adapter_ast) if isinstance(node, ast.Call)
                    and isinstance(node.func, ast.Attribute) and node.func.attr == "engine"
                    and isinstance(node.func.value, ast.Name) and node.func.value.id == "self"]
    assert len(engine_calls) == 1
    assert not any(isinstance(node, ast.Call) and isinstance(node.func, ast.Attribute) and node.func.attr in {"logits", "warmup"}
                   for node in ast.walk(adapter_ast))
    shared_ast = ast.parse(Path(identity["runner_source"]["path"]).read_text())
    warmup = next(ast.literal_eval(node.value) for node in shared_ast.body if isinstance(node, ast.Assign)
                  and any(isinstance(target, ast.Name) and target.id == "WARMUP_REQUEST" for target in node.targets))
    assert digest(warmup) == identity["warmup_request_sha256"]
    cases_value = strict_json(Path(identity["cases_source"]["path"]).read_bytes())
    cases = cases_value["cases"] if isinstance(cases_value, dict) else cases_value
    assert len(cases) == identity["total_corpus_cases"]
    assert [case["case_id"] for case in cases] == identity["case_order"]
    assert digest([case["request"] for case in cases]) == identity["requests_sha256"]
    assert digest([case["expected"] for case in cases]) == identity["gold_sha256"]
    requests = [strict_json(line) for line in (directory / "requests.ndjson").read_bytes().splitlines()]
    assert requests == [{"case_id": case["case_id"], "request": case["request"]} for case in cases]
    verify_descriptor(manifest["requests_file"])
    snapshot = (directory / "results.ndjson").read_bytes()
    partial_tail = bool(snapshot and not snapshot.endswith(b"\n"))
    if partial_tail:
        assert live, "Final journal has an incomplete tail"
        snapshot = snapshot[:snapshot.rfind(b"\n") + 1]
    rows = [strict_json(line) for line in snapshot.splitlines()]
    assert len(rows) <= len(cases)
    reconstruct = load_layout_reconstructor(identity) if layouts != "none" else None
    sample_groups, layout_checked_ids = set(), []
    failure_count = 0
    for index, row in enumerate(rows):
        case = cases[index]
        assert row["record_sha256"] == digest({key: value for key, value in row.items() if key != "record_sha256"})
        assert row["case_index"] == index and row["case_id"] == case["case_id"]
        assert row["expected"] == case["expected"] and row["request_sha256"] == digest(case["request"])
        assert type(row["duration_ms"]) in (int, float) and math.isfinite(row["duration_ms"]) and row["duration_ms"] >= 0
        call = check_native_call(row, case["request"], TEMPERATURES)
        if call is None:
            assert row["correct"] is False
            failure_count += 1
            continue
        correct = set(row["predictions"]) == set(case["expected"]) and all(
            type(row["predictions"][name]) is type(value) and row["predictions"][name] == value
            for name, value in case["expected"].items())
        assert row["correct"] == correct
        group = (case.get("suite"), case.get("language"), tuple(q["type"] for q in case["request"]["questions"].values()))
        if reconstruct and (layouts == "all" or group not in sample_groups):
            assert reconstruct(case["request"]) == call["layouts"], case["case_id"]
            sample_groups.add(group)
            layout_checked_ids.append(case["case_id"])
    for session in manifest["sessions"]:
        assert encoded(session["warmup_request"]) == encoded(warmup)
        if "warmup" in session:
            call = check_native_call(session["warmup"], warmup, TEMPERATURES)
            if call and reconstruct:
                assert reconstruct(warmup) == call["layouts"]
        else:
            assert live, "Final session has no fixed warmup observation"
    if not live:
        result = strict_json((directory / "result.json").read_bytes())
        assert result["status"] == "complete" and len(rows) == result["completed_cases"] == len(cases)
        assert result["identity"] == identity and result["results"] == rows
        assert result["manifest_sha256"] == hashlib.sha256(manifest_bytes).hexdigest()
        assert result["journal_sha256"] == hashlib.sha256(snapshot).hexdigest()
    return {"directory": str(directory), "planned_cases": len(cases), "audited_complete_prefix_rows": len(rows),
            "journal_snapshot_sha256": hashlib.sha256(snapshot).hexdigest(), "manifest_snapshot_sha256": hashlib.sha256(manifest_bytes).hexdigest(),
            "partial_live_tail_ignored": partial_tail, "inference_failures_preserved": failure_count,
            "checked_source_files": checked_sources, "weight_file_hashes_checked_in_this_audit": verify_weights,
            "layout_reconstruction": layouts, "layout_reconstructed_case_ids": layout_checked_ids,
            "recorded_warmups": len([session for session in manifest["sessions"] if "warmup" in session]),
            "session_resume_indices": [session["resume_from"] for session in manifest["sessions"]],
            "runtime": {key: description[key] for key in ("decoder_dtype", "decoder_parameter_dtypes", "head_dtype", "attention", "reading", "read_twice", "fast_path", "native_temperatures", "packages", "torch_threads", "torch_interop_threads")},
            "checks_passed": ["original requests and original case order", "journal checksums and gold isolation", "single recorded native call and frozen one-call source", "native calibrated probability decoding without renormalization", "original-order choice argmax and noul threshold", "token-layout accounting", "fixed nonbenchmark warmup", "frozen sources and native runtime descriptors"]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--main", type=Path, required=True)
    parser.add_argument("--controls", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--live", action="store_true")
    parser.add_argument("--layouts", choices=("none", "sample", "all"), default="sample")
    parser.add_argument("--verify-weights", action="store_true")
    args = parser.parse_args()
    runs = [audit_run(args.main, live=args.live, layouts=args.layouts, verify_weights=args.verify_weights)]
    if args.controls:
        runs.append(audit_run(args.controls, live=args.live, layouts=args.layouts, verify_weights=args.verify_weights))
    report = {"schema_version": 1, "created_utc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
              "status": "passed", "live_prefix_only": args.live, "auditor": descriptor(__file__), "runs": runs,
              "scope": "Read-only interface and evidence audit, no engine construction, model weights loaded, model inference, prompt changes, calibration changes or quality-based tuning. Recorded calls plus frozen source establish the call contract; no separate profiler trace is claimed."}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_bytes(encoded(report) + b"\n")
    print(json.dumps({"status": report["status"], "rows": [run["audited_complete_prefix_rows"] for run in runs],
                      "layout_reconstructions": [len(run["layout_reconstructed_case_ids"]) for run in runs], "output": str(args.output)}))


if __name__ == "__main__":
    main()
