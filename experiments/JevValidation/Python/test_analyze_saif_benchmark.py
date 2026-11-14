"""Saif analysis boundary tests; no model, native patch, or inference imports."""
import copy
import csv
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import analyze_saif_benchmark as analysis


TEMPERATURES = {"choice": 1.3492052555084229, "noul": 1.6690177917480469, "score": 0.5360531806945801}


def fixture_case(kind="noul", language="ja"):
    question = {"type": kind, "instructions": "Original question.",
                "criteria": {"false": "No", "true": "Yes"} if kind == "noul" else {"p_a": "Plan A", "p_b": "Plan B"}}
    return {"case_id": kind + "_" + language, "world_id": kind + "_world", "family_id": "one",
            "mechanism_group": "one", "suite": "memory" if kind == "noul" else "commander",
            "language": language, "request": {"state": {"fact": "original"}, "questions": {"q": question}},
            "expected": {"q": True if kind == "noul" else "p_a"}}


def fixture_row(case):
    boolean = case["request"]["questions"]["q"]["type"] == "noul"
    probabilities = {"true": 0.5, "false": 0.5} if boolean else {"p_a": 0.5, "p_b": 0.5}
    answer = {"type": "noul", "noul": 0.5, "probabilities": probabilities} if boolean else {
        "type": "choice", "choice": "p_a", "probabilities": probabilities}
    call = {"request": copy.deepcopy(case["request"]), "raw": {"model": "saif-v2", "answers": {"q": answer}},
            "native_answers": {"q": copy.deepcopy(answer)}, "probabilities": {"q": copy.deepcopy(probabilities)},
            "native_temperatures": dict(TEMPERATURES), "native_elapsed_ms": 20.0, "native_auxiliary": None,
            "input_tokens": 40, "output_tokens": 0, "layouts": [{"input_tokens": 40, "prefix_tokens": 10,
                "input_ids_sha256": "b" * 64, "fields": ["q"], "slots": [38], "blocks": [[10, 40]],
                "label_token_ids": [[101, 102]]}]}
    return {"case_id": case["case_id"], "status": "complete", "error": None,
            "predictions": copy.deepcopy(case["expected"]), "correct": True, "duration_ms": 21.0,
            "input_tokens": 40, "output_tokens": 0, "raw": {"predictions": copy.deepcopy(case["expected"]),
                "details": {"calls": [call], "raw_logits": None, "initialization_excluded_from_request": True,
                            "model_initialization_ms": 1000.0}}}


class SaifAnalysisTests(unittest.TestCase):
    def test_NativeProbabilities_AreDecodedWithoutSoftmaxOrTemperatureChanges(self):
        for kind in ("noul", "choice"):
            case = fixture_case(kind)
            row = fixture_row(case)
            saved = copy.deepcopy(row)
            decoded = analysis.decode_native(case, row, TEMPERATURES, 8192)
            self.assertEqual(decoded["predictions"], case["expected"])
            self.assertEqual(decoded["input_tokens"], 40)
            self.assertEqual(decoded["native_elapsed_ms"], 20)
            self.assertEqual(row, saved)

    def test_NativeBoolean_UsesRawProbabilityIncludingExactlyHalf(self):
        case = fixture_case()
        row = fixture_row(case)
        call = row["raw"]["details"]["calls"][0]
        answer = call["raw"]["answers"]["q"]
        answer.update(noul=0.49999, probabilities={"true": 0.49999, "false": 0.50001})
        call["native_answers"] = copy.deepcopy(call["raw"]["answers"])
        call["probabilities"] = {"q": copy.deepcopy(answer["probabilities"])}
        row["predictions"] = row["raw"]["predictions"] = {"q": False}
        self.assertEqual(analysis.decode_native(case, row, TEMPERATURES, 8192)["predictions"], {"q": False})

    def test_NativeBoundary_RejectsIncompleteProbabilitiesCopiesAndCanonicalDisagreement(self):
        case, original = fixture_case(), fixture_row(fixture_case())
        for change in ("missing", "nan", "bool", "sum", "answer", "copy", "prediction", "temperature", "leak"):
            row = copy.deepcopy(original)
            call = row["raw"]["details"]["calls"][0]
            probabilities = call["raw"]["answers"]["q"]["probabilities"]
            if change == "missing": probabilities.pop("false")
            elif change == "nan": probabilities["true"] = float("nan")
            elif change == "bool": probabilities["true"] = True
            elif change == "sum": probabilities["false"] = 0.8
            elif change == "answer": call["raw"]["answers"]["q"]["noul"] = 0.6
            elif change == "copy": call["probabilities"]["q"]["true"] = 0.6
            elif change == "prediction": row["predictions"] = {"q": False}
            elif change == "temperature": call["native_temperatures"]["noul"] = 1.0
            else: call["request"]["expected"] = {"q": True}
            with self.subTest(change=change), self.assertRaises(ValueError):
                analysis.decode_native(case, row, TEMPERATURES, 8192)

    def test_ChoiceTie_RequiresOriginalCandidateOrderAndNativeChoice(self):
        case = fixture_case("choice")
        for change in ("order", "choice"):
            row = fixture_row(case)
            answer = row["raw"]["details"]["calls"][0]["raw"]["answers"]["q"]
            if change == "order": answer["probabilities"] = {"p_b": 0.5, "p_a": 0.5}
            else: answer["choice"] = "p_b"
            with self.assertRaises(ValueError):
                analysis.decode_native(case, row, TEMPERATURES, 8192)

    def test_LayoutValidation_RejectsMissingSlotsBadCountsAndUnknownLabels(self):
        case = fixture_case()
        for change in ("count", "slot", "field", "hash", "labels", "duplicate_call"):
            row = fixture_row(case)
            call = row["raw"]["details"]["calls"][0]
            layout = call["layouts"][0]
            if change == "count": row["input_tokens"] = 41
            elif change == "slot": layout["slots"] = [40]
            elif change == "field": layout["fields"] = ["other"]
            elif change == "hash": layout["input_ids_sha256"] = "unknown"
            elif change == "labels": layout["label_token_ids"] = [[1]]
            else: row["raw"]["details"]["calls"].append(copy.deepcopy(call))
            with self.subTest(change=change), self.assertRaises(ValueError):
                analysis.decode_native(case, row, TEMPERATURES, 8192)

    def test_ExplicitFailures_RemainWrongWithoutInventingNativeAnswers(self):
        case = fixture_case()
        row = {"case_id": case["case_id"], "status": "failed", "error": {"type": "TimeoutError"},
               "predictions": {}, "correct": False, "raw": {"partial": "evidence"}}
        decoded = analysis.decode_native(case, row, TEMPERATURES, 8192)
        self.assertFalse(decoded["available"])
        self.assertEqual(decoded["predictions"], {})

    def test_DescriptorValidation_DetectsChangedBytesAndProtocolHash(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "frozen.json"
            path.write_text('{"frozen":true}')
            value = analysis.file_descriptor(path)
            self.assertEqual(analysis.check_descriptor(path, value), value)
            path.write_text('{"frozen":false}')
            with self.assertRaises(ValueError):
                analysis.check_descriptor(path, value)

    def test_BaselineCsvs_RequireAllSixModelsAndExactTypedGoldAndWorlds(self):
        cases = [fixture_case(kind, lang) for kind in ("noul", "choice") for lang in ("ja", "en")]
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            paired, additional = root / "paired.csv", root / "additional.csv"
            analysis_fixture_csvs(cases, paired, additional)
            baselines = analysis.load_baseline_csvs(cases, paired, additional)
            self.assertEqual(set(baselines), set(analysis.BASELINE_KEYS))
            self.assertTrue(all(len(rows) == 4 for rows in baselines.values()))
            with additional.open() as source:
                data = list(csv.DictReader(source))
            data[0]["predictions"] = '{"q":1}'
            with additional.open("w", newline="") as out:
                writer = csv.DictWriter(out, fieldnames=list(data[0]));writer.writeheader();writer.writerows(data)
            with self.assertRaises(ValueError):
                analysis.load_baseline_csvs(cases, paired, additional)

    def test_MetadataObjectOrderIsSemanticButInputAndCandidateOrderRemainExact(self):
        self.assertTrue(analysis._same({"true": 0.5, "false": 0.5}, {"false": 0.5, "true": 0.5}))
        self.assertFalse(analysis._same({"value": True}, {"value": 1}))
        case = fixture_case()
        case["request"]["state"]["second"] = "another original fact"
        row = fixture_row(case)
        row["raw"]["details"]["calls"][0]["request"]["state"] = dict(reversed(list(case["request"]["state"].items())))
        with self.assertRaises(ValueError):
            analysis.decode_native(case, row, TEMPERATURES, 8192)

    def test_CompletedRun_RequiresMatchingJournalManifestRequestsAndFixedWarmup(self):
        cases = [fixture_case("noul"), fixture_case("choice")]
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            protocol = {"model": {"temperatures": TEMPERATURES}}
            report, manifest, journal = analysis_fixture_run(root, cases)
            with patch.object(analysis, "_verify_identity", return_value={"max_context_tokens": 8192}):
                verified = analysis.read_completed_saif(root, cases, root / "protocol.json", protocol)
                self.assertEqual(set(verified["rows"]), {case["case_id"] for case in cases})
                report["results"][0]["predictions"] = {"q": False}
                (root / "result.json").write_text(json.dumps(report))
                with self.assertRaises(ValueError):
                    analysis.read_completed_saif(root, cases, root / "protocol.json", protocol)
            analysis_fixture_run(root, cases)
            manifest = analysis._read(root / "manifest.json")
            manifest["sessions"][0]["warmup_request"] = {"state": "changed", "questions": {}}
            (root / "manifest.json").write_text(json.dumps(manifest))
            report = analysis._read(root / "result.json")
            report["sessions"] = manifest["sessions"]
            report["manifest_sha256"] = analysis.file_descriptor(root / "manifest.json")["sha256"]
            (root / "result.json").write_text(json.dumps(report))
            with patch.object(analysis, "_verify_identity", return_value={"max_context_tokens": 8192}), self.assertRaises(ValueError):
                analysis.read_completed_saif(root, cases, root / "protocol.json", protocol)

    def test_NativeIdentity_RejectsModelByteTemperatureAndSourceChanges(self):
        cases = [fixture_case()]
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            protocol, identity = analysis_fixture_identity(root, cases)
            analysis._verify_identity(identity, protocol, root / "protocol.json", cases)
            bad = copy.deepcopy(identity)
            bad["adapter_description"]["native_temperatures"]["choice"] = 1.0
            with self.assertRaises(ValueError):
                analysis._verify_identity(bad, protocol, root / "protocol.json", cases)
            weight = root / "model" / "decoder.safetensors"
            weight.write_bytes(b"changed fake weight bytes")
            with self.assertRaises(ValueError):
                analysis._verify_identity(identity, protocol, root / "protocol.json", cases)

    def test_Frozen1200InputsAndSixHistoricalBaselines_IntegrateWithSyntheticSaifOnly(self):
        protocol_path = Path(__file__).parent.parent / "Results/saif-benchmark-protocol.json"
        if not protocol_path.is_file():
            self.skipTest("The immutable full-trial input snapshot is not installed")
        protocol = analysis._read(protocol_path)
        required = [protocol["cases"]["path"]] + [row["path"] for row in protocol["baseline_sources"].values()]
        if not all(Path(path).is_file() for path in required):
            self.skipTest("Historical frozen native/CSV inputs are not installed")
        cases = analysis.shared.load_cases(protocol["cases"]["path"])
        rows, diagnostics = {}, {}
        for case in cases:
            row = synthetic_frozen_case_row(case, protocol["model"]["temperatures"])
            rows[case["case_id"]] = row
            diagnostics[case["case_id"]] = analysis.decode_native(case, row, protocol["model"]["temperatures"], 8192)
        loaded = {"rows": rows, "diagnostics": diagnostics,
                  "identity": {"entrypoint_source": analysis.file_descriptor(Path(__file__).with_name("run_saif_benchmark.py"))},
                  "sessions": [], "evidence": {}}
        with patch.object(analysis, "read_completed_saif", return_value=loaded):
            report, exported = analysis.analyze(protocol["cases"]["path"], "NOT_READ_SYNTHETIC_RUN", protocol_path,
                analysis.file_descriptor(protocol_path)["sha256"], protocol["baseline_sources"]["expanded_csv"]["path"],
                protocol["baseline_sources"]["additional_csv"]["path"])
        self.assertEqual(len(exported), 1200)
        self.assertEqual(set(report["baseline_summary"]), set(analysis.BASELINE_KEYS))
        model = report["models"][analysis.MODEL_KEY]
        self.assertNotIn("wilson_95", model["overall"])
        self.assertNotIn("paired_baselines", model["overall"])
        for task in ("memory", "commander"):
            for language, total in (("ja", 500), ("en", 100)):
                group = model["tasks"][task][language]
                self.assertEqual(group["n"], total)
                self.assertEqual(group["correct"], total)
                self.assertEqual(set(group["paired_baselines"]), set(analysis.BASELINE_KEYS))
        descriptive = model["tasks"]["memory"]["ja"]["native_probability_diagnostics"]
        self.assertEqual(descriptive["typed_prediction_histogram"], {"false": 250, "true": 250})
        self.assertEqual(descriptive["native_p_true"]["min"], 0.25)
        self.assertEqual(descriptive["native_p_true"]["max"], 0.75)
        self.assertEqual(model["native_only_latency"]["mean_ms"], 1)
        self.assertEqual(model["latency"]["mean_ms"], 2)


def analysis_fixture_csvs(cases, paired, additional):
    metadata = ["case_id", "world_id", "suite", "family_id", "mechanism_group", "language", "primary", "gold"]
    fields = metadata + [name + suffix for name in ("hindsight", "tinyjev") for suffix in ("_status", "_prediction", "_correct")]
    with paired.open("w", newline="") as out:
        writer = csv.DictWriter(out, fieldnames=fields);writer.writeheader()
        for case in cases:
            row = {key: case[key] for key in metadata if key not in {"primary", "gold"}}
            row.update(primary=case["language"] == "ja", gold=json.dumps(case["expected"]))
            for name in ("hindsight", "tinyjev"):
                row.update({name + "_status": "complete", name + "_prediction": json.dumps(case["expected"]), name + "_correct": True})
            writer.writerow(row)
    fields = ["model_key"] + metadata + ["status", "predictions", "correct"]
    with additional.open("w", newline="") as out:
        writer = csv.DictWriter(out, fieldnames=fields);writer.writeheader()
        for name in analysis.ADDITIONAL_KEYS:
            for case in cases:
                row = {key: case[key] for key in metadata if key not in {"primary", "gold"}}
                row.update(model_key=name, primary=case["language"] == "ja", gold=json.dumps(case["expected"]),
                           status="complete", predictions=json.dumps(case["expected"]), correct=True)
                writer.writerow(row)


def analysis_fixture_run(root, cases):
    shared = analysis.shared
    rows = []
    for index, case in enumerate(cases):
        row = {"schema_version": 1, "case_index": index, "suite": case["suite"], "language": case["language"],
               "expected": copy.deepcopy(case["expected"]), "request_sha256": shared.digest(case["request"]), **fixture_row(case)}
        rows.append(shared.seal_record(row))
    request_path, journal = root / "requests.ndjson", root / "results.ndjson"
    request_path.write_bytes(b"".join(shared.encoded({"case_id": case["case_id"], "request": case["request"]}) + b"\n" for case in cases))
    journal.write_bytes(b"".join(shared.encoded(row) + b"\n" for row in rows))
    manifest = {"identity": {"synthetic": True}, "requests_file": analysis.file_descriptor(request_path),
                "sessions": [{"resume_from": 0, "warmup_request": shared.WARMUP_REQUEST,
                              "warmup": {"status": "complete", "error": None, "predictions": {"warmup": "red"}}}]}
    (root / "manifest.json").write_text(json.dumps(manifest))
    report = {"status": "complete", "error": None, "completed_cases": len(rows), "planned_cases": len(rows),
              "results": rows, "identity": manifest["identity"], "sessions": manifest["sessions"],
              "manifest_sha256": analysis.file_descriptor(root / "manifest.json")["sha256"],
              "journal_sha256": analysis.file_descriptor(journal)["sha256"]}
    (root / "result.json").write_text(json.dumps(report))
    return report, manifest, journal


def analysis_fixture_identity(root, cases):
    shared = analysis.shared
    protocol_path = root / "protocol.json"
    protocol_path.write_text('{"synthetic_protocol":true}')
    case_path = root / "cases.json"
    case_path.write_text(json.dumps(cases))
    model_root, source_root, sdk_root = root / "model", root / "source", root / "sdk"
    for path in (model_root, source_root, sdk_root): path.mkdir()
    source_names = ["decision_index_engine.py", "saif_v2_core.py", "singlepass.py"]
    for name in source_names + ["decoder.safetensors"]:
        (model_root / name).write_bytes(b"synthetic fixture bytes: " + name.encode())
    source_files = []
    for name in source_names:
        (source_root / name).write_bytes((model_root / name).read_bytes())
        source_files.append(analysis.file_descriptor(source_root / name))
    sdk_files = []
    for name in ("a.py", "b.py", "c.py"):
        (sdk_root / name).write_bytes(b"synthetic SDK fixture")
        sdk_files.append(analysis.file_descriptor(sdk_root / name))
    model_manifest = {"repo": analysis.MODEL_ID, "revision": analysis.REVISION,
                      "files": [{"filename": name, **analysis.file_descriptor(model_root / name)}
                                for name in source_names + ["decoder.safetensors"]]}
    manifests = {"checkpoint_manifest": model_manifest,
                 "source_manifest": {"files": [{**row, "path": Path(row["path"]).name} for row in source_files]},
                 "sdk_manifest": {"files": sdk_files}}
    model = {"model_id": analysis.MODEL_ID, "revision": analysis.REVISION, "temperatures": copy.deepcopy(TEMPERATURES)}
    for name, value in manifests.items():
        path = root / (name + ".json")
        path.write_text(json.dumps(value))
        model[name] = analysis.file_descriptor(path)
    frozen = []
    for name in ("compare_saif_lightning.py", "run_additional_candidates.py", "run_saif_benchmark.py", "run_expanded_benchmark.py"):
        path = root / name;path.write_text("# synthetic frozen source")
        frozen.append(analysis.file_descriptor(path))
    sources = {Path(row["path"]).name: row for row in frozen}
    runtime = {"threads": 4, "affinity": [0, 1, 2, 3], "request_timeout_seconds": 300, "python": "3.12.14",
               "torch": "2.9.1+cpu", "transformers": "5.17.0", "accelerate": "1.15.0", "huggingface_hub": "1.33.0"}
    checkpoint = {"repository": analysis.MODEL_ID, "revision": analysis.REVISION, "model_dir": str(model_root.resolve()),
                  "manifest": model["checkpoint_manifest"], "files": copy.deepcopy(model_manifest["files"])}
    description = {"decoder_dtype": "torch.bfloat16", "head_dtype": "torch.float32", "decoder_device": "cpu", "head_device": "cpu",
                   "torch_threads": 4, "reading": "joint", "read_twice": False, "fast_path": True,
                   "calibration_fit_on_this_corpus": False, "native_temperatures": copy.deepcopy(TEMPERATURES),
                   "packages": {"torch": runtime["torch"], "transformers": runtime["transformers"],
                                "accelerate": runtime["accelerate"], "huggingface-hub": runtime["huggingface_hub"]}}
    identity = {"adapter": "saif-lightning-native", "server_url": "native://cpu", "model_id": analysis.MODEL_ID,
                "model_revision": analysis.REVISION, "limit": None, "total_corpus_cases": len(cases),
                "case_order": [case["case_id"] for case in cases], "requests_sha256": shared.digest([case["request"] for case in cases]),
                "gold_sha256": shared.digest([case["expected"] for case in cases]), "protocol": analysis.file_descriptor(protocol_path),
                "cases_source": analysis.file_descriptor(case_path), "source_manifest": model["source_manifest"],
                "sdk_manifest": model["sdk_manifest"], "adapter_source": sources["compare_saif_lightning.py"],
                "runner_source": sources["run_additional_candidates.py"], "entrypoint_source": sources["run_saif_benchmark.py"],
                "affinity_helper_source": sources["run_expanded_benchmark.py"], "threads": 4, "affinity": [0, 1, 2, 3],
                "timeout_seconds": 300, "warmup_request_sha256": shared.digest(shared.WARMUP_REQUEST), "python": runtime["python"],
                "adapter_description": description, "checkpoint": checkpoint, "model_sha256": shared.digest(checkpoint),
                "native_sources": source_files + sdk_files}
    protocol = {"model": model, "cases": analysis.file_descriptor(case_path), "runtime": runtime, "frozen_sources": frozen}
    return protocol, identity


def synthetic_frozen_case_row(case, temperatures):
    """Schema-only oracle-perfect packets: no SAIF output, tokenizer or inference."""
    name, question = next(iter(case["request"]["questions"].items()))
    gold = case["expected"][name]
    if question["type"] == "noul":
        probabilities = {"false": 0.25 if gold else 0.75, "true": 0.75 if gold else 0.25}
        answer = {"type": "noul", "noul": probabilities["true"], "probabilities": probabilities}
    else:
        keys = list(question["criteria"])
        probabilities = {key: 0.8 if key == gold else 0.2 / (len(keys) - 1) for key in keys}
        answer = {"type": "choice", "choice": gold, "probabilities": probabilities}
    call = {"request": copy.deepcopy(case["request"]), "raw": {"model": "synthetic", "answers": {name: answer}},
            "native_answers": {name: copy.deepcopy(answer)}, "probabilities": {name: copy.deepcopy(probabilities)},
            "native_temperatures": copy.deepcopy(temperatures), "native_elapsed_ms": 1.0,
            "input_tokens": 40, "output_tokens": 0,
            "layouts": [{"input_tokens": 40, "prefix_tokens": 10, "input_ids_sha256": "b" * 64,
                         "fields": [name], "slots": [38], "blocks": [[10, 40]],
                         "label_token_ids": [list(range(100, 100 + len(probabilities)))]}]}
    return {"case_id": case["case_id"], "status": "complete", "error": None, "correct": True,
            "predictions": copy.deepcopy(case["expected"]), "duration_ms": 2.0, "input_tokens": 40, "output_tokens": 0,
            "record_sha256": "a" * 64, "raw": {"predictions": copy.deepcopy(case["expected"]),
                "details": {"calls": [call], "raw_logits": None, "initialization_excluded_from_request": True}}}


if __name__ == "__main__":
    unittest.main()
