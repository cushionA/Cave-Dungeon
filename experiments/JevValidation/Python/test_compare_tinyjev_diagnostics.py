"""Exercise label safety and scoring without pretrained-model execution."""

import copy
import io
import json
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

import compare_tinyjev_diagnostics as diagnostic


def fixture():
    return {"case_id": "memory_ja", "language": "ja", "category": "new_fact", "pair_id": "memory",
            "request": {"state": {"memory": "門は開いている。", "candidate": "東の門は施錠された。"},
                        "questions": {"retain": {"type": "noul", "instructions": "新しい関連事実を保存する。",
                                                  "criteria": {"false": "保存しない", "true": "保存する"}},
                                      "plan": {"type": "choice", "instructions": "制約を満たす計画を選ぶ。",
                                               "criteria": {"plan_b": "役割を分担する。", "plan_a": "全員で攻撃する。"}}}},
            "expected": {"retain": True, "plan": "plan_b"}}


def response(case, p_true=0.75, choices=None):
    raw = {}
    for question_id, question in case["request"]["questions"].items():
        boolean = question["type"] in ("noul", "boolean")
        ids = ["false", "true"] if boolean else list(question["criteria"])
        probabilities = [1 - p_true, p_true] if boolean else (choices or [0.7, 0.3])
        raw[case["case_id"] + ":" + question_id] = {
            "type": "boolean" if boolean else "choice", "candidate_ids": ids,
            "probabilities": probabilities, "path_token_counts": [101],
            "answer": {"noul": round(p_true, 2)} if boolean else {"choice": ids[0]},
            "logits": [0.0, 1.0]}
    return raw


class TinyJevDiagnosticTests(unittest.TestCase):
    def test_LabelSafety_WhitelistsOriginalRequestAndCopiesInputs(self):
        case = fixture()
        case["rationale"] = "This contains the gold answer."
        original = copy.deepcopy(case)
        payload = diagnostic.model_payload(case)
        self.assertEqual({"id", "state", "questions"}, set(payload))
        self.assertEqual(case["case_id"], payload["id"])
        self.assertEqual(case["request"]["state"], payload["state"])
        self.assertEqual(case["request"]["questions"], payload["questions"])
        changed_gold = copy.deepcopy(case)
        changed_gold["expected"] = {"retain": False, "plan": "plan_a"}
        self.assertEqual(payload, diagnostic.model_payload(changed_gold))
        self.assertNotEqual(diagnostic.fingerprint(case), diagnostic.fingerprint(changed_gold))
        self.assertEqual(diagnostic.fingerprint(case["request"]), diagnostic.fingerprint(changed_gold["request"]))
        payload["state"]["candidate"] = "mutated"
        self.assertEqual(original, case)

    def test_Schema_RequiresTypedLabelsAndRejectsLabelFieldsInsideRequests(self):
        case = fixture()
        diagnostic.validate_cases([case])
        for change in ("string_bool", "integer_bool", "missing_option", "extra_question", "expected_in_request", "id_in_request", "yes_criteria"):
            invalid = copy.deepcopy(case)
            if change == "string_bool":
                invalid["expected"]["retain"] = "true"
            elif change == "integer_bool":
                invalid["expected"]["retain"] = 1
            elif change == "missing_option":
                invalid["expected"]["plan"] = "unknown"
            elif change == "extra_question":
                invalid["expected"]["unknown"] = True
            elif change == "expected_in_request":
                invalid["request"]["expected"] = invalid["expected"]
            elif change == "id_in_request":
                invalid["request"]["id"] = invalid["case_id"]
            else:
                invalid["request"]["questions"]["retain"]["criteria"] = {"yes": "keep", "no": "drop"}
            with self.subTest(change=change), self.assertRaises(ValueError):
                diagnostic.validate_cases([invalid])

    def test_BooleanScoring_UsesRawTrueProbabilityAndInclusiveHalfThreshold(self):
        case = fixture()
        for probability, expected in ((0.499999, False), (0.5, True), (0.500001, True)):
            raw = response(case, p_true=probability)
            self.assertEqual(0.5, raw["memory_ja:retain"]["answer"]["noul"])
            normalized = diagnostic.normalize_response(case, raw)
            self.assertIs(expected, normalized["retain"]["prediction"])
            self.assertEqual(probability, normalized["retain"]["p_true"])
            scored = diagnostic.score_predictions(case, normalized)
            self.assertIs(expected, scored["questions"]["retain"]["correct"])

    def test_ChoiceScoring_UsesOriginalCandidateIDsAndOrderForTies(self):
        case = fixture()
        normalized = diagnostic.normalize_response(case, response(case, choices=[0.1, 0.9]))
        self.assertEqual("plan_a", normalized["plan"]["prediction"])
        self.assertEqual({"plan_b": 0.1, "plan_a": 0.9}, normalized["plan"]["probabilities"])
        tied = diagnostic.normalize_response(case, response(case, choices=[0.5, 0.5]))
        self.assertEqual("plan_b", tied["plan"]["prediction"])
        rotated = copy.deepcopy(case)
        rotated["request"]["questions"]["plan"]["criteria"] = dict(reversed(list(case["request"]["questions"]["plan"]["criteria"].items())))
        selected = diagnostic.normalize_response(rotated, response(rotated, choices=[0.9, 0.1]))
        self.assertEqual(normalized["plan"]["probabilities"], selected["plan"]["probabilities"])
        self.assertEqual("plan_a", selected["plan"]["prediction"])

    def test_InvalidProbabilitiesOrIDs_AreIncorrectAndRemainSerializable(self):
        case = fixture()
        for change in ("nan", "negative", "sum", "candidate_order", "missing_question", "count"):
            raw = response(case)
            if change == "nan":
                raw["memory_ja:retain"]["probabilities"][0] = float("nan")
            elif change == "negative":
                raw["memory_ja:retain"]["probabilities"] = [-0.2, 1.2]
            elif change == "sum":
                raw["memory_ja:retain"]["probabilities"] = [0.2, 0.7]
            elif change == "candidate_order":
                raw["memory_ja:plan"]["candidate_ids"].reverse()
            elif change == "missing_question":
                raw.pop("memory_ja:plan")
            else:
                raw["memory_ja:retain"]["path_token_counts"] = [True]
            with self.subTest(change=change):
                row = diagnostic.evaluate_case(case, lambda payload: raw)
                self.assertEqual("failed", row["status"])
                self.assertFalse(row["correct"])
                self.assertTrue(all(question["correct"] is False for question in row["questions"].values()))
                json.dumps(row, allow_nan=False)
        row = diagnostic.evaluate_case(case, lambda payload: response(case))
        self.assertTrue(row["correct"])

    def test_Aggregates_CountFailuresAndExcludeUnlabeledCasesFromAccuracy(self):
        cases = [fixture() for _ in range(4)]
        for index, case in enumerate(cases):
            case["case_id"] += str(index)
        cases[1]["language"] = "en"
        cases[1]["expected"]["retain"] = False
        cases[2]["category"] = "failure"
        cases[3].pop("expected")
        rows = [diagnostic.evaluate_case(cases[0], lambda payload: response(cases[0])),
                diagnostic.evaluate_case(cases[1], lambda payload: response(cases[1])),
                diagnostic.evaluate_case(cases[2], lambda payload: {}),
                diagnostic.evaluate_case(cases[3], lambda payload: response(cases[3]))]
        summary = diagnostic.summarize(rows)
        overall = summary["overall"]
        self.assertEqual(4, overall["case_count"])
        self.assertEqual(1, overall["failed_cases"])
        self.assertEqual(3, overall["labeled_cases"])
        self.assertEqual(1, overall["correct_cases"])
        self.assertEqual(6, overall["labeled_questions"])
        self.assertEqual(3, overall["correct_questions"])
        self.assertEqual(2, overall["unlabeled_questions"])
        self.assertEqual(0.5, overall["question_accuracy"])
        self.assertEqual(1, overall["boolean_confusion"]["true_positive"])
        self.assertEqual(1, overall["boolean_confusion"]["false_positive"])
        self.assertEqual(1, overall["boolean_confusion"]["invalid_expected_true"])
        self.assertEqual(0, summary["by_category"]["failure"]["question_accuracy"])
        self.assertEqual(2, summary["by_language"]["en"]["labeled_questions"])

    def test_Run_OneWarmupIncrementalPersistenceAndLabelFreeRequests(self):
        cases = [fixture(), fixture()]
        cases[1]["case_id"] = "memory_en"
        cases[1]["language"] = "en"
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            case_path = root / "cases.json"
            case_path.write_text(json.dumps(cases, ensure_ascii=False))
            args = SimpleNamespace(cases=case_path, output=root / "output", checkpoint=root / "unused", threads=4, prepare_only=False)
            calls = []

            def predict(payload):
                calls.append(copy.deepcopy(payload))
                if len(calls) == 3:
                    saved = json.loads((args.output / "result.json").read_text())
                    self.assertEqual(1, len(saved["results"]))
                    return {}
                selected = next(case for case in cases if case["case_id"] == payload["id"])
                self.assertEqual(diagnostic.model_payload(selected), payload)
                return response(selected)

            with patch.object(diagnostic, "verified_checkpoint", return_value={"fixture": True}), \
                    patch.object(diagnostic, "load_runner", return_value=(predict, {"actual_model_dtype": "test"}, 12.5)), \
                    patch("sys.stdout", new_callable=io.StringIO):
                report = diagnostic.run(args)
            self.assertEqual(["memory_ja", "memory_ja", "memory_en"], [payload["id"] for payload in calls])
            self.assertTrue(report["execution_completed"])
            self.assertEqual(1, report["warmup"]["requests"])
            self.assertEqual(1, report["summary"]["overall"]["failed_cases"])
            self.assertEqual(12.5, report["initialization_ms"])
            saved = json.loads((args.output / "result.json").read_text())
            self.assertEqual(report, saved)
            requests = json.loads((args.output / "requests.json").read_text())
            self.assertEqual([{"case_id": case["case_id"], "request": case["request"]} for case in cases], requests)
            self.assertEqual(diagnostic.file_sha256(case_path), report["cases_file_sha256"])


if __name__ == "__main__":
    unittest.main()
