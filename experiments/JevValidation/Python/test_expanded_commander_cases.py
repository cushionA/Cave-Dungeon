"""Formal and model-boundary checks for expanded commander diagnostics."""
import copy
import hashlib
import json
import unittest
from collections import Counter, defaultdict

import expanded_commander_cases as corpus


class ExpandedCommanderTests(unittest.TestCase):
    def test_DefaultCorpus_HasIndependentWorldsAndLinkedTranslations(self):
        cases = corpus.generate_cases()
        primary = [case for case in cases if case["language"] == "ja"]
        secondary = [case for case in cases if case["language"] == "en"]
        self.assertEqual((len(primary), len(secondary)), (300, 100))
        self.assertEqual(len(corpus.FAMILIES), 25)
        self.assertEqual(set(Counter(case["family"] for case in primary).values()), {12})
        self.assertEqual(len({case["scenario_id"] for case in primary}), 300)
        self.assertEqual(len({case["audit"]["formal_world_sha256"] for case in primary}), 300)
        by_id = {case["scenario_id"]: case for case in primary}
        for case in secondary:
            original = by_id[case["scenario_id"]]
            self.assertEqual(case["expected"], original["expected"])
            self.assertEqual(case["audit"], original["audit"])

    def test_ConfirmatoryCorpus_Has500PrimaryWorldsAndBalancedPositions(self):
        cases = corpus.generate_cases(worlds_per_family=20, english_per_family=4)
        primary = [case for case in cases if case["language"] == "ja"]
        self.assertEqual(len(cases), 600)
        self.assertEqual(len(primary), 500)
        self.assertEqual(len({case["scenario_id"] for case in primary}), 500)
        self.assertEqual(len({case["audit"]["formal_world_sha256"] for case in primary}), 500)
        self.assertEqual(len({case["audit"]["semantic_world_sha256"] for case in primary}), 500)
        position_counts = defaultdict(Counter)
        quota_profiles = set()
        for case in primary:
            gold = case["expected"]["plan"]
            order = list(case["request"]["questions"]["plan"]["criteria"])
            position_counts[case["family"]][order.index(gold)] += 1
            if case["family"] == "exact_role_quota":
                quota_profiles.add(tuple(sorted(case["audit"]["world"]["role_counts"].items())))
        self.assertTrue(all(set(count.values()) == {5} for count in position_counts.values()))
        self.assertEqual(len(quota_profiles), 20)

    def test_Gold_IsUniqueFormalOptimumAndOrderIsBalanced(self):
        positions = defaultdict(Counter)
        for case in corpus.generate_cases():
            audit = case["audit"]
            world, plans = audit["world"], audit["plans"]
            evaluated = {key: corpus.evaluate_plan(world, plan) for key, plan in plans.items()}
            gold = corpus.choose_plan(world, plans)
            self.assertEqual(case["expected"], {"plan": gold})
            self.assertTrue(evaluated[gold]["feasible"])
            self.assertEqual(audit["plan_evaluations"], evaluated)
            self.assertEqual(audit["normalized_regret"][gold], 0.0)
            self.assertEqual(set(audit["normalized_regret"]), set(plans))
            if case["language"] == "ja":
                order = list(case["request"]["questions"]["plan"]["criteria"])
                positions[case["family"]][order.index(gold)] += 1
        for count in positions.values():
            self.assertLessEqual(max(count.values()) - min(count.values()), 1)

    def test_Oracle_RejectsIncompleteAssignmentsAndChecksNumericalBoundary(self):
        world = {"units": {"u1": {"capacity": 9}, "u2": {"capacity": 10}, "u3": {"capacity": 12}},
                 "role_counts": {"escort": 1, "block": 1, "reserve": 1},
                 "rules": [{"op": "each_ge", "role": "escort", "attribute": "capacity", "value": 10}],
                 "objective": None}
        good = {"assignments": {"u1": "block", "u2": "escort", "u3": "reserve"}}
        self.assertTrue(corpus.evaluate_plan(world, good)["feasible"])
        bad = {"assignments": {"u1": "escort", "u2": "block", "u3": "reserve"}}
        self.assertFalse(corpus.evaluate_plan(world, bad)["feasible"])
        missing = copy.deepcopy(good)
        del missing["assignments"]["u3"]
        self.assertFalse(corpus.evaluate_plan(world, missing)["feasible"])

    def test_Oracle_UsesDeclaredWeightedAndLexicographicObjectives(self):
        world = {"units": {"u1": {"power": 9, "speed": 1}, "u2": {"power": 5, "speed": 8},
                           "u3": {"power": 2, "speed": 9}},
                 "role_counts": {"block": 1, "escort": 1, "reserve": 1}, "rules": [],
                 "objective": {"kind": "weighted", "terms": [
                     {"role": "block", "attribute": "power", "weight": 2},
                     {"role": "escort", "attribute": "speed", "weight": 1}]}}
        plans = {"x": {"assignments": {"u1": "block", "u2": "escort", "u3": "reserve"}},
                 "y": {"assignments": {"u1": "reserve", "u2": "block", "u3": "escort"}}}
        self.assertEqual(corpus.choose_plan(world, plans), "x")
        world["objective"] = {"kind": "lexicographic", "terms": [
            {"role": "escort", "attribute": "speed", "weight": 1},
            {"role": "block", "attribute": "power", "weight": 1}]}
        self.assertEqual(corpus.choose_plan(world, plans), "y")
        self.assertLess(corpus.normalized_regret(world, plans)["y"], corpus.normalized_regret(world, plans)["x"])

    def test_ModelBoundary_IsLabelFreeAndFullyDeterministic(self):
        cases = corpus.generate_cases(seed=123, worlds_per_family=2, english_per_family=1)
        self.assertEqual(cases, corpus.generate_cases(seed=123, worlds_per_family=2, english_per_family=1))
        self.assertNotEqual(cases, corpus.generate_cases(seed=124, worlds_per_family=2, english_per_family=1))
        for case in cases:
            self.assertEqual(set(case["request"]), {"state", "questions"})
            self.assertIsInstance(case["request"]["state"], str)
            self.assertNotIn("audit", case["request"])
            self.assertNotIn("expected", case["request"])
            question = case["request"]["questions"]["plan"]
            self.assertEqual(set(question), {"type", "instructions", "criteria"})
            self.assertTrue(3 <= len(question["criteria"]) <= 6)
            self.assertEqual(set(question["criteria"]), set(case["audit"]["plans"]))
            expected_hash = hashlib.sha256(json.dumps(case["audit"]["world"], sort_keys=True,
                                                      separators=(",", ":")).encode()).hexdigest()
            self.assertEqual(expected_hash, case["audit"]["formal_world_sha256"])

    def test_TranslationAndTotalCounts_DoNotChangeTheSamePrimaryWorld(self):
        small = corpus.generate_cases(seed=71, worlds_per_family=2, english_per_family=0)
        larger = corpus.generate_cases(seed=71, worlds_per_family=4, english_per_family=2)
        lookup = {case["case_id"]: case for case in larger}
        for case in small:
            self.assertEqual(case, lookup[case["case_id"]])

    def test_SemanticWorldSignature_IgnoresArbitraryUnitNamesAndRuleOrder(self):
        case = corpus.generate_cases(worlds_per_family=1, english_per_family=0)[0]
        world = case["audit"]["world"]
        mapping = {unit: f"renamed{index}" for index, unit in enumerate(world["units"])}
        renamed = copy.deepcopy(world)
        renamed["units"] = {mapping[unit]: copy.deepcopy(values) for unit, values in world["units"].items()}
        renamed["rules"] = list(reversed(renamed["rules"]))
        self.assertEqual(corpus.semantic_world_sha256(world), corpus.semantic_world_sha256(renamed))
        next(iter(renamed["units"].values()))["capacity"] += 1
        self.assertNotEqual(corpus.semantic_world_sha256(world), corpus.semantic_world_sha256(renamed))

    def test_OrderedPlans_ExplicitlyStatePhaseRosterExcludingReserve(self):
        for case in corpus.generate_cases(worlds_per_family=1, english_per_family=1):
            if case["family"] != "ordered_prerequisites":
                continue
            if case["language"] == "en":
                self.assertIn("Required phases: scout,unlock,heal,escort; each exactly once.", case["request"]["state"])
            else:
                self.assertIn("手順対象:偵察,解錠,治療,護送。各手順を必ず1回ずつ含める。", case["request"]["state"])


if __name__ == "__main__":
    unittest.main()
