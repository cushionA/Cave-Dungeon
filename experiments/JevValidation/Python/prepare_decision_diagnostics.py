"""Export prespecified diagnostics into each native model's input interface."""
import argparse
import copy
import hashlib
import json
from pathlib import Path

import decision_diagnostic_cases as corpus


def compact(value):
    return json.dumps(value, ensure_ascii=False, separators=(",", ":"))


def metadata(case):
    return {key: case[key] for key in ("case_id", "suite", "pair_id", "language", "category")}


def needle_suite(cases, language):
    rows = []
    system = None
    for case in cases:
        if case["language"] != language:
            continue
        evidence = corpus.model_input(case)
        tools = evidence.pop("tools")
        if tools != corpus.tool_catalog(language):
            raise ValueError("A command case changed the frozen tool catalog")
        instruction = evidence.pop("instruction")
        current_system = compact(evidence)
        if system is not None and current_system != system:
            raise ValueError("Cases within a language must share the same system facts")
        system = current_system
        rows.append({**metadata(case), "prompt": instruction,
                     "expected_calls": [copy.deepcopy(case["expected"])],
                     "safe_handling": case["safe_handling"]})
    return {"schema_version": 1, "system": system, "tool_schemas": corpus.tool_catalog(language), "cases": rows}


def decision_case(case):
    evidence = corpus.model_input(case)
    english = case["language"] == "en"
    if case["suite"] == "memory":
        question_id = "relevant"
        question = {
            "type": "noul",
            "instructions": ("Under the stated policy, does the quoted memory add useful information for the specific current decision?"
                             if english else "記載された方針に従い、引用された記憶は現在の具体的な判断に役立つ新しい情報を加えますか？"),
            "criteria": {
                "false": "It does not add useful information under the policy." if english else "方針上、役立つ新しい情報を加えない。",
                "true": "It adds useful information under the policy." if english else "方針上、役立つ新しい情報を加える。",
            },
        }
    elif case["suite"] == "commander":
        question_id = "plan"
        plans = evidence.pop("plans")
        question = {
            "type": "choice",
            "instructions": ("Select the complete plan satisfying every stated mission and unit-role constraint."
                             if english else "記載された任務と隊員の役割制約を全て満たす、完成した作戦を選んでください。"),
            "criteria": {key: compact(plan) for key, plan in plans.items()},
        }
    else:
        raise ValueError("This exporter handles memory and commander decisions")
    return {**metadata(case), "request": {"state": evidence, "questions": {question_id: question}},
            "expected": {question_id: copy.deepcopy(case["expected"])}}


def exports():
    commands, memory, commander = corpus.command_cases(), corpus.memory_cases(), corpus.commander_cases()
    for cases in (commands, memory, commander):
        corpus.validate_cases(cases)
    memory_rows = [decision_case(case) for case in memory]
    commander_rows = [decision_case(case) for case in commander]
    return {
        "needle-commands-en.json": needle_suite(commands, "en"),
        "needle-commands-ja.json": needle_suite(commands, "ja"),
        "memory.json": memory_rows,
        "commander.json": commander_rows,
        "decisions.json": memory_rows + commander_rows,
        "frozen-corpus.json": {"commands": commands, "memory": memory, "commander": commander},
    }


def prepare(output):
    output = Path(output)
    output.mkdir(parents=True, exist_ok=True)
    files = {}
    for name, payload in exports().items():
        path = output / name
        path.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n")
        files[name] = {"bytes": path.stat().st_size,
                       "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}
    manifest = {
        "schema_version": 1, "label_scope": corpus.LABEL_SCOPE,
        "gold_fixed_before_model_outputs": True,
        "bilingual_pairs_are_not_independent_samples": True,
        "corpus_sha256": {"commands": corpus.suite_sha256(corpus.command_cases()),
                          "memory": corpus.suite_sha256(corpus.memory_cases()),
                          "commander": corpus.suite_sha256(corpus.commander_cases())},
        "source_sha256": {path.name: hashlib.sha256(path.read_bytes()).hexdigest()
                          for path in (Path(corpus.__file__), Path(__file__))},
        "files": files,
    }
    (output / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
    return manifest


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=Path("Artifacts/decision-diagnostics"))
    args = parser.parse_args()
    print(json.dumps(prepare(args.output), indent=2))
