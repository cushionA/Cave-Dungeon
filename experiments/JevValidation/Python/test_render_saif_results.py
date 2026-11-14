"""Report-boundary tests using explicit synthetic fixtures, never model outcomes."""
import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import render_saif_results as render


def fixture_group(task, language, candidate=False):
    n = 500 if language == "ja" else 100
    correct = n * (6 if candidate else 5) // 10 if task == "memory" else n * (3 if candidate else 2) // 10
    result = {"n": n, "correct": correct, "accuracy": correct / n,
              "wilson_95": render._wilson(correct, n), "failures": 0, "coverage": 1,
              "latency": {"n": n, "mean_ms": 12, "p50_ms": 10, "p95_ms": 15} if candidate else
                         {"n": 0, "mean_ms": None, "p50_ms": None, "p95_ms": None}}
    if task == "memory":
        result["memory_confusion"] = {"true_positive": correct // 2, "true_negative": correct // 2,
            "false_positive": (n - correct) // 2, "false_negative": (n - correct) // 2,
            "failed_expected_true": 0, "failed_expected_false": 0, "actual_positive": n // 2, "actual_negative": n // 2}
    else:
        result["commander"] = {"correct": correct, "known_infeasible": n // 2,
            "feasible_but_not_best": n // 2 - correct, "failed_or_invalid": 0,
            "known_infeasible_rate": .5, "unsafe_or_invalid": n // 2, "unsafe_or_invalid_rate": .5,
            "mean_normalized_ordinal_rank_loss": .5}
    return result


def fixture_statistics():
    tasks = {task: {language: fixture_group(task, language, True) for language in ("ja", "en")} for task in render.TASKS}
    baselines = {name: {task: {language: fixture_group(task, language) for language in ("ja", "en")}
                       for task in render.TASKS} for name in render.BASELINES}
    for task in render.TASKS:
        for language in ("ja", "en"):
            group = tasks[task][language]
            pairs = {}
            for name in render.BASELINES:
                baseline = baselines[name][task][language]
                a = baseline["correct"] - 5
                b, c = group["correct"] - a, baseline["correct"] - a
                delta = (b - c) / group["n"]
                pairs[name] = {"n": group["n"], "both_correct": a, "candidate_only": b,
                    "baseline_only": c, "both_wrong": group["n"] - a - b - c,
                    "difference": delta, "difference_percentage_points": 100 * delta, "conservative_95": [-.3, .3]}
            group["paired_baselines"] = pairs
    model = {"identity": {"model_id": render.MODEL_ID, "revision": render.REVISION, "license": "CC-BY-NC-4.0",
        "quantization": "none; native BF16decoder and FP32readout rows", "run_identity": {"adapter_description": {
            "decoder_dtype": "torch.bfloat16", "head_dtype": "torch.float32", "decoder_device": "cpu", "head_device": "cpu",
            "native_temperatures": dict(render.TEMPERATURES), "calibration_fit_on_this_corpus": False,
            "torch_threads": 4, "torch_interop_threads": 5, "reading": "joint", "read_twice": False, "fast_path": True}}},
        "tasks": tasks, "overall": {"n": 1200, "correct": sum(value["correct"] for task in tasks.values() for value in task.values()), "failures": 0},
        "latency": {"n": 1200, "mean_ms": 12, "p50_ms": 10, "p95_ms": 15},
        "native_only_latency": {"n": 1200, "mean_ms": 10, "p50_ms": 8, "p95_ms": 13},
        "runtime": {"cpu_model": "Fixture CPU", "affinity": [0, 1, 2, 3], "python": "3.12.14", "torch": "2.9.1+cpu", "transformers": "5.17.0"},
        "tokens": {field: {"known_n": 1200, "missing_n": 0, "total": count * 1200, "mean": count, "p50": count, "p95": count}
                   for field, count in (("input_tokens", 40), ("output_tokens", 0))}, "sessions": []}
    return {"schema_version": 1, "exploratory": True, "models": {render.MODEL_KEY: model}, "baseline_summary": baselines}


def fixture_controls(root):
    cases, rows = [], []
    for index in range(8):
        is_bool = index >= 4
        expected = {"q": bool(index % 2)} if is_bool else {"q": "abcd"[index]}
        case = {"case_id": "control_" + str(index), "kind": "boolean" if is_bool else "color",
                "request": {"state": '<script>alert("fixture")</script>', "questions": {"q": {"criteria": dict.fromkeys("abcd", "color")}}}, "expected": expected}
        cases.append(case)
        rows.append({"case_index": index, "case_id": case["case_id"], "suite": "simple_controls", "language": "ja",
                     "expected": copy.deepcopy(expected), "predictions": copy.deepcopy(expected), "status": "complete", "error": None, "correct": True})
    cases_path, result_path = root / "cases.json", root / "controls.json"
    cases_path.write_text(json.dumps(cases))
    result = {"status": "complete", "planned_cases": 8, "completed_cases": 8, "results": rows,
              "identity": {"adapter_description": {"model_id": render.MODEL_ID, "revision": render.REVISION}}}
    result_path.write_text(json.dumps(result))
    return {"controls_cases": render.descriptor(cases_path)}, result_path, result


class SaifRendererTests(unittest.TestCase):
    def test_complete_fixture_and_known_wilson_counts_are_accepted_without_mutation(self):
        statistics = fixture_statistics()
        saved = copy.deepcopy(statistics)
        render.validate_statistics(statistics)
        self.assertEqual(statistics, saved)
        self.assertAlmostEqual(render._wilson(250, 500)[0], .4563412653, places=10)
        self.assertEqual(render._wilson(0, 500)[0], 0)
        self.assertEqual(render._wilson(500, 500)[1], 1)

    def test_incomplete_samples_missing_baselines_and_pooled_ci_are_rejected(self):
        for change in ("size", "baseline", "interval", "pooled", "failure", "token"):
            statistics = fixture_statistics()
            model = statistics["models"][render.MODEL_KEY]
            if change == "size": model["tasks"]["memory"]["en"]["n"] = 500
            elif change == "baseline": statistics["baseline_summary"].pop("tinyjev")
            elif change == "interval": model["tasks"]["memory"]["ja"]["wilson_95"][0] = .01
            elif change == "pooled": model["overall"]["wilson_95"] = [.4, .6]
            elif change == "failure": model["tasks"]["commander"]["ja"]["failures"] = 1
            else: model["tokens"]["output_tokens"]["total"] = 1
            with self.subTest(change=change), self.assertRaises(ValueError):
                render.validate_statistics(statistics)

    def test_paired_cells_must_match_both_models_not_only_delta(self):
        statistics = fixture_statistics()
        pair = statistics["models"][render.MODEL_KEY]["tasks"]["memory"]["ja"]["paired_baselines"]["tinyjev"]
        pair["both_correct"] -= 1
        pair["both_wrong"] += 1
        with self.assertRaises(ValueError):
            render.validate_statistics(statistics)

    def test_failed_memory_answers_remain_separate_from_valid_confusion_cells(self):
        group = fixture_group("memory", "ja", True)
        group["failures"], group["coverage"] = 1, 499 / 500
        cells = group["memory_confusion"]
        cells["false_negative"] -= 1
        cells["failed_expected_true"] += 1
        render.validate_group(group, "memory", "ja")
        cells["true_positive"] -= 1
        cells["false_negative"] += 1
        with self.assertRaises(ValueError):
            render.validate_group(group, "memory", "ja")

    def test_duplicate_keys_and_nonfinite_json_are_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "invalid.json"
            for text in ('{"accuracy":0.5,"accuracy":1}', '{"accuracy":NaN}', '{"accuracy":Infinity}'):
                path.write_text(text)
                with self.subTest(text=text), self.assertRaises(ValueError):
                    render.read_json(path)

    def test_controls_are_all_eight_in_order_and_boolean_types_cannot_be_numeric(self):
        with tempfile.TemporaryDirectory() as directory:
            protocol, path, result = fixture_controls(Path(directory))
            self.assertEqual(render.read_controls(path, protocol)["correct"], 8)
            for change in ("order", "typed", "incomplete"):
                value = copy.deepcopy(result)
                if change == "order": value["results"][0], value["results"][1] = value["results"][1], value["results"][0]
                elif change == "typed": value["results"][5]["predictions"] = {"q": 1}
                else: value["results"].pop()
                path.write_text(json.dumps(value))
                with self.subTest(change=change), self.assertRaises(ValueError):
                    render.read_controls(path, protocol)

    def test_protocol_link_is_hash_checked_and_reported_runtime_cannot_drift(self):
        with tempfile.TemporaryDirectory() as directory:
            statistics = fixture_statistics()
            model = statistics["models"][render.MODEL_KEY]
            protocol = {"model": {"revision": render.REVISION, "temperatures": render.TEMPERATURES,
                                   "quantization": model["identity"]["quantization"]}, "runtime": copy.deepcopy(model["runtime"])}
            path = Path(directory) / "protocol.json"
            path.write_text(json.dumps(protocol))
            statistics["protocol"] = render.descriptor(path)
            with patch.object(render, "PROTOCOL_SHA256", statistics["protocol"]["sha256"]):
                render.verify_protocol(statistics, path)
                model["runtime"]["torch"] = "different"
                with self.assertRaises(ValueError): render.verify_protocol(statistics, path)
                path.write_text(json.dumps({**protocol, "changed": True}))
                with self.assertRaises(ValueError): render.verify_protocol(statistics, path)

    def test_resource_scope_does_not_accept_a_failed_or_impossible_worker_summary(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "resources.json"
            data = {"exit_code": 0, "max_vmrss_kib": 100, "max_vmhwm_kib": 110, "scope": "whole worker"}
            path.write_text(json.dumps(data))
            self.assertEqual(render.read_resources(path)["data"]["max_vmhwm_kib"], 110)
            for change in ({"exit_code": 1}, {"max_vmhwm_kib": 99}):
                path.write_text(json.dumps({**data, **change}))
                with self.assertRaises(ValueError): render.read_resources(path)

    def test_html_escapes_control_text_and_shows_timing_precision_and_interval_scope(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            protocol, controls_path, _ = fixture_controls(root)
            statistics = fixture_statistics()
            source = root / '<statistics>.json'
            source.write_text('{}')
            evidence = render.descriptor(source)
            protocol["model"] = dict.fromkeys(("checkpoint_manifest", "source_manifest", "sdk_manifest"), evidence)
            statistics["sources"] = {"cases": evidence}
            prefix = root / "fixture-report"
            render.write_html(statistics, evidence, protocol, evidence, prefix, render.read_controls(controls_path, protocol))
            page = prefix.with_suffix('.html').read_text()
            self.assertNotIn('<script>', page)
            for text in ('&lt;script&gt;', '&lt;statistics&gt;', 'inter-op 5', 'BF16', 'FP32', 'pointwise', '2.9.1',
                         render.REVISION, 'CC-BY-NC-4.0', 'native API', '主試験と別run', '英語の副評価'):
                with self.subTest(text=text): self.assertIn(text, page)

    @unittest.skipUnless(importlib.util.find_spec("matplotlib"), "Matplotlib is not installed in this interpreter")
    def test_figure_produces_standalone_png_and_svg_in_temporary_directory(self):
        with tempfile.TemporaryDirectory() as directory:
            prefix = Path(directory) / "explicit-synthetic-fixture"
            render.draw(fixture_statistics(), prefix, "a" * 64)
            self.assertGreater(prefix.with_suffix('.png').stat().st_size, 10000)
            self.assertIn('<svg', prefix.with_suffix('.svg').read_text())


if __name__ == "__main__":
    unittest.main()
