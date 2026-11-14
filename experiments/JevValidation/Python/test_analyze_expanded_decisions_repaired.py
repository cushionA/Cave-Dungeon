"""Integration repair tests using only frozen inputs and synthetic predictions.

No live model output is read. The preregistered source and its original tests stay
unchanged; this repairs only task dispatch for generic oracle-audit metadata.
"""
import copy
import hashlib
import json
from pathlib import Path
import unittest

import analyze_expanded_decisions as preregistered
import analyze_expanded_decisions_repaired as repaired


ROOT = Path(__file__).parent
ORIGINAL_SOURCE_SHA256 = "5ef49a47c5f43aa578e823a59cff0029eb6b46e2d3d53e83dc36e7e89d43a75e"
ORIGINAL_TEST_SHA256 = "83e6af598d72db4374cf513077fa08728dd8c5310044d0fd071f8577a26aba70"


def frozen_inputs(name="decisions.json"):
    path = ROOT / "Artifacts/expanded-diagnostics" / name
    if path.exists():
        return repaired.load_cases(path)
    # Pure deterministic generation permits the same integration check in a clean
    # checkout. This function writes no dataset/protocol or model result files.
    from prepare_expanded_diagnostics import build_payloads
    return build_payloads()[name]


def fake_predictions(cases):
    perfect, wrong = [], []
    for case in cases:
        gold = copy.deepcopy(case["expected"])
        if case["suite"] == "memory":
            other = {key: not value for key, value in gold.items()}
        else:
            question_id, correct = next(iter(gold.items()))
            candidates = case["request"]["questions"][question_id]["criteria"]
            other = {question_id: next(plan for plan in candidates if plan != correct)}
        hindsight_details, tiny_native = {}, {}
        for question_id, selected in gold.items():
            boolean = type(selected) is bool
            candidates = ["false", "true"] if boolean else list(case["request"]["questions"][question_id]["criteria"])
            value = str(selected).lower() if boolean else selected
            hindsight_details[question_id] = {"type": "noul" if boolean else "choice", "selected": selected,
                                              "probabilities": {key: float(key == value) for key in candidates}}
            wrong_value = str(other[question_id]).lower() if boolean else other[question_id]
            tiny_native[case["case_id"] + ":" + question_id] = {
                "type": "boolean" if boolean else "choice", "candidate_ids": candidates,
                "probabilities": [float(key == wrong_value) for key in candidates]}
        perfect.append({"case_id": case["case_id"], "status": "complete", "predictions": gold,
                        "native": {"details": hindsight_details}})
        wrong.append({"case_id": case["case_id"], "status": "complete", "predictions": other,
                      "native": tiny_native})
    return perfect, wrong


class RepairedAnalysisIntegrationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.cases = frozen_inputs()
        cls.perfect, cls.wrong = fake_predictions(cls.cases)

    def test_PreregisteredFiles_RemainByteExact(self):
        self.assertEqual(hashlib.sha256((ROOT / "analyze_expanded_decisions.py").read_bytes()).hexdigest(), ORIGINAL_SOURCE_SHA256)
        self.assertEqual(hashlib.sha256((ROOT / "test_analyze_expanded_decisions.py").read_bytes()).hexdigest(), ORIGINAL_TEST_SHA256)

    def test_RealMemoryOracleAudit_ReproducesOriginalDispatchBug(self):
        memory = next(case for case in self.cases if case["suite"] == "memory")
        self.assertIn("answers_before", memory["audit"])
        self.assertIn("answers_after", memory["audit"])
        self.assertNotIn("plan_evaluations", memory["audit"])
        with self.assertRaisesRegex(ValueError, "Commander plan audit"):
            preregistered.analyze(self.cases, self.perfect, self.wrong, bootstrap_repetitions=10)

    def test_All1200FrozenCases_AnalyzeWithoutReadingModelOutputs(self):
        self.assertEqual(len(self.cases), 1200)
        result = repaired.analyze(self.cases, self.perfect, self.wrong, bootstrap_repetitions=20)
        for task in ("memory", "commander"):
            for language, total in (("primary_ja", 500), ("secondary_en", 100)):
                group = result["tasks"][task][language]
                self.assertEqual(group["worlds"], total)
                self.assertEqual(group["first"]["correct"], total)
                self.assertEqual(group["second"]["correct"], 0)
                self.assertEqual(group["difference_first_minus_second"], 1)
                if task == "commander":
                    self.assertIn("commander_secondary", group["first"])
                else:
                    self.assertNotIn("commander_secondary", group["first"])
        commander = result["tasks"]["commander"]["primary_ja"]
        self.assertEqual(commander["first"]["commander_secondary"]["mean_normalized_ordinal_plan_rank_loss"], 0)
        self.assertGreater(commander["second"]["commander_secondary"]["mean_normalized_ordinal_plan_rank_loss"], 0)

    def test_MethodsAndNumbers_UnchangedWhenGenericMemoryAuditIsIgnored(self):
        no_memory_audit = copy.deepcopy(self.cases)
        for case in no_memory_audit:
            if case["suite"] == "memory":
                del case["audit"]
        original = preregistered.analyze(no_memory_audit, self.perfect, self.wrong, bootstrap_repetitions=20)
        current = repaired.analyze(self.cases, self.perfect, self.wrong, bootstrap_repetitions=20)
        self.assertEqual(current, original)

    def test_PartiallyMissingCommanderAudit_StillRejectsIncompletePlanContract(self):
        for field in ("audit", "plan_evaluations", "normalized_regret"):
            changed = copy.deepcopy(self.cases)
            commander = next(case for case in changed if case["suite"] == "commander")
            if field == "audit":
                del commander["audit"]
            else:
                del commander["audit"][field]
            with self.assertRaises(ValueError):
                repaired.analyze(changed, self.perfect, self.wrong, bootstrap_repetitions=10)

    def test_All128NeedleInputs_AnalyzeCleanSyntheticAbstentionsWithCorrectDenominators(self):
        cases = frozen_inputs("commands.json")
        self.assertEqual(len(cases), 128)
        rows = [{"case_id": case["case_id"], "status": "complete", "response_valid": True,
                 "accepted_calls": [], "raw": {"success": True, "error": None,
                                                "function_calls": [], "validation": {}}}
                for case in cases]
        result = repaired.analyze_needle(cases, rows)
        for language, executable, clarify in (("primary_ja", 72, 24), ("secondary_en", 24, 8)):
            group = result["languages"][language]
            self.assertEqual(group["executable"]["total"], executable)
            self.assertEqual(group["executable"]["exact_correct"], 0)
            self.assertEqual(group["clarify"]["total"], clarify)
            self.assertEqual(group["clarify"]["safe_handling_correct"], clarify)


if __name__ == "__main__":
    unittest.main()
