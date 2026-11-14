"""No weights/import of the native global patches: schema, one-call and durability tests."""
import copy
import json
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

import compare_saif_lightning as adapter
import run_additional_candidates as shared
import run_saif_benchmark as runner


def boolean_request(state="ordinary"):
    return {"state": state, "questions": {"q": {"type": "noul", "instructions": "Is it true?"}}}


class FakeEngine:
    def __init__(self, interrupt_at=None):
        self.calls, self.events = [], []
        self.interrupt_at = interrupt_at
        self.model = SimpleNamespace(tokenizer=object(), reading="joint", max_length=8192,
                                     read_twice=False, temperatures={"noul": 1.669, "choice": 1.349})
        self.closed = False

    def __call__(self, state, questions):
        self.calls.append(copy.deepcopy({"state": state, "questions": questions}))
        self.events.append("native")
        if len(self.calls) == self.interrupt_at:
            raise KeyboardInterrupt("simulated process interruption")
        if state == "raise":
            raise ValueError("native failure remains an incorrect row")
        answers = {}
        for name, question in questions.items():
            if question["type"] == "noul":
                answers[name] = {"type": "noul", "noul": 0.5,
                                 "probabilities": {"true": 0.5, "false": 0.5}}
            else:
                keys = list(question["criteria"])
                probabilities = {key: (0.8 if index == len(keys) - 1 else 0.2 / (len(keys) - 1)) for index, key in enumerate(keys)}
                answers[name] = {"type": "choice", "choice": keys[-1], "probabilities": probabilities}
        return {"model": "fake-native", "answers": answers}, None

    def close(self):
        self.closed = True


class FakeCore:
    def __init__(self, engine):
        self.engine = engine

    def layouts(self, tokenizer, state, questions, reading, max_length, read_twice):
        self.engine.events.append("layouts")
        assert tokenizer is self.engine.model.tokenizer
        assert (reading, max_length, read_twice) == ("joint", 8192, False)
        yield SimpleNamespace(ids=list(range(37)), prefix=12, fields=list(questions),
                              slots=[35] * len(questions), blocks=[(12, 37)], label_ids=[[1, 2]] * len(questions))


def backend(interrupt_at=None):
    engine = FakeEngine(interrupt_at)
    return adapter.NativeBackend(engine, FakeCore(engine), {"native": "fixed", "dtype": "bfloat16"}, 1234.5)


class SaifAdapterTests(unittest.TestCase):
    def test_OneNativeCallPreservesProbabilitiesAndCountsActualPostcallLayouts(self):
        native = backend()
        request = boolean_request()
        request["questions"]["role"] = {"type": "choice", "instructions": "Choose a role.",
                                          "criteria": {"guard": "Guard", "scout": "Scout"}}
        result = native.predict("native://cpu", request)
        self.assertEqual({"q": True, "role": "scout"}, result["predictions"])
        self.assertEqual(["native", "layouts"], native.engine.events)
        self.assertEqual([request], native.engine.calls)
        call = result["details"]["calls"][0]
        self.assertEqual(37, call["input_tokens"])
        self.assertNotEqual(len(str(request)), call["input_tokens"])
        self.assertEqual(0, call["output_tokens"])
        self.assertEqual({"true": 0.5, "false": 0.5}, call["probabilities"]["q"])
        self.assertEqual({"guard": 0.2, "scout": 0.8}, call["probabilities"]["role"])
        self.assertEqual(native.engine.model.temperatures, call["native_temperatures"])
        self.assertEqual(1234.5, result["details"]["model_initialization_ms"])
        self.assertEqual(request, call["request"])

    def test_RejectsLabelsBeforeNativeInferenceAndWrongBackendOrConcurrency(self):
        native = backend()
        bad = boolean_request()
        bad["expected"] = {"q": True}
        with self.assertRaises(ValueError):
            native.predict("native://cpu", bad)
        bad = boolean_request()
        bad["questions"]["q"]["answer"] = True
        with self.assertRaises(ValueError):
            native.predict("native://cpu", bad)
        with self.assertRaises(ValueError):
            native.predict("http://localhost:8831", boolean_request())
        native._lock.acquire()
        try:
            with self.assertRaises(RuntimeError):
                native.predict("native://cpu", boolean_request())
        finally:
            native._lock.release()
        self.assertEqual([], native.engine.calls)

    def test_NativeDistributionValidationUsesKeysAndRejectsInvalidCoverageAndChoices(self):
        request = boolean_request()
        raw = {"answers": {"q": {"type": "noul", "noul": 0.49,
                                  "probabilities": {"true": 0.49, "false": 0.51}}}}
        self.assertEqual({"q": False}, adapter.predictions_from_native(request, raw))
        for change in ["noul", "nan", "sum", "missing", "bool", "extra_question"]:
            value = copy.deepcopy(raw)
            p = value["answers"]["q"]["probabilities"]
            if change == "noul": value["answers"]["q"]["noul"] = 0.7
            elif change == "nan": p["true"] = float("nan")
            elif change == "sum": p["false"] = 0.8
            elif change == "missing": p.pop("false")
            elif change == "bool": p["true"] = True
            else: value["answers"]["leaked"] = value["answers"]["q"]
            with self.subTest(change=change), self.assertRaises(ValueError):
                adapter.predictions_from_native(request, value)
        choice = {"state": "s", "questions": {"q": {"type": "choice", "criteria": {"a": "A", "b": "B"}}}}
        with self.assertRaises(ValueError):
            adapter.predictions_from_native(choice, {"answers": {"q": {"type": "choice", "choice": "a", "probabilities": {"a": 0.1, "b": 0.9}}}})
        tied = {"answers": {"q": {"type": "choice", "choice": "a", "probabilities": {"a": 0.5, "b": 0.5}}}}
        self.assertEqual({"q": "a"}, adapter.predictions_from_native(choice, tied))
        tied["answers"]["q"]["probabilities"] = {"b": 0.5, "a": 0.5}
        with self.assertRaises(ValueError):
            adapter.predictions_from_native(choice, tied)


class SaifRunnerTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        for name in ["source-manifest.json", "sdk-manifest.json"]:
            (self.root / name).write_text('{"frozen":true}')
        self.cases = [{"case_id": "one", "suite": "memory", "language": "ja", "audit": "NEVER_MODEL_INPUT",
                       "request": boolean_request(), "expected": {"q": True}},
                      {"case_id": "two", "suite": "commander", "language": "en",
                       "request": {"state": "ordinary", "questions": {"q": {"type": "choice", "instructions": "Choose.",
                                        "criteria": {"alpha": "First", "beta": "Second"}}}}, "expected": {"q": "beta"}},
                      {"case_id": "three", "suite": "memory", "language": "ja",
                       "request": boolean_request("raise"), "expected": {"q": True}}]
        self.path = self.root / "cases.json"
        self.path.write_text(json.dumps(self.cases))
        self.protocol = self.root / "protocol.json"
        self.protocol.write_text('{"frozen":true}')
        self.args = SimpleNamespace(model_dir=self.root / "weights", cases=self.path, output_dir=self.root / "run",
                    protocol=self.protocol, threads=4, affinity=None, timeout_seconds=1.0, resume=False)
        for mock in [patch.object(adapter, "ROOT", self.root),
                     patch.object(adapter, "verify_checkpoint", return_value={"model": "verified"}),
                     patch.object(adapter, "verify_sources", return_value=[{"source": "verified"}])]:
            mock.start()
            self.addCleanup(mock.stop)

    def test_UnchangedJournalHelperFixedWarmupFailuresAndCompleteResume(self):
        native = backend()
        registry = dict(shared.ADAPTERS)
        result = runner.run(self.args, native)
        self.assertEqual(registry, shared.ADAPTERS)
        self.assertEqual([shared.WARMUP_REQUEST] + [row["request"] for row in self.cases], native.engine.calls)
        self.assertEqual(3, result["completed_cases"])
        self.assertEqual(2, result["summary"]["overall"]["correct"])
        self.assertEqual(1, result["summary"]["overall"]["failed"])
        self.assertEqual(37, result["results"][0]["input_tokens"])
        self.assertTrue(native.engine.closed)
        self.assertNotIn("NEVER_MODEL_INPUT", json.dumps(native.engine.calls))
        self.args.resume = True
        resumed = backend()
        again = runner.run(self.args, resumed)
        self.assertEqual([], resumed.engine.calls)
        self.assertEqual(1, again["summary"]["overall"]["failed"])
        self.assertEqual("native://cpu", again["identity"]["server_url"])

    def test_InterruptedPrefixResumesOnlyRemainingRowsAndRejectsIdentityChanges(self):
        with self.assertRaises(KeyboardInterrupt):
            runner.run(self.args, backend(interrupt_at=3))
        self.assertEqual(1, len(shared.read_journal(self.args.output_dir / "results.ndjson", self.cases)))
        self.args.resume = True
        for key, value in [("threads", 2), ("timeout_seconds", 2.0)]:
            changed = copy.copy(self.args)
            setattr(changed, key, value)
            with self.subTest(key=key), self.assertRaises(ValueError):
                runner.run(changed, backend())
        native = backend()
        result = runner.run(self.args, native)
        self.assertEqual([shared.WARMUP_REQUEST, self.cases[1]["request"], self.cases[2]["request"]], native.engine.calls)
        self.assertEqual(3, result["completed_cases"])
        self.protocol.write_text('{"frozen":false}')
        with self.assertRaises(ValueError):
            runner.run(self.args, backend())

    def test_NativeInitializationCompletesBeforeRequestTimer(self):
        native = backend()
        clock = [0.0]
        def initialize(*args):
            clock[0] += 50.0
            return native
        with patch.object(adapter, "initialize", side_effect=initialize) as factory, \
                patch.object(shared.time, "perf_counter", side_effect=lambda: clock[0]):
            result = runner.run(self.args)
        factory.assert_called_once_with(self.args.model_dir, 4)
        self.assertTrue(all(row["duration_ms"] == 0 for row in result["results"]))
        self.assertEqual(shared.WARMUP_REQUEST, native.engine.calls[0])


if __name__ == "__main__":
    unittest.main()
