"""Strict exploratory analysis for the separately frozen native Saif trial.

The previous two-task Holm family and all historical artifacts remain unchanged.
Native calibrated probabilities are authoritative: no second softmax, floor,
temperature fit, output repair or exclusion of explicit failed observations.
"""
import argparse
import copy
import csv
import datetime
from collections import Counter
import hashlib
import json
import math
from pathlib import Path

import run_additional_candidates as shared
from analyze_additional_candidates import (attempt_summary, commander_outcome, latency_summary,
                                           task_summaries, tokens_summary, validate_oracles)
from analyze_expanded_decisions_repaired import load_records, score_records, validate_design


MODEL_KEY = "saif-lightning"
MODEL_ID = "GyrTechnology/saif-0.8b-lightning"
REVISION = "0cc7d0954a7aa48e0ba7302c4839185eb5aa164d"
ADDITIONAL_KEYS = ("unee-0.8b", "unee-2b", "lfm25-jp", "lfm3")
BASELINE_KEYS = ("hindsight", "tinyjev") + ADDITIONAL_KEYS
METADATA = ("case_id", "world_id", "suite", "family_id", "mechanism_group", "language")


def file_descriptor(path):
    path = Path(path).resolve()
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for chunk in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(chunk)
    return {"path": str(path), "bytes": path.stat().st_size, "sha256": digest.hexdigest()}


def check_descriptor(path, expected):
    actual = file_descriptor(path)
    if any(actual[key] != expected[key] for key in ("sha256", "bytes")):
        raise ValueError("Frozen artifact changed: " + str(path))
    return actual


def _read(path):
    return shared.strict_json(Path(path).read_text(encoding="utf-8"))


def _same(first, second):
    # JSON object field order is not a label/type difference. Candidate order and
    # model-input byte order are validated explicitly at their own boundaries.
    return json.dumps(first, ensure_ascii=False, sort_keys=True, allow_nan=False) == json.dumps(
        second, ensure_ascii=False, sort_keys=True, allow_nan=False)


def _hash_string(value):
    return isinstance(value, str) and len(value) == 64 and all(c in "0123456789abcdef" for c in value)


def _finite_nonnegative(value):
    return type(value) in (int, float) and math.isfinite(value) and value >= 0


def verify_protocol(path, expected_sha256, cases_path, paired_csv, additional_csv):
    if not _hash_string(expected_sha256) or file_descriptor(path)["sha256"] != expected_sha256:
        raise ValueError("The externally supplied frozen Saif protocol SHA does not match")
    protocol = _read(path)
    if protocol.get("schema_version") != 1 or protocol.get("exploratory") is not True:
        raise ValueError("Expected a separate exploratory Saif protocol")
    model = protocol.get("model", {})
    if model.get("model_id") != MODEL_ID or model.get("revision") != REVISION:
        raise ValueError("Only the prespecified Saif checkpoint is supported")
    if set(protocol.get("baseline_names", [])) != set(BASELINE_KEYS) or len(protocol["baseline_names"]) != 6:
        raise ValueError("The six prespecified baseline identities changed")
    check_descriptor(cases_path, protocol["cases"])
    for item in protocol["frozen_sources"]:
        check_descriptor(item["path"], item)
    for key in ("checkpoint_manifest", "source_manifest", "sdk_manifest"):
        check_descriptor(model[key]["path"], model[key])
    for item in protocol["baseline_sources"].values():
        check_descriptor(item["path"], item)
    check_descriptor(paired_csv, protocol["baseline_sources"]["expanded_csv"])
    check_descriptor(additional_csv, protocol["baseline_sources"]["additional_csv"])
    sizes = protocol.get("sample_sizes", {})
    if (sizes.get("main_requests") != 1200 or sizes.get("primary_ja") != {"memory": 500, "commander": 500}
            or sizes.get("secondary_en_paired_translations") != {"memory": 100, "commander": 100}):
        raise ValueError("The frozen main sample sizes changed")
    return protocol


def decode_native(case, row, temperatures, max_context_tokens):
    """Independent decode of the saved __call__ distribution and its token layouts."""
    if row.get("status") == "failed":
        if row.get("predictions") != {} or row.get("correct") is not False or not isinstance(row.get("error"), dict):
            raise ValueError("Explicit failed observations must remain incorrect")
        return {"available": False, "predictions": {}, "input_tokens": None,
                "native_elapsed_ms": None, "layout_count": 0}
    if row.get("status") != "complete" or row.get("error") is not None:
        raise ValueError("A successful native observation has an invalid status")
    raw = row.get("raw")
    details = raw.get("details") if isinstance(raw, dict) else None
    calls = details.get("calls") if isinstance(details, dict) else None
    if not isinstance(calls, list) or len(calls) != 1:
        raise ValueError("Exactly one official native API call is required")
    call = calls[0]
    request = case["request"]
    if (not _same(call.get("request"), request) or set(call["request"]) != {"state", "questions"}
            or shared.digest(call["request"]) != shared.digest(request)):
        raise ValueError("Native call did not preserve the original state/questions boundary")
    if not _same(call.get("native_temperatures"), temperatures):
        raise ValueError("Published native temperatures changed")
    response = call.get("raw")
    answers = response.get("answers") if isinstance(response, dict) else None
    questions = request["questions"]
    if not isinstance(answers, dict) or set(answers) != set(questions):
        raise ValueError("Native answer coverage differs from the original questions")
    predictions, distributions = {}, {}
    for question_id, question in questions.items():
        answer = answers[question_id]
        if not isinstance(answer, dict) or answer.get("type") != question["type"]:
            raise ValueError("Native answer type changed")
        probabilities = answer.get("probabilities")
        keys = {"false", "true"} if question["type"] == "noul" else set(question["criteria"])
        if (not isinstance(probabilities, dict) or set(probabilities) != keys
                or any(type(value) not in (int, float) or not math.isfinite(value) or not 0 <= value <= 1
                       for value in probabilities.values()) or abs(sum(probabilities.values()) - 1) > 1e-4):
            raise ValueError("Native probabilities are missing, invalid or unnormalized")
        if question["type"] == "noul":
            if type(answer.get("noul")) not in (int, float) or answer["noul"] != probabilities["true"]:
                raise ValueError("Native noul value differs from its full probability")
            selected = probabilities["true"] >= 0.5
        elif question["type"] == "choice":
            order = list(question["criteria"])
            if list(probabilities) != order:
                raise ValueError("Native choice distribution reordered the original candidates")
            selected = max(order, key=probabilities.__getitem__)
            if answer.get("choice") != selected:
                raise ValueError("Native choice differs from original-order argmax")
        else:
            raise ValueError("Only the prespecified boolean/choice tasks are scored")
        predictions[question_id], distributions[question_id] = selected, probabilities
    if (not _same(call.get("native_answers"), answers) or not _same(call.get("probabilities"), distributions)
            or not _same(row.get("predictions"), predictions) or not _same(raw.get("predictions"), predictions)):
        raise ValueError("Native probability copies or typed canonical predictions disagree")
    layouts = call.get("layouts")
    if not isinstance(layouts, list) or not layouts:
        raise ValueError("Actual native token layouts are required")
    covered, total = [], 0
    for layout in layouts:
        count, prefix = layout.get("input_tokens"), layout.get("prefix_tokens")
        fields, slots = layout.get("fields"), layout.get("slots")
        blocks, labels = layout.get("blocks"), layout.get("label_token_ids")
        if (type(count) is not int or not 0 < count <= max_context_tokens or type(prefix) is not int
                or not 0 <= prefix < count or not _hash_string(layout.get("input_ids_sha256"))
                or not isinstance(fields, list) or not fields or not isinstance(slots, list)
                or not isinstance(blocks, list) or not isinstance(labels, list)
                or not len(fields) == len(slots) == len(blocks) == len(labels)):
            raise ValueError("Native layout dimensions, token hash or context are invalid")
        for name, slot, block, token_ids in zip(fields, slots, blocks, labels):
            if name not in questions or type(slot) is not int or not 0 <= slot < count:
                raise ValueError("Native layout has an unknown question or out-of-range slot")
            if (not isinstance(block, (list, tuple)) or len(block) != 2
                    or any(type(value) is not int for value in block) or not prefix <= block[0] <= slot < block[1] <= count):
                raise ValueError("Native slot lies outside its full question block")
            wanted = 2 if questions[name]["type"] == "noul" else len(questions[name]["criteria"])
            if (not isinstance(token_ids, list) or len(token_ids) != wanted
                    or any(type(value) is not int or value < 0 for value in token_ids)
                    or len(set(token_ids)) != wanted):
                raise ValueError("Native answer label token inventory changed")
        covered.extend(fields)
        total += count
    if len(covered) != len(set(covered)) or set(covered) != set(questions):
        raise ValueError("Token layouts omitted or duplicated a question")
    if (call.get("input_tokens") != total or row.get("input_tokens") != total
            or call.get("output_tokens") != 0 or row.get("output_tokens") != 0
            or not _finite_nonnegative(call.get("native_elapsed_ms"))
            or details.get("raw_logits") is not None or details.get("initialization_excluded_from_request") is not True):
        raise ValueError("Native token counts, one-pass readout or timing scope changed")
    return {"available": True, "predictions": predictions, "input_tokens": total,
            "native_elapsed_ms": call["native_elapsed_ms"], "layout_count": len(layouts)}


def _baseline_metadata(case, row):
    if any(row.get(key) != case[key] for key in METADATA):
        raise ValueError("Baseline world/task/language metadata changed")
    if row.get("primary") not in {"True", "False"} or (row["primary"] == "True") != (case["language"] == "ja"):
        raise ValueError("Baseline pooled translations into primary worlds")
    if not _same(shared.strict_json(row["gold"]), case["expected"]):
        raise ValueError("Baseline gold changed")


def _baseline_prediction(case, status, prediction, correct):
    if correct not in {"True", "False"}:
        raise ValueError("Baseline correctness must be explicitly boolean")
    if status == "complete":
        shared.valid_predictions(case["request"], prediction)
        computed = shared.predictions_match(prediction, case["expected"])
    elif status == "failed" and prediction == {}:
        computed = False
    else:
        raise ValueError("Baseline has an invalid status or partial failed prediction")
    if computed != (correct == "True"):
        raise ValueError("Baseline cached correctness contradicts typed gold")
    return {"status": status, "predictions": prediction, "correct": computed}


def load_baseline_csvs(cases, paired_path, additional_path):
    result = {name: {} for name in BASELINE_KEYS}
    with Path(paired_path).open(encoding="utf-8", newline="") as source:
        rows = list(csv.DictReader(source))
    if len(rows) != len(cases):
        raise ValueError("Paired baseline CSV must preserve every frozen world")
    for case, row in zip(cases, rows):
        _baseline_metadata(case, row)
        for name in ("hindsight", "tinyjev"):
            result[name][case["case_id"]] = _baseline_prediction(case, row[name + "_status"],
                shared.strict_json(row[name + "_prediction"]), row[name + "_correct"])
    by_id = {case["case_id"]: case for case in cases}
    with Path(additional_path).open(encoding="utf-8", newline="") as source:
        rows = list(csv.DictReader(source))
    if len(rows) != len(cases) * len(ADDITIONAL_KEYS):
        raise ValueError("Additional CSV must include all four previous pipelines")
    for row in rows:
        name, case_id = row.get("model_key"), row.get("case_id")
        if name not in ADDITIONAL_KEYS or case_id not in by_id or case_id in result[name]:
            raise ValueError("Duplicate or foreign additional baseline world")
        case = by_id[case_id]
        _baseline_metadata(case, row)
        result[name][case_id] = _baseline_prediction(case, row["status"],
            shared.strict_json(row["predictions"]), row["correct"])
    if any(set(rows) != set(by_id) for rows in result.values()):
        raise ValueError("Missing baseline observations cannot be discarded")
    return result


def verify_baseline_native(cases, baselines, protocol):
    """Tie convenience CSVs to the immutable historical native report bytes."""
    sources = protocol["baseline_sources"]
    expanded = _read(sources["expanded_statistics"]["path"])
    additional = _read(sources["additional_statistics"]["path"])
    for statistics_snapshot in (expanded, additional):
        recorded_cases = statistics_snapshot.get("sources", {}).get("cases")
        if not _same(recorded_cases, protocol["cases"]):
            raise ValueError("Historical statistics refer to different model inputs")
        check_descriptor(recorded_cases["path"], recorded_cases)
    evidence = {}
    for name in BASELINE_KEYS:
        reference = expanded["sources"][name] if name in {"hindsight", "tinyjev"} else additional["models"][name]["raw_source"]["result.json"]
        evidence[name] = check_descriptor(reference["path"], reference)
        native_report = _read(reference["path"])
        native_rows = load_records(reference["path"])
        if len(native_rows) != len(cases):
            raise ValueError("Historical native report has missing or extra observations")
        directory = Path(reference["path"]).parent
        identity = native_report.get("identity", {})
        if identity.get("case_order") != [case["case_id"] for case in cases]:
            raise ValueError("Historical native case order differs from the current input worlds")
        if name in {"hindsight", "tinyjev"}:
            if identity.get("cases_file_sha256") != protocol["cases"]["sha256"]:
                raise ValueError("Historical decision baseline used different native requests")
            rescored = score_records(cases, native_rows)
            normalized = {key: {"status": "failed" if value["failed"] else "complete",
                                 "predictions": value["predictions"], "correct": value["correct"]}
                          for key, value in rescored.items()}
        else:
            if (not _same(identity.get("cases_source"), protocol["cases"])
                    or identity.get("requests_sha256") != shared.digest([case["request"] for case in cases])
                    or identity.get("gold_sha256") != shared.digest([case["expected"] for case in cases])
                    or identity.get("limit") is not None):
                raise ValueError("Historical additional baseline used different native inputs")
            normalized = {}
            for case, row in zip(cases, native_rows):
                if case["case_id"] != row.get("case_id") or not _same(row.get("expected"), case["expected"]):
                    raise ValueError("Historical additional native ID/order/gold changed")
                normalized[case["case_id"]] = _baseline_prediction(case, row["status"], row["predictions"], str(row["correct"]))
            manifest_ref = additional["models"][name]["raw_source"]["manifest.json"]
            journal_ref = additional["models"][name]["raw_source"]["results.ndjson"]
            check_descriptor(manifest_ref["path"], manifest_ref)
            check_descriptor(journal_ref["path"], journal_ref)
            if (native_report.get("manifest_sha256") != manifest_ref["sha256"]
                    or native_report.get("journal_sha256") != journal_ref["sha256"]):
                raise ValueError("Historical native report lost its manifest/journal linkage")
        manifest_path, journal_path = directory / "manifest.json", directory / "results.ndjson"
        if (native_report.get("manifest_sha256") != file_descriptor(manifest_path)["sha256"]
                or native_report.get("journal_sha256") != file_descriptor(journal_path)["sha256"]):
            raise ValueError("Historical native manifest or durable journal changed")
        manifest = _read(manifest_path)
        if not _same(manifest.get("identity"), identity):
            raise ValueError("Historical native manifest identity changed")
        request_path = directory / "requests.ndjson"
        if name in {"hindsight", "tinyjev"}:
            if file_descriptor(request_path)["sha256"] != manifest.get("requests_file_sha256"):
                raise ValueError("Historical native projected-request file changed")
        else:
            check_descriptor(request_path, manifest["requests_file"])
        saved_requests = request_path.read_bytes()
        projected = [shared.strict_json(line) for line in saved_requests.splitlines()]
        if not saved_requests.endswith(b"\n") or len(projected) != len(cases):
            raise ValueError("Historical saved model inputs differ from the unchanged current corpus")
        if name in {"hindsight", "tinyjev"}:
            if identity.get("model_requests_sha256") != shared.digest(projected):
                raise ValueError("Historical native projected-request digest changed")
            for case, item in zip(cases, projected):
                native_request = item.get("request", {})
                if (set(item) != {"case_id", "request"} or item["case_id"] != case["case_id"]
                        or not {"state", "questions"} <= set(native_request)
                        or set(native_request) - {"state", "questions", "id", "model"}
                        or ("id" in native_request and native_request["id"] != case["case_id"])
                        or shared.digest({key: native_request[key] for key in case["request"]}) != shared.digest(case["request"])):
                    raise ValueError("Historical native projection changed state/questions or leaked outer metadata")
        elif shared.digest(projected) != shared.digest([
                {"case_id": case["case_id"], "request": case["request"]} for case in cases]):
            raise ValueError("Historical additional saved inputs changed")
        for case in cases:
            key = case["case_id"]
            if not _same(normalized[key], baselines[name][key]):
                raise ValueError("A baseline CSV differs from its frozen native outcome: " + name)
    return evidence


def native_distribution_diagnostics(cases, rows):
    """Descriptive audits only, using the already verified full native distributions."""
    labels, positions, p_true, maximum = Counter(), Counter(), [], []
    first_gold, choice_count, completed = 0, 0, 0
    for case in cases:
        row = rows[case["case_id"]]
        if row["status"] != "complete":
            continue
        completed += 1
        answers = row["raw"]["details"]["calls"][0]["raw"]["answers"]
        for question_id, question in case["request"]["questions"].items():
            selected = row["predictions"][question_id]
            probabilities = answers[question_id]["probabilities"]
            maximum.append(max(probabilities.values()))
            label = str(selected).lower() if type(selected) is bool else selected
            labels[label] += 1
            if question["type"] == "noul":
                p_true.append(probabilities["true"])
            else:
                order = list(question["criteria"])
                positions[str(order.index(selected) + 1)] += 1
                first_gold += case["expected"][question_id] == order[0]
                choice_count += 1
    def distribution(values):
        return {"n": len(values), "mean": sum(values) / len(values) if values else None,
                "min": min(values) if values else None, "max": max(values) if values else None}
    return {"planned_observations": len(cases), "completed_probability_observations": completed,
            "failed_observations": len(cases) - completed, "typed_prediction_histogram": dict(labels),
            "choice_observations": choice_count, "choice_selected_position_histogram_one_based": dict(positions),
            "choice_first_position_count": positions["1"],
            "choice_first_position_share": positions["1"] / choice_count if choice_count else None,
            "gold_first_position_count": first_gold,
            "gold_first_position_share": first_gold / choice_count if choice_count else None,
            "native_p_true": distribution(p_true), "native_max_option_probability": distribution(maximum),
            "scope": "Descriptive saved-native-distribution audit only. No thresholds, prompts, temperatures or scoring change; option probabilities are not calibrated game-success probabilities. Failed attempts remain in quality denominators."}


def _verify_identity(identity, protocol, protocol_path, cases):
    model = protocol["model"]
    if (identity.get("adapter") != "saif-lightning-native" or identity.get("server_url") != "native://cpu"
            or identity.get("model_id") != model["model_id"] or identity.get("model_revision") != model["revision"]
            or identity.get("limit") is not None or identity.get("total_corpus_cases") != len(cases)
            or identity.get("case_order") != [case["case_id"] for case in cases]
            or identity.get("requests_sha256") != shared.digest([case["request"] for case in cases])
            or identity.get("gold_sha256") != shared.digest([case["expected"] for case in cases])):
        raise ValueError("Saif identity differs from the full frozen native trial")
    for field, expected in (("protocol", file_descriptor(protocol_path)), ("cases_source", protocol["cases"]),
                            ("source_manifest", model["source_manifest"]), ("sdk_manifest", model["sdk_manifest"])):
        if not _same(identity.get(field), expected):
            raise ValueError("Native identity reference changed: " + field)
    frozen = {Path(item["path"]).name: item for item in protocol["frozen_sources"]}
    for field, filename in (("adapter_source", "compare_saif_lightning.py"),
                            ("runner_source", "run_additional_candidates.py"),
                            ("entrypoint_source", "run_saif_benchmark.py"),
                            ("affinity_helper_source", "run_expanded_benchmark.py")):
        if not _same(identity.get(field), frozen[filename]):
            raise ValueError("Native source differs from the frozen source: " + field)
    runtime = protocol["runtime"]
    if (identity.get("threads") != runtime["threads"] or identity.get("affinity") != runtime["affinity"]
            or identity.get("timeout_seconds") != runtime["request_timeout_seconds"]
            or identity.get("warmup_request_sha256") != shared.digest(shared.WARMUP_REQUEST)
            or identity.get("python") != runtime["python"]):
        raise ValueError("Native CPU, timeout, Python or warmup settings changed")
    description = identity.get("adapter_description", {})
    if (description.get("decoder_dtype") != "torch.bfloat16" or description.get("head_dtype") != "torch.float32"
            or description.get("decoder_device") != "cpu" or description.get("head_device") != "cpu"
            or description.get("torch_threads") != runtime["threads"] or description.get("reading") != "joint"
            or description.get("read_twice") is not False or description.get("fast_path") is not True
            or description.get("calibration_fit_on_this_corpus") is not False
            or not _same(description.get("native_temperatures"), model["temperatures"])):
        raise ValueError("Native dtype, reading path, calibration or temperatures changed")
    for expected, package in (("torch", "torch"), ("transformers", "transformers"),
                              ("accelerate", "accelerate"), ("huggingface_hub", "huggingface-hub")):
        if description.get("packages", {}).get(package) != runtime[expected]:
            raise ValueError("Native package version changed: " + package)
    checkpoint = identity.get("checkpoint", {})
    if shared.digest(checkpoint) != identity.get("model_sha256") or not _same(checkpoint.get("manifest"), model["checkpoint_manifest"]):
        raise ValueError("Checkpoint identity digest or manifest changed")
    manifest = _read(model["checkpoint_manifest"]["path"])
    if manifest.get("repo") != MODEL_ID or manifest.get("revision") != REVISION:
        raise ValueError("Checkpoint manifest identifies another model")
    actual = checkpoint.get("files", [])
    if (checkpoint.get("repository") != MODEL_ID or checkpoint.get("revision") != REVISION
            or len(actual) != len(manifest["files"])):
        raise ValueError("Incomplete checkpoint identity")
    directory = Path(checkpoint["model_dir"]).resolve()
    for expected, item in zip(manifest["files"], actual):
        filename = expected["filename"]
        if Path(filename).name != filename or item.get("filename") != filename:
            raise ValueError("Checkpoint filename/order changed")
        path = (directory / filename).resolve()
        if not path.is_relative_to(directory) or Path(item["path"]).resolve() != path:
            raise ValueError("Checkpoint path escapes the pinned model directory")
        check_descriptor(path, expected)
        if any(item[field] != expected[field] for field in ("bytes", "sha256")):
            raise ValueError("Run used different checkpoint bytes")
    native_sources = identity.get("native_sources", [])
    for item in native_sources:
        check_descriptor(item["path"], item)
    source_manifest = _read(model["source_manifest"]["path"])
    sdk_manifest = _read(model["sdk_manifest"]["path"])
    imported = {Path(item["path"]).name: Path(item["path"]).parent for item in native_sources
                if Path(item["path"]).name in {"decision_index_engine.py", "saif_v2_core.py", "singlepass.py"}}
    if len(imported) != 3 or len(set(imported.values())) != 1:
        raise ValueError("Pinned native implementation source coverage changed")
    source_root = next(iter(imported.values()))
    for item in source_manifest["files"]:
        name = item["path"]
        if Path(name).name != name:
            raise ValueError("Unexpected relative native source path")
        check_descriptor(source_root / name, item)
    for item in sdk_manifest["files"]:
        check_descriptor(item["path"], item)
        if not any(_same(item, source) for source in native_sources):
            raise ValueError("SDK source was not part of the verified native run")
    return description


def read_completed_saif(directory, cases, protocol_path, protocol):
    directory = Path(directory)
    manifest_path, journal_path, result_path = [directory / name for name in ("manifest.json", "results.ndjson", "result.json")]
    manifest, report = _read(manifest_path), _read(result_path)
    rows = shared.read_journal(journal_path, cases)
    if (len(rows) != len(cases) or report.get("status") != "complete" or report.get("error") is not None
            or report.get("completed_cases") != len(cases) or report.get("planned_cases") != len(cases)
            or report.get("results") != rows or report.get("sessions") != manifest.get("sessions")
            or report.get("identity") != manifest.get("identity")):
        raise ValueError("Only a complete run exactly matching its durable manifest/journal may be analyzed")
    if report.get("manifest_sha256") != file_descriptor(manifest_path)["sha256"] or report.get("journal_sha256") != file_descriptor(journal_path)["sha256"]:
        raise ValueError("Final report linkage to native evidence changed")
    description = _verify_identity(manifest["identity"], protocol, protocol_path, cases)
    request_path = directory / "requests.ndjson"
    check_descriptor(request_path, manifest["requests_file"])
    saved = request_path.read_bytes()
    if not saved.endswith(b"\n") or [shared.strict_json(line) for line in saved.splitlines()] != [
            {"case_id": case["case_id"], "request": case["request"]} for case in cases]:
        raise ValueError("Saved native inputs changed or contain bookkeeping/gold")
    sessions, previous = manifest.get("sessions"), -1
    if not isinstance(sessions, list) or not sessions:
        raise ValueError("Run lost its separately recorded process warmup")
    for session in sessions:
        resume = session.get("resume_from")
        if type(resume) is not int or not previous <= resume < len(cases) or session.get("warmup_request") != shared.WARMUP_REQUEST:
            raise ValueError("Session resume or fixed warmup changed")
        warmup = session.get("warmup", {})
        if warmup.get("status") == "complete":
            shared.valid_predictions(shared.WARMUP_REQUEST, warmup.get("predictions"))
            if warmup.get("error") is not None:
                raise ValueError("Successful warmup contains an execution error")
        elif warmup.get("status") != "failed" or warmup.get("predictions") != {} or not isinstance(warmup.get("error"), dict):
            raise ValueError("Warmup failure evidence was lost")
        previous = resume
    diagnostics = {}
    for case, row in zip(cases, rows):
        if row.get("suite") != case["suite"] or row.get("language") != case["language"]:
            raise ValueError("Native task/language metadata changed")
        diagnostics[case["case_id"]] = decode_native(case, row, protocol["model"]["temperatures"], description["max_context_tokens"])
    return {"rows": {row["case_id"]: row for row in rows}, "diagnostics": diagnostics,
            "identity": manifest["identity"], "sessions": sessions,
            "evidence": {name: file_descriptor(directory / name) for name in
                         ("manifest.json", "result.json", "results.ndjson", "requests.ndjson")}}


def csv_rows(cases, rows, baselines, diagnostics):
    result = []
    for case in cases:
        row = rows[case["case_id"]]
        item = {key: case[key] for key in METADATA}
        item.update(model_key=MODEL_KEY, model_id=MODEL_ID, primary=case["language"] == "ja",
                    gold=shared.encoded(case["expected"]).decode(), predictions=shared.encoded(row["predictions"]).decode(),
                    status=row["status"], correct=row["correct"], failure_type=(row.get("error") or {}).get("type"),
                    duration_ms=row["duration_ms"], input_tokens=row["input_tokens"], output_tokens=row["output_tokens"],
                    native_elapsed_ms=diagnostics[case["case_id"]]["native_elapsed_ms"], record_sha256=row["record_sha256"])
        for name, baseline in baselines.items():
            good = baseline[case["case_id"]]["correct"]
            item[name + "_correct"] = good
            item[name + "_pair_outcome"] = "both_correct" if row["correct"] and good else "candidate_only" if row["correct"] else "baseline_only" if good else "both_wrong"
        item.update(memory_expected=None, memory_prediction=None, commander_class=None, rank_loss=None)
        if case["suite"] == "memory":
            item.update(memory_expected=next(iter(case["expected"].values())), memory_prediction=next(iter(row["predictions"].values()), None))
        else:
            item["commander_class"], item["rank_loss"] = commander_outcome(case, row)
        result.append(item)
    return result


def analyze(cases_path, run_directory, protocol_path, protocol_sha256, paired_csv, additional_csv, caller_facts=None):
    protocol = verify_protocol(protocol_path, protocol_sha256, cases_path, paired_csv, additional_csv)
    cases = shared.load_cases(cases_path)
    validate_design(cases, 500, 25, 100)
    validate_oracles(cases)
    baselines = load_baseline_csvs(cases, paired_csv, additional_csv)
    baseline_evidence = verify_baseline_native(cases, baselines, protocol)
    loaded = read_completed_saif(run_directory, cases, protocol_path, protocol)
    rows, diagnostics = loaded["rows"], loaded["diagnostics"]
    model = {"identity": {"model_id": MODEL_ID, "revision": REVISION, "license": protocol["model"].get("license"),
                           "quantization": protocol["model"].get("quantization"), "run_identity": loaded["identity"]},
             "overall": attempt_summary(cases, rows), "tasks": task_summaries(cases, rows, baselines),
             "latency": latency_summary(list(rows.values())),
             "successful_only_latency": latency_summary([row for row in rows.values() if row["status"] == "complete"]),
             "native_only_latency": latency_summary([{"duration_ms": d["native_elapsed_ms"]} for d in diagnostics.values()]),
             "native_only_latency_scope": "Completed native envelopes with recorded native wall time. Failed/partial attempts remain in full-adapter latency and quality denominators.",
             "tokens": tokens_summary(list(rows.values())), "runtime": protocol["runtime"],
             "sessions": loaded["sessions"], "raw_source": loaded["evidence"],
             "native_probability_scope": "Unmodified official calibrated distributions, no floor or re-softmax; one API call per observation."}
    model["diagnostics"] = native_distribution_diagnostics(cases, rows)
    for task in ("memory", "commander"):
        for language in ("ja", "en"):
            selected = [case for case in cases if case["suite"] == task and case["language"] == language]
            model["tasks"][task][language]["native_probability_diagnostics"] = native_distribution_diagnostics(selected, rows)
    report = {"schema_version": 1, "exploratory": True, "created_utc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
              "protocol": file_descriptor(protocol_path), "models": {MODEL_KEY: model},
              "baseline_summary": {name: task_summaries(cases, values) for name, values in baselines.items()},
              "sources": {"cases": file_descriptor(cases_path), "baseline_paired_csv": file_descriptor(paired_csv),
                          "baseline_additional_csv": file_descriptor(additional_csv), "baseline_native_reports": baseline_evidence,
                          "baseline_statistics": copy.deepcopy(protocol["baseline_sources"]),
                          "analysis_entrypoint": file_descriptor(__file__),
                          "saif_entrypoint": copy.deepcopy(loaded["identity"]["entrypoint_source"]),
                          "statistical_helpers": file_descriptor(Path(__file__).with_name("analyze_additional_candidates.py"))},
              "limitations": [
                  "Exploratory reuse of the previously published fixed synthetic generator; no new confirmatory p-values or amendment to the previous Holm2 family.",
                  "Japanese500 worlds per task and English100 paired translations are separate. Overall combines inference attempts only, without pooled CI or paired inference.",
                  "All six paired-baseline intervals are pointwise conservative95% CP projections and are not multiplicity-adjusted or proof of equivalence.",
                  "Formal memory relevance and supplied-plan constraint/rank tests are not live-game success, free planning or future prediction.",
                  "Explicit failed observations remain incorrect. Missing, duplicate, foreign, changed or incomplete observations are rejected.",
                  "Native published temperatures are retained. Probabilities express conditional typed decisions, not success probabilities in the game.",
                  "Model formats, prompts, precision and historical CPU allocations differ. Recorded latency is a pipeline observation, not architecture-only superiority or Unity performance.",
                  "Token-layout counts/hashes are preserved and structurally validated; the analyzer does not load a tokenizer/model or perform another forward pass."]}
    if caller_facts is not None:
        report["caller_facts"] = _read(caller_facts)
        report["sources"]["caller_facts"] = file_descriptor(caller_facts)
        report["caller_facts_scope"] = "Caller-supplied CPU/resource/environment evidence; not used to select cases, compute predictions or modify statistical results."
    return report, csv_rows(cases, rows, baselines, diagnostics)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--protocol", type=Path, required=True)
    parser.add_argument("--protocol-sha256", required=True)
    parser.add_argument("--cases", type=Path, required=True)
    parser.add_argument("--run", type=Path, required=True)
    parser.add_argument("--baseline-paired-csv", type=Path, required=True)
    parser.add_argument("--baseline-additional-csv", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--csv", type=Path, required=True)
    parser.add_argument("--caller-facts", type=Path)
    args = parser.parse_args()
    report, rows = analyze(args.cases, args.run, args.protocol, args.protocol_sha256,
                           args.baseline_paired_csv, args.baseline_additional_csv, args.caller_facts)
    args.csv.parent.mkdir(parents=True, exist_ok=True)
    temporary = args.csv.with_suffix(args.csv.suffix + ".partial")
    with temporary.open("w", encoding="utf-8", newline="") as target:
        writer = csv.DictWriter(target, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)
    temporary.replace(args.csv)
    report["case_level_csv"] = file_descriptor(args.csv)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    shared.atomic_json(args.output, report)
    print(json.dumps({"output": str(args.output), "cases": len(rows),
                      "tasks": {task: {lang: (value["n"], value["correct"]) for lang, value in groups.items()}
                                for task, groups in report["models"][MODEL_KEY]["tasks"].items()}}, indent=2))


if __name__ == "__main__":
    main()
