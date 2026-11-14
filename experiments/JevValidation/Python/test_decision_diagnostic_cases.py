"""Contract checks for the frozen, model-independent diagnostic questions."""
import copy
import unittest

import decision_diagnostic_cases as corpus


class DecisionDiagnosticCaseTests(unittest.TestCase):
    def test_Suites_AreBilingualPairedUniqueAndFreshCopies(self):
        for loader, count in [(corpus.command_cases, 24), (corpus.memory_cases, 24),
                              (corpus.commander_cases, 6)]:
            cases = loader()
            self.assertEqual(len(cases), count)
            self.assertEqual(len({item["case_id"] for item in cases}), count)
            groups = {}
            for item in cases:
                groups.setdefault(item["pair_id"], []).append(item)
            for pair in groups.values():
                self.assertEqual({item["language"] for item in pair}, {"en", "ja"})
                self.assertEqual(pair[0]["expected"], pair[1]["expected"])
            cases[0]["input"]["policy"] = "mutated"
            self.assertNotEqual(loader()[0]["input"]["policy"], "mutated")
            corpus.validate_cases(loader())

    def test_CommandGold_UsesOnlyAvailableToolsAndTypedCatalogTargets(self):
        cases = corpus.command_cases()
        self.assertEqual({case["expected"]["name"] for case in cases},
                         {"follow", "hold_position", "attack", "defend_ally", "retreat",
                          "interact", "scout", "clarify"})
        for case in cases:
            expected = case["expected"]
            tool = next(tool for tool in case["input"]["tools"] if tool["name"] == expected["name"])
            schema = tool["parameters"]
            self.assertEqual(set(expected["arguments"]), set(schema["required"]))
            if expected["arguments"]:
                target = expected["arguments"]["target_id"]
                self.assertIn(target, schema["properties"]["target_id"]["enum"])
                self.assertIn(target, {entity["id"] for entity in case["input"]["entities"]})
            if expected["name"] == "clarify":
                self.assertEqual(case["safe_handling"], "clarify_or_no_calls")
                self.assertEqual(expected["arguments"], {})

    def test_MemoryGold_IsBalancedAndCoversRelevanceBoundaries(self):
        cases = corpus.memory_cases()
        self.assertEqual(sum(case["expected"] is True for case in cases), 12)
        self.assertEqual(sum(case["expected"] is False for case in cases), 12)
        category_labels = {case["category"]: case["expected"] for case in cases}
        self.assertTrue(category_labels["negated_fact"])
        for category in ["superseded", "duplicate", "quoted_instruction", "unrelated_entity", "keyword_distractor"]:
            self.assertFalse(category_labels[category])

    def test_CommanderGold_IsTheOnlyPlanMeetingExplicitConstraints(self):
        for case in corpus.commander_cases():
            feasible = [plan_id for plan_id in case["input"]["plans"]
                        if corpus.plan_satisfies_constraints(case["input"], plan_id)]
            self.assertEqual(feasible, [case["expected"]])
        pairs = [case for case in corpus.commander_cases() if case["language"] == "en"]
        self.assertEqual(len({case["expected"] for case in pairs}), 3)
        self.assertEqual(pairs[0]["input"]["plans"], pairs[1]["input"]["plans"])

    def test_ModelInputs_ExcludeLabelsAndHashChangesWithSemanticEdits(self):
        cases = corpus.command_cases() + corpus.memory_cases() + corpus.commander_cases()
        original = corpus.suite_sha256(cases)
        for case in cases:
            evidence = corpus.model_input(case)
            self.assertEqual(evidence, case["input"])
            self.assertNotIn("expected", evidence)
            self.assertNotIn("rationale", evidence)
            self.assertNotIn("safe_handling", evidence)
        altered = copy.deepcopy(cases)
        altered[0]["expected"] = {"name": "clarify", "arguments": {}}
        self.assertNotEqual(corpus.suite_sha256(altered), original)
        self.assertEqual(corpus.suite_sha256(cases), original)

    def test_Validator_RejectsInvalidGoldAndChangedBilingualLabels(self):
        cases = corpus.command_cases()
        cases[0]["expected"]["arguments"]["target_id"] = "unknown_entity"
        with self.assertRaises(ValueError):
            corpus.validate_cases(cases)
        cases = corpus.memory_cases()
        cases[1]["expected"] = not cases[1]["expected"]
        with self.assertRaises(ValueError):
            corpus.validate_cases(cases)
        case = corpus.commander_cases()[0]
        case["input"]["plans"][case["expected"]]["assignments"]["unit_a"] = "unknown_role"
        self.assertFalse(corpus.plan_satisfies_constraints(case["input"], case["expected"]))


if __name__ == "__main__":
    unittest.main()
