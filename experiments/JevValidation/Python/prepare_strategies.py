import argparse
import copy
import hashlib
import json
from pathlib import Path
import tempfile

from prepare import MODEL_ID, MODEL_REVISION, _sha256, validate_case
from prepare_gguf import (QUANTIZATIONS, _tool_identity, _validate_artifact, ensure_quantizer,
                          ensure_source, export_head, validate_checkpoint)

TOKEN_COUNT = 512
STRATEGIES = {
    "direct_assault": "Maintain a direct offensive plan: close distance and engage the threat head-on until resolved.",
    "lure_ambush": "Use a deceptive terrain plan spanning baiting, drawing pursuit through cover, staging an ambush, and finishing the engagement.",
    "flank": "Use a repositioning plan around the enemy's front to gain a favorable angle before committing.",
    "guard_ally": "Maintain an ally-defense plan that stays between the ally and danger and intercepts threats.",
    "ranged_pressure": "Maintain distance and sustained ranged pressure while managing ammunition.",
    "observe": "Use a reconnaissance plan that monitors enemy behavior and terrain before committing to engagement.",
    "disengage": "Use an extraction plan to break contact and create a safe route away from the encounter.",
    "bypass": "Use an avoidance plan to pass around the threat and continue toward the destination.",
}
PERSONALITIES = {
    "cunning": "Patient, deceptive, exploits terrain.",
    "aggressive": "Bold, direct, risk-tolerant.",
    "cautious": "Deliberate, risk-averse, values safety.",
}
GOALS = {
    "protect_ally": "Keep the ally alive during this encounter.",
    "reach_exit_alive": "Reach the exit alive.",
}
BASE_WORLD = {
    "Player health": "60/100", "Enemy health": "40/100", "Ally health": "45/100",
    "Enemy distance from player": "6 meters", "Enemy distance from ally": "4 meters",
    "Melee range": "2 meters", "Player ammunition": 12, "Effective ranged distance": "8 meters",
    "Enemy target": "ally", "Enemy following player": "no", "Enemy preparing a strike": "no",
    "Chokepoint with cover": "available", "Exit distance": "12 meters",
}


def catalog_fingerprint(catalog):
    return hashlib.sha256(json.dumps(catalog, sort_keys=True, ensure_ascii=False, separators=(",", ":")).encode("utf-8")).hexdigest()


def catalog_metadata():
    catalog = dict(STRATEGIES)
    return {"strategy_catalog": catalog, "strategy_catalog_sha256": catalog_fingerprint(catalog)}


def strategy_cases():
    cases = []
    for personality_id, personality in PERSONALITIES.items():
        for goal_id, goal in GOALS.items():
            cases.append({"case_id": f"strategy_{personality_id}_{goal_id}",
                          "personality_id": personality_id, "personality": personality,
                          "goal_id": goal_id, "goal": goal, "world_id": "opening",
                          "world": copy.deepcopy(BASE_WORLD), "current_strategy": "lure_ambush"})
    changes = {
        "pursuit": {"Enemy target": "player", "Enemy following player": "yes", "Enemy distance from player": "4 meters"},
        "ally_imminent": {"Ally health": "10/100", "Enemy distance from ally": "0.8 meters", "Enemy preparing a strike": "yes"},
        "enemy_weakened": {"Enemy health": "8/100", "Enemy distance from player": "1.5 meters"},
    }
    for world_id, change in changes.items():
        case = copy.deepcopy(cases[0])
        case.update(case_id=f"strategy_cunning_protect_ally_{world_id}", world_id=world_id)
        case["world"].update(change)
        cases.append(case)
    for current in ("direct_assault", "observe", "none"):
        case = copy.deepcopy(cases[0])
        case.update(case_id=f"strategy_cunning_protect_ally_current_{current}", current_strategy=current)
        cases.append(case)
    rotated = copy.deepcopy(cases[0])
    rotated.update(case_id="strategy_cunning_protect_ally_rotated4",
                   candidate_order=list(STRATEGIES)[4:] + list(STRATEGIES)[:4])
    cases.append(rotated)
    for original in cases[1:6]:
        case = copy.deepcopy(original)
        case.update(case_id=original["case_id"] + "_current_none", current_strategy="none")
        cases.append(case)
    return cases


def _state(specification):
    facts = " ".join(f"{key}: {value}." for key, value in specification["world"].items())
    return (f"Personality: {specification['personality']}\nGoal: {specification['goal']}\n"
            f"World facts: {facts}\nCurrent strategy: {specification['current_strategy']}.")


def _request(specification):
    return {"id": specification["case_id"], "state": _state(specification), "questions": {"strategy": {
        "type": "choice", "instructions": (
            "Choose the strategy to continue or switch to for this personality, goal, and situation. "
            "Each strategy is a multi-step behavior preset; its internal phases proceed without selecting a different strategy."),
        "criteria": {key: STRATEGIES[key] for key in specification.get("candidate_order", STRATEGIES)}}}}


def build_reference_cases(agent, specifications):
    import tinyjev

    cases = []
    for specification in specifications:
        case_id = specification["case_id"]
        payload = _request(specification)
        state = payload["state"]
        choices = list(payload["questions"]["strategy"]["criteria"])
        encoded = agent.family.encode(tinyjev.normalize_request(payload)[0])
        if len(encoded.questions) != 1 or len(encoded.rows) != 1:
            raise ValueError("A strategy case requires exactly one question and one causal row")
        question = encoded.questions[0]
        if question["keys"] != choices or len(question["opts"]) != 8:
            raise ValueError("The encoded strategy order must match the eight-choice contract")
        ids = list(encoded.prefix) + list(encoded.rows[0])
        if len(ids) > TOKEN_COUNT:
            raise ValueError(f"{case_id}: {len(ids)} tokens exceeds the 512-token strategy contract")
        reference = agent.logits(payload)[case_id + ":strategy"]
        if reference["candidate_ids"] != choices:
            raise ValueError("The upstream strategy order differs from the prepared candidates")
        case = {"case_id": case_id, "state_id": case_id, "state": state, "choice_ids": choices,
                "input_ids": ids + [agent.family.pad_token_id] * (TOKEN_COUNT - len(ids)),
                "attention_mask": [1] * len(ids) + [0] * (TOKEN_COUNT - len(ids)),
                "option_positions": list(question["opts"]), "decision_position": [question["decide"]],
                "reference_probabilities": list(reference["probabilities"])}
        validate_case(case, agent.backbone.model.config.vocab_size, token_count=TOKEN_COUNT, choice_count=8)
        cases.append(case)
        print(f"Reference {case_id}: tokens={len(ids)}, probabilities={reference['probabilities']}", flush=True)
    return cases


def validate_variant(directory, quantization, expected, head_sha256):
    directory = Path(directory)
    model = directory / f"tinyjev-{quantization}.gguf"
    record = _validate_artifact(model, expected)
    if _sha256(directory / "tinyjev.head") != head_sha256:
        raise ValueError("The prepared head does not match the fixed trained TinyJev source")
    return {"directory": directory, "model": model, "model_sha256": record["artifact_sha256"],
            "head_sha256": head_sha256}


def _trusted_models(checkpoint, gguf_root):
    source = ensure_source(gguf_root / "tools")
    source_sha256 = validate_checkpoint(checkpoint)
    f16_expected = {"model_id": MODEL_ID, "model_revision": MODEL_REVISION, "quantization": "F16",
                    "source_sha256": source_sha256, "tools": _tool_identity(source), "parent_f16_sha256": None,
                    "settings": {"source_directory_name": checkpoint.name, "use_temp_file": True}}
    with tempfile.TemporaryDirectory() as temporary:
        head_sha = export_head(checkpoint, Path(temporary) / "tinyjev.head")["head_sha256"]
    variants = {"F16": validate_variant(gguf_root / "F16", "F16", f16_expected, head_sha)}
    quantizer_tools = _tool_identity(source, ensure_quantizer(gguf_root / "tools"))
    for quantization in QUANTIZATIONS[1:]:
        directory = gguf_root / quantization
        manifest = json.loads((directory / f"tinyjev-{quantization}.gguf.provenance.json").read_text(encoding="utf-8"))
        settings = manifest["provenance"]["settings"]
        if not isinstance(settings, dict) or set(settings) != {"threads"} or type(settings["threads"]) is not int or not 1 <= settings["threads"] <= 64:
            raise ValueError("The trusted quantizer settings must record a supported thread count")
        expected = {"model_id": MODEL_ID, "model_revision": MODEL_REVISION, "quantization": quantization,
                    "source_sha256": source_sha256, "tools": quantizer_tools,
                    "parent_f16_sha256": variants["F16"]["model_sha256"], "settings": settings}
        variants[quantization] = validate_variant(directory, quantization, expected, head_sha)
    return variants, source_sha256


def write_documents(cases, variants, reference_source):
    for case in cases:
        validate_case(case, 151936, token_count=TOKEN_COUNT, choice_count=8)
        if set(case["choice_ids"]) != set(STRATEGIES):
            raise ValueError("Prepared cases must preserve strategy candidate identity and order")
    for variant in variants.values():
        if _sha256(variant["model"]) != variant["model_sha256"] or _sha256(variant["directory"] / "tinyjev.head") != variant["head_sha256"]:
            raise ValueError("The model or head changed after trusted preparation was validated")
    documents = {}
    for quantization, variant in variants.items():
        document = {"schema_version": 1, "model_id": MODEL_ID, "model_revision": MODEL_REVISION,
                    "model_format": "gguf", "model_sha256": variant["model_sha256"],
                    "head_file": "tinyjev.head", "head_sha256": variant["head_sha256"],
                    "backbone_quantization": quantization, "token_count": TOKEN_COUNT,
                    "reference_source": reference_source, "cases": copy.deepcopy(cases)}
        output = variant["directory"] / "cases-strategies.json"
        temporary = output.with_suffix(".json.partial")
        temporary.write_text(json.dumps(document, indent=2, allow_nan=False) + "\n", encoding="utf-8")
        temporary.replace(output)
        documents[quantization] = document
    return documents


def prepare(checkpoint, gguf_root, output, device=None):
    import tinyjev
    import torch

    checkpoint, gguf_root, output = map(lambda value: Path(value).resolve(), (checkpoint, gguf_root, output))
    legacy_paths = [checkpoint.parent / name for name in ("cases.json", "tinyjev.onnx")]
    legacy_paths += [gguf_root / quant / "cases.json" for quant in QUANTIZATIONS]
    legacy_before = {str(path): _sha256(path) for path in legacy_paths}
    variants, source_sha256 = _trusted_models(checkpoint, gguf_root)
    device = device or ("cuda" if torch.cuda.is_available() else "cpu")
    agent = tinyjev.load(str(checkpoint), backend="torch", device=device)
    if agent.family.name != "pointer":
        raise ValueError("The fixed strategy checkpoint must use the trained pointer head")
    specifications = strategy_cases()
    cases = build_reference_cases(agent, specifications)
    reference_source = {"backend": "tinyjev.torch", "tinyjev_version": tinyjev.__version__,
                        "torch_version": torch.__version__, "device": device,
                        "model_revision": MODEL_REVISION, "source_sha256": source_sha256, **catalog_metadata()}
    documents = write_documents(cases, variants, reference_source)
    legacy_after = {str(path): _sha256(path) for path in legacy_paths}
    if legacy_before != legacy_after or validate_checkpoint(checkpoint) != source_sha256:
        raise ValueError("The original checkpoint, ONNX, or six-case suite changed during strategy preparation")
    results = []
    for specification, case in zip(specifications, cases):
        probabilities = dict(zip(case["choice_ids"], case["reference_probabilities"]))
        selected = max(probabilities, key=probabilities.__getitem__)
        results.append({**specification, "active_tokens": sum(case["attention_mask"]),
                        "selected_strategy": selected, "kept_current_strategy": selected == specification["current_strategy"],
                        "probabilities": probabilities})
    report = {"model_id": MODEL_ID, "model_revision": MODEL_REVISION, "token_count": TOKEN_COUNT,
              "reference_source": reference_source, "results": results, "legacy_sha256": legacy_after,
              "prepared_cases": {quant: {"model_sha256": doc["model_sha256"], "head_sha256": doc["head_sha256"],
                                         "cases_file": str(variants[quant]["directory"] / "cases-strategies.json")}
                                 for quant, doc in documents.items()},
              "status": "Strategy selection only; no game preset or ML parameter integration"}
    output.mkdir(parents=True, exist_ok=True)
    (output / "reference.json").write_text(json.dumps({"token_count": TOKEN_COUNT, "cases": cases,
                                                       "reference_source": reference_source}, indent=2) + "\n", encoding="utf-8")
    (output / "strategy-validation.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, indent=2), flush=True)
    return report


def main():
    parser = argparse.ArgumentParser()
    base = Path(__file__).parent / "Artifacts"
    parser.add_argument("--checkpoint", type=Path, default=base / "tinyjev/checkpoint")
    parser.add_argument("--gguf-root", type=Path, default=base / "gguf")
    parser.add_argument("--output", type=Path, default=base / "strategies")
    parser.add_argument("--device", choices=("cpu", "cuda"))
    args = parser.parse_args()
    prepare(args.checkpoint, args.gguf_root, args.output, args.device)


if __name__ == "__main__":
    main()
