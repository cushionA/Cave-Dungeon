"""Boundary and semantic checks for prespecified expanded command cases."""
import copy
import unittest
from collections import Counter, defaultdict

import expanded_command_cases as corpus


class ExpandedCommandCaseTests(unittest.TestCase):
    def test_DefaultScale_HasOneJapaneseQuestionPerWorldAndLinkedEnglishSubset(self):
        rows = corpus.generate_cases()
        self.assertEqual(Counter(row["language"] for row in rows), {"ja": 96, "en": 32})
        primary = [row for row in rows if row["language"] == "ja"]
        self.assertEqual(len({row["scenario_id"] for row in primary}), 96)
        families = defaultdict(list)
        for row in primary:
            families[row["family"]].append(row)
        self.assertEqual(len(families), 16)
        self.assertTrue(all(len(group) == 6 for group in families.values()))
        self.assertEqual(sum(row["executable"] for row in primary), 72)
        self.assertEqual(sum(row["executable"] for row in rows if row["language"] == "en"), 24)
        by_world = defaultdict(list)
        for row in rows:
            self.assertEqual(row["world_id"], row["scenario_id"])
            self.assertEqual(row["family_id"], row["family"])
            by_world[row["scenario_id"]].append(row)
        for group in by_world.values():
            self.assertEqual(sum(row["language"] == "ja" for row in group), 1)
            if len(group) == 2:
                self.assertEqual(group[0]["expected"], group[1]["expected"])
                self.assertEqual(group[0]["audit"]["world"], group[1]["audit"]["world"])
        self.assertTrue(corpus.validate_cases(rows))

    def test_Worlds_AreDistinctAndTheirSemanticCounterfactualChangesGold(self):
        rows = [row for row in corpus.generate_cases() if row["language"] == "ja"]
        signatures = set()
        for row in rows:
            world = row["audit"]["world"]
            self.assertEqual(corpus.oracle(world), row["expected"])
            signatures.add(corpus.semantic_fingerprint(world))
            changed = corpus.counterfactual(world)
            self.assertNotEqual(corpus.oracle(changed), row["expected"], row["case_id"])
            self.assertNotEqual(corpus.semantic_fingerprint(changed), corpus.semantic_fingerprint(world))
        self.assertEqual(len(signatures), 96)

    def test_NativeInputs_HaveSharedSchemasAndNeverContainGoldOrFormalAst(self):
        rows = corpus.generate_cases()
        shared = {}
        for row in rows:
            evidence = corpus.model_input(row)
            self.assertEqual(set(evidence), {"policy", "instruction", "entities", "tools"})
            for key in ("audit", "expected", "executable", "safe_handling", "command"):
                self.assertNotIn(key, evidence)
            fixed = {key: evidence[key] for key in ("policy", "entities", "tools")}
            shared.setdefault(row["language"], fixed)
            self.assertEqual(shared[row["language"]], fixed)
            changed = copy.deepcopy(row)
            changed["expected"] = {"name": "invented", "arguments": {}}
            changed["audit"]["world"] = {"mutated": True}
            self.assertEqual(corpus.model_input(changed), evidence)
            actual = row["expected"]
            tool = next(tool for tool in evidence["tools"] if tool["name"] == actual["name"])
            self.assertEqual(set(actual["arguments"]), set(tool["parameters"]["required"]))
            if actual["arguments"]:
                self.assertIn(actual["arguments"]["target_id"],
                              tool["parameters"]["properties"]["target_id"]["enum"])

    def test_Oracle_ResolvesFactsConditionsQuotesAndLatestOrderWithoutRenderedText(self):
        world = {"facts": {"x": 4, "y": 8}, "command": {
            "kind": "if", "condition": {"op": "lt", "key": "x", "value": 6},
            "then": {"kind": "call", "name": "attack", "reference": {"kind": "entity", "id": "enemy_archer"}},
            "else": {"kind": "call", "name": "hold_position"}}}
        self.assertEqual(corpus.oracle(world), {"name": "attack", "arguments": {"target_id": "enemy_archer"}})
        world["facts"]["x"] = None
        self.assertEqual(corpus.oracle(world), {"name": "clarify", "arguments": {}})
        world["command"] = {"kind": "quoted", "quoted": {"kind": "unsupported", "action": "heal"},
                            "actual": {"kind": "call", "name": "hold_position"}}
        self.assertEqual(corpus.oracle(world), {"name": "hold_position", "arguments": {}})
        world["command"] = {"kind": "latest", "orders": [
            {"time": 50, "command": {"kind": "call", "name": "hold_position"}},
            {"time": 40, "command": {"kind": "call", "name": "follow", "reference": {"kind": "entity", "id": "player"}}}]}
        self.assertEqual(corpus.oracle(world), {"name": "hold_position", "arguments": {}})

    def test_Generation_IsDeterministicFreshAndSensitiveToSeed(self):
        first = corpus.generate_cases(seed=451)
        self.assertEqual(first, corpus.generate_cases(seed=451))
        self.assertNotEqual(first, corpus.generate_cases(seed=452))
        first[0]["input"]["policy"] = "mutated"
        self.assertNotEqual(first, corpus.generate_cases(seed=451))
        with self.assertRaises(ValueError):
            corpus.generate_cases(worlds_per_family=0)
        with self.assertRaises(ValueError):
            corpus.generate_cases(worlds_per_family=3, english_per_family=4)
        with self.assertRaises(ValueError):
            corpus.generate_cases(worlds_per_family=7)

    def test_ReferenceOracle_UsesUniqueMatchingValuesAndIgnoresCandidatePresentationOrder(self):
        facts = {"enemy_raider": {"distance": 9, "tag": "marked"},
                 "enemy_archer": {"distance": 2, "tag": "plain"}}
        candidates = ["enemy_raider", "enemy_archer"]
        fixtures = [
            ({"kind": "extreme", "operator": "min", "field": "distance", "candidates": candidates}, "enemy_archer"),
            ({"kind": "extreme", "operator": "max", "field": "distance", "candidates": candidates}, "enemy_raider"),
            ({"kind": "match", "field": "tag", "value": "marked", "candidates": candidates}, "enemy_raider"),
            ({"kind": "exclude", "excluded": "enemy_raider", "candidates": candidates}, "enemy_archer"),
        ]
        for reference, expected in fixtures:
            world = {"facts": facts, "command": {"kind": "call", "name": "attack", "reference": reference}}
            self.assertEqual(corpus.oracle(world), {"name": "attack", "arguments": {"target_id": expected}})
            changed = copy.deepcopy(world)
            changed["command"]["reference"]["candidates"].reverse()
            self.assertEqual(corpus.oracle(changed), corpus.oracle(world))
            self.assertEqual(corpus.semantic_fingerprint(changed), corpus.semantic_fingerprint(world))
        tied = copy.deepcopy(world)
        tied["command"]["reference"] = fixtures[0][0]
        tied["facts"]["enemy_raider"]["distance"] = 2
        self.assertEqual(corpus.oracle(tied), {"name": "clarify", "arguments": {}})

    def test_SelectorFactPresentation_CounterbalancesCorrectTargetPosition(self):
        rows = [row for row in corpus.generate_cases() if row["language"] == "ja"]
        families = ("follow_reference", "attack_reference", "defend_reference",
                    "retreat_reference", "interact_reference", "scout_reference", "quoted_order")
        for family in families:
            family_rows = [row for row in rows if row["family"] == family]
            positions = Counter()
            for row in family_rows:
                world = row["audit"]["world"]
                chosen = row["expected"]["arguments"]["target_id"]
                facts = list(world["facts"])
                positions[facts.index(chosen)] += 1
                displayed = row["input"]["instruction"]
                self.assertEqual(sorted(facts, key=displayed.index), facts)
                reversed_world = copy.deepcopy(world)
                reversed_world["facts"] = dict(reversed(list(world["facts"].items())))
                self.assertEqual(corpus.oracle(reversed_world), row["expected"])
                self.assertEqual(corpus.semantic_fingerprint(reversed_world), corpus.semantic_fingerprint(world))
            self.assertEqual(positions, {0: 2, 1: 2, 2: 2} if family == "follow_reference" else {0: 3, 1: 3})

    def test_CodeIdentifierMatches_AreNotAlwaysTheSmallestNumber(self):
        ranks = Counter()
        for row in corpus.generate_cases():
            if row["language"] != "ja" or not row["executable"]:
                continue
            world = row["audit"]["world"]
            command = world["command"]
            if command["kind"] == "quoted":
                command = command["actual"]
            reference = command.get("reference", {})
            if reference.get("field") != "code":
                continue
            values = [facts["code"] for facts in world["facts"].values()]
            self.assertEqual(len(values), len(set(values)))
            chosen = row["expected"]["arguments"]["target_id"]
            wanted = world["facts"][chosen]["code"]
            self.assertEqual(wanted, reference["value"])
            ranks["minimum" if wanted == min(values) else "maximum" if wanted == max(values) else "middle"] += 1
        self.assertEqual(ranks, {"minimum": 3, "maximum": 3, "middle": 1})

    def test_Validator_RejectsAlteredLabelsDuplicateWorldsAndMismatchedTranslation(self):
        rows = corpus.generate_cases()
        altered = copy.deepcopy(rows)
        altered[0]["expected"] = {"name": "clarify", "arguments": {}}
        with self.assertRaises(ValueError):
            corpus.validate_cases(altered)
        altered = copy.deepcopy(rows)
        altered.append(copy.deepcopy(altered[0]))
        with self.assertRaises(ValueError):
            corpus.validate_cases(altered)
        altered = copy.deepcopy(rows)
        translated = next(row for row in altered if row["language"] == "en")
        translated["audit"]["world"]["facts"]["unexpected"] = 7
        with self.assertRaises(ValueError):
            corpus.validate_cases(altered)


if __name__ == "__main__":
    unittest.main()
