"""Tests for denominators, paired inference and prespecified boundary cases."""
import json
from pathlib import Path
import tempfile
import unittest

import analyze_expanded_decisions as analysis


def fixture():
    cases, first, second = [], [], []
    for task in ("memory", "commander"):
        for index in range(4):
            for language in (["ja", "en"] if index in (0, 2) else ["ja"]):
                case_id = f"{task}_{index}_{language}"
                expected = True if task == "memory" else "plan_good"
                wrong = False if task == "memory" else "plan_bad"
                cases.append({"case_id": case_id, "world_id": f"{task}_{index}",
                              "family_id": f"family_{index // 2}", "suite": task,
                              "language": language, "expected": {"answer": expected}})
                first.append({"case_id": case_id, "status": "complete",
                              "predictions": {"answer": wrong if index == 3 else expected}})
                second.append({"case_id": case_id, "status": "complete",
                               "predictions": {"answer": expected if index in (1, 3) else wrong}})
    return cases, first, second


class ExpandedAnalysisTests(unittest.TestCase):
    def test_ExactMcNemarAndHolm_UseDiscordantPairsNotMarginalRates(self):
        self.assertEqual(analysis.exact_mcnemar(0, 0), 1)
        self.assertAlmostEqual(analysis.exact_mcnemar(10, 0), 2 / 2 ** 10)
        self.assertEqual(analysis.exact_mcnemar(1, 1), 1)
        self.assertEqual(analysis.holm_adjust([0.04, 0.01]), [0.04, 0.02])
        self.assertEqual(analysis.holm_adjust([0.04, 0.04]), [0.08, 0.08])

    def test_Intervals_PreserveUncertaintyAtZeroAndAllSuccess(self):
        zero = analysis.wilson_interval(0, 100)
        all_correct = analysis.wilson_interval(100, 100)
        self.assertEqual(zero[0], 0)
        self.assertGreater(zero[1], 0)
        self.assertLess(all_correct[0], 1)
        self.assertEqual(all_correct[1], 1)
        self.assertAlmostEqual(analysis.one_sided_upper(0, 96), 0.03072367683353283)
        difference = analysis.conservative_paired_interval(0, 0, 100)
        self.assertLess(difference[0], 0)
        self.assertGreater(difference[1], 0)
        self.assertLess(analysis.conservative_paired_interval(100, 0, 100)[0], 1)

    def test_Power_AccountsForExactTestAndConservativeMultiplicityPlanning(self):
        self.assertAlmostEqual(analysis.exact_mcnemar_power(300, 0.3, 0.1, 0.025), 0.806942, places=5)
        self.assertLess(analysis.exact_mcnemar_power(240, 0.3, 0.1, 0.025), 0.70)
        self.assertAlmostEqual(analysis.exact_mcnemar_power(500, 0.5, 0.1, 0.025), 0.809229, places=5)
        with self.assertRaises(ValueError):
            analysis.exact_mcnemar_power(100, 0.1, 0.2, 0.05)

    def test_Analysis_KeepsTranslationsSecondaryAndRecomputesGold(self):
        cases, first, second = fixture()
        # Deliberately misleading cached correctness must not affect scoring.
        for row in second:
            row["correct"] = True
        result = analysis.analyze(cases, first, second, bootstrap_repetitions=100,
                                  expected_primary_worlds=4, expected_families=2,
                                  expected_english_worlds=2)
        for task in ("memory", "commander"):
            primary = result["tasks"][task]["primary_ja"]
            self.assertEqual(primary["worlds"], 4)
            self.assertEqual(primary["first"]["correct"], 3)
            self.assertEqual(primary["second"]["correct"], 2)
            self.assertEqual(primary["difference_first_minus_second"], 0.25)
            self.assertEqual(primary["discordant_first_only"], 2)
            self.assertEqual(primary["discordant_second_only"], 1)
            self.assertEqual(result["tasks"][task]["secondary_en"]["worlds"], 2)

    def test_RecordBoundary_RejectsMissingDuplicateForeignAndNonBooleanLabels(self):
        cases, first, second = fixture()
        for changed in [first[:-1], first + [first[0]], first + [{"case_id": "foreign"}]]:
            with self.assertRaises(ValueError):
                analysis.analyze(cases, changed, second, bootstrap_repetitions=10,
                                 expected_primary_worlds=4, expected_families=2,
                                 expected_english_worlds=2)
        first[0]["predictions"]["answer"] = 1  # JSON true must not equal numeric one.
        scored = analysis.score_records(cases, first)
        self.assertFalse(scored[cases[0]["case_id"]]["correct"])

    def test_Failure_IsIncorrectEvenIfPartialPredictionsMatchGold(self):
        cases, first, _ = fixture()
        first[0]["status"] = "failed"
        scored = analysis.score_records(cases, first)
        self.assertFalse(scored[cases[0]["case_id"]]["correct"])
        self.assertTrue(scored[cases[0]["case_id"]]["failed"])

    def test_CommanderDiagnostics_SeparatesInfeasibleFromFailedAndUsesOrdinalLoss(self):
        cases, first, _ = fixture()
        selected = [case for case in cases if case["suite"] == "commander" and case["language"] == "ja"]
        rows = [row for row in first if row["case_id"] in {case["case_id"] for case in selected}]
        for case in selected:
            case["audit"] = {
                "plan_evaluations": {"plan_good": {"feasible": True}, "plan_bad": {"feasible": False}},
                "normalized_regret": {"plan_good": 0, "plan_bad": 1}}
        rows[0]["status"] = "failed"
        diagnostics, losses = analysis.commander_diagnostics(selected, analysis.score_records(selected, rows))
        self.assertEqual(diagnostics["known_infeasible_plan_count"], 1)
        self.assertEqual(diagnostics["unknown_or_failed_plan_count"], 1)
        self.assertEqual(diagnostics["unsafe_or_invalid_rate"], 0.5)
        self.assertEqual(diagnostics["mean_normalized_ordinal_plan_rank_loss"], 0.5)
        self.assertEqual(losses[selected[0]["case_id"]], 1)

    def test_NativeRows_ReadBothAdaptersAndCheckProbabilityThreshold(self):
        case = {"case_id": "one", "expected": {"relevant": True}}
        hindsight = [{"case_id": "one", "details": {"relevant": {
            "type": "noul", "selected": True, "probabilities": {"false": 0.2, "true": 0.8}}}}]
        tiny = [{"case_id": "one", "status": "complete", "questions": {"relevant": {
            "type": "noul", "prediction": True, "p_true": 0.8}}}]
        self.assertTrue(analysis.score_records([case], hindsight)["one"]["correct"])
        self.assertTrue(analysis.score_records([case], tiny)["one"]["correct"])
        hindsight[0]["details"]["relevant"]["selected"] = False
        with self.assertRaises(ValueError):
            analysis.score_records([case], hindsight)

    def test_RunnerNative_CrossChecksCanonicalPredictionsAndIgnoresRoundedNoul(self):
        case = {"case_id": "one", "expected": {"relevant": True}}
        row = {"case_id": "one", "status": "complete", "predictions": {"relevant": True},
               "native": {"one:relevant": {"type": "boolean", "candidate_ids": ["false", "true"],
                                           "probabilities": [0.49996, 0.50004],
                                           "answer": {"noul": 0.5, "p_true": 0.50004}}}}
        self.assertTrue(analysis.score_records([case], [row])["one"]["correct"])
        row["predictions"]["relevant"] = False
        with self.assertRaises(ValueError):
            analysis.score_records([case], [row])

    def test_Bootstrap_IsPairedReproducibleAndFamilyDiagnosticsAreExplicit(self):
        rows = [{"family_id": "a", "mechanism_group": "x", "difference": 1},
                {"family_id": "a", "mechanism_group": "y", "difference": 0},
                {"family_id": "b", "mechanism_group": "x", "difference": -1},
                {"family_id": "b", "mechanism_group": "y", "difference": 0}]
        first = analysis.bootstrap_intervals(rows, repetitions=100, seed=7)
        self.assertEqual(first, analysis.bootstrap_intervals(rows, repetitions=100, seed=7))
        flipped = [{**row, "difference": -row["difference"]} for row in rows]
        second = analysis.bootstrap_intervals(flipped, repetitions=100, seed=7)
        for key in ("world_stratified_95", "family_block_95", "mechanism_block_95"):
            self.assertAlmostEqual(first[key][0], -second[key][1])
            self.assertAlmostEqual(first[key][1], -second[key][0])
        leave_out = analysis.leave_family_out(rows)
        self.assertEqual(leave_out["minimum_difference"], -0.5)
        self.assertEqual(leave_out["maximum_difference"], 0.5)

    def test_Needle_UsesExecutableDenominatorAndNeverCountsFailuresAsSafeAbstention(self):
        cases, rows = [], []
        attack = [{"name": "attack", "arguments": {"target_id": "enemy_a"}}]
        clarify = [{"name": "clarify", "arguments": {}}]
        for index in range(4):
            for language in (["ja", "en"] if index in (0, 2) else ["ja"]):
                case_id = f"command_{index}_{language}"
                expected = attack if index < 2 else clarify
                cases.append({"case_id": case_id, "world_id": f"world_{index}",
                              "family_id": "action" if index < 2 else "clarify", "language": language,
                              "expected_calls": expected})
                calls = attack if index == 0 else []
                rows.append({"case_id": case_id, "status": "failed" if index == 3 else "complete",
                             "accepted_calls": calls, "response_valid": index != 3,
                             "raw": {"success": index != 3, "error": "timeout" if index == 3 else None,
                                     "function_calls": calls, "validation": {}}})
        result = analysis.analyze_needle(cases, rows, expected_ja=4, expected_en=2, expected_executable_ja=2, expected_executable_en=1)["languages"]["primary_ja"]
        self.assertEqual(result["executable"]["total"], 2)
        self.assertEqual(result["executable"]["exact_correct"], 1)
        self.assertEqual(result["executable"]["issued_action_coverage"], 0.5)
        self.assertEqual(result["clarify"]["safe_handling_correct"], 1)
        self.assertEqual(result["invalid_or_failed_responses"], 1)
        rows[0]["raw"]["function_calls"] = []
        rows[0]["raw"]["suppressed_calls"] = attack
        with self.assertRaises(ValueError):
            analysis.analyze_needle(cases, rows, expected_ja=4, expected_en=2, expected_executable_ja=2, expected_executable_en=1)

    def test_FileLoader_AcceptsNativeFinalAndNDJSONButNotSilentDuplicates(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            rows = [{"case_id": "one", "predictions": {"q": True}}]
            final = root / "final.json"
            final.write_text(json.dumps({"status": "complete", "results": rows}))
            streaming = root / "rows.ndjson"
            streaming.write_text(json.dumps(rows[0]) + "\n")
            self.assertEqual(analysis.load_records(final), rows)
            self.assertEqual(analysis.load_records(streaming), rows)
            final.write_text(json.dumps({"status": "running", "results": rows}))
            with self.assertRaises(ValueError):
                analysis.load_records(final)


if __name__ == "__main__":
    unittest.main()
