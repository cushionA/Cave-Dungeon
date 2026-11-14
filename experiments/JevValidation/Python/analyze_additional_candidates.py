"""Strict descriptive analysis of the four frozen additional candidate pipelines.

This adds exploratory confidence intervals only. It does not change the previous
two-task Holm family, select models, fit thresholds, or discard failed requests.
"""

import argparse
from collections import Counter
import csv
import datetime
import importlib
import json
import math
from pathlib import Path
import statistics

import run_additional_candidates as runner
from analyze_expanded_decisions_repaired import (conservative_paired_interval, load_records,
                                                  score_records, validate_design, wilson_interval)
import expanded_commander_cases as commander_oracle
import expanded_memory_cases as memory_oracle


PROTOCOL_SHA256 = "fa45d4f11a8475a6b78178fc8ee183e62586f508b9e35847699c8a810b741bbd"
MODEL_KEYS = ("unee-0.8b", "unee-2b", "lfm25-jp", "lfm3")
BASELINES = ("hindsight", "tinyjev")
CSV_FIELDS = ("model_key", "model_id", "case_id", "world_id", "suite", "family_id", "mechanism_group",
              "language", "primary", "gold", "predictions", "status", "correct", "failure_type",
              "hindsight_correct", "tinyjev_correct", "hindsight_pair_outcome", "tinyjev_pair_outcome",
              "memory_expected", "memory_prediction", "commander_class", "rank_loss", "duration_ms",
              "input_tokens", "output_tokens", "unee_missing_option_letters", "unee_missing_option_count",
              "unee_calls_with_missing", "unee_selected_option_floored", "record_sha256")


def equal_typed(first, second):
    """JSON booleans and integers must never compare as the same label."""
    return type(first) is type(second) and runner.encoded(first) == runner.encoded(second)


def descriptor(path):
    return runner.file_descriptor(path)


def check_descriptor(path, expected):
    actual = descriptor(path)
    if any(actual[key] != expected[key] for key in ("sha256", "bytes")):
        raise ValueError("Frozen artifact bytes or SHA changed: " + str(path))
    return actual


def verify_protocol(protocol_path, cases_path):
    if descriptor(protocol_path)["sha256"] != PROTOCOL_SHA256:
        raise ValueError("Only the frozen additional-candidates protocol is supported")
    protocol = runner.strict_json(Path(protocol_path).read_text(encoding="utf-8"))
    check_descriptor(cases_path, protocol["cases"])
    original_path = Path(protocol["original_protocol"]["path"])
    check_descriptor(original_path, protocol["original_protocol"])
    original = runner.strict_json(original_path.read_text(encoding="utf-8"))
    repository_root = Path(__file__).resolve().parent.parent
    for record in protocol["frozen_sources"]:
        check_descriptor(repository_root / record["path"], record)
    for name, expected in original["source_files"].items():
        if descriptor(Path(__file__).with_name(name))["sha256"] != expected:
            raise ValueError("An original frozen source changed: " + name)
    check_descriptor(protocol["original_statistics"]["path"], protocol["original_statistics"])
    if (len(protocol["models"]) != 4 or {model["key"] for model in protocol["models"]} != set(MODEL_KEYS)
            or protocol["per_model"]["all"] != 1200):
        raise ValueError("The four-model or full-corpus design differs from the frozen protocol")
    return protocol


def validate_oracles(cases):
    """Validate every supplied label and all commander audits without a model."""
    for case in cases:
        if len(case["request"]["questions"]) != 1 or set(case["expected"]) != set(case["request"]["questions"]):
            raise ValueError("The frozen analysis requires exactly one labeled question per world")
        audit = case.get("audit", {})
        if case["suite"] == "memory":
            world = audit["world"]
            before, after = memory_oracle.possible_answers(world, False), memory_oracle.possible_answers(world, True)
            if (not equal_typed(case["expected"], {"relevant": before != after})
                    or audit["answers_before"] != before or audit["answers_after"] != after):
                raise ValueError("Frozen memory gold disagrees with the finite answer-set oracle")
        elif case["suite"] == "commander":
            world, plans = audit["world"], audit["plans"]
            question = next(iter(case["request"]["questions"].values()))
            if list(plans) != list(question["criteria"]):
                raise ValueError("Commander audit and native candidate identities/order differ")
            gold = commander_oracle.choose_plan(world, plans)
            evaluations = {key: commander_oracle.evaluate_plan(world, plan) for key, plan in plans.items()}
            losses = commander_oracle.normalized_regret(world, plans)
            if (not equal_typed(case["expected"], {"plan": gold}) or audit["plan_evaluations"] != evaluations
                    or audit["normalized_regret"] != losses):
                raise ValueError("Frozen commander gold or diagnostics disagree with the formal oracle")
        else:
            raise ValueError("Only the frozen memory and commander tasks are supported")


def _csv_bool(value):
    if value not in {"True", "False"}:
        raise ValueError("Baseline CSV booleans must be explicit True/False")
    return value == "True"


def load_baseline_csv(path, cases):
    required = {"case_id", "world_id", "suite", "family_id", "mechanism_group", "language", "primary", "gold"}
    for name in BASELINES:
        required |= {name + "_status", name + "_prediction", name + "_correct"}
    with Path(path).open(encoding="utf-8", newline="") as source:
        reader = csv.DictReader(source)
        if reader.fieldnames is None or len(set(reader.fieldnames)) != len(reader.fieldnames) or set(reader.fieldnames) != required:
            raise ValueError("Baseline CSV schema changed")
        rows = list(reader)
    if len(rows) != len(cases):
        raise ValueError("Baseline CSV must contain every frozen case exactly once")
    result = {name: {} for name in BASELINES}
    for case, row in zip(cases, rows):
        if any(row[key] != case[key] for key in ("case_id", "world_id", "suite", "family_id", "mechanism_group", "language")):
            raise ValueError("Baseline CSV IDs/order/world metadata differ from the frozen corpus")
        if _csv_bool(row["primary"]) != (case["language"] == "ja"):
            raise ValueError("Baseline CSV confuses primary worlds and translations")
        if not equal_typed(runner.strict_json(row["gold"]), case["expected"]):
            raise ValueError("Baseline CSV contains a changed gold label")
        for name in BASELINES:
            status = row[name + "_status"]
            predictions = runner.strict_json(row[name + "_prediction"])
            correct = _csv_bool(row[name + "_correct"])
            if status == "complete":
                runner.valid_predictions(case["request"], predictions)
                computed = runner.predictions_match(predictions, case["expected"])
            elif status == "failed" and predictions == {}:
                computed = False
            else:
                raise ValueError("Baseline CSV contains an invalid status or failed prediction")
            if correct != computed:
                raise ValueError("Baseline CSV correctness contradicts native predictions and gold")
            result[name][case["case_id"]] = {"status": status, "predictions": predictions, "correct": computed}
    return result


def verify_baseline_sources(protocol, cases, baselines):
    """Tie the convenience CSV back to the already published native reports."""
    path = Path(protocol["original_statistics"]["path"])
    old_statistics = runner.strict_json(path.read_text(encoding="utf-8"))
    evidence = {}
    for name in BASELINES:
        source = old_statistics["sources"][name]
        evidence[name] = check_descriptor(source["path"], source)
        scored = score_records(cases, load_records(source["path"]))
        for case in cases:
            original, copied = scored[case["case_id"]], baselines[name][case["case_id"]]
            if (original["failed"] != (copied["status"] == "failed") or original["correct"] != copied["correct"]
                    or not equal_typed(original["predictions"], copied["predictions"])):
                raise ValueError("Baseline CSV differs from the frozen native report: " + name)
    return evidence


def native_diagnostics(case, row, adapter):
    """Independently decode saved native evidence, including Unee's floor rule."""
    empty = {"available": False, "calls": 0, "missing_letters": [], "missing_option_count": 0,
             "calls_with_missing": 0, "all_options_missing_calls": 0, "selected_option_floored": 0,
             "floor_probabilities": []}
    if row["status"] == "failed":
        return empty
    raw = row.get("raw")
    if not isinstance(raw, dict) or not equal_typed(raw.get("predictions"), row["predictions"]):
        raise ValueError("Canonical and raw adapter predictions disagree")
    calls = raw.get("details", {}).get("calls")
    questions = case["request"]["questions"]
    if not isinstance(calls, list) or [call.get("question_id") for call in calls] != list(questions):
        raise ValueError("Native calls must preserve every original question exactly once and in order")
    unee_tasks = {}
    if adapter == "unee":
        module = importlib.import_module("compare_unee_candidates")
        unee_tasks = {task.qid: body for task, body in module.prepare_tasks(case["request"])}
    diagnostic = {**empty, "available": True, "calls": len(calls)}
    for call in calls:
        qid = call["question_id"]
        question = questions[qid]
        boolean = question["type"] in {"noul", "boolean"}
        if adapter == "unee":
            if not equal_typed(call.get("body"), unee_tasks[qid]):
                raise ValueError("Unee's native body differs from the frozen prompt/parser/date-facts projection")
            ids = ["true", "false"] if boolean else list(question["criteria"])
            if call.get("candidate_ids") != ids:
                raise ValueError("Unee native candidate mapping differs from its fixed parser")
            native = call["raw"]
            top = native["choices"][0]["logprobs"]["content"][0]["top_logprobs"]
            if not isinstance(top, list):
                raise ValueError("Unee needs the original native top20 token evidence")
            seen = {}
            for item in top:
                logprob, token = item["logprob"], item["token"]
                if type(logprob) not in (int, float) or not math.isfinite(logprob) or logprob > 0 or type(token) is not str:
                    raise ValueError("Invalid native Unee token log probability")
                seen[token.strip()] = seen.get(token.strip(), 0) + math.exp(logprob)
            floor = min(seen.values(), default=1e-6) * 0.5
            letters = list("ABCDEFGHIJKLMNOPQRSTUVWXYZ"[:len(ids)])
            missing = [letter for letter in letters if letter not in seen]
            masses = [seen.get(letter, floor) for letter in letters]
            total = math.fsum(masses)
            if not math.isfinite(total) or total <= 0:
                raise ValueError("Invalid native Unee option mass")
            probabilities = dict(zip(ids, [mass / total for mass in masses]))
            saved = call.get("probabilities")
            if (not isinstance(saved, dict) or list(saved) != ids or
                    any(type(saved[key]) not in (int, float) or not math.isfinite(saved[key]) or
                        not math.isclose(saved[key], probabilities[key], rel_tol=0, abs_tol=1e-12) for key in ids)):
                raise ValueError("Unee reported probabilities disagree with native top20/floor evidence")
            reported_floor = call.get("native_floor_probability")
            if (type(reported_floor) not in (int, float) or not math.isfinite(reported_floor)
                    or not math.isclose(reported_floor, floor, rel_tol=1e-12, abs_tol=0)
                    or call.get("missing_option_letters") != missing):
                raise ValueError("Unee floor/missing-option diagnostics disagree with native evidence")
            prediction = probabilities["true"] >= 0.5 if boolean else max(ids, key=probabilities.__getitem__)
            answer = call.get("native_answer", {})
            if boolean:
                if answer.get("type") != "noul" or answer.get("noul") != round(saved["true"], 4):
                    raise ValueError("Unee's rounded API answer differs from its raw probability evidence")
            elif answer.get("type") != "choice" or answer.get("choice") != prediction:
                raise ValueError("Unee's native choice answer differs from its option probabilities")
            selected_key = ("true" if prediction else "false") if boolean else prediction
            diagnostic["missing_letters"].extend(missing)
            diagnostic["missing_option_count"] += len(missing)
            diagnostic["calls_with_missing"] += bool(missing)
            diagnostic["all_options_missing_calls"] += len(missing) == len(ids)
            diagnostic["selected_option_floored"] += letters[ids.index(selected_key)] in missing
            diagnostic["floor_probabilities"].append(floor)
        elif adapter == "chat":
            module = importlib.import_module("compare_llamacpp_candidates")
            task = next(task for task in module.prepare_tasks(case["request"]) if task["question_id"] == qid)
            for key in ("body", "candidate_ids", "symbol_mapping"):
                if not equal_typed(call.get(key), task[key]):
                    raise ValueError("Chat native prompt/candidate/symbol mapping changed")
            symbol = module._decode(call["raw"], task["symbol_mapping"])
            if call.get("symbol") != symbol:
                raise ValueError("Chat stored symbol disagrees with its unmodified native content")
            prediction = task["symbol_mapping"][symbol]
        else:
            raise ValueError("Unknown candidate adapter")
        if not equal_typed(prediction, row["predictions"][qid]):
            raise ValueError("Native evidence and typed canonical prediction disagree")
    if runner.token_counts(raw) != (row["input_tokens"], row["output_tokens"]):
        raise ValueError("Journal token counts differ from native usage")
    return diagnostic


def read_completed_run(directory, cases, model, protocol_path, protocol):
    directory = Path(directory)
    manifest_path, result_path = directory / "manifest.json", directory / "result.json"
    manifest = runner.strict_json(manifest_path.read_text(encoding="utf-8"))
    report = runner.strict_json(result_path.read_text(encoding="utf-8"))
    rows = runner.read_journal(directory / "results.ndjson", cases)
    if (len(rows) != len(cases) or report.get("status") != "complete" or report.get("error") is not None
            or report.get("planned_cases") != len(cases) or report.get("completed_cases") != len(cases)):
        raise ValueError("Only fully completed runs on every frozen case may be analyzed")
    if report.get("results") != rows or report.get("sessions") != manifest.get("sessions"):
        raise ValueError("Final report differs from its durable journal or sessions")
    identity = manifest["identity"]
    if report.get("identity") != identity:
        raise ValueError("Result and manifest run identities differ")
    if (report.get("manifest_sha256") != descriptor(manifest_path)["sha256"] or
            report.get("journal_sha256") != descriptor(directory / "results.ndjson")["sha256"]):
        raise ValueError("Final manifest or journal hash differs from the saved result")
    if (identity.get("model_id") != model["base_model_id"] or identity.get("model_sha256") != model["sha256"]
            or identity.get("case_order") != [case["case_id"] for case in cases]
            or identity.get("total_corpus_cases") != len(cases) or identity.get("limit") is not None
            or identity.get("requests_sha256") != runner.digest([case["request"] for case in cases])
            or identity.get("gold_sha256") != runner.digest([case["expected"] for case in cases])):
        raise ValueError("Model artifact, selected cases, requests or gold differ from the frozen design")
    check_descriptor(protocol_path, identity["protocol"])
    if any(identity["cases_source"][key] != protocol["cases"][key] for key in ("bytes", "sha256")):
        raise ValueError("Run used a different case file")
    frozen = {Path(record["path"]).name: record for record in protocol["frozen_sources"]}
    adapter = "unee" if model["key"].startswith("unee-") else "chat"
    adapter_name = runner.ADAPTERS[adapter] + ".py"
    for key, filename in (("runner_source", "run_additional_candidates.py"), ("adapter_source", adapter_name)):
        expected = frozen[filename]
        if any(identity[key][field] != expected[field] for field in ("bytes", "sha256")):
            raise ValueError("Run did not use the frozen runner/adapter source")
    if identity.get("adapter") != adapter:
        raise ValueError("Wrong native adapter for this candidate")
    sessions = manifest.get("sessions")
    if not isinstance(sessions, list) or not sessions:
        raise ValueError("A complete run must retain its process sessions and separate warmup")
    prior_resume = -1
    for session in sessions:
        resume = session.get("resume_from")
        warmup = session.get("warmup", {})
        if (type(resume) is not int or not prior_resume <= resume < len(cases)
                or session.get("warmup_request") != runner.WARMUP_REQUEST
                or warmup.get("status") not in {"complete", "failed"}):
            raise ValueError("Run sessions lost the fixed separate warmup or valid resume position")
        if warmup["status"] == "complete":
            runner.valid_predictions(runner.WARMUP_REQUEST, warmup.get("predictions"))
            if warmup.get("error") is not None:
                raise ValueError("Complete warmup contains an execution error")
        elif warmup.get("predictions") != {} or not isinstance(warmup.get("error"), dict):
            raise ValueError("Failed warmup must retain an explicit error without guessed predictions")
        prior_resume = resume
    request_path = directory / "requests.ndjson"
    check_descriptor(request_path, manifest["requests_file"])
    data = request_path.read_bytes()
    if not data.endswith(b"\n"):
        raise ValueError("Saved model requests contain a partial tail")
    requests = [runner.strict_json(line) for line in data.splitlines()]
    if requests != [{"case_id": case["case_id"], "request": case["request"]} for case in cases]:
        raise ValueError("Saved model requests changed inputs or leaked outer labels")
    diagnostics = {}
    for case, row in zip(cases, rows):
        if row.get("suite") != case["suite"] or row.get("language") != case["language"]:
            raise ValueError("Run record task/language metadata differs from the frozen corpus")
        diagnostics[case["case_id"]] = native_diagnostics(case, row, adapter)
    evidence = {name: descriptor(directory / name) for name in
                ("manifest.json", "result.json", "results.ndjson", "requests.ndjson")}
    return {"rows": {row["case_id"]: row for row in rows}, "diagnostics": diagnostics,
            "identity": identity, "sessions": report["sessions"], "evidence": evidence}


def percentile(values, probability):
    if not values:
        return None
    ordered = sorted(values)
    position = (len(ordered) - 1) * probability
    lower, upper = math.floor(position), math.ceil(position)
    return ordered[lower] + (ordered[upper] - ordered[lower]) * (position - lower)


def latency_summary(rows):
    values = [row["duration_ms"] for row in rows if row.get("duration_ms") is not None]
    return {"n": len(values), "mean_ms": statistics.mean(values) if values else None,
            "p50_ms": percentile(values, 0.5), "p95_ms": percentile(values, 0.95)}


def tokens_summary(rows):
    result = {}
    for field in ("input_tokens", "output_tokens"):
        values = [row[field] for row in rows if row.get(field) is not None]
        result[field] = {"known_n": len(values), "missing_n": len(rows) - len(values),
                         "total": sum(values), "mean": statistics.mean(values) if values else None,
                         "min": min(values) if values else None, "max": max(values) if values else None,
                         "p50": percentile(values, 0.5), "p95": percentile(values, 0.95)}
    return result


def commander_outcome(case, row):
    selected = next(iter(row["predictions"].values()), None)
    audit = case["audit"]
    if row["status"] != "complete" or type(selected) is not str or selected not in audit["plans"]:
        return "failed_or_invalid", 1.0
    evaluation = audit["plan_evaluations"][selected]
    if not evaluation["feasible"]:
        kind = "known_infeasible"
    else:
        kind = "correct" if row["correct"] else "feasible_but_not_best"
    return kind, float(audit["normalized_regret"][selected])


def paired_summary(cases, candidate, baseline):
    cells = Counter()
    for case in cases:
        first, second = candidate[case["case_id"]]["correct"], baseline[case["case_id"]]["correct"]
        cells["both_correct" if first and second else "candidate_only" if first else "baseline_only" if second else "both_wrong"] += 1
    n = len(cases)
    b, c = cells["candidate_only"], cells["baseline_only"]
    return {"n": n, **{key: cells[key] for key in ("both_correct", "candidate_only", "baseline_only", "both_wrong")},
            "candidate_correct": cells["both_correct"] + b, "baseline_correct": cells["both_correct"] + c,
            "difference": (b - c) / n, "difference_percentage_points": 100 * (b - c) / n,
            "conservative_95": conservative_paired_interval(b, c, n),
            "interval_method": "Prespecified conservative paired Clopper-Pearson rectangle projection; exploratory, pointwise, not multiplicity-adjusted."}


def probability_summary(cases, diagnostics):
    values = [diagnostics[case["case_id"]] for case in cases] if diagnostics else []
    floors = [floor for value in values for floor in value["floor_probabilities"]]
    return {"rows_with_native_diagnostics": sum(value["available"] for value in values),
            "native_calls": sum(value["calls"] for value in values),
            "rows_with_missing_options": sum(value["missing_option_count"] > 0 for value in values),
            "calls_with_missing_options": sum(value["calls_with_missing"] for value in values),
            "missing_option_count": sum(value["missing_option_count"] for value in values),
            "all_options_missing_calls": sum(value["all_options_missing_calls"] for value in values),
            "selected_option_floored_count": sum(value["selected_option_floored"] for value in values),
            "native_floor_min": min(floors) if floors else None, "native_floor_max": max(floors) if floors else None,
            "scope": "Unee's native top20 missing-letter floor is retained; no rows are removed. Chat pipelines do not expose candidate probabilities."}


def group_summary(cases, rows_by_id, baselines=None, diagnostics=None):
    if len({case["suite"] for case in cases}) != 1 or len({case["language"] for case in cases}) != 1:
        raise ValueError("Intervals require a single task and language; translations must never be pooled")
    rows = [rows_by_id[case["case_id"]] for case in cases]
    n = len(cases)
    correct = sum(row["correct"] for row in rows)
    failures = sum(row["status"] != "complete" for row in rows)
    result = {"n": n, "correct": correct, "accuracy": correct / n, "wilson_95": wilson_interval(correct, n),
              "failures": failures, "valid_predictions": n - failures, "coverage": (n - failures) / n,
              "latency": latency_summary(rows), "tokens": tokens_summary(rows)}
    if baselines:
        result["paired_baselines"] = {name: paired_summary(cases, rows_by_id, baseline) for name, baseline in baselines.items()}
    tasks = {case["suite"] for case in cases}
    if tasks == {"memory"}:
        confusion = Counter()
        for case, row in zip(cases, rows):
            gold = next(iter(case["expected"].values()))
            prediction = next(iter(row["predictions"].values()), None)
            key = ("failed_expected_true" if gold else "failed_expected_false") if row["status"] != "complete" else (
                "true_positive" if gold and prediction else "true_negative" if not gold and not prediction else
                "false_positive" if prediction else "false_negative")
            confusion[key] += 1
        positives = sum(next(iter(case["expected"].values())) for case in cases)
        negatives = n - positives
        result["memory_confusion"] = {key: confusion[key] for key in
            ("true_positive", "true_negative", "false_positive", "false_negative", "failed_expected_true", "failed_expected_false")}
        result["memory_confusion"].update({"actual_positive": positives, "actual_negative": negatives,
            "false_positive_rate": confusion["false_positive"] / negatives if negatives else None,
            "false_negative_rate": confusion["false_negative"] / positives if positives else None,
            "note": "Failures count incorrect in accuracy and remain separate from valid binary confusion cells."})
    if tasks == {"commander"}:
        outcomes = [commander_outcome(case, row) for case, row in zip(cases, rows)]
        counts = Counter(kind for kind, _ in outcomes)
        unsafe = counts["known_infeasible"] + counts["failed_or_invalid"]
        result["commander"] = {key: counts[key] for key in ("correct", "known_infeasible", "feasible_but_not_best", "failed_or_invalid")}
        result["commander"].update({"known_infeasible_rate": counts["known_infeasible"] / n,
            "known_infeasible_wilson_95": wilson_interval(counts["known_infeasible"], n),
            "unsafe_or_invalid": unsafe, "unsafe_or_invalid_rate": unsafe / n,
            "unsafe_or_invalid_wilson_95": wilson_interval(unsafe, n),
            "mean_normalized_ordinal_rank_loss": statistics.mean(loss for _, loss in outcomes),
            "first_candidate_correct": sum(next(iter(case["request"]["questions"]["plan"]["criteria"])) == case["expected"]["plan"] for case in cases),
            "scope": "Ordinal rank among supplied plans; not numerical game utility or free plan generation."})
    if diagnostics is not None:
        result["native_probability_diagnostics"] = probability_summary(cases, diagnostics)
    return result


def attempt_summary(cases, rows_by_id):
    """Describe all inference attempts without treating translations as new worlds."""
    rows = [rows_by_id[case["case_id"]] for case in cases]
    n, correct = len(rows), sum(row["correct"] for row in rows)
    failures = sum(row["status"] != "complete" for row in rows)
    return {"n": n, "correct": correct, "accuracy": correct / n, "failures": failures,
            "valid_predictions": n - failures, "coverage": (n - failures) / n,
            "latency": latency_summary(rows), "tokens": tokens_summary(rows),
            "scope": "Descriptive inference-attempt counts only. Includes translations of the same worlds and different tasks; no pooled-world Wilson interval or paired inference is calculated."}


def task_summaries(cases, rows, baselines=None, diagnostics=None):
    return {task: {language: group_summary([case for case in cases if case["suite"] == task and case["language"] == language],
                                         rows, baselines, diagnostics)
                   for language in ("ja", "en")} for task in ("memory", "commander")}


def csv_outcomes(key, model_id, cases, rows, diagnostics, baselines):
    output = []
    for case in cases:
        row, diagnostic = rows[case["case_id"]], diagnostics[case["case_id"]]
        item = {field: case[field] for field in ("case_id", "world_id", "suite", "family_id", "mechanism_group", "language")}
        item.update(model_key=key, model_id=model_id, primary=case["language"] == "ja",
            gold=runner.encoded(case["expected"]).decode(), predictions=runner.encoded(row["predictions"]).decode(),
            status=row["status"], correct=row["correct"], failure_type=(row.get("error") or {}).get("type"),
            duration_ms=row["duration_ms"], input_tokens=row["input_tokens"], output_tokens=row["output_tokens"],
            record_sha256=row["record_sha256"], unee_missing_option_letters="|".join(diagnostic["missing_letters"]),
            unee_missing_option_count=diagnostic["missing_option_count"], unee_calls_with_missing=diagnostic["calls_with_missing"],
            unee_selected_option_floored=diagnostic["selected_option_floored"])
        for name, baseline in baselines.items():
            good = baseline[case["case_id"]]["correct"]
            item[name + "_correct"] = good
            item[name + "_pair_outcome"] = "both_correct" if row["correct"] and good else "candidate_only" if row["correct"] else "baseline_only" if good else "both_wrong"
        if case["suite"] == "memory":
            item.update(memory_expected=next(iter(case["expected"].values())), memory_prediction=next(iter(row["predictions"].values()), None))
        else:
            item["commander_class"], item["rank_loss"] = commander_outcome(case, row)
        output.append(item)
    return output


def analyze(cases_path, runs, baseline_csv, protocol_path):
    protocol = verify_protocol(protocol_path, cases_path)
    if set(runs) != set(MODEL_KEYS) or len(runs) != 4:
        raise ValueError("Exactly the four preselected model keys are required")
    cases = runner.load_cases(cases_path)
    validate_design(cases, 500, 25, 100)
    validate_oracles(cases)
    baselines = load_baseline_csv(baseline_csv, cases)
    baseline_sources = verify_baseline_sources(protocol, cases, baselines)
    report = {"schema_version": 1, "exploratory": True, "created_utc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        "protocol": descriptor(protocol_path), "sources": {"cases": descriptor(cases_path), "baseline_csv": descriptor(baseline_csv),
            "baseline_native_reports": baseline_sources, "analysis_source": descriptor(__file__),
            "statistical_helpers": descriptor(Path(__file__).with_name("analyze_expanded_decisions_repaired.py"))},
        "baseline_summary": {name: task_summaries(cases, baseline) for name, baseline in baselines.items()},
        "models": {}, "limitations": [
            "Exploratory reuse of the frozen published synthetic generator mix; no new confirmatory p-values or changes to the original two-task Holm family.",
            "Japanese500worlds per task and English100pairedtranslations are reported separately. Translations do not add independent primary worlds.",
            "Confidence intervals are pointwise and conditional on this fixed synthetic family mix; they do not establish model equivalence or real-game strength.",
            "Explicit runtime/parse failures count wrong; missing, duplicate, foreign, changed or incomplete observations are rejected rather than dropped.",
            "Unee native probability floors remain part of its pipeline. LFM3 is tested only in the frozen fast constrained mode, not full-thinking mode.",
            "Prompt formats, quantization and implementations differ. Serial resident-model latency describes these CPU pipelines, not architecture-only superiority or Unity/target-device performance.",
            "Known commander infeasibility, feasible-but-suboptimal choices and failed/invalid choices are distinct; ordinal rank loss is not game utility."]}
    csv_rows = []
    for model in protocol["models"]:
        key = model["key"]
        loaded = read_completed_run(runs[key], cases, model, protocol_path, protocol)
        rows, diagnostics = loaded["rows"], loaded["diagnostics"]
        report["models"][key] = {"identity": {"model_id": model["base_model_id"], "revision": model["revision"],
            "quantization": model["quantization"], "repository": model["repository"], "model_sha256": model["sha256"],
            "run_identity": loaded["identity"]}, "overall": attempt_summary(cases, rows),
            "tasks": task_summaries(cases, rows, baselines, diagnostics), "latency": latency_summary(list(rows.values())),
            "successful_only_latency": latency_summary([row for row in rows.values() if row["status"] == "complete"]),
            "tokens": tokens_summary(list(rows.values())), "diagnostics": probability_summary(cases, diagnostics),
            "runtime": protocol["runtime"], "sessions": loaded["sessions"], "raw_source": loaded["evidence"]}
        csv_rows.extend(csv_outcomes(key, model["base_model_id"], cases, rows, diagnostics, baselines))
    return report, csv_rows


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cases", type=Path, required=True)
    parser.add_argument("--run", action="append", required=True, metavar="NAME=OUTPUTDIR")
    parser.add_argument("--baseline-csv", type=Path, required=True)
    parser.add_argument("--protocol", type=Path, default=Path(__file__).parent.parent / "Results/additional-candidates-protocol.json")
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--csv", type=Path, required=True)
    args = parser.parse_args()
    runs = {}
    for specification in args.run:
        if "=" not in specification:
            parser.error("Each --run must be NAME=OUTPUTDIR")
        name, directory = specification.split("=", 1)
        if name in runs or not directory:
            parser.error("Run names must be unique and directories nonempty")
        runs[name] = Path(directory)
    output, csv_rows = analyze(args.cases, runs, args.baseline_csv, args.protocol)
    args.csv.parent.mkdir(parents=True, exist_ok=True)
    temporary = args.csv.with_suffix(args.csv.suffix + ".partial")
    with temporary.open("w", encoding="utf-8", newline="") as target:
        writer = csv.DictWriter(target, fieldnames=CSV_FIELDS)
        writer.writeheader()
        writer.writerows(csv_rows)
    temporary.replace(args.csv)
    output["case_level_csv"] = descriptor(args.csv)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    runner.atomic_json(args.output, output)
    print(json.dumps({"output": str(args.output), "csv": str(args.csv), "models": list(output["models"]),
                      "case_rows": len(csv_rows), "exploratory": True}), flush=True)


if __name__ == "__main__":
    main()
