"""Check experiment boundaries without loading either pretrained model."""

import copy
import json
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

import compare_intern_decision as comparison
from prepare_strategies import STRATEGIES, _request, catalog_fingerprint, strategy_cases


def intern_response(case, selected="lure_ambush"):
    choices = case["request"]["questions"][case["question_id"]]["criteria"]
    probabilities = {key: 0.65 if key == selected else 0.05 for key in choices}
    return {"answers": {case["question_id"]: {"type": "choice", "choice": selected,
            "confidence": probabilities[selected], "probabilities": probabilities}},
            "usage": {"input_tokens": 637, "output_tokens": 1}, "timing": {"inference_ms": 120.25}}


def result_row(case, probabilities=None):
    probabilities = probabilities or intern_response(case)["answers"][case["question_id"]]["probabilities"]
    selected = max(probabilities, key=probabilities.__getitem__)
    return {"case_id": case["case_id"], "kind": case["kind"], "metadata": case["metadata"],
            "selected_id": selected, "probabilities": probabilities,
            "upstream_inference_ms": 10, "end_to_end_predict_ms": 12}


class InternDecisionComparisonTests(unittest.TestCase):
    def test_Requests_Preserve18OriginalQuestionsAndPermutationWithoutGold(self):
        suite = comparison.build_suite()
        self.assertEqual(18, suite["case_count"])
        self.assertEqual([_request(specification) for specification in strategy_cases()],
                         [case["request"] for case in suite["cases"]])
        self.assertEqual(catalog_fingerprint(STRATEGIES), suite["strategy_catalog_sha256"])
        self.assertEqual(comparison.json_bytes(suite), comparison.json_bytes(comparison.build_suite()))
        original, rotated = suite["cases"][0], suite["cases"][12]
        self.assertEqual(original["request"]["state"], rotated["request"]["state"])
        order = list(STRATEGIES)
        self.assertEqual(order[4:] + order[:4], list(rotated["request"]["questions"]["strategy"]["criteria"]))
        self.assertNotEqual(original["request_sha256"], rotated["request_sha256"])
        for case in suite["cases"]:
            comparison.validate_request(case)
            self.assertEqual({"id", "state", "questions"}, set(case["request"]))
            self.assertEqual({"type", "instructions", "criteria"}, set(case["request"]["questions"]["strategy"]))
            for leak in ("targets", "gold", "answer", "expected"):
                invalid = copy.deepcopy(case)
                invalid["request"]["questions"]["strategy"][leak] = "lure_ambush"
                with self.subTest(leak=leak), self.assertRaises(ValueError):
                    comparison.validate_request(invalid)

    def test_OptionalActions_AppendExactSixQuestionsWithoutChangingStrategies(self):
        from prepare import _request as action_request, load_states

        suite = comparison.build_suite(include_actions=True)
        self.assertEqual(24, suite["case_count"])
        self.assertEqual(comparison.build_suite()["cases"], suite["cases"][:18])
        self.assertEqual([action_request(case_id, state) for case_id, state in load_states()],
                         [case["request"] for case in suite["cases"][18:]])

    def test_ProbabilityBoundary_RejectsMissingInvalidOrUnnormalizedOutput(self):
        case = comparison.build_suite()["cases"][0]
        original = intern_response(case)
        normalized = comparison.validate_intern_result(case, original)
        self.assertEqual("lure_ambush", normalized["selected_id"])
        self.assertEqual(637, normalized["input_tokens"])
        self.assertEqual(120.25, normalized["upstream_inference_ms"])
        changes = ("missing", "extra", "nan", "negative", "bool", "sum", "wrong_choice", "question", "tokens", "time")
        for change in changes:
            invalid = copy.deepcopy(original)
            answer = invalid["answers"]["strategy"]
            if change == "missing":
                answer["probabilities"].pop("observe")
            elif change == "extra":
                answer["probabilities"]["invented"] = 0
            elif change in {"nan", "negative", "bool", "sum"}:
                answer["probabilities"]["observe"] = {"nan": float("nan"), "negative": -0.05, "bool": True, "sum": 0.1}[change]
            elif change == "wrong_choice":
                answer["choice"] = "observe"
            elif change == "question":
                invalid["answers"]["another"] = invalid["answers"].pop("strategy")
            elif change == "tokens":
                invalid["usage"]["input_tokens"] = 0
            else:
                invalid["timing"]["inference_ms"] = float("inf")
            with self.subTest(change=change), self.assertRaises(ValueError):
                comparison.validate_intern_result(case, invalid)
        self.assertEqual(intern_response(case), original)

    def test_TinyJevBoundary_MapsNativeCandidatesAndActualPathLength(self):
        case = comparison.build_suite()["cases"][0]
        answer = intern_response(case)["answers"]["strategy"]
        key = case["case_id"] + ":strategy"
        raw = {key: {"candidate_ids": list(STRATEGIES), "probabilities": list(answer["probabilities"].values()),
                     "answer": answer, "path_token_counts": [459]}}
        normalized = comparison.validate_tinyjev_result(case, raw)
        self.assertEqual(answer["probabilities"], normalized["probabilities"])
        self.assertEqual(459, normalized["input_tokens"])
        self.assertIsNone(normalized["upstream_inference_ms"])
        for change in ("order", "length", "answer", "id"):
            invalid = copy.deepcopy(raw)
            if change == "order":
                invalid[key]["candidate_ids"].reverse()
            elif change == "length":
                invalid[key]["path_token_counts"] = [459, 459]
            elif change == "answer":
                invalid[key]["answer"]["probabilities"]["observe"] = 0
            else:
                invalid["different:strategy"] = invalid.pop(key)
            with self.subTest(change=change), self.assertRaises(ValueError):
                comparison.validate_tinyjev_result(case, invalid)

    def test_Controls_AlignCandidateIdentityAndCountOriginalNine(self):
        suite = comparison.build_suite()
        results = [result_row(case) for case in suite["cases"]]
        # Same probability distribution in a different candidate order is invariant.
        results[12]["probabilities"] = dict(reversed(list(results[0]["probabilities"].items())))
        summary = comparison.summarize(results)
        self.assertEqual({"lure_ambush": 18}, summary["selected_strategy_counts"])
        self.assertEqual(9, summary["original_nine"]["completed_cases"])
        self.assertEqual(9, summary["original_nine"]["kept_current_strategy"])
        self.assertEqual(6, len(summary["no_current_grid"]))
        self.assertEqual(3, len(summary["current_strategy_controls"]))
        self.assertEqual(6, len(summary["personality_goal_current_controls"]))
        self.assertEqual(0, summary["order_control"]["id_aligned_total_variation"])
        self.assertEqual(0, summary["order_control"]["id_aligned_max_probability_difference"])
        altered = dict(results[0]["probabilities"])
        altered["lure_ambush"] -= 0.2
        altered["observe"] += 0.2
        delta = comparison.distribution_delta(results[0]["probabilities"], altered)
        self.assertAlmostEqual(0.2, delta["id_aligned_total_variation"])
        self.assertAlmostEqual(0.2, delta["id_aligned_max_probability_difference"])

    def test_CheckpointProvenance_HashesTemplateAndRejectsChangedBytes(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            template = root / "chat_template.jinja"
            template.write_text("original trained chat template\n")
            expected = comparison.file_sha256(template)
            provenance = root / "download-provenance.json"
            provenance.write_text(json.dumps({"model_id": "test/fake", "revision": "pinned",
                "files": [{"file": template.name, "bytes": template.stat().st_size, "sha256": expected}]}))
            record = comparison.checkpoint_identity(root)
            self.assertEqual(expected, record["files"][template.name]["sha256"])
            self.assertEqual(comparison.file_sha256(provenance), record["download_manifest"]["sha256"])
            template.write_text("changed chat template\n")
            with self.assertRaisesRegex(ValueError, "disagree with download provenance"):
                comparison.checkpoint_identity(root)

    def test_Run_OneWarmupPersistsEachCompletedCaseAndFailure(self):
        cases = comparison.build_suite()["cases"]
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            checkpoint = root / "checkpoint"
            checkpoint.mkdir()
            (checkpoint / "config.json").write_text("{}\n")
            args = SimpleNamespace(output=root / "output", backend="tinyjev", checkpoint=checkpoint,
                model_id="test/fake", revision="pinned", checkpoint_manifest=None, case_limit=3,
                prepare_only=False, include_actions=False)
            calls = []

            def predict(payload):
                calls.append(payload["id"])
                if len(calls) == 4:  # warmup + two completed cases; fail on third timed request
                    saved = json.loads((args.output / "result.json").read_text())
                    self.assertEqual(2, len(saved["results"]))
                    raise ValueError("deliberate invalid output")
                case = next(case for case in cases if case["case_id"] == payload["id"])
                return intern_response(case)

            runner = (predict, comparison.validate_intern_result, {"backend": "test fake"}, 15.0)
            with patch.object(comparison, "load_runner", return_value=runner):
                with self.assertRaisesRegex(ValueError, "deliberate invalid"):
                    comparison.run(args)
            saved = json.loads((args.output / "result.json").read_text())
            self.assertEqual("failed", saved["status"])
            self.assertEqual(2, len(saved["results"]))
            self.assertEqual(1, saved["warmup"]["requests"])
            self.assertEqual([cases[0]["case_id"], cases[0]["case_id"], cases[1]["case_id"], cases[2]["case_id"]], calls)
            self.assertEqual(15, saved["initialization_ms"])
            self.assertEqual("ValueError", saved["failure"]["type"])
            self.assertGreater(saved["memory"]["lifetime_peak_rss_bytes"], 0)
            requests = json.loads((args.output / "requests.json").read_text())
            self.assertEqual(18, requests["case_count"])
            self.assertEqual(saved["inputs_sha256"], requests["inputs_sha256"])
            self.assertEqual(comparison.file_sha256(args.output / "requests.json"), saved["requests_file_sha256"])


if __name__ == "__main__":
    unittest.main()
