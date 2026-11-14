"""Checks for formal memory relevance and frozen bilingual projection."""
import copy
from collections import Counter
import unittest

import expanded_memory_cases as memory


class ExpandedMemoryCasesTests(unittest.TestCase):
    def test_DefaultCorpus_HasBalancedSemanticWorldsAndLinkedTranslations(self):
        cases = memory.generate_cases()
        ja = [row for row in cases if row["language"] == "ja"]
        en = [row for row in cases if row["language"] == "en"]
        self.assertEqual((len(ja), len(en)), (300, 100))
        self.assertEqual(len({row["scenario_id"] for row in ja}), 300)
        self.assertEqual(Counter(row["family"] for row in ja), {name: 12 for name in memory.FAMILY_IDS})
        for family in memory.FAMILY_IDS:
            group = [row for row in ja if row["family"] == family]
            self.assertEqual(sum(row["expected"]["relevant"] for row in group), 6)
        lookup = {row["scenario_id"]: row for row in ja}
        for row in en:
            self.assertEqual(row["expected"], lookup[row["scenario_id"]]["expected"])
            self.assertEqual(row["audit"], lookup[row["scenario_id"]]["audit"])
        self.assertEqual(len({row["audit"]["world_sha256"] for row in ja}), 300)

    def test_GoldIsComputedByAnswerSetAndNotByReportPresence(self):
        cases = memory.generate_cases()
        for row in cases:
            audit = row["audit"]
            self.assertIs(memory.relevance_oracle(audit["world"]), row["expected"]["relevant"])
            self.assertEqual(set(audit["answers_before"]) != set(audit["answers_after"]), row["expected"]["relevant"])
        negative = next(row for row in cases if row["audit"]["mode"] == "duplicate")
        world = copy.deepcopy(negative["audit"]["world"])
        self.assertEqual(world["memory"]["property"], "p")
        self.assertFalse(memory.relevance_oracle(world))
        before = memory.possible_answers(world)
        world["memory"]["value"] = next(value for value in world["domains"]["p"]
                                           if memory.possible_answers({**world, "current": [
                                               {**fact, "value": value} if fact["property"] == "p" else fact
                                               for fact in world["current"]]}) != before)
        world["memory"]["time"] += 100
        self.assertTrue(memory.relevance_oracle(world))

    def test_OracleHonorsSourceTimeIdentityAndQuotation(self):
        world = memory.generate_cases()[0]["audit"]["world"]
        source = next(row["audit"]["world"] for row in memory.generate_cases() if row["audit"]["mode"] == "fresh")
        self.assertTrue(memory.relevance_oracle(source))
        for key, value in [("kind", "instruction"), ("verified", False), ("condition", False), ("entity", "unrelated")]:
            edited = copy.deepcopy(source)
            edited["memory"][key] = value
            self.assertFalse(memory.relevance_oracle(edited))
        stale = next(row["audit"]["world"] for row in memory.generate_cases() if row["audit"]["mode"] == "superseded")
        self.assertFalse(memory.relevance_oracle(stale))

    def test_FreshOnTopicEvidenceCanBeIrrelevantByDecisionStructure(self):
        cases = memory.generate_cases()
        for mode in ("non_pivotal", "unresolved"):
            relevant_rows = [row for row in cases if row["language"] == "ja" and row["audit"]["mode"] == mode]
            self.assertTrue(relevant_rows)
            for row in relevant_rows:
                world = row["audit"]["world"]
                m = world["memory"]
                self.assertEqual((m["entity"], m["property"], m["kind"], m["verified"], m["condition"]),
                                 (world["entity"], "p", "observation", True, True))
                self.assertFalse(memory.relevance_oracle(world))
                if mode == "unresolved":
                    self.assertEqual(memory.possible_answers(world, True), [False, True])
        self.assertEqual(len({row["mechanism_group"] for row in cases}), 9)

    def test_ProjectionAndSeedReproducibility_ExcludeGoldAndRationale(self):
        first = memory.generate_cases(seed=421)
        self.assertEqual(first, memory.generate_cases(seed=421))
        self.assertNotEqual(first, memory.generate_cases(seed=422))
        for row in first:
            self.assertEqual(set(row["request"]), {"state", "questions"})
            serialized = str(row["request"])
            self.assertNotIn("answers_before", serialized)
            self.assertNotIn("world_sha256", serialized)
            self.assertNotIn("expected", serialized)
            self.assertEqual(set(row["request"]["questions"]["relevant"]["criteria"]), {"false", "true"})
        self.assertTrue(memory.validate_cases(first))

    def test_ValidationRejectsChangedLabelAndBrokenTranslation(self):
        cases = memory.generate_cases()
        cases[0]["expected"]["relevant"] = not cases[0]["expected"]["relevant"]
        with self.assertRaises(ValueError):
            memory.validate_cases(cases)
        for invalid in (0, 3, 11):
            with self.assertRaises(ValueError):
                memory.generate_cases(worlds_per_family=invalid)

    def test_LargerPreregisteredDesign_KeepsBalanceAndSameIdPopulation(self):
        cases = memory.generate_cases(worlds_per_family=20)
        ja = [row for row in cases if row["language"] == "ja"]
        self.assertEqual((len(cases), len(ja)), (600, 500))
        for family in memory.FAMILY_IDS:
            group = [row for row in ja if row["family"] == family]
            self.assertEqual((len(group), sum(row["expected"]["relevant"] for row in group)), (20, 10))
        for row in ja:
            world = row["audit"]["world"]
            self.assertNotEqual(world["entity"], world["other_entity"])
            for entity in (world["entity"], world["other_entity"]):
                self.assertTrue(10 <= int(entity[1:]) < 100)


if __name__ == "__main__":
    unittest.main()
