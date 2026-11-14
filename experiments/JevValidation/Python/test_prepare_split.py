import copy
import json
from pathlib import Path
import tempfile
import unittest

import numpy as np
import onnx
from onnx import TensorProto, helper, numpy_helper
import onnxruntime as ort

from prepare_split import discover_boundaries, partition_model, sha256, split, verify


def residual_model():
    """Small weights with the same 28-block boundary/dependency structure."""
    rng = np.random.default_rng(42)
    inputs = [helper.make_tensor_value_info("input_ids", TensorProto.INT32, [1, 256]),
              helper.make_tensor_value_info("attention_mask", TensorProto.INT32, [1, 256]),
              helper.make_tensor_value_info("option_positions", TensorProto.INT32, [4]),
              helper.make_tensor_value_info("decision_position", TensorProto.INT32, [1])]
    initializers = [numpy_helper.from_array(rng.normal(size=(32, 4)).astype(np.float32), "backbone.embed_tokens.weight"),
                    numpy_helper.from_array(np.asarray([2], np.int64), "axes_2"),
                    numpy_helper.from_array(np.asarray([0], np.int64), "axes_0")]
    nodes = [helper.make_node("Gather", ["backbone.embed_tokens.weight", "input_ids"], ["embedding_result"],
                              axis=0, name="/backbone/embed_tokens/Gather"),
             helper.make_node("Cast", ["attention_mask"], ["mask_float"], to=TensorProto.FLOAT, name="/backbone/Cast"),
             helper.make_node("Unsqueeze", ["mask_float", "axes_2"], ["mask"], name="/backbone/Unsqueeze")]
    hidden = "embedding_result"
    for index in range(28):
        weight = f"layer_{index}_bias"
        initializers.append(numpy_helper.from_array(rng.normal(size=(4,)).astype(np.float32) * .01, weight))
        temporary, output = f"add_{index}", f"residual_{index}"
        nodes.append(helper.make_node("Add", [hidden, weight], [temporary], name=f"/backbone/layers.{index}/Add"))
        nodes.append(helper.make_node("Mul", [temporary, "mask"], [output], name=f"/backbone/layers.{index}/Mul"))
        hidden = output
    nodes.extend([helper.make_node("Gather", [hidden, "option_positions"], ["options"], axis=1, name="/head/Gather"),
                  helper.make_node("ReduceMean", ["options"], ["logits_2d"], axes=[2], keepdims=0),
                  helper.make_node("Squeeze", ["logits_2d", "axes_0"], ["logits"]),
                  helper.make_node("Softmax", ["logits"], ["probabilities"], axis=0)])
    model = helper.make_model(helper.make_graph(nodes, "synthetic_residual", inputs,
        [helper.make_tensor_value_info("probabilities", TensorProto.FLOAT, [4])], initializers),
        opset_imports=[helper.make_opsetid("", 17)])
    model.ir_version = 10
    onnx.checker.check_model(model)
    return model


def write_fixture(directory, uniform=False):
    directory = Path(directory)
    source = directory / "source.onnx"
    model = residual_model()
    if uniform:
        model.graph.initializer[0].CopyFrom(numpy_helper.from_array(np.zeros((32, 4), np.float32),
                                                                  "backbone.embed_tokens.weight"))
    onnx.save_model(model, source)
    options = ort.SessionOptions()
    options.intra_op_num_threads = 1
    options.inter_op_num_threads = 1
    session = ort.InferenceSession(str(source), sess_options=options, providers=["CPUExecutionProvider"])
    cases = []
    for index in range(2):
        case = {"case_id": f"case_{index}", "state_id": f"state_{index}",
                "choice_ids": ["a", "b", "c", "d"],
                "input_ids": [int((value + index) % 32) for value in range(256)],
                "attention_mask": [1] * 256, "option_positions": [5, 12, 24, 30], "decision_position": [31]}
        tensors = {name: np.asarray(case[name], dtype=np.int32) for name in
                   ("input_ids", "attention_mask", "option_positions", "decision_position")}
        for name in ("input_ids", "attention_mask"):
            tensors[name] = tensors[name].reshape(1, 256)
        case["reference_probabilities"] = session.run(None, tensors)[0].tolist()
        cases.append(case)
    document = {"schema_version": 1, "model_id": "smoke", "model_revision": "synthetic-split-v1",
                "model_sha256": sha256(source), "token_count": 256, "cases": cases}
    cases_path = directory / "cases.json"
    cases_path.write_text(json.dumps(document), encoding="utf-8")
    return source, cases_path


class SplitModelTests(unittest.TestCase):
    def test_ResidualGraph_FourParts_PreservesOriginalProbabilitiesAndLargeWeightOwnership(self):
        with tempfile.TemporaryDirectory() as directory:
            source, cases = write_fixture(directory)
            output = Path(directory) / "split"
            manifest = split(source, cases, output)
            self.assertEqual(["embedding", "blocks_00_09", "blocks_10_18", "blocks_19_27_head"],
                             [chunk["name"] for chunk in manifest["chunks"]])
            self.assertEqual(["hidden_0", "hidden_10", "hidden_19", "probabilities"],
                             [chunk["outputs"][0] for chunk in manifest["chunks"]])
            self.assertEqual(["input_ids"], manifest["chunks"][0]["inputs"])
            self.assertEqual(["hidden_0", "attention_mask"], manifest["chunks"][1]["inputs"])
            embedding_owners = [chunk["name"] for chunk in manifest["chunks"]
                                if any(weight["name"] == "backbone.embed_tokens.weight" for weight in chunk["initializers"])]
            self.assertEqual(["embedding"], embedding_owners)
            self.assertEqual(sha256(cases), manifest["case_sha256"])
            report = verify(output / "split-manifest.json", threads=1, tolerance=1e-6)
            self.assertTrue(report["success"])
            self.assertEqual(2, report["choice_matches"])
            self.assertLess(report["max_reference_error"], 1e-6)

    def test_AmbiguousCrossBlockDependency_IsRejected(self):
        model = residual_model()
        model.graph.node.insert(25, helper.make_node("Identity", ["add_9"], ["extra"],
                                                     name="/backbone/layers.10/Extra"))
        with self.assertRaisesRegex(ValueError, "Expected one hidden-state edge"):
            discover_boundaries(model)

    def test_UnresolvedDependency_IsRejectedBeforeExtraction(self):
        model = residual_model()
        for node in model.graph.node:
            if node.name == "/backbone/layers.12/Add":
                node.input[1] = "missing_tensor"
        with self.assertRaisesRegex(ValueError, "unresolved tensor dependency"):
            list(partition_model(model, 256))

    def test_ModifiedSourceOrCases_AreRejected(self):
        with tempfile.TemporaryDirectory() as directory:
            source, cases = write_fixture(directory)
            original = json.loads(cases.read_text())
            altered = copy.deepcopy(original)
            altered["model_sha256"] = "0" * 64
            cases.write_text(json.dumps(altered))
            with self.assertRaisesRegex(ValueError, "exact source ONNX SHA"):
                split(source, cases, Path(directory) / "wrong")
            cases.write_text(json.dumps(original))
            output = Path(directory) / "split"
            split(source, cases, output)
            (output / "cases.json").write_text(json.dumps(altered))
            with self.assertRaisesRegex(ValueError, "case SHA-256 mismatch"):
                verify(output / "split-manifest.json", threads=1)

    def test_WithinTolerance_ChangedSelectedAction_IsRejectedAndRecorded(self):
        with tempfile.TemporaryDirectory() as directory:
            source, cases = write_fixture(directory, uniform=True)
            document = json.loads(cases.read_text())
            document["cases"][0]["reference_probabilities"] = [.249, .251, .25, .25]
            cases.write_text(json.dumps(document))
            output = Path(directory) / "split"
            split(source, cases, output)
            with self.assertRaisesRegex(ValueError, "selected-action agreement"):
                verify(output / "split-manifest.json", threads=1, tolerance=.005)
            report = json.loads((output / "split-ort-parity.json").read_text())
            self.assertFalse(report["success"])
            self.assertTrue(report["cases"][0]["numeric_matches"])
            self.assertFalse(report["cases"][0]["choice_matches"])


if __name__ == "__main__":
    unittest.main()
