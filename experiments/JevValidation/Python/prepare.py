import argparse
import hashlib
import json
import math
from pathlib import Path

TOKEN_COUNT = 256
MODEL_ID = "AnkitAI/TinyJev-0.6B"
MODEL_REVISION = "c559c2f7ea95069f92af8b999f512345bbb87d22"
CHOICE_IDS = ["wait", "approach", "attack", "retreat"]
STATES = [
    ("healthy_melee", "I am healthy and well rested. An enemy is within melee reach. I can safely attack now."),
    ("critical_melee", "I am critically wounded and exhausted. The enemy is within melee reach. Another hit will kill me; I need to retreat to safety."),
]
NUMERIC_STATE = ("Player health: {health}/100. Player stamina: 50/100. Player ammo: 10. "
                 "Enemy health: 80/100. Enemy distance: 1.5 meters. Melee range: 2.0 meters. "
                 "Player melee damage: 20. Enemy damage per hit: 10. Cover distance: 4.0 meters.")
REAL_STATES = [("verbal_" + state_id, state) for state_id, state in STATES] + [
    ("numeric_hp90", NUMERIC_STATE.format(health=90)),
    ("numeric_hp5", NUMERIC_STATE.format(health=5)),
]


def load_states(path=None):
    values = json.loads(Path(path).read_text(encoding="utf-8")) if path is not None else [
        {"state_id": state_id, "state": state} for state_id, state in REAL_STATES]
    if not isinstance(values, list) or len(values) < 2:
        raise ValueError("The states document must contain at least two states")
    result = []
    seen = set()
    for value in values:
        if not isinstance(value, dict) or set(value) != {"state_id", "state"}:
            raise ValueError("Each state must contain exactly state_id and state")
        state_id, state = value["state_id"], value["state"]
        if not isinstance(state_id, str) or not state_id.strip() or state_id in seen:
            raise ValueError("State IDs must be nonempty unique strings")
        if not isinstance(state, str) or not state.strip():
            raise ValueError("Each state must contain nonempty text")
        seen.add(state_id)
        result.append((state_id, state))
    return result


def validate_case(case, vocab_size=None):
    if not isinstance(case, dict):
        raise ValueError("A prepared case must be an object")
    for name in ("case_id", "state_id"):
        if not isinstance(case.get(name), str) or not case[name].strip():
            raise ValueError(f"{name} must be a nonempty string")
    if "state" in case and (not isinstance(case["state"], str) or not case["state"].strip()):
        raise ValueError("state must be a nonempty string when supplied")
    choices = case.get("choice_ids")
    if not isinstance(choices, list) or len(choices) != 4:
        raise ValueError("Exactly four choice_ids are required")
    if any(not isinstance(choice, str) or not choice.strip() for choice in choices) or len(set(choices)) != 4:
        raise ValueError("choice_ids must be nonempty and unique")
    ids = case.get("input_ids")
    mask = case.get("attention_mask")
    if not isinstance(ids, list) or len(ids) != TOKEN_COUNT:
        raise ValueError("input_ids must contain exactly 256 integers")
    if any(type(value) is not int or value < 0 or value > 2147483647 for value in ids):
        raise ValueError("input_ids must be nonnegative int32 values")
    if vocab_size is not None and any(value >= vocab_size for value in ids):
        raise ValueError("An input token exceeds the model vocabulary")
    if not isinstance(mask, list) or len(mask) != TOKEN_COUNT:
        raise ValueError("attention_mask must contain exactly 256 integers")
    if any(type(value) is not int or value not in (0, 1) for value in mask) or not any(mask):
        raise ValueError("attention_mask must contain zeroes and ones with at least one active token")
    active = sum(mask)
    if mask != [1] * active + [0] * (TOKEN_COUNT - active):
        raise ValueError("attention_mask must describe right-padded input")
    for name, count in (("option_positions", 4), ("decision_position", 1)):
        values = case.get(name)
        if not isinstance(values, list) or len(values) != count:
            raise ValueError(f"{name} must contain exactly {count} positions")
        if any(type(value) is not int or value < 0 or value >= active for value in values):
            raise ValueError(f"{name} contains a position outside the active tokens")
        if len(set(values)) != count:
            raise ValueError(f"{name} contains duplicate positions")
    if case["decision_position"][0] in case["option_positions"]:
        raise ValueError("The decision token must be distinct from option tokens")
    probabilities = case.get("reference_probabilities")
    if not isinstance(probabilities, list) or len(probabilities) != 4:
        raise ValueError("reference_probabilities must contain exactly four values")
    if any(type(value) not in (int, float) or not math.isfinite(value) or value < 0 or value > 1 for value in probabilities):
        raise ValueError("reference_probabilities must be finite values in [0, 1]")
    if not math.isclose(sum(probabilities), 1.0, rel_tol=1e-5, abs_tol=1e-5):
        raise ValueError("reference_probabilities must sum to one")


def _smoke_model():
    import onnx
    from onnx import TensorProto, helper, numpy_helper
    import numpy as np

    inputs = [
        helper.make_tensor_value_info("input_ids", TensorProto.INT32, [1, TOKEN_COUNT]),
        helper.make_tensor_value_info("attention_mask", TensorProto.INT32, [1, TOKEN_COUNT]),
        helper.make_tensor_value_info("option_positions", TensorProto.INT32, [4]),
        helper.make_tensor_value_info("decision_position", TensorProto.INT32, [1]),
    ]
    initializers = [
        numpy_helper.from_array(np.asarray([TOKEN_COUNT], dtype=np.int64), "flat_shape"),
        numpy_helper.from_array(np.asarray(0.5, dtype=np.float32), "option_scale"),
        numpy_helper.from_array(np.asarray([0.0, -0.2, -0.6, 0.7], dtype=np.float32), "state_coefficients"),
    ]
    nodes = [
        helper.make_node("Cast", ["input_ids"], ["ids_float"], to=TensorProto.FLOAT),
        helper.make_node("Cast", ["attention_mask"], ["mask_float"], to=TensorProto.FLOAT),
        helper.make_node("Mul", ["ids_float", "mask_float"], ["masked_ids"]),
        helper.make_node("Reshape", ["masked_ids", "flat_shape"], ["flat_ids"]),
        helper.make_node("Gather", ["flat_ids", "option_positions"], ["options"], axis=0),
        helper.make_node("Gather", ["flat_ids", "decision_position"], ["state_value"], axis=0),
        helper.make_node("Mul", ["options", "option_scale"], ["option_logits"]),
        helper.make_node("Mul", ["state_value", "state_coefficients"], ["state_logits"]),
        helper.make_node("Add", ["option_logits", "state_logits"], ["logits"]),
        helper.make_node("Softmax", ["logits"], ["probabilities"], axis=0),
    ]
    graph = helper.make_graph(nodes, "JevValidationSmoke", inputs,
                              [helper.make_tensor_value_info("probabilities", TensorProto.FLOAT, [4])], initializers)
    model = helper.make_model(graph, opset_imports=[helper.make_opsetid("", 17)], producer_name="JevValidation smoke")
    model.ir_version = 10
    onnx.checker.check_model(model)
    return model


def build_smoke_cases():
    coefficients = [0.0, -0.2, -0.6, 0.7]
    cases = []
    for (state_id, state), state_value in zip(STATES, (1, 5)):
        ids = [0] * TOKEN_COUNT
        positions = [4, 8, 12, 16]
        for position, value in zip(positions, (1, 2, 4, 1)):
            ids[position] = value
        ids[20] = state_value
        logits = [value * 0.5 + state_value * coefficient for value, coefficient in zip((1, 2, 4, 1), coefficients)]
        weights = [math.exp(value - max(logits)) for value in logits]
        case = {
            "case_id": state_id,
            "state_id": state_id,
            "state": state,
            "choice_ids": list(CHOICE_IDS),
            "input_ids": ids,
            "attention_mask": [1] * 24 + [0] * (TOKEN_COUNT - 24),
            "option_positions": positions,
            "decision_position": [20],
            "reference_probabilities": [weight / sum(weights) for weight in weights],
        }
        validate_case(case)
        cases.append(case)
    return {
        "schema_version": 1,
        "model_id": "smoke",
        "model_revision": "smoke-v1",
        "model_sha256": hashlib.sha256(_smoke_model().SerializeToString()).hexdigest(),
        "token_count": TOKEN_COUNT,
        "cases": cases,
    }


def _write_document(document, path):
    for case in document["cases"]:
        validate_case(case)
    path.write_text(json.dumps(document, indent=2, allow_nan=False) + "\n", encoding="utf-8")


def _check_onnx(model_path, document, tolerance):
    import numpy as np
    import onnxruntime as ort

    session = ort.InferenceSession(str(model_path), providers=["CPUExecutionProvider"])
    expected_inputs = {"input_ids": [1, TOKEN_COUNT], "attention_mask": [1, TOKEN_COUNT],
                       "option_positions": [4], "decision_position": [1]}
    if {value.name: value.shape for value in session.get_inputs()} != expected_inputs or any(
            value.type != "tensor(int32)" for value in session.get_inputs()):
        raise ValueError("The ONNX input names, shapes, or dtypes violate the fixed contract")
    outputs = session.get_outputs()
    if len(outputs) != 1 or outputs[0].name != "probabilities" or outputs[0].shape != [4] or outputs[0].type != "tensor(float)":
        raise ValueError("The ONNX output must be probabilities: float32[4]")
    results = []
    for case in document["cases"]:
        inputs = {name: np.asarray(case[name], dtype=np.int32) for name in
                  ("input_ids", "attention_mask", "option_positions", "decision_position")}
        inputs["input_ids"] = inputs["input_ids"].reshape(1, TOKEN_COUNT)
        inputs["attention_mask"] = inputs["attention_mask"].reshape(1, TOKEN_COUNT)
        actual = session.run(["probabilities"], inputs)[0]
        if actual.shape != (4,) or not np.isfinite(actual).all() or np.any(actual < 0) or not np.isclose(actual.sum(), 1.0, atol=1e-5):
            raise ValueError("The ONNX output is not a four-choice probability distribution")
        expected = np.asarray(case["reference_probabilities"], dtype=np.float32)
        np.testing.assert_allclose(actual, expected, atol=tolerance, rtol=tolerance)
        results.append({"case_id": case["case_id"], "probabilities": actual.tolist(),
                        "max_absolute_error": float(np.max(np.abs(actual - expected)))})
    return results


def export_smoke(output):
    output = Path(output)
    output.mkdir(parents=True, exist_ok=True)
    model_path = output / "smoke.onnx"
    model_path.write_bytes(_smoke_model().SerializeToString())
    document = build_smoke_cases()
    _write_document(document, output / "cases.json")
    parity = _check_onnx(model_path, document, 1e-6)
    (output / "parity.json").write_text(json.dumps(parity, indent=2) + "\n", encoding="utf-8")
    return document


def _sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _download_checkpoint(directory):
    from huggingface_hub import snapshot_download

    return Path(snapshot_download(
        MODEL_ID, revision=MODEL_REVISION, token=False, local_dir=directory,
        allow_patterns=["config.json", "head.safetensors", "model.safetensors", "tinyjev.json",
                        "tokenizer.json", "tokenizer_config.json"],
    ))


def _request(state_id, state):
    return {
        "id": state_id,
        "state": state,
        "questions": {
            "action": {
                "type": "choice",
                "instructions": "Which action should I take now to survive this combat encounter?",
                "criteria": {
                    "wait": "Stay here without moving or attacking.",
                    "approach": "Move closer to the enemy.",
                    "attack": "Perform a melee attack on the enemy now.",
                    "retreat": "Move away from the enemy to safety.",
                },
            },
        },
    }


def _real_cases(agent, states_path=None):
    import tinyjev

    cases = []
    for state_id, state in load_states(states_path):
        payload = _request(state_id, state)
        encoded = agent.family.encode(tinyjev.normalize_request(payload)[0])
        if len(encoded.questions) != 1 or len(encoded.rows) != 1:
            raise ValueError("The export requires exactly one question and one causal row")
        question = encoded.questions[0]
        if question["keys"] != CHOICE_IDS or len(question["opts"]) != 4:
            raise ValueError("The encoded choice order does not match the four-choice contract")
        ids = list(encoded.prefix) + list(encoded.rows[0])
        if len(ids) > TOKEN_COUNT:
            raise ValueError(f"{state_id}: {len(ids)} tokens exceeds the fixed 256-token export")
        reference = agent.logits(payload)[f"{state_id}:action"]
        if reference["candidate_ids"] != CHOICE_IDS:
            raise ValueError("The upstream reference returned a different choice order")
        case = {
            "case_id": state_id,
            "state_id": state_id,
            "state": state,
            "choice_ids": list(CHOICE_IDS),
            "input_ids": ids + [agent.family.pad_token_id] * (TOKEN_COUNT - len(ids)),
            "attention_mask": [1] * len(ids) + [0] * (TOKEN_COUNT - len(ids)),
            "option_positions": list(question["opts"]),
            "decision_position": [question["decide"]],
            "reference_probabilities": list(reference["probabilities"]),
        }
        validate_case(case, agent.backbone.model.config.vocab_size)
        cases.append(case)
        print(f"Reference {state_id}: tokens={len(ids)}, probabilities={reference['probabilities']}", flush=True)
    return cases


def _torch_wrapper(agent):
    import torch

    class PointerModel(torch.nn.Module):
        def __init__(self):
            super().__init__()
            self.backbone = agent.backbone.model.half().eval()
            self.backbone.set_attn_implementation("eager")
            device = agent.backbone.device
            for name in ("q.weight", "q.bias", "k.weight", "k.bias"):
                self.register_buffer(name.replace(".", "_"), torch.tensor(agent.family.w[name], dtype=torch.float32, device=device))
            self.register_buffer("causal_allowed", torch.tril(torch.ones((TOKEN_COUNT, TOKEN_COUNT), dtype=torch.bool, device=device))[None, None])
            self.register_buffer("positions", torch.arange(TOKEN_COUNT, dtype=torch.long, device=device)[None])
            self.scale = math.sqrt(agent.family.dp) * agent.family.temperature

        def forward(self, input_ids, attention_mask, option_positions, decision_position):
            allowed = self.causal_allowed & attention_mask[:, None, None, :].bool()
            causal_mask = torch.where(allowed, 0.0, torch.finfo(torch.float16).min).half()
            # Supplying the prepared mask bypasses Transformers' non-exportable vmap mask construction.
            hidden = self.backbone(input_ids=input_ids, attention_mask={"full_attention": causal_mask},
                                   position_ids=self.positions, use_cache=False).last_hidden_state[0].float()
            query = hidden.index_select(0, decision_position.long()) @ self.q_weight.T + self.q_bias
            keys = hidden.index_select(0, option_positions.long()) @ self.k_weight.T + self.k_bias
            logits = (keys @ query.T).reshape(4) / self.scale
            return torch.softmax(logits, dim=0)

    return PointerModel().eval()


def _finish_real(output, model_path, cases, device, tolerance, wrapper_checks, preparation_mode):
    import onnx
    import onnxruntime
    import tinyjev
    import torch
    import transformers

    document = {
        "schema_version": 1,
        "model_id": MODEL_ID,
        "model_revision": MODEL_REVISION,
        "model_sha256": _sha256(model_path),
        "token_count": TOKEN_COUNT,
        "cases": cases,
    }
    print(f"Running ONNX Runtime CPU parity against upstream agent.logits for {len(cases)} states", flush=True)
    parity = _check_onnx(model_path, document, tolerance)
    _write_document(document, output / "cases.json")
    pairs = []
    for first in range(len(cases)):
        for second in range(first + 1, len(cases)):
            pairs.append({
                "case_ids": [cases[first]["case_id"], cases[second]["case_id"]],
                "reference_max_absolute_difference": max(abs(a - b) for a, b in zip(
                    cases[first]["reference_probabilities"], cases[second]["reference_probabilities"])),
                "onnx_max_absolute_difference": max(abs(a - b) for a, b in zip(
                    parity[first]["probabilities"], parity[second]["probabilities"])),
            })
    report = {
        "model_id": MODEL_ID,
        "model_revision": MODEL_REVISION,
        "model_sha256": document["model_sha256"],
        "preparation_mode": preparation_mode,
        "tinyjev_version": tinyjev.__version__,
        "torch_version": torch.__version__,
        "transformers_version": transformers.__version__,
        "onnx_version": onnx.__version__,
        "onnxruntime_version": onnxruntime.__version__,
        "reference_device": device,
        "export_backbone_dtype": "float16",
        "export_head_dtype": "float32",
        "absolute_and_relative_tolerance": tolerance,
        "torch_wrapper_checks": wrapper_checks,
        "onnx_cases": parity,
        "state_sensitivity_pairs": pairs,
    }
    (output / "parity.json").write_text(json.dumps(report, indent=2, allow_nan=False) + "\n", encoding="utf-8")
    print(json.dumps(report, indent=2), flush=True)
    return document


def prepare_cases(output, model_path, device=None, tolerance=0.005, states_path=None):
    import tinyjev
    import torch

    output, model_path = Path(output), Path(model_path)
    if not model_path.is_file():
        raise ValueError("An existing ONNX model is required for cases-only preparation")
    if not math.isfinite(tolerance) or tolerance <= 0:
        raise ValueError("tolerance must be finite and positive")
    load_states(states_path)
    output.mkdir(parents=True, exist_ok=True)
    checkpoint = _download_checkpoint(model_path.parent / "checkpoint")
    device = device or ("cuda" if torch.cuda.is_available() else "cpu")
    agent = tinyjev.load(str(checkpoint), backend="torch", device=device)
    if agent.family.name != "pointer":
        raise ValueError("The fixed checkpoint must use the pointer family")
    cases = _real_cases(agent, states_path)
    return _finish_real(output, model_path, cases, device, tolerance, [], "cases-only")


def export_real(output, device=None, tolerance=0.005, states_path=None):
    import numpy as np
    import onnx
    import tinyjev
    import torch

    output = Path(output)
    output.mkdir(parents=True, exist_ok=True)
    if not math.isfinite(tolerance) or tolerance <= 0:
        raise ValueError("tolerance must be finite and positive")
    load_states(states_path)
    checkpoint = _download_checkpoint(output / "checkpoint")
    device = device or ("cuda" if torch.cuda.is_available() else "cpu")
    print(f"Loading {MODEL_ID}@{MODEL_REVISION} on {device}", flush=True)
    agent = tinyjev.load(str(checkpoint), backend="torch", device=device)
    if agent.family.name != "pointer":
        raise ValueError("The fixed checkpoint must use the pointer family")
    cases = _real_cases(agent, states_path)
    wrapper = _torch_wrapper(agent)
    wrapper_checks = []
    for case in cases:
        tensors = tuple(torch.tensor(case[name], dtype=torch.int32, device=device).reshape(1, TOKEN_COUNT)
                        if name in ("input_ids", "attention_mask") else
                        torch.tensor(case[name], dtype=torch.int32, device=device)
                        for name in ("input_ids", "attention_mask", "option_positions", "decision_position"))
        with torch.inference_mode():
            actual = wrapper(*tensors).float().cpu().numpy()
        expected = np.asarray(case["reference_probabilities"], dtype=np.float32)
        np.testing.assert_allclose(actual, expected, atol=tolerance, rtol=tolerance)
        wrapper_checks.append({"case_id": case["case_id"], "max_absolute_error": float(np.abs(actual - expected).max())})
    model_path = output / "tinyjev.onnx"
    print("Exporting the actual FP16 backbone and FP32 pointer head to ONNX", flush=True)
    with torch.inference_mode():
        torch.onnx.export(wrapper, tensors, str(model_path), input_names=["input_ids", "attention_mask", "option_positions", "decision_position"],
                          output_names=["probabilities"], opset_version=17, dynamo=False,
                          do_constant_folding=True, external_data=False)
    onnx.checker.check_model(str(model_path))
    return _finish_real(output, model_path, cases, device, tolerance, wrapper_checks, "export")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("mode", choices=("smoke", "real", "cases"))
    parser.add_argument("--output", type=Path)
    parser.add_argument("--model", type=Path)
    parser.add_argument("--states", type=Path)
    parser.add_argument("--device", choices=("cpu", "cuda"))
    parser.add_argument("--tolerance", type=float, default=0.005)
    args = parser.parse_args()
    output = args.output or Path(__file__).parent / "Artifacts" / ("smoke" if args.mode == "smoke" else "tinyjev")
    if args.mode == "smoke":
        export_smoke(output)
    elif args.mode == "cases":
        prepare_cases(output, args.model or output / "tinyjev.onnx", args.device, args.tolerance, args.states)
    else:
        export_real(output, args.device, args.tolerance, args.states)


if __name__ == "__main__":
    main()
