import copy
import io
import json
import math
import unittest
from unittest.mock import patch
import urllib.error

import compare_llamacpp_candidates as candidate


def question(kind="noul", criteria=None):
    return {"type": kind, "instructions": "元の規則に従って判断して。",
            "criteria": criteria if criteria is not None else {"true": "新情報として役立つ。", "false": "役立たない。"}}


def reply(letter="A", **extra):
    return {"choices": [{"index": 0, "finish_reason": "stop", "message": {
        "role": "assistant", "content": letter}}], **extra}


class LlamaCandidateTests(unittest.TestCase):
    def test_BooleanMapping_UsesFalseAAndTrueBDespiteCriteriaOrder(self):
        request = {"state": "橋の情報が更新された。", "questions": {"private_question_id": question()}}
        with patch.object(candidate, "_post_json", side_effect=[reply("A"), reply("B")]) as post:
            first = candidate.predict("http://127.0.0.1:9000", request)
            second = candidate.predict("http://127.0.0.1:9000", request)
        self.assertIs(first["predictions"]["private_question_id"], False)
        self.assertIs(second["predictions"]["private_question_id"], True)
        body = post.call_args.args[1]
        text = body["messages"][1]["content"]
        self.assertLess(text.index("役立たない。"), text.index("新情報として役立つ。"))
        self.assertNotIn("private_question_id", json.dumps(body))

    def test_ChoiceMapping_PreservesOriginalOrderAndDoesNotRepairWrongChoices(self):
        request = {"state": "目的は味方の保護。", "questions": {"private_qid": question("choice", {
            "private_guard_id": "味方を護衛する。", "private_attack_id": "敵を攻撃する。"})}}
        original = copy.deepcopy(request)
        with patch.object(candidate, "_post_json", return_value=reply("B")) as post:
            result = candidate.predict("http://localhost:9000", request)
        self.assertEqual(result["predictions"], {"private_qid": "private_attack_id"})
        serialized = json.dumps(post.call_args.args[1], ensure_ascii=False)
        self.assertNotIn("private_guard_id", serialized)
        self.assertNotIn("private_attack_id", serialized)
        self.assertIn("味方を護衛する。", serialized)
        self.assertEqual(request, original)

    def test_DictionaryStateAndInstructions_ArePreservedWithNativeChatSettings(self):
        state = {"地形": "細い橋", "nested": {"unit_id": "B", "value": 7}, "history": ["撤退", "救出"]}
        request = {"state": state, "questions": {"memory": question()}}
        tasks = candidate.prepare_tasks(request)
        body = tasks[0]["body"]
        user = body["messages"][1]["content"]
        self.assertIn(json.dumps(state, ensure_ascii=False, allow_nan=False), user)
        self.assertIn(request["questions"]["memory"]["instructions"], user)
        self.assertIn("記号1文字", body["messages"][0]["content"])
        self.assertEqual(body["temperature"], 0)
        self.assertEqual(body["seed"], 20261107)
        self.assertEqual(body["max_tokens"], 4)
        self.assertIs(body["cache_prompt"], False)
        self.assertEqual(body["chat_template_kwargs"], {"enable_thinking": False})
        self.assertEqual(body["reasoning_format"], "deepseek")
        self.assertEqual(body["grammar"], 'root ::= "A" | "B"')

    def test_MetadataGoldAndInvalidInputs_AreRejectedBeforeHttp(self):
        valid = {"state": "facts", "questions": {"q": question()}}
        invalid = [{**valid, "expected": {"q": True}}, {**valid, "id": "case_label"},
                   {**valid, "state": {"bad": math.nan}},
                   {**valid, "questions": {"q": {**question(), "answer": True}}},
                   {**valid, "questions": {"q": question("choice", {str(i): str(i) for i in range(5)})}},
                   {**valid, "questions": {"q": question("choice", {"a": {"gold": True}, "b": "other"})}}]
        with patch.object(candidate, "_post_json") as post:
            for request in invalid:
                with self.assertRaises(ValueError):
                    candidate.predict("http://localhost:9000", request)
            for timeout in [0, True, math.nan]:
                with self.assertRaises(ValueError):
                    candidate.predict("http://localhost:9000", valid, timeout_seconds=timeout)
        post.assert_not_called()

    def test_InvalidOrTruncatedResponses_NeverBecomePredictions(self):
        request = {"state": "facts", "questions": {"q": question()}}
        truncated = reply("A")
        truncated["choices"][0]["finish_reason"] = "length"
        thinking = reply("A")
        thinking["choices"][0]["message"]["reasoning_content"] = "unexpected reasoning"
        invalid = [reply("A\n"), reply("AA"), reply("C"), reply("<think></think>B"), truncated, thinking,
                   {"error": {"message": "native failure"}}, {"choices": []}]
        for raw in invalid:
            with patch.object(candidate, "_post_json", return_value=raw):
                with self.assertRaises(candidate.CandidateProtocolError) as raised:
                    candidate.predict("http://localhost:9000", request)
            self.assertEqual(raised.exception.details["calls"][0]["raw"], raw)

    def test_UsageFullPayloadAndFullRaw_AreRetainedWithoutInventedTokenCounts(self):
        request = {"state": "facts", "questions": {"q": question()}}
        raw = reply("B", usage={"prompt_tokens": 42, "completion_tokens": 2, "total_tokens": 44},
                    timings={"prompt_ms": 12.3, "predicted_ms": 4.5}, extra_native_field={"kept": True})
        # llama.cpp's native parser may move a template-generated empty thinking
        # wrapper into this field. The client never strips wrappers itself.
        raw["choices"][0]["message"]["reasoning_content"] = ""
        with patch.object(candidate, "_post_json", return_value=raw):
            result = candidate.predict("http://localhost:9000", request)
        detail = result["details"]["calls"][0]
        self.assertEqual(detail["raw"], raw)
        self.assertEqual(detail["input_tokens"], 42)
        self.assertEqual(detail["output_tokens"], 2)
        self.assertEqual(detail["timings"], raw["timings"])
        self.assertIs(result["details"]["by_question"]["q"], detail)
        with patch.object(candidate, "_post_json", return_value=reply()):
            detail = candidate.predict("http://localhost:9000", request)["details"]["calls"][0]
        self.assertIsNone(detail["input_tokens"])
        self.assertIsNone(detail["output_tokens"])
        json.dumps(candidate.describe(), allow_nan=False)

    def test_LocalHttpErrorsAndTimeouts_PreserveFailedPayload(self):
        request = {"state": "facts", "questions": {"q": question()}}
        http_error = urllib.error.HTTPError("http://localhost:9000/v1/chat/completions", 400, "Bad Request", {}, io.BytesIO(b'{"error":"bad grammar"}'))
        for error in [http_error, TimeoutError("fixture timeout")]:
            with patch("urllib.request.OpenerDirector.open", side_effect=error):
                with self.assertRaises(candidate.CandidateProtocolError) as raised:
                    candidate.predict("http://localhost:9000", request, timeout_seconds=1)
            self.assertIn("messages", raised.exception.details["calls"][0]["body"])
        for url in ["https://example.com", "http://example.com", "http://localhost:9000/?expected=A"]:
            with self.assertRaises(ValueError):
                candidate.predict(url, request)


if __name__ == "__main__":
    unittest.main()
