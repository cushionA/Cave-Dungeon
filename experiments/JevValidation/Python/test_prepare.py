import copy
import json
import tempfile
import unittest
from pathlib import Path

from prepare import CHOICE_IDS, _request, build_smoke_cases, export_smoke, load_states, validate_case


class PreparedCaseTests(unittest.TestCase):
    def test_SmokeCases_ChangedState_ChangesReferenceProbabilities(self):
        document = build_smoke_cases()
        first, second = document["cases"]
        self.assertEqual("smoke", document["model_id"])
        self.assertNotEqual(first["state_id"], second["state_id"])
        self.assertNotEqual(first["reference_probabilities"], second["reference_probabilities"])
        for case in document["cases"]:
            validate_case(case)
        numeric = {state_id: state for state_id, state in load_states() if state_id.startswith("numeric_")}
        self.assertEqual(2, len(numeric))
        self.assertEqual(numeric["numeric_hp90"].replace("90/100", "5/100"), numeric["numeric_hp5"])

    def test_PreparedCase_TruncatedInput_IsRejected(self):
        case = build_smoke_cases()["cases"][0]
        case["input_ids"].pop()
        with self.assertRaises(ValueError):
            validate_case(case)
        valid = [{"state_id": "a", "state": "Health: 90/100"}, {"state_id": "b", "state": "Health: 5/100"}]
        invalid = [None, {}, [], valid[:1], [valid[0], valid[0]],
                   [valid[0], {"state_id": "b", "state": " "}],
                   [valid[0], {"state_id": "b", "state": 5}],
                   [valid[0], {"state_id": "b", "state": "x", "extra": True}]]
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "states.json"
            path.write_text(json.dumps(valid))
            self.assertEqual([("a", "Health: 90/100"), ("b", "Health: 5/100")], load_states(path))
            for value in invalid:
                path.write_text(json.dumps(value))
                with self.subTest(states=value), self.assertRaises(ValueError):
                    load_states(path)

    def test_PreparedCase_PositionOutsideAttentionMask_IsRejected(self):
        case = build_smoke_cases()["cases"][0]
        case["option_positions"][0] = 255
        with self.assertRaises(ValueError):
            validate_case(case)

    def test_PersonalityPair_OnlyTraitChanges_PreservesCombatAndChoiceInstructions(self):
        states = dict(load_states())
        self.assertEqual(6, len(states))
        aggressive = states["personality_aggressive"]
        cautious = states["personality_cautious"]
        combat, aggressive_trait = aggressive.split(" Personality: ")
        cautious_combat, cautious_trait = cautious.split(" Personality: ")
        self.assertEqual(combat, cautious_combat)
        self.assertIn("Player health: 35/100", combat)
        self.assertIn("Enemy health: 80/100", combat)
        self.assertIn("Enemy distance: 1.5 meters", combat)
        self.assertEqual("Aggressive, risk-tolerant, drawn to confrontation.", aggressive_trait)
        self.assertEqual("Cautious, risk-averse, values safety.", cautious_trait)
        first, second = (_request("personality", state) for state in (aggressive, cautious))
        self.assertEqual(first["questions"], second["questions"])
        self.assertEqual(CHOICE_IDS, list(first["questions"]["action"]["criteria"]))
        first.pop("state")
        second.pop("state")
        self.assertEqual(first, second)

    def test_PreparedCase_InvalidProbabilityDistribution_IsRejected(self):
        for probabilities in ([0.25, 0.25, float("nan"), 0.25], [0.1] * 4, [-0.1, 0.2, 0.4, 0.5]):
            case = copy.deepcopy(build_smoke_cases()["cases"][0])
            case["reference_probabilities"] = probabilities
            with self.assertRaises(ValueError):
                validate_case(case)

    def test_SmokeExport_OnnxExecution_MatchesReferenceForBothStates(self):
        import numpy as np
        import onnxruntime as ort

        with tempfile.TemporaryDirectory() as directory:
            export_smoke(Path(directory))
            document = json.loads((Path(directory) / "cases.json").read_text())
            session = ort.InferenceSession(str(Path(directory) / "smoke.onnx"), providers=["CPUExecutionProvider"])
            for case in document["cases"]:
                inputs = {name: np.asarray(case[name], dtype=np.int32) for name in
                          ("input_ids", "attention_mask", "option_positions", "decision_position")}
                inputs["input_ids"] = inputs["input_ids"].reshape(1, 256)
                inputs["attention_mask"] = inputs["attention_mask"].reshape(1, 256)
                actual = session.run(["probabilities"], inputs)[0]
                np.testing.assert_allclose(actual, case["reference_probabilities"], atol=1e-6)


if __name__ == "__main__":
    unittest.main()
