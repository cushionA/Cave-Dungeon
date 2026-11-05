import copy
import hashlib
import json
import struct
import tempfile
import unittest
import zipfile
from pathlib import Path

import numpy as np
from safetensors.numpy import save_file

from prepare import MODEL_ID, MODEL_REVISION, build_smoke_cases
from prepare_gguf import _extract, ensure_artifact, export_head, validate_checkpoint, verify_existing_artifact, write_cases


class GgufPreparationTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.root = Path(self.directory.name)
        self.checkpoint = self.root / "checkpoint"
        self.checkpoint.mkdir()
        (self.checkpoint / "config.json").write_text(json.dumps({"hidden_size": 3}))
        (self.checkpoint / "tinyjev.json").write_text(json.dumps({"head": {"head_dim": 2, "temperature": 1.5}}))
        self.weights = {"q.weight": np.arange(1, 7, dtype=np.float32).reshape(2, 3),
                        "q.bias": np.arange(7, 9, dtype=np.float32),
                        "k.weight": np.arange(9, 15, dtype=np.float32).reshape(2, 3),
                        "k.bias": np.arange(15, 17, dtype=np.float32)}
        save_file(self.weights, str(self.checkpoint / "head.safetensors"))
        self.head = self.root / "tinyjev.head"
        self.model = self.root / "fixture.gguf"
        self.model.write_bytes(b"GGUF" + struct.pack("<IQQ", 3, 0, 0))
        self.model_sha = hashlib.sha256(self.model.read_bytes()).hexdigest()
        self.cases = build_smoke_cases()
        self.cases.update(model_id=MODEL_ID, model_revision=MODEL_REVISION)
        self.source = self.root / "source.json"
        self.source.write_text(json.dumps(self.cases))

    def tearDown(self):
        self.directory.cleanup()

    def test_HeadBinary_HeaderOffsetsAndRowMajorWeights_MatchContract(self):
        export_head(self.checkpoint, self.head)
        data = self.head.read_bytes()
        self.assertEqual((b"JEVHEAD1", 3, 2, 1.5), struct.unpack("<8siif", data[:20]))
        self.assertEqual(84, len(data))
        self.assertEqual(tuple(range(1, 17)), struct.unpack("<16f", data[20:]))
        first = export_head(self.checkpoint, self.head)
        second_path = self.root / "second.head"
        second = export_head(self.checkpoint, second_path)
        self.assertEqual(self.head.read_bytes(), second_path.read_bytes())
        self.assertEqual(first["head_sha256"], second["head_sha256"])
        for name in ("config.json", "tinyjev.json", "head.safetensors"):
            self.assertEqual(hashlib.sha256((self.checkpoint / name).read_bytes()).hexdigest(), first["source_sha256"][name])

    def test_ArtifactReuse_CorruptionOrChangedProvenance_IsRejectedWithoutBlessingBytes(self):
        original = self.model.read_bytes()
        expected = {"source_sha256": {"weights": "trained"}, "tools": {"converter": "pinned"},
                    "parent_f16_sha256": "original-parent", "quantization": "Q8_0"}
        generated = []

        def generate(path):
            generated.append(path)
            path.write_bytes(original)

        with self.assertRaises(ValueError):
            ensure_artifact(self.model, expected, generate)
        self.assertEqual([], generated)
        verify_existing_artifact(self.model, expected, generate)
        self.assertEqual(original, self.model.read_bytes())
        ensure_artifact(self.model, expected, generate)
        self.assertEqual(1, len(generated))
        manifest = self.model.with_suffix(".gguf.provenance.json")
        trusted_manifest = manifest.read_bytes()
        self.model.write_bytes(original[:-1] + b"x")
        with self.assertRaises(ValueError):
            ensure_artifact(self.model, expected, generate)
        with self.assertRaises(ValueError):
            verify_existing_artifact(self.model, expected, generate)
        self.assertEqual(trusted_manifest, manifest.read_bytes())
        self.assertEqual(original[:-1] + b"x", self.model.read_bytes())
        export_head(self.checkpoint, self.head)
        with self.assertRaises(ValueError):
            write_cases(self.source, self.model, self.head, "Q8_0", self.root / "cases.json", self.model_sha)
        self.assertFalse((self.root / "cases.json").exists())
        self.model.write_bytes(original)
        for field, value in (("source_sha256", {"weights": "other"}),
                             ("tools", {"converter": "other"}), ("parent_f16_sha256", "other-parent")):
            with self.subTest(field=field), self.assertRaises(ValueError):
                ensure_artifact(self.model, {**expected, field: value}, generate)
        self.assertEqual(trusted_manifest, manifest.read_bytes())
        fresh = self.root / "fresh.gguf"
        ensure_artifact(fresh, expected, generate)
        self.assertTrue(fresh.with_suffix(".gguf.provenance.json").exists())
        self.assertEqual(original, fresh.read_bytes())

    def test_HeadExport_InvalidDimensionsTemperatureOrValues_IsRejected(self):
        for temperature in (0, -1, float("nan")):
            (self.checkpoint / "tinyjev.json").write_text(json.dumps({"head": {"head_dim": 2, "temperature": temperature}}))
            with self.assertRaises(ValueError):
                export_head(self.checkpoint, self.head)
        (self.checkpoint / "tinyjev.json").write_text(json.dumps({"head": {"head_dim": 2, "temperature": 1.5}}))
        for broken in (np.zeros((3, 2), dtype=np.float32), np.full((2, 3), np.nan, dtype=np.float32)):
            save_file({**self.weights, "q.weight": broken}, str(self.checkpoint / "head.safetensors"))
            with self.assertRaises(ValueError):
                export_head(self.checkpoint, self.head)

    def test_GgufCases_PreservesCandidatesInputsAndReferenceProvenance(self):
        export_head(self.checkpoint, self.head)
        original = copy.deepcopy(self.cases)
        document = write_cases(self.source, self.model, self.head, "Q8_0", self.root / "cases.json", self.model_sha)
        self.assertEqual(original["cases"], document["cases"])
        self.assertEqual(original, json.loads(self.source.read_text()))
        self.assertEqual("gguf", document["model_format"])
        self.assertEqual("Q8_0", document["backbone_quantization"])
        self.assertEqual("tinyjev.head", document["head_file"])
        self.assertEqual(hashlib.sha256(self.model.read_bytes()).hexdigest(), document["model_sha256"])
        self.assertEqual(hashlib.sha256(self.head.read_bytes()).hexdigest(), document["head_sha256"])
        self.assertEqual(original["model_sha256"], document["reference_source"]["model_sha256"])

    def test_GgufCases_InvalidModelHeadSourceOrQuantization_IsRejected(self):
        with self.assertRaises(ValueError):
            validate_checkpoint(self.checkpoint)
        archive = self.root / "unsafe.zip"
        with zipfile.ZipFile(archive, "w") as package:
            package.writestr("../escape", "invalid")
        with self.assertRaises(ValueError):
            _extract(archive, self.root / "tools")
        self.assertFalse((self.root / "escape").exists())
        export_head(self.checkpoint, self.head)
        with self.assertRaises(ValueError):
            write_cases(self.source, self.model, self.head, "Q4_0", self.root / "cases.json", self.model_sha)
        self.model.write_bytes(b"not gguf")
        with self.assertRaises(ValueError):
            write_cases(self.source, self.model, self.head, "F16", self.root / "cases.json", self.model_sha)
        self.model.write_bytes(b"GGUF" + struct.pack("<IQQ", 3, 0, 0))
        self.head.write_bytes(self.head.read_bytes()[:-4])
        with self.assertRaises(ValueError):
            write_cases(self.source, self.model, self.head, "F16", self.root / "cases.json", self.model_sha)
        export_head(self.checkpoint, self.head)
        self.cases["model_revision"] = "unverified"
        self.source.write_text(json.dumps(self.cases))
        with self.assertRaises(ValueError):
            write_cases(self.source, self.model, self.head, "F16", self.root / "cases.json", self.model_sha)
        self.source.write_text("null")
        with self.assertRaises(ValueError):
            write_cases(self.source, self.model, self.head, "F16", self.root / "cases.json", self.model_sha)
        self.cases["model_revision"] = MODEL_REVISION
        original = copy.deepcopy(self.cases)
        for field, value in (("case_id", []), ("input_ids", [151936] * 256)):
            document = copy.deepcopy(original)
            document["cases"][0][field] = value
            self.source.write_text(json.dumps(document))
            with self.assertRaises(ValueError):
                write_cases(self.source, self.model, self.head, "F16", self.root / "cases.json", self.model_sha)


if __name__ == "__main__":
    unittest.main()
