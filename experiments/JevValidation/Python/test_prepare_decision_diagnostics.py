"""Check the model boundary rather than any model's preferred answers."""
import copy
import json
import unittest

import decision_diagnostic_cases as corpus
import prepare_decision_diagnostics as export


class DiagnosticExportTests(unittest.TestCase):
    def test_NeedleBoundary_PreservesLocalizedEvidenceToolsAndGoldSeparately(self):
        for language in ("en", "ja"):
            source = [case for case in corpus.command_cases() if case["language"] == language]
            suite = export.needle_suite(corpus.command_cases(), language)
            self.assertEqual(len(suite["cases"]), 12)
            self.assertEqual(suite["tool_schemas"], corpus.tool_catalog(language))
            for original, row in zip(source, suite["cases"]):
                evidence = copy.deepcopy(original["input"])
                evidence.pop("tools")
                self.assertEqual(row["prompt"], evidence.pop("instruction"))
                self.assertEqual(json.loads(suite["system"]), evidence)
                self.assertEqual(row["expected_calls"], [original["expected"]])

    def test_GoldChanges_CannotChangeEitherModelsEvidence(self):
        for original in corpus.memory_cases() + corpus.commander_cases():
            changed = copy.deepcopy(original)
            changed["expected"] = "unexpected-label"
            self.assertEqual(export.decision_case(original)["request"], export.decision_case(changed)["request"])
        original = corpus.command_cases()
        changed = copy.deepcopy(original)
        changed[0]["expected"] = {"name": "clarify", "arguments": {}}
        first = export.needle_suite(original, "en")["cases"][0]
        second = export.needle_suite(changed, "en")["cases"][0]
        self.assertEqual(first["prompt"], second["prompt"])

    def test_MemoryUsesNativeBooleanKeysAndCommanderRetainsEveryPlan(self):
        for case in corpus.memory_cases():
            row = export.decision_case(case)
            self.assertEqual(row["request"]["state"], case["input"])
            self.assertEqual(set(row["request"]["questions"]["relevant"]["criteria"]), {"false", "true"})
            self.assertIs(type(row["expected"]["relevant"]), bool)
        for case in corpus.commander_cases():
            row = export.decision_case(case)
            plans = row["request"]["questions"]["plan"]["criteria"]
            self.assertEqual({key: json.loads(value) for key, value in plans.items()}, case["input"]["plans"])
            evidence = dict(case["input"])
            evidence.pop("plans")
            self.assertEqual(row["request"]["state"], evidence)


if __name__ == "__main__":
    unittest.main()
