"""Contract checks for the native Unee adapter; no model or weights are loaded."""
import copy
import json
import math
import unittest
from unittest.mock import patch

import compare_unee_candidates as adapter


def response(entries):
    return {"choices": [{"logprobs": {"content": [{"top_logprobs": [
        {"token": token, "logprob": math.log(prob)} for token, prob in entries]}]}}],
        "usage": {"prompt_tokens": 42, "completion_tokens": 1}}


class NativeUneeAdapterTests(unittest.TestCase):
    def test_missing_candidates_follow_native_floor_and_collapsed_whitespace(self):
        options = [{"key": "x"}, {"key": "y"}, {"key": "z"}]
        raw = response([("A", 0.15), (" A", 0.25), ("B", 0.2), ("other", 0.1)])
        p, missing, floor = adapter.native_probabilities(raw, options)
        self.assertEqual(missing, ["C"])
        self.assertAlmostEqual(floor, 0.05)
        self.assertAlmostEqual(p["x"], 0.4 / 0.65)
        self.assertAlmostEqual(p["y"], 0.2 / 0.65)
        self.assertAlmostEqual(p["z"], 0.05 / 0.65)

    def test_nonfinite_probabilities_cannot_enter_the_journal(self):
        raw = response([("A", 0.6), ("B", 0.3)])
        raw["choices"][0]["logprobs"]["content"][0]["top_logprobs"][0]["logprob"] = float("nan")
        with self.assertRaises(ValueError):
            adapter.native_probabilities(raw, [{"key": "x"}, {"key": "y"}])

    def test_annotated_case_is_rejected_before_any_http_call(self):
        case = {"state": "s", "questions": {"q": {"type": "noul", "instructions": "q"}},
                "expected": {"q": True}}
        with patch.object(adapter, "_post_json") as post:
            with self.assertRaises(ValueError):
                adapter.predict("http://127.0.0.1:8080", case)
            post.assert_not_called()

    def test_native_prompt_has_true_first_and_native_date_facts(self):
        request = {"state": "2026年5月1日から2026年6月5日。", "questions": {
            "q": {"type": "noul", "instructions": "関連するか？",
                  "criteria": {"false": "関連なし", "true": "関連あり"}}}}
        original = copy.deepcopy(request)
        task, body = adapter.prepare_tasks(request)[0]
        user = json.loads(body["messages"][1]["content"])
        self.assertEqual([o["key"] for o in task.options], ["true", "false"])
        self.assertEqual(user["options"][0], {"label": "A", "key": "true", "description": "関連あり"})
        self.assertEqual(user["facts"], ["2026年6月5日 is 35 days after 2026年5月1日"])
        self.assertEqual(set(user), {"question", "options", "state", "facts"})
        self.assertEqual(request, original)

    def test_predict_preserves_opaque_choice_ids_and_raw_response(self):
        request = {"state": {"s": 1}, "questions": {"q": {"type": "choice", "instructions": "選ぶ",
                    "criteria": {"p_93": "第一候補", "p_17": "第二候補"}}}}
        raw = response([("A", 0.2), ("B", 0.7)])
        with patch.object(adapter, "_post_json", return_value=raw) as post:
            result = adapter.predict("http://127.0.0.1:8080", request, timeout_seconds=12)
        self.assertEqual(result["predictions"], {"q": "p_17"})
        call = result["details"]["calls"][0]
        self.assertIs(call["raw"], raw)
        self.assertEqual(call["input_tokens"], 42)
        self.assertEqual(call["missing_option_letters"], [])
        self.assertEqual(post.call_args.args[2], 12)
        self.assertFalse(post.call_args.args[1]["chat_template_kwargs"]["enable_thinking"])

    def test_boolean_threshold_uses_unrounded_native_probability(self):
        request = {"state": "s", "questions": {"q": {"type": "noul", "instructions": "q"}}}
        raw = response([("A", 0.49999), ("B", 0.50001)])
        with patch.object(adapter, "_post_json", return_value=raw):
            result = adapter.predict("http://127.0.0.1:8080", request)
        self.assertFalse(result["predictions"]["q"])
        self.assertEqual(result["details"]["calls"][0]["native_answer"]["noul"], 0.5)


if __name__ == "__main__":
    unittest.main()
