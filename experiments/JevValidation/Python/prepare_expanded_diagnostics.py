"""Freeze the larger, prespecified synthetic benchmark before model inference."""
import argparse
import copy
import datetime
import hashlib
import json
from pathlib import Path
import random
from collections import Counter

import expanded_memory_cases as memory
import expanded_commander_cases as commander
import expanded_command_cases as commands
from prepare_decision_diagnostics import needle_suite


ORDER_SEED = 2026110627
ANALYSIS_SEED = 20261106


def encode(value):
    return (json.dumps(value, ensure_ascii=False, indent=2, allow_nan=False) + "\n").encode()


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def canonical_case(original):
    row = copy.deepcopy(original)
    for canonical, alias in (("world_id", "scenario_id"), ("family_id", "family")):
        value = row.get(canonical, row.get(alias))
        if not isinstance(value, str) or not value or (alias in row and row[alias] != value):
            raise ValueError("Conflicting or missing semantic-world metadata")
        row[canonical] = value
    if "request" in row and set(row["request"]) != {"state", "questions"}:
        raise ValueError("Only native state/questions may reach the decision models")
    if row.get("suite") == "commands":
        row["expected_calls"] = [copy.deepcopy(row["expected"])]
    return row


def build_payloads():
    decisions = [canonical_case(row) for row in (
        memory.generate_cases(worlds_per_family=20, english_per_family=4)
        + commander.generate_cases(worlds_per_family=20, english_per_family=4))]
    command_rows = [canonical_case(row) for row in commands.generate_cases()]
    random.Random(ORDER_SEED).shuffle(decisions)
    random.Random(ORDER_SEED ^ 0x63).shuffle(command_rows)
    if len({row["case_id"] for row in decisions + command_rows}) != 1328:
        raise ValueError("Unexpected case count or duplicate identity")
    for suite in ("memory", "commander"):
        for language, expected in (("ja", 500), ("en", 100)):
            selected = [row for row in decisions if row["suite"] == suite and row["language"] == language]
            if len(selected) != expected or len({row["world_id"] for row in selected}) != expected:
                raise ValueError("Decision task sample size differs from the fixed design")
        japanese = [row for row in decisions if row["suite"] == suite and row["language"] == "ja"]
        if Counter(row["family_id"] for row in japanese) != Counter({family: 20 for family in {row["family_id"] for row in japanese}}) or len({row["family_id"] for row in japanese}) != 25:
            raise ValueError("Decision domain families must each contain twenty primary worlds")
    payloads = {"decisions.json": decisions, "commands.json": command_rows}
    for language, expected_count in (("ja", 96), ("en", 32)):
        selected = [row for row in command_rows if row["language"] == language]
        if len(selected) != expected_count:
            raise ValueError("Command sample size differs from the fixed design")
        suite = needle_suite(command_rows, language)
        by_id = {row["case_id"]: row for row in selected}
        for row in suite["cases"]:
            source = by_id[row["case_id"]]
            row.update({key: source[key] for key in ("world_id", "family_id", "scenario_id", "family")})
        payloads[f"needle-{language}.json"] = suite
    return payloads


def prepare(output, protocol_path):
    output, protocol_path = Path(output), Path(protocol_path)
    if protocol_path.exists() or (output.exists() and any(output.iterdir())):
        raise FileExistsError("A frozen protocol/dataset must not be overwritten")
    payloads = build_payloads()
    output.mkdir(parents=True, exist_ok=True)
    files = {}
    for name, value in payloads.items():
        path = output / name
        path.write_bytes(encode(value))
        files[name] = {"path": str(path.resolve()), "bytes": path.stat().st_size, "sha256": sha(path)}
    names = ["prepare_expanded_diagnostics.py", "expanded_memory_cases.py", "expanded_commander_cases.py",
             "expanded_command_cases.py", "run_expanded_benchmark.py", "analyze_expanded_decisions.py",
             "compare_hindsight.py", "compare_tinyjev_diagnostics.py", "compare_needle3.py",
             "compare_intern_decision.py", "prepare_decision_diagnostics.py", "decision_diagnostic_cases.py"]
    source = Path(__file__).parent
    protocol = {
        "schema_version": 1, "created_utc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        "client_timezone": "Asia/Tokyo", "frozen_before_expanded_model_inference": True,
        "scope": "Confirmatory model comparison conditional on explicitly specified synthetic world generators; not combat strength or unrestricted natural-language accuracy.",
        "primary": {"language": "ja", "tasks": ["memory", "commander"], "worlds_per_task": 500,
                    "one_binary_correctness_outcome_per_world": True, "domain_families_per_task": 25,
                    "worlds_per_domain_family": 20, "higher_level_mechanisms_are_not_independent_domain_families": True,
                    "effect": "Hindsight exact-correct rate minus TinyJev exact-correct rate",
                    "test": "Two-sided exact paired McNemar, Holm correction across two primary tasks",
                    "familywise_alpha": 0.05, "intervals": ["95% Wilson individual rates", "Conservative 95% paired Clopper-Pearson rectangle projection", "Prespecified paired world-stratified bootstrap"],
                    "planning": {"meaningful_absolute_difference": 0.10, "discordance": 0.50,
                                 "worst_holm_threshold": 0.025, "exact_per_task_power": 0.8092294946933138,
                                 "power_is_conditional_not_probability_both_tasks_reject": True}},
        "secondary": {"english_translations_per_task": 100, "translations_never_add_primary_worlds": True,
                      "family_and_mechanism_block_bootstrap_are_robustness_diagnostics": True,
                      "measures": ["FPR/FNR", "domain/mechanism performance", "leave-group-out sensitivity", "commander hard-constraint violation", "ordinal rank loss among supplied candidate plans"],
                      "no_additional_confirmatory_p_values": True},
        "needle": {"japanese_worlds": 96, "japanese_executable": 72, "japanese_clarify": 24,
                   "english_translations": 32, "english_executable": 24, "english_clarify": 8,
                   "primary_descriptive_metrics": ["exact correct executable coverage", "issued action coverage", "conditional correctness when issued", "unsafe actions on clarify requests"],
                   "intervals": ["95% Wilson", "Exact one-sided 95% upper bounds"],
                   "suppressed_calls_never_accepted": True, "native_gates_unchanged": True,
                   "max_new_tokens": 128, "stateless": True, "auto_date": False,
                   "selection_of_72_executable_worlds_fixed_before_outputs": True},
        "execution": {"quality_runs_may_run_in_parallel": True, "quality_wall_times_not_latency_benchmark": True,
                      "initial_cpu_allocation": {"hindsight": {"threads": 1, "cpus": [0]},
                                                 "tinyjev": {"threads": 2, "cpus": [1, 2]},
                                                 "needle_ja": {"cpus": [3], "native_thread_count_not_exposed": True}},
                      "needle_affinity_may_expand_to_cpus_of_completed_peer_jobs": True,
                      "external_cpu_affinity_changes_are_logged_and_never_quality_based": True,
                      "resource_changes_do_not_change_inputs_models_decoding_or_sample_counts": True,
                      "one_separate_warmup_per_process": True, "one_scored_pass_per_case": True,
                      "all_returned_runtime_failures_count_incorrect": True,
                      "missing_duplicate_or_foreign_records_rejected": True,
                      "resume_only_same_dataset_model_source_and_protocol": True,
                      "no_early_stopping_or_result_based_sample_changes": True,
                      "no_prompt_threshold_temperature_or_checkpoint_tuning": True},
        "models": {"hindsight": {"model_id": "hindsight-ai/hs-decide-1-flash", "revision": "b9a782058dbc81ad59d9397bf2a9fed946778cb3", "dtype": "bfloat16", "backend": "Official prompt and alias rules via Torch CPU adapter; vLLM numeric parity not measured"},
                   "tinyjev": {"model_id": "AnkitAI/TinyJev-0.6B", "revision": "c559c2f7ea95069f92af8b999f512345bbb87d22", "dtype": "float32", "native_head_temperature": 1.4640856959456252},
                   "needle": {"model_id": "Cactus-Compute/needle3", "revision": "c95d25b4efa1e9601e785d92438e4ade0dbddbf8", "sdk": "4.0.1", "native_runtime": "4.0.0"}},
        "seeds": {"memory": memory.DEFAULT_SEED, "commander": commander.DEFAULT_SEED,
                  "commands": commands.DEFAULT_SEED, "case_order": ORDER_SEED,
                  "bootstrap": ANALYSIS_SEED},
        "bootstrap_repetitions": 10000, "files": files,
        "source_files": {name: sha(source / name) for name in names},
        "limitations": ["Repeated mathematical operators are tagged as shared mechanisms, not claimed as independent reasoning types.",
                        "Generator-specific significance does not establish performance in real gameplay or unseen task families.",
                        "Formal oracles validate stated constraints and answer sets; strategic taste and future combat outcomes are outside this target population."]}
    protocol_path.parent.mkdir(parents=True, exist_ok=True)
    protocol_path.write_bytes(encode(protocol))
    return protocol


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--protocol", type=Path, required=True)
    args = parser.parse_args()
    result = prepare(args.output, args.protocol)
    print(json.dumps({"protocol": str(args.protocol), "files": result["files"]}, indent=2))
