import copy
import json
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest

from compare_needle3 import assess_response, create_agent, load_suite, prepare_tools, run_cases


TOOLS = [
    {"name": "attack", "description": "Attack the specified enemy.", "parameters": {
        "type": "object", "properties": {"target_id": {"type": "string"}},
        "required": ["target_id"], "additionalProperties": False}},
    {"name": "clarify", "description": "Ask for clarification.", "parameters": {
        "type": "object", "properties": {}, "additionalProperties": False}},
]


def response(calls=None, **values):
    return {"success": True, "error": None, "function_calls": calls or [],
            "suppressed_calls": [], "validation": {"ungrounded": [], "negation": False}, **values}


def case(name="attack", arguments=None):
    return {"case_id": "command_1", "language": "en", "prompt": "Attack enemy_red.",
            "expected_calls": [{"name": name, "arguments": arguments if arguments is not None else {"target_id": "enemy_red"}}]}


class NeedleBoundaryTests(unittest.TestCase):
    def test_SuppressedCalls_AreRetainedButNeverExecutedOrPromoted(self):
        hidden = {"name": "attack", "arguments": {"target_id": "enemy_red"}}
        raw = response(suppressed_calls=[hidden], confidence=0.99)
        saved = copy.deepcopy(raw)
        result = assess_response(raw, case(), TOOLS)
        self.assertEqual(result["accepted_calls"], [])
        self.assertTrue(result["abstained"])
        self.assertFalse(result["exact_match"])
        self.assertEqual(raw, saved)

    def test_ErrorEnvelope_BlocksEvenOtherwiseCorrectCalls(self):
        calls = [{"name": "attack", "arguments": {"target_id": "enemy_red"}}]
        for raw in [response(calls, success=False), response(calls, error="native failure")]:
            result = assess_response(raw, case(), TOOLS)
            self.assertFalse(result["response_valid"])
            self.assertEqual(result["accepted_calls"], [])
            self.assertFalse(result["exact_match"])

    def test_GroundingAndNegationFlags_BlockHighConfidenceCalls(self):
        calls = [{"name": "attack", "arguments": {"target_id": "enemy_red"}}]
        for validation in [{"ungrounded": ["target_id"], "negation": False},
                           {"ungrounded": [], "negation": True}]:
            result = assess_response(response(calls, confidence=1.0, validation=validation), case(), TOOLS)
            self.assertTrue(result["blocked_by_validation"])
            self.assertEqual(result["accepted_calls"], [])
            self.assertFalse(result["exact_match"])

    def test_Arguments_KeepCaseAndTypesAndRejectMissingOrExtraFields(self):
        for arguments in [{"target_id": "Enemy_Red"}, {"target_id": 12}, {},
                          {"target_id": "enemy_red", "unrequested": True}]:
            result = assess_response(response([{"name": "attack", "arguments": arguments}]), case(), TOOLS)
            self.assertFalse(result["exact_match"])
        correct = assess_response(response([{"name": "attack", "arguments": {"target_id": "enemy_red"}}]), case(), TOOLS)
        self.assertTrue(correct["exact_match"])

    def test_SingleActionContract_RejectsDuplicatesAndUnknownTools(self):
        attack = {"name": "attack", "arguments": {"target_id": "enemy_red"}}
        for calls in [[attack, attack], [{"name": "invented_action", "arguments": {}}]]:
            result = assess_response(response(calls), case(), TOOLS)
            self.assertEqual(result["accepted_calls"], [])
            self.assertFalse(result["exact_match"])
            self.assertTrue(result["call_validation_errors"])

    def test_Clarification_StrictMatchAndAcceptedActionSafetyStaySeparate(self):
        question = case("clarify", {})
        abstain = assess_response(response(), question, TOOLS)
        self.assertFalse(abstain["exact_match"])
        self.assertTrue(abstain["safe_handling"])
        explicit = assess_response(response([{"name": "clarify", "arguments": {}}]), question, TOOLS)
        self.assertTrue(explicit["exact_match"])
        self.assertTrue(explicit["safe_handling"])
        unsafe = assess_response(response([{"name": "attack", "arguments": {"target_id": "enemy_red"}}]), question, TOOLS)
        self.assertFalse(unsafe["safe_handling"])

    def test_FrozenSuite_OnlyPromptReachesModelAndFailureIsPersisted(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "suite.json"
            system = "Entity enemy_red is a hostile enemy. Select one action."
            path.write_text(json.dumps({"system": system,
                                        "tool_schemas": [{"type": "function", "function": tool} for tool in TOOLS],
                                        "cases": [case(), {**case(), "case_id": "command_2"}]}))
            suite = load_suite(path)
            self.assertEqual(prepare_tools(suite["tool_schemas"]), TOOLS)
            class Fake:
                def __init__(self): self.prompts = []
                def complete(self, prompt, max_new_tokens):
                    self.prompts.append((prompt, max_new_tokens))
                    if len(self.prompts) == 3:
                        raise RuntimeError("fixture failure")
                    return response([{"name": "attack", "arguments": {"target_id": "enemy_red"}}])
            model = Fake()
            opened = []
            def factory(**arguments):
                opened.append(arguments)
                return model
            self.assertIs(create_agent(SimpleNamespace(Cactus=factory), suite, Path(directory) / "model.cact"), model)
            self.assertEqual(opened[0]["system"], system)
            self.assertEqual(set(opened[0]), {"tools", "weights", "stateless", "auto_date", "system"})
            self.assertTrue(opened[0]["stateless"])
            self.assertFalse(opened[0]["auto_date"])
            report = {"status": "running", "results": []}
            output = Path(directory) / "result.json"
            run_cases(model, suite, report, output, max_new_tokens=128)
            self.assertEqual(model.prompts, [("Attack enemy_red.", 128)] * 3)
            saved = json.loads(output.read_text())
            self.assertEqual(saved["status"], "failed")
            self.assertEqual(len(saved["results"]), 1)
            self.assertIn("fixture failure", saved["error"])
            self.assertEqual(saved["results"][0]["case_id"], "command_1")

    def test_OptionalSystem_PreservesLegacyDefaultAndRejectsNonText(self):
        opened = []
        def factory(**arguments):
            opened.append(arguments)
            return object()
        create_agent(SimpleNamespace(Cactus=factory), {"tool_schemas": TOOLS}, Path("model.cact"))
        self.assertNotIn("system", opened[0])
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "suite.json"
            path.write_text(json.dumps({"system": {"secret_label": "attack"},
                                        "tool_schemas": TOOLS, "cases": [case()]}))
            with self.assertRaises(ValueError):
                load_suite(path)


if __name__ == "__main__":
    unittest.main()
