import copy
import json
import unittest

import prepare_expanded_diagnostics as prepare


class ExpandedProjectionTests(unittest.TestCase):
    def test_MetadataAliases_RejectConflictingWorlds(self):
        with self.assertRaises(ValueError):
            prepare.canonical_case({"scenario_id": "a", "world_id": "b", "family": "f"})

    def test_Projection_PreservesExactNativeRequestAndKeepsAuditOutside(self):
        original = {"case_id": "x", "scenario_id": "w", "family": "f", "suite": "memory",
                    "request": {"state": "facts", "questions": {"q": {"type": "noul", "instructions": "ask"}}},
                    "expected": {"q": True}, "audit": {"secret_gold": True}}
        result = prepare.canonical_case(original)
        self.assertEqual(result["request"], original["request"])
        self.assertEqual(result["world_id"], "w")
        self.assertEqual(result["family_id"], "f")
        result["audit"]["secret_gold"] = False
        self.assertTrue(original["audit"]["secret_gold"])

    def test_NeedleProjection_SeparatesGoldAndPreservesEveryInputFact(self):
        rows = prepare.commands.generate_cases()
        suite = prepare.needle_suite(rows, "ja")
        originals = {row["case_id"]: row for row in rows}
        for row in suite["cases"]:
            original = originals[row["case_id"]]
            evidence = copy.deepcopy(original["input"])
            tools = evidence.pop("tools")
            instruction = evidence.pop("instruction")
            self.assertEqual(suite["tool_schemas"], tools)
            self.assertEqual(json.loads(suite["system"]), evidence)
            self.assertEqual(row["prompt"], instruction)
            self.assertEqual(row["expected_calls"], [original["expected"]])


if __name__ == "__main__":
    unittest.main()
