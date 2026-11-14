"""No models/server: native evidence, strict completion, scoring and interval math."""

import copy
import csv
import json
import math
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

import analyze_additional_candidates as analysis
import compare_llamacpp_candidates as chat
import run_additional_candidates as runner


def memory_case(case_id="one", gold=True):
    return {"case_id": case_id, "world_id": "world_" + case_id, "suite": "memory", "family_id": "fact",
            "mechanism_group": "boolean", "language": "ja", "expected": {"relevant": gold},
            "request": {"state": "The indicator is blue.", "questions": {"relevant": {
                "type": "noul", "instructions": "Is this useful?", "criteria": {"false": "No", "true": "Yes"}}}}}


def chat_row(case, prediction=True, failed=False, index=0):
    task = chat.prepare_tasks(case["request"])[0]
    symbol = next(key for key, value in task["symbol_mapping"].items() if analysis.equal_typed(value, prediction))
    native = {"choices": [{"finish_reason": "stop", "message": {"role": "assistant", "content": symbol}}],
              "usage": {"prompt_tokens": 7, "completion_tokens": 1}}
    call = {**task, "symbol": symbol, "raw": native, "input_tokens": 7, "output_tokens": 1}
    predictions = {} if failed else {next(iter(case["expected"])): prediction}
    row = {"schema_version": 1, "case_index": index, "case_id": case["case_id"], "suite": case["suite"],
           "language": case["language"], "expected": copy.deepcopy(case["expected"]),
           "correct": not failed and runner.predictions_match(predictions, case["expected"]),
           "status": "failed" if failed else "complete", "predictions": predictions,
           "duration_ms": 10.0, "input_tokens": None if failed else 7, "output_tokens": None if failed else 1,
           "error": {"type": "RuntimeError", "message": "fixture"} if failed else None,
           "raw": None if failed else {"predictions": predictions, "details": {"calls": [call]}},
           "request_sha256": runner.digest(case["request"])}
    return runner.seal_record(row)


def unee_row(case, p_true=0.75, all_missing=False):
    row = chat_row(case, p_true >= 0.5)
    entries = [{"token": "X", "logprob": math.log(0.1)}] if all_missing else [
        {"token": "A", "logprob": math.log(p_true)}, {"token": " B", "logprob": math.log(1 - p_true)}]
    floor = min(math.exp(item["logprob"]) for item in entries) * 0.5
    probabilities = {"true": 0.5, "false": 0.5} if all_missing else {"true": p_true, "false": 1 - p_true}
    native = {"choices": [{"logprobs": {"content": [{"top_logprobs": entries}]}}],
              "usage": {"prompt_tokens": 7, "completion_tokens": 1}}
    call = {"question_id": "relevant", "body": unee_prepared(case["request"])[0][1],
            "candidate_ids": ["true", "false"], "probabilities": probabilities,
            "native_floor_probability": floor, "missing_option_letters": ["A", "B"] if all_missing else [],
            "native_answer": {"type": "noul", "noul": round(probabilities["true"], 4)}, "raw": native,
            "input_tokens": 7, "usage": native["usage"]}
    row["raw"] = {"predictions": row["predictions"], "details": {"calls": [call]}}
    return row


def unee_prepared(request):
    """A pure native-projector fixture: no external SDK checkout is needed."""
    return [(SimpleNamespace(qid=qid), {"messages": [{"role": "user", "content": runner.encoded(request).decode()}],
                                      "max_tokens": 1}) for qid in request["questions"]]


class AdditionalCandidateAnalysisTests(unittest.TestCase):
    @patch("compare_unee_candidates.prepare_tasks", side_effect=unee_prepared)
    def test_NativeUnee_TrueFirstAndWhitespaceAggregationPreserveRawThreshold(self, _prepared):
        case = memory_case()
        for probability in (0.499999, 0.5, 0.500001, 0.75):
            row = unee_row(case, probability)
            diagnostic = analysis.native_diagnostics(case, row, "unee")
            self.assertTrue(diagnostic["available"])
            self.assertEqual(0, diagnostic["missing_option_count"])
        broken = unee_row(case)
        broken["raw"]["details"]["calls"][0]["candidate_ids"].reverse()
        with self.assertRaises(ValueError):
            analysis.native_diagnostics(case, broken, "unee")
        broken = unee_row(case)
        broken["raw"]["details"]["calls"][0]["probabilities"]["true"] = 0.74
        with self.assertRaisesRegex(ValueError, "top20/floor"):
            analysis.native_diagnostics(case, broken, "unee")
        broken = unee_row(case)
        broken["raw"]["details"]["calls"][0]["body"]["messages"][0]["content"] += " Gold=true"
        with self.assertRaisesRegex(ValueError, "native body"):
            analysis.native_diagnostics(case, broken, "unee")

    @patch("compare_unee_candidates.prepare_tasks", side_effect=unee_prepared)
    def test_NativeFloor_AllMissingStillCountsAsARealIncorrectObservation(self, _prepared):
        case = memory_case(gold=False)
        row = unee_row(case, 0.5, all_missing=True)
        diagnostic = analysis.native_diagnostics(case, row, "unee")
        self.assertFalse(row["correct"])
        self.assertEqual(2, diagnostic["missing_option_count"])
        self.assertEqual(1, diagnostic["all_options_missing_calls"])
        self.assertEqual(1, diagnostic["selected_option_floored"])
        summary = analysis.group_summary([case], {case["case_id"]: row}, diagnostics={case["case_id"]: diagnostic})
        self.assertEqual(1, summary["n"])
        self.assertEqual(0, summary["correct"])
        self.assertEqual(1, summary["native_probability_diagnostics"]["rows_with_missing_options"])
        self.assertIsNone(summary["memory_confusion"]["false_negative_rate"])

    def test_NativeChat_RejectsPromptChangesExtraReasoningAndTokenMismatch(self):
        case = memory_case()
        row = chat_row(case)
        self.assertTrue(analysis.native_diagnostics(case, row, "chat")["available"])
        for change in ("body", "content", "reasoning", "tokens", "prediction"):
            invalid = copy.deepcopy(row)
            call = invalid["raw"]["details"]["calls"][0]
            if change == "body":
                call["body"]["messages"][1]["content"] += " Gold=true"
            elif change == "content":
                call["raw"]["choices"][0]["message"]["content"] = " B "
            elif change == "reasoning":
                call["raw"]["choices"][0]["message"]["reasoning_content"] = "generated reasoning"
            elif change == "tokens":
                invalid["output_tokens"] = 2
            else:
                invalid["predictions"]["relevant"] = False
            with self.subTest(change=change), self.assertRaises(ValueError):
                analysis.native_diagnostics(case, invalid, "chat")

    def test_BaselineCsv_RejectsChangedLabelsTypesIDsAndCorrectness(self):
        cases = [memory_case("one", True), memory_case("two", False)]
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "baseline.csv"
            rows = []
            for case in cases:
                row = {key: case[key] for key in ("case_id", "world_id", "suite", "family_id", "mechanism_group", "language")}
                row.update(primary=True, gold=json.dumps(case["expected"]))
                for name in analysis.BASELINES:
                    row.update({name + "_status": "complete", name + "_prediction": json.dumps(case["expected"]), name + "_correct": True})
                rows.append(row)

            def write(values):
                with path.open("w", newline="") as target:
                    writer = csv.DictWriter(target, fieldnames=rows[0])
                    writer.writeheader()
                    writer.writerows(values)

            write(rows)
            baseline = analysis.load_baseline_csv(path, cases)
            self.assertTrue(baseline["hindsight"]["one"]["correct"])
            for change in ("gold", "boolean_type", "correct", "foreign", "duplicate", "missing"):
                invalid = copy.deepcopy(rows)
                if change == "gold":
                    invalid[0]["gold"] = '{"relevant":false}'
                elif change == "boolean_type":
                    invalid[0]["tinyjev_prediction"] = '{"relevant":1}'
                elif change == "correct":
                    invalid[0]["hindsight_correct"] = False
                elif change == "foreign":
                    invalid[0]["case_id"] = "foreign"
                elif change == "duplicate":
                    invalid[1] = invalid[0]
                else:
                    invalid.pop()
                write(invalid)
                with self.subTest(change=change), self.assertRaises(ValueError):
                    analysis.load_baseline_csv(path, cases)

    def test_FailuresAndPairedCells_UseAllWorldsWithoutDroppingOrPooling(self):
        cases = [memory_case(str(index), index % 2 == 0) for index in range(4)]
        candidate = {case["case_id"]: chat_row(case, not next(iter(case["expected"].values())) if index in (2, 3)
                                               else next(iter(case["expected"].values())), failed=index == 3)
                     for index, case in enumerate(cases)}
        baseline = {case["case_id"]: {"correct": index in (0, 2)} for index, case in enumerate(cases)}
        summary = analysis.group_summary(cases, candidate, {"baseline": baseline})
        self.assertEqual((4, 2, 1), (summary["n"], summary["correct"], summary["failures"]))
        self.assertEqual(1, summary["memory_confusion"]["failed_expected_false"])
        paired = summary["paired_baselines"]["baseline"]
        self.assertEqual([1, 1, 1, 1], [paired[key] for key in ("both_correct", "candidate_only", "baseline_only", "both_wrong")])
        self.assertEqual(0, paired["difference"])
        self.assertLess(paired["conservative_95"][0], 0)
        self.assertGreater(paired["conservative_95"][1], 0)

    def test_CommanderDiagnostic_SeparatesInfeasibleSuboptimalAndFailure(self):
        case = {"case_id": "plan", "expected": {"plan": "best"}, "audit": {"plans": {"best": {}, "worse": {}, "unsafe": {}},
            "plan_evaluations": {"best": {"feasible": True}, "worse": {"feasible": True}, "unsafe": {"feasible": False}},
            "normalized_regret": {"best": 0, "worse": 0.5, "unsafe": 1}}}
        for selected, status, correct, expected in (
                ("best", "complete", True, ("correct", 0.0)),
                ("worse", "complete", False, ("feasible_but_not_best", 0.5)),
                ("unsafe", "complete", False, ("known_infeasible", 1.0)),
                (None, "failed", False, ("failed_or_invalid", 1.0))):
            row = {"predictions": {"plan": selected} if selected else {}, "status": status, "correct": correct}
            self.assertEqual(expected, analysis.commander_outcome(case, row))

    def test_OverallAttempts_NeverPoolTranslationsForConfidenceIntervals(self):
        cases = [memory_case("one", True), memory_case("two", False)]
        cases[1]["language"] = "en"
        rows = {case["case_id"]: chat_row(case, case["expected"]["relevant"]) for case in cases}
        overall = analysis.attempt_summary(cases, rows)
        self.assertEqual((2, 2), (overall["n"], overall["correct"]))
        self.assertNotIn("wilson_95", overall)
        self.assertNotIn("paired_baselines", overall)
        with self.assertRaisesRegex(ValueError, "translations must never be pooled"):
            analysis.group_summary(cases, rows)

    def test_FullFrozenDesignOracleFixture_PreservesTaskLanguageDenominators(self):
        import prepare_expanded_diagnostics as corpus

        cases = corpus.build_payloads()["decisions.json"]
        analysis.validate_design(cases, 500, 25, 100)
        analysis.validate_oracles(cases)
        rows = {case["case_id"]: {"status": "complete", "predictions": case["expected"], "correct": True,
                                 "duration_ms": 1.0, "input_tokens": 10, "output_tokens": 1} for case in cases}
        bad = {case["case_id"]: {"correct": False} for case in cases}
        groups = analysis.task_summaries(cases, rows, {"all_wrong": bad})
        for task in ("memory", "commander"):
            for language, expected in (("ja", 500), ("en", 100)):
                group = groups[task][language]
                self.assertEqual((expected, expected), (group["n"], group["correct"]))
                self.assertEqual(expected, group["paired_baselines"]["all_wrong"]["candidate_only"])
                self.assertEqual(1, group["paired_baselines"]["all_wrong"]["difference"])
        self.assertEqual(250, groups["memory"]["ja"]["memory_confusion"]["true_positive"])
        self.assertEqual(250, groups["memory"]["ja"]["memory_confusion"]["true_negative"])
        self.assertEqual(125, groups["commander"]["ja"]["commander"]["first_candidate_correct"])
        self.assertEqual(0, groups["commander"]["ja"]["commander"]["mean_normalized_ordinal_rank_loss"])

    def test_WilsonPercentilesAndPairedIntervals_MatchIndependentBoundaryMath(self):
        self.assertAlmostEqual(0.4038315303659957, analysis.wilson_interval(50, 100)[0])
        self.assertAlmostEqual(0.5961684696340044, analysis.wilson_interval(50, 100)[1])
        self.assertEqual(0, analysis.wilson_interval(0, 10)[0])
        self.assertEqual(1, analysis.wilson_interval(10, 10)[1])
        self.assertAlmostEqual(28.5, analysis.percentile([0, 10, 20, 30], 0.95))
        # With no discordances the CP rectangle must retain uncertainty.
        interval = analysis.conservative_paired_interval(0, 0, 20)
        bound = 1 - 0.0125 ** (1 / 20)
        self.assertAlmostEqual(-bound, interval[0])
        self.assertAlmostEqual(bound, interval[1])
        self.assertGreater(interval[1], 0)
        # Independently sum trinomial outcomes for several known paired populations.
        for first_only, second_only in ((0.1, 0.2), (0.4, 0.0), (0.0, 0.0), (0.1, 0.8)):
            n, coverage = 6, 0.0
            delta = first_only - second_only
            for b in range(n + 1):
                for c in range(n - b + 1):
                    rest = n - b - c
                    probability = (math.factorial(n) / math.factorial(b) / math.factorial(c) / math.factorial(rest)
                                   * first_only ** b * second_only ** c * (1 - first_only - second_only) ** rest)
                    lo, hi = analysis.conservative_paired_interval(b, c, n)
                    coverage += probability * (lo - 1e-14 <= delta <= hi + 1e-14)
            self.assertGreaterEqual(coverage, 0.95 - 1e-12)

    def test_StrictCompletedRun_RejectsPartialTamperedGoldAndNativeDisagreement(self):
        cases = [memory_case("one", True), memory_case("two", False)]
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            case_path = root / "cases.json"
            case_path.write_bytes(runner.encoded(cases))
            protocol_path = root / "protocol.json"
            protocol_path.write_text('{"frozen_fixture":true}')
            requests_path = root / "requests.ndjson"
            requests_path.write_bytes(b"".join(runner.encoded({"case_id": c["case_id"], "request": c["request"]}) + b"\n" for c in cases))
            source_root = Path(analysis.__file__).parent
            frozen = [{"path": str(source_root / name), **analysis.descriptor(source_root / name)}
                      for name in ("run_additional_candidates.py", "compare_llamacpp_candidates.py")]
            protocol = {"cases": analysis.descriptor(case_path), "frozen_sources": frozen}
            model = {"key": "lfm25-jp", "base_model_id": "fixture/model", "sha256": "a" * 64}
            identity = {"adapter": "chat", "model_id": model["base_model_id"], "model_sha256": model["sha256"],
                "case_order": [c["case_id"] for c in cases], "total_corpus_cases": len(cases), "limit": None,
                "requests_sha256": runner.digest([c["request"] for c in cases]),
                "gold_sha256": runner.digest([c["expected"] for c in cases]),
                "protocol": analysis.descriptor(protocol_path), "cases_source": analysis.descriptor(case_path),
                "runner_source": frozen[0], "adapter_source": frozen[1]}
            manifest = {"identity": identity, "requests_file": analysis.descriptor(requests_path), "sessions": [{
                "resume_from": 0, "warmup_request": runner.WARMUP_REQUEST,
                "warmup": {"status": "complete", "predictions": {"warmup": "blue"}, "error": None}}]}
            original = [chat_row(case, case["expected"]["relevant"], index=index) for index, case in enumerate(cases)]

            def write(rows, status="complete"):
                runner.atomic_json(root / "manifest.json", manifest)
                (root / "results.ndjson").write_bytes(b"".join(runner.encoded(row) + b"\n" for row in rows))
                result = {"status": status, "error": None, "identity": identity, "sessions": manifest["sessions"],
                    "planned_cases": len(cases), "completed_cases": len(rows), "results": rows,
                    "manifest_sha256": analysis.descriptor(root / "manifest.json")["sha256"],
                    "journal_sha256": analysis.descriptor(root / "results.ndjson")["sha256"]}
                runner.atomic_json(root / "result.json", result)

            write(original)
            loaded = analysis.read_completed_run(root, cases, model, protocol_path, protocol)
            self.assertEqual(2, len(loaded["rows"]))
            for change in ("partial", "gold", "type", "native", "order", "hash"):
                rows = copy.deepcopy(original)
                if change == "partial":
                    rows.pop()
                elif change == "gold":
                    rows[0]["expected"]["relevant"] = False
                elif change == "type":
                    rows[0]["predictions"]["relevant"] = 1
                elif change == "native":
                    rows[0]["raw"]["details"]["calls"][0]["raw"]["choices"][0]["message"]["content"] = "A"
                elif change == "order":
                    rows.reverse()
                else:
                    rows[0]["duration_ms"] = 999
                if change not in {"partial", "hash"}:
                    for row in rows:
                        row.pop("record_sha256")
                        row.update(runner.seal_record(row))
                write(rows)
                with self.subTest(change=change), self.assertRaises(ValueError):
                    analysis.read_completed_run(root, cases, model, protocol_path, protocol)


if __name__ == "__main__":
    unittest.main()
