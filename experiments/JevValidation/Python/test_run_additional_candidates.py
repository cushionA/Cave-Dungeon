"""No weights/server required: durability, leakage, failures and strict resume."""
import copy
import json
from pathlib import Path
import tempfile
import time
from types import SimpleNamespace
import unittest

import run_additional_candidates as runner


class FakeAdapter:
    __file__ = __file__

    def __init__(self, interrupt_at=None):
        self.calls = []
        self.interrupt_at = interrupt_at

    def describe(self):
        return {"adapter": "fake", "version": 1}

    def predict(self, server_url, request, *, timeout_seconds):
        self.calls.append(copy.deepcopy(request))
        if len(self.calls) == self.interrupt_at:
            raise KeyboardInterrupt("simulated interruption after a durable prefix")
        if request["state"] == "raise":
            raise RuntimeError("simulated server error")
        if request["state"] == "slow":
            time.sleep(0.2)
        predictions = {key: (True if value["type"] in {"noul", "boolean"}
                             else next(iter(value["criteria"]))) for key, value in request["questions"].items()}
        if request["state"] == "wrong_type":
            predictions = {key: 1 for key in predictions}
        return {"predictions": predictions, "details": {"calls": [
            {"usage": {"prompt_tokens": 7, "completion_tokens": 1}, "raw": {"untouched": "reply"}}]}}


class AdditionalRunnerTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.cases = [
            {"case_id": "one", "suite": "memory", "language": "ja", "audit": {"hidden_gold": True},
             "request": {"state": "ordinary", "questions": {"q": {"type": "noul", "instructions": "Is it true?"}}},
             "expected": {"q": True}},
            {"case_id": "two", "suite": "commander", "language": "en",
             "request": {"state": "ordinary", "questions": {"q": {"type": "choice", "instructions": "Choose.",
                 "criteria": {"alpha": "First", "beta": "Second"}}}}, "expected": {"q": "alpha"}},
            {"case_id": "three", "suite": "memory", "language": "ja",
             "request": {"state": "raise", "questions": {"q": {"type": "noul", "instructions": "Is it true?"}}},
             "expected": {"q": True}},
        ]
        self.path = self.root / "cases.json"
        self.write_cases()
        self.protocol = self.root / "protocol.json"
        self.protocol.write_text('{"frozen":true}')
        self.args = SimpleNamespace(adapter="chat", server_url="http://127.0.0.1:9999", cases=self.path,
                                    model_id="test/model", model_sha256="a" * 64,
                                    output_dir=self.root / "run", protocol=self.protocol,
                                    timeout_seconds=1.0, limit=None, resume=False)

    def write_cases(self):
        self.path.write_text(json.dumps(self.cases))

    def test_WhitelistedRequestsFixedWarmupAndFullDurableRows(self):
        adapter = FakeAdapter()
        result = runner.run(self.args, adapter)
        self.assertEqual(result["status"], "complete")
        self.assertEqual(result["summary"]["overall"]["correct"], 2)
        self.assertEqual(result["summary"]["overall"]["failed"], 1)
        self.assertEqual(adapter.calls[0], runner.WARMUP_REQUEST)
        self.assertNotEqual(adapter.calls[0], self.cases[0]["request"])
        self.assertTrue(all(set(call) == {"state", "questions"} for call in adapter.calls))
        self.assertEqual(len(adapter.calls), 4)
        first = result["results"][0]
        self.assertEqual(first["input_tokens"], 7)
        self.assertEqual(first["output_tokens"], 1)
        self.assertEqual(first["raw"]["details"]["calls"][0]["raw"], {"untouched": "reply"})
        self.assertEqual(len((self.args.output_dir / "results.ndjson").read_text().splitlines()), 3)
        self.assertFalse(result["results"][2]["correct"])

    def test_CompleteResumeDoesNotRetryFailedObservationsOrWarmup(self):
        adapter = FakeAdapter()
        runner.run(self.args, adapter)
        self.args.resume = True
        resumed = runner.run(self.args, adapter)
        self.assertEqual(len(adapter.calls), 4)
        self.assertEqual(resumed["summary"]["overall"]["failed"], 1)

    def test_InterruptedRunResumesOnlyAfterDurablePrefix(self):
        with self.assertRaises(KeyboardInterrupt):
            runner.run(self.args, FakeAdapter(interrupt_at=3))
        rows = runner.read_journal(self.args.output_dir / "results.ndjson", self.cases)
        self.assertEqual(len(rows), 1)
        self.args.resume = True
        adapter = FakeAdapter()
        result = runner.run(self.args, adapter)
        self.assertEqual(result["completed_cases"], 3)
        self.assertEqual(adapter.calls, [runner.WARMUP_REQUEST, self.cases[1]["request"], self.cases[2]["request"]])

    def test_ResumeRejectsChangedModelProtocolAndLimits(self):
        runner.run(self.args, FakeAdapter())
        self.args.resume = True
        for key, value in [("model_sha256", "b" * 64), ("limit", 1), ("timeout_seconds", 2)]:
            changed = copy.copy(self.args)
            setattr(changed, key, value)
            with self.subTest(key=key), self.assertRaises(ValueError):
                runner.run(changed, FakeAdapter())
        self.protocol.write_text('{"frozen":false}')
        with self.assertRaises(ValueError):
            runner.run(self.args, FakeAdapter())

    def test_JournalTamperResealedIncorrectScoringAndPartialTailAreRejected(self):
        runner.run(self.args, FakeAdapter())
        path = self.args.output_dir / "results.ndjson"
        original = path.read_bytes()
        rows = [json.loads(line) for line in original.splitlines()]
        rows[0]["correct"] = False
        path.write_bytes(b"\n".join(runner.encoded(row) for row in rows) + b"\n")
        with self.assertRaises(ValueError):
            runner.read_journal(path, self.cases)
        rows[0].pop("record_sha256")
        rows[0] = runner.seal_record(rows[0])
        path.write_bytes(b"\n".join(runner.encoded(row) for row in rows) + b"\n")
        with self.assertRaises(ValueError):
            runner.read_journal(path, self.cases)
        path.write_bytes(original[:-1])
        with self.assertRaises(ValueError):
            runner.read_journal(path, self.cases)

    def test_WallTimeoutAndWrongTypedPredictionBecomeIncorrectFailures(self):
        request = {"state": "slow", "questions": {"q": {"type": "noul", "instructions": "Question?"}}}
        result = runner.call_adapter(FakeAdapter(), self.args.server_url, request, 0.01)
        self.assertEqual(result["status"], "failed")
        self.assertEqual(result["error"]["type"], "TimeoutError")
        request["state"] = "wrong_type"
        result = runner.call_adapter(FakeAdapter(), self.args.server_url, request, 1)
        self.assertEqual(result["status"], "failed")
        self.assertEqual(result["predictions"], {})
        self.assertEqual(result["raw"]["predictions"], {"q": 1})

    def test_ExceptionsWithResponseDetailsKeepFullFailureEvidence(self):
        class DetailAdapter(FakeAdapter):
            def predict(self, *args, **kwargs):
                error = ValueError("invalid symbol")
                error.details = {"body": {"prompt": "sent"}, "raw": {"choices": ["bad"]}}
                raise error
        result = runner.call_adapter(DetailAdapter(), self.args.server_url, runner.WARMUP_REQUEST, 1)
        self.assertEqual(result["raw"]["adapter_exception_details"]["raw"], {"choices": ["bad"]})

    def test_UnknownTokenCountsAreNullAndAllCallsAreRequiredForTotals(self):
        self.assertEqual(runner.token_counts({"details": {}}), (None, None))
        raw = {"details": {"calls": [{"input_tokens": 7, "output_tokens": 1},
                                      {"usage": {"prompt_tokens": 8, "completion_tokens": 2}}]}}
        self.assertEqual(runner.token_counts(raw), (15, 3))
        raw["details"]["calls"][1].pop("usage")
        self.assertEqual(runner.token_counts(raw), (None, None))

    def test_InputBoundaryRejectsGoldLeakageDuplicatesAndNumericBooleans(self):
        self.cases[0]["request"]["expected"] = {"q": True}
        self.write_cases()
        with self.assertRaises(ValueError):
            runner.load_cases(self.path)
        self.cases[0]["request"].pop("expected")
        self.cases[0]["expected"]["q"] = 1
        self.write_cases()
        with self.assertRaises(ValueError):
            runner.load_cases(self.path)

    def test_PredictionObjectOrderDoesNotChangeExactScoring(self):
        self.assertTrue(runner.predictions_match({"b": "x", "a": True}, {"a": True, "b": "x"}))
        self.assertFalse(runner.predictions_match({"a": 1}, {"a": True}))

    def test_NonfiniteRawRepliesAreFailedWithDiagnosticRepresentation(self):
        class InvalidAdapter(FakeAdapter):
            def predict(self, *args, **kwargs):
                return {"predictions": {"warmup": "blue"}, "details": {"bad": float("nan")}}
        result = runner.call_adapter(InvalidAdapter(), self.args.server_url, runner.WARMUP_REQUEST, 1)
        self.assertEqual(result["status"], "failed")
        self.assertEqual(result["raw"]["details"]["bad"], {"invalid_nonfinite": "nan"})


if __name__ == "__main__":
    unittest.main()
