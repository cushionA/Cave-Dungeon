"""Fixture-only checks for durable inference and the model/gold boundary."""
import copy
import json
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
from unittest import mock

import run_expanded_benchmark as runner


def cases():
    return [{"case_id": f"case_{index}", "scenario_id": f"world_{index}",
             "family": "fixture", "language": "ja", "category": "fixture", "suite": "memory",
             "request": {"state": {"observation": index}, "questions": {"relevant": {
                 "type": "noul", "instructions": "Is this useful?", "criteria": {
                     "false": "not useful", "true": "useful"}}}},
             "expected": {"relevant": bool(index)}, "audit": {"secret": "gold must stay outside"}}
            for index in range(2)]


class FakeAdapter:
    def __init__(self, behavior=None, identity_marker="stable"):
        self.behavior = behavior
        self.payloads = []
        self.loaded = 0
        self.identity_marker = identity_marker

    def describe(self):
        return {"model_id": "fixture", "revision": self.identity_marker,
                "source_files": {}, "packages": {"fixture": "1"}}

    def verify_checkpoint(self, path):
        return {"sha256": runner.file_sha256(Path(path) / "weight.txt")}

    def project(self, case):
        return {"id": case["case_id"], **copy.deepcopy(case["request"])}

    def load(self, checkpoint, threads):
        self.loaded += 1

        def predict(payload):
            self.payloads.append(copy.deepcopy(payload))
            if self.behavior:
                return self.behavior(payload, len(self.payloads))
            return {"prediction": True}

        return predict, {"packages": {"fixture": "1"}, "threads": threads}, 0.25

    def normalize(self, case, raw):
        if type(raw["prediction"]) is not bool:
            raise ValueError("not a boolean")
        return {"relevant": raw["prediction"]}


class ExpandedRunnerTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.root = Path(self.directory.name)
        self.cases_path = self.root / "cases.json"
        self.cases_path.write_text(json.dumps(cases()))
        self.checkpoint = self.root / "checkpoint"
        self.checkpoint.mkdir()
        (self.checkpoint / "weight.txt").write_text("fixed weights")
        self.protocol = self.root / "protocol.json"
        self.protocol.write_text('{"fixed":true}')
        self.args = SimpleNamespace(backend="tinyjev", cases=self.cases_path,
            checkpoint=self.checkpoint, output=self.root / "output", threads=1,
            affinity_cpus=None, protocol=self.protocol, resume=False)

    def tearDown(self):
        self.directory.cleanup()

    def test_Run_ProjectsOnlyRequestsAndKeepsGoldOutsideEveryCall(self):
        adapter = FakeAdapter()
        report = runner.run(self.args, adapter=adapter)
        self.assertEqual(report["status"], "complete")
        self.assertEqual(report["completed_case_count"], 2)
        self.assertEqual(adapter.loaded, 1)
        self.assertEqual(len(adapter.payloads), 3)  # one warmup plus two scored calls
        for payload in adapter.payloads:
            self.assertEqual(set(payload), {"id", "state", "questions"})
            self.assertNotIn("expected", payload)
            self.assertNotIn("audit", payload)
        rows = runner.read_journal(self.args.output / "results.ndjson", cases())
        self.assertEqual([row["predictions"] for row in rows], [{"relevant": True}] * 2)
        self.assertTrue(all("correct" not in row for row in rows))

    def test_Resume_RetainsFailedPrefixAndNeverRerunsIt(self):
        def interrupted(payload, call):
            if call == 2:
                raise ValueError("native fixture failure")
            if call == 3:
                raise KeyboardInterrupt()
            return {"prediction": True}

        with self.assertRaises(KeyboardInterrupt):
            runner.run(self.args, adapter=FakeAdapter(interrupted))
        before = (self.args.output / "results.ndjson").read_bytes()
        prefix = runner.read_journal(self.args.output / "results.ndjson", cases())
        self.assertEqual(len(prefix), 1)
        self.assertEqual(prefix[0]["status"], "failed")
        progress = json.loads((self.args.output / "progress.json").read_text())
        self.assertEqual(progress["status"], "interrupted")
        self.assertFalse(progress["execution_completed"])
        self.args.resume = True
        adapter = FakeAdapter()
        report = runner.run(self.args, adapter=adapter)
        self.assertTrue((self.args.output / "results.ndjson").read_bytes().startswith(before))
        self.assertEqual(report["failed_case_count"], 1)
        self.assertEqual({payload["id"] for payload in adapter.payloads}, {"case_1"})

    def test_Resume_RejectsChangedDatasetProtocolThreadsCheckpointOrSources(self):
        runner.run(self.args, adapter=FakeAdapter())
        self.args.resume = True
        for kind in ("dataset", "protocol", "threads", "checkpoint", "sources"):
            original_cases = self.cases_path.read_text()
            original_protocol = self.protocol.read_text()
            original_weight = (self.checkpoint / "weight.txt").read_text()
            changed = copy.copy(self.args)
            adapter = FakeAdapter()
            if kind == "dataset":
                altered = list(reversed(cases()))
                self.cases_path.write_text(json.dumps(altered))
            elif kind == "protocol":
                self.protocol.write_text('{"fixed":false}')
            elif kind == "threads":
                changed.threads = 2
            elif kind == "checkpoint":
                (self.checkpoint / "weight.txt").write_text("different weights")
            else:
                adapter = FakeAdapter(identity_marker="changed source")
            with self.subTest(kind=kind), self.assertRaises(ValueError):
                runner.run(changed, adapter=adapter)
            self.assertEqual(adapter.loaded, 0)
            self.cases_path.write_text(original_cases)
            self.protocol.write_text(original_protocol)
            (self.checkpoint / "weight.txt").write_text(original_weight)

    def test_Journal_RejectsPartialDuplicateForeignOutOfOrderAndCorruptRows(self):
        runner.run(self.args, adapter=FakeAdapter())
        journal = self.args.output / "results.ndjson"
        original = journal.read_text()
        rows = [json.loads(line) for line in original.splitlines()]
        variants = [original.rstrip("\n")]
        for kind in ("duplicate", "foreign", "order", "checksum"):
            changed = copy.deepcopy(rows)
            if kind == "duplicate":
                changed[1]["case_id"] = changed[0]["case_id"]
            elif kind == "foreign":
                changed[1]["case_id"] = "foreign"
            elif kind == "order":
                changed = list(reversed(changed))
            else:
                changed[1]["predictions"]["relevant"] = False
            if kind != "checksum":
                changed = [runner.seal_record(row) for row in changed]
            variants.append("".join(json.dumps(row) + "\n" for row in changed))
        for variant in variants:
            journal.write_text(variant)
            with self.assertRaises(ValueError):
                runner.read_journal(journal, cases())
        journal.write_text(original)

    def test_CompletedResume_DoesNotLoadModelAndNewRunCannotOverwrite(self):
        with mock.patch.object(runner, "memory_peak", return_value={"lifetime_peak_rss_bytes": 1000}):
            first = runner.run(self.args, adapter=FakeAdapter())
        with self.assertRaises(FileExistsError):
            runner.run(self.args, adapter=FakeAdapter())
        self.args.resume = True
        adapter = FakeAdapter()
        with mock.patch.object(runner, "memory_peak", return_value={"lifetime_peak_rss_bytes": 100}):
            resumed = runner.run(self.args, adapter=adapter)
        self.assertEqual(adapter.loaded, 0)
        self.assertEqual(first["results"], resumed["results"])
        self.assertEqual(resumed["memory"]["lifetime_peak_rss_bytes"], 1000)

    def test_NonfiniteNativeReply_IsAnExplicitDurableFailure(self):
        adapter = FakeAdapter(lambda payload, call: {"prediction": float("nan")})
        result = runner.run(self.args, adapter=adapter)
        self.assertEqual(result["failed_case_count"], 2)
        self.assertTrue(all(row["predictions"] == {} for row in result["results"]))
        journal = (self.args.output / "results.ndjson").read_text()
        self.assertNotIn("NaN", journal)
        self.assertIn("invalid_nonfinite_float", journal)
        self.assertEqual(len(runner.read_journal(self.args.output / "results.ndjson", cases())), 2)

    def test_Validation_RejectsGoldInsideRequestInvalidIdsAndInvalidAffinity(self):
        bad = cases()
        bad[0]["request"]["expected"] = {"relevant": True}
        with self.assertRaises(ValueError):
            runner.validate_cases(bad)
        bad = cases()
        bad[1]["case_id"] = bad[0]["case_id"]
        with self.assertRaises(ValueError):
            runner.validate_cases(bad)
        with mock.patch.object(runner.os, "sched_getaffinity", return_value={0, 1, 2}), \
                mock.patch.object(runner.os, "sched_setaffinity") as setter:
            self.assertEqual(runner.apply_affinity("1,2"), [1, 2])
            setter.assert_called_once_with(0, {1, 2})
            for value in ("1,1", "3", "-1", "x"):
                with self.subTest(value=value), self.assertRaises(ValueError):
                    runner.apply_affinity(value)

    def test_HindsightNormalization_RecomputesFromNativeProbabilities(self):
        adapter = runner.NativeAdapter("hindsight", module=SimpleNamespace())
        case = cases()[0]
        raw = {"details": {"relevant": {"type": "noul", "option_keys": ["false", "true"],
            "probabilities": {"false": 0.5, "true": 0.5}, "selected": True}}}
        self.assertEqual(adapter.normalize(case, raw), {"relevant": True})
        raw["details"]["relevant"]["selected"] = False
        with self.assertRaises(ValueError):
            adapter.normalize(case, raw)
        raw["details"]["relevant"]["selected"] = True
        raw["details"]["relevant"]["probabilities"]["true"] = float("nan")
        with self.assertRaises(ValueError):
            adapter.normalize(case, raw)


if __name__ == "__main__":
    unittest.main()
