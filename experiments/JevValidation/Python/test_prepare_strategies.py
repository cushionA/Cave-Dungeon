import copy
import json
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest

from prepare import _sha256, build_smoke_cases, load_states, validate_case
from prepare_gguf import ensure_artifact
from prepare_strategies import STRATEGIES, build_reference_cases, strategy_cases, validate_variant, write_documents


class StrategyPreparationTests(unittest.TestCase):
    def agent(self, length=512):
        family = SimpleNamespace(pad_token_id=0)

        def encode(record):
            keys = list(record["questions"][0]["criteria"])
            row = [2] * (length - 1)
            for position, key in zip(range(10, 90, 10), keys):
                row[position - 1] = list(STRATEGIES).index(key) + 20
            return SimpleNamespace(prefix=[1], rows=[row], questions=[{
                "keys": keys, "opts": list(range(10, 90, 10)), "decide": length - 1}])

        family.encode = encode
        return SimpleNamespace(family=family, backbone=SimpleNamespace(model=SimpleNamespace(config=SimpleNamespace(vocab_size=1000))),
                               logits=lambda payload: {payload["id"] + ":strategy": {
                                   "candidate_ids": list(payload["questions"]["strategy"]["criteria"]),
                                   "probabilities": [(list(STRATEGIES).index(key) + 1) / 36 for key in payload["questions"]["strategy"]["criteria"]]}})

    def test_StrategyInputs_ControlPersonalityGoalWorldAndCurrentStrategySeparately(self):
        cases = strategy_cases()
        self.assertEqual(18, len(cases))
        self.assertEqual(18, len({case["case_id"] for case in cases}))
        self.assertEqual({"cunning", "aggressive", "cautious"}, {case["personality_id"] for case in cases[:6]})
        self.assertEqual({"protect_ally", "reach_exit_alive"}, {case["goal_id"] for case in cases[:6]})
        self.assertTrue(all(case["world"] == cases[0]["world"] for case in cases[:6]))
        self.assertTrue(all(case["current_strategy"] == "lure_ambush" for case in cases[:9]))
        for case in cases[6:9]:
            self.assertEqual(cases[0]["personality"], case["personality"])
            self.assertEqual(cases[0]["goal"], case["goal"])
            self.assertNotEqual(cases[0]["world"], case["world"])
        for case, current in zip(cases[9:12], ("direct_assault", "observe", "none")):
            self.assertEqual(current, case["current_strategy"])
            self.assertEqual({key: value for key, value in cases[0].items() if key not in ("case_id", "current_strategy")},
                             {key: value for key, value in case.items() if key not in ("case_id", "current_strategy")})
        self.assertEqual({key: value for key, value in cases[12].items() if key not in ("case_id", "candidate_order")},
                         {key: value for key, value in cases[0].items() if key != "case_id"})
        self.assertEqual(list(STRATEGIES)[4:] + list(STRATEGIES)[:4], cases[12]["candidate_order"])
        for base, control in zip(cases[1:6], cases[13:]):
            self.assertEqual("none", control["current_strategy"])
            self.assertEqual({key: value for key, value in base.items() if key not in ("case_id", "current_strategy")},
                             {key: value for key, value in control.items() if key not in ("case_id", "current_strategy")})
        self.assertEqual(6, len([case for case in cases if case["current_strategy"] == "none"]))
        for case in cases:
            self.assertFalse(any(choice in case["goal"] for choice in STRATEGIES))
        self.assertEqual(["direct_assault", "lure_ambush", "flank", "guard_ally", "ranged_pressure", "observe", "disengage", "bypass"], list(STRATEGIES))

    def test_EncodedCases_EightChoices512Tokens_RejectsOverflowAndInvalidChoiceOrder(self):
        spec = strategy_cases()[:1]
        case = build_reference_cases(self.agent(), spec)[0]
        self.assertEqual(512, len(case["input_ids"]))
        self.assertEqual([1] * 512, case["attention_mask"])
        self.assertEqual(list(STRATEGIES), case["choice_ids"])
        self.assertEqual(list(range(10, 90, 10)), case["option_positions"])
        self.assertEqual([511], case["decision_position"])
        rotated = build_reference_cases(self.agent(), strategy_cases()[12:13])[0]
        self.assertEqual(list(STRATEGIES)[4:] + list(STRATEGIES)[:4], rotated["choice_ids"])
        for key, position, probability in zip(rotated["choice_ids"], rotated["option_positions"], rotated["reference_probabilities"]):
            self.assertEqual(list(STRATEGIES).index(key) + 20, rotated["input_ids"][position])
            self.assertEqual((list(STRATEGIES).index(key) + 1) / 36, probability)
        with self.assertRaises(ValueError):
            build_reference_cases(self.agent(513), spec)
        agent = self.agent()
        agent.logits = lambda payload: {payload["id"] + ":strategy": {
            "candidate_ids": list(reversed(STRATEGIES)), "probabilities": [0.125] * 8}}
        with self.assertRaises(ValueError):
            build_reference_cases(agent, spec)

    def test_StrategyDocuments_PreserveLegacyFilesAndBindEachModelHash(self):
        cases = build_reference_cases(self.agent(120), strategy_cases())
        with tempfile.TemporaryDirectory() as temporary:
            variants = {}
            for quant in ("F16", "Q8_0", "Q4_K_M"):
                directory = Path(temporary) / quant
                directory.mkdir()
                model = directory / f"tinyjev-{quant}.gguf"
                model.write_bytes(b"GGUF" + quant.encode())
                head = directory / "tinyjev.head"
                head.write_bytes(b"original trained head")
                (directory / "cases.json").write_text("original six-case suite")
                variants[quant] = {"directory": directory, "model": model,
                                   "model_sha256": _sha256(model), "head_sha256": _sha256(head)}
            documents = write_documents(cases, variants, {"backend": "torch"})
            for quant, document in documents.items():
                self.assertEqual(cases, document["cases"])
                self.assertEqual(512, document["token_count"])
                self.assertEqual(variants[quant]["model_sha256"], document["model_sha256"])
                self.assertEqual("original six-case suite", (variants[quant]["directory"] / "cases.json").read_text())
                self.assertEqual(document, json.loads((variants[quant]["directory"] / "cases-strategies.json").read_text()))
            variants["F16"]["model"].write_bytes(b"GGUFchanged")
            with self.assertRaises(ValueError):
                write_documents(cases, variants, {"backend": "torch"})

    def test_TrustedVariant_ChangedModelHeadOrGenerationIdentity_IsRejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            model = directory / "tinyjev-F16.gguf"
            expected = {"source_sha256": {"weights": "fixed"}, "tools": {"converter": "pinned"}, "parent_f16_sha256": None}
            ensure_artifact(model, expected, lambda path: path.write_bytes(b"GGUFtrained"))
            head = directory / "tinyjev.head"
            head.write_bytes(b"trained head")
            head_sha = _sha256(head)
            self.assertEqual(_sha256(model), validate_variant(directory, "F16", expected, head_sha)["model_sha256"])
            model.write_bytes(b"GGUFchanged")
            with self.assertRaises(ValueError):
                validate_variant(directory, "F16", expected, head_sha)
            model.write_bytes(b"GGUFtrained")
            with self.assertRaises(ValueError):
                validate_variant(directory, "F16", {**expected, "parent_f16_sha256": "other"}, head_sha)
            head.write_bytes(b"changed head")
            with self.assertRaises(ValueError):
                validate_variant(directory, "F16", expected, head_sha)

    def test_LegacyDefaults_RemainFourChoices256Tokens(self):
        smoke = build_smoke_cases()
        self.assertEqual(2, len(smoke["cases"]))
        self.assertEqual(6, len(load_states()))
        for case in smoke["cases"]:
            validate_case(case)
        strategy = build_reference_cases(self.agent(), strategy_cases()[:1])[0]
        with self.assertRaises(ValueError):
            validate_case(strategy)
        validate_case(strategy, 1000, token_count=512, choice_count=8)
        broken = copy.deepcopy(strategy)
        broken["option_positions"][1] = broken["option_positions"][0]
        with self.assertRaises(ValueError):
            validate_case(broken, 1000, token_count=512, choice_count=8)


if __name__ == "__main__":
    unittest.main()
