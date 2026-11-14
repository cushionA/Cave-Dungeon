"""Verify full frozen runs and export inspectable paired outcomes and provenance."""
import argparse
import csv
import datetime
import hashlib
import json
import math
from pathlib import Path

from run_expanded_benchmark import read_journal, strict_json


def read(path):
    return strict_json(Path(path).read_text())


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def descriptor(path):
    path = Path(path)
    return {"path": str(path.resolve()), "bytes": path.stat().st_size, "sha256": sha(path)}


def collect(experiment_root, stats_path, log_root):
    from scipy import __version__ as scipy_version
    from scipy.stats import binomtest

    root, log_root = Path(experiment_root), Path(log_root)
    artifacts = root / "Python/Artifacts/expanded-diagnostics"
    protocol_path = root / "Results/expanded-benchmark-protocol.json"
    protocol = read(protocol_path)
    for name, item in protocol["files"].items():
        if sha(artifacts / name) != item["sha256"]:
            raise ValueError("A frozen dataset changed: " + name)
    for name, digest in protocol["source_files"].items():
        if sha(root / "Python" / name) != digest:
            raise ValueError("A registered source changed: " + name)
    amendment_path = root / "Results/expanded-analysis-amendment.json"
    amendment = read(amendment_path)
    if amendment["protocol_sha256"] != sha(protocol_path) or sha(root / "Python/analyze_expanded_decisions_repaired.py") != amendment["repaired_source"]["sha256"]:
        raise ValueError("The prespecified-method implementation repair changed")
    cases = read(artifacts / "decisions.json")
    reports, records = {}, {}
    for backend in ("hindsight", "tinyjev"):
        folder = artifacts / backend
        report, manifest = read(folder / "result.json"), read(folder / "manifest.json")
        identity = manifest["identity"]
        journal = read_journal(folder / "results.ndjson", cases)
        if (report["status"] != "complete" or not report["execution_completed"]
                or len(journal) != 1200 or report["results"] != journal
                or report["identity"] != identity or identity["backend"] != backend
                or identity["protocol_sha256"] != sha(protocol_path)
                or identity["cases_file_sha256"] != sha(artifacts / "decisions.json")
                or report["manifest_sha256"] != sha(folder / "manifest.json")
                or report["journal_sha256"] != sha(folder / "results.ndjson")):
            raise ValueError("Incomplete or inconsistent native run: " + backend)
        requests = [strict_json(line) for line in (folder / "requests.ndjson").read_text().splitlines()]
        if len(requests) != len(cases) or sha(folder / "requests.ndjson") != manifest["requests_file_sha256"]:
            raise ValueError("Saved model requests changed")
        for request, case in zip(requests, cases):
            allowed = {"model", "state", "questions"} if backend == "hindsight" else {"id", "state", "questions"}
            if request["case_id"] != case["case_id"] or set(request["request"]) != allowed or {key: request["request"][key] for key in ("state", "questions")} != case["request"]:
                raise ValueError("Models did not receive the frozen evidence")
        reports[backend], records[backend] = report, {row["case_id"]: row for row in journal}
    stats = read(stats_path)
    if stats["first_model"] != "hindsight" or stats["second_model"] != "tinyjev":
        raise ValueError("Analysis model labels differ")
    independent = {"scipy_version": scipy_version, "tasks": {}}
    p_values = []
    for task in ("memory", "commander"):
        selected = [case for case in cases if case["suite"] == task and case["language"] == "ja"]
        correct = {name: [] for name in records}
        for case in selected:
            for name in records:
                row = records[name][case["case_id"]]
                matched = row["status"] == "complete" and all(
                    type(row["predictions"].get(q)) is type(gold) and row["predictions"][q] == gold
                    for q, gold in case["expected"].items())
                correct[name].append(matched)
        wins = sum(a and not b for a, b in zip(correct["hindsight"], correct["tinyjev"]))
        losses = sum(b and not a for a, b in zip(correct["hindsight"], correct["tinyjev"]))
        p = binomtest(wins, wins + losses, .5).pvalue if wins + losses else 1.0
        p_values.append(p)
        group = stats["tasks"][task]["primary_ja"]
        if group["worlds"] != 500 or group["discordant_first_only"] != wins or group["discordant_second_only"] != losses or not math.isclose(p, group["exact_mcnemar_two_sided_p"], rel_tol=1e-10, abs_tol=1e-300):
            raise ValueError("Independent McNemar/count check disagrees")
        q = binomtest(wins+losses, 500).proportion_ci(confidence_level=.975, method="exact")
        theta = binomtest(wins, wins+losses).proportion_ci(confidence_level=.975, method="exact") if wins+losses else None
        bounds = [(theta.low, theta.high)] if theta is not None else [(0.0, 1.0)]
        corners = [value*(2*angle-1) for value in (q.low, q.high) for angle in bounds[0]]
        if any(abs(a-b) > 1e-11 for a, b in zip((min(corners), max(corners)), group["conservative_paired_difference_95"])):
            raise ValueError("Independent paired-interval check disagrees")
        for model, key in (("hindsight", "first"), ("tinyjev", "second")):
            count = sum(correct[model])
            ci = binomtest(count, 500).proportion_ci(method="wilson")
            if count != group[key]["correct"] or any(abs(a-b) > 1e-12 for a, b in zip((ci.low, ci.high), group[key]["wilson_95"])):
                raise ValueError("Independent Wilson/count check disagrees")
        independent["tasks"][task] = {"worlds": 500, "hindsight_correct": sum(correct["hindsight"]),
                                      "tinyjev_correct": sum(correct["tinyjev"]),
                                      "discordant_first_only": wins, "discordant_second_only": losses,
                                      "scipy_exact_mcnemar_p": p}
    adjusted = [0.0, 0.0]
    previous = 0.0
    for rank, index in enumerate(sorted(range(2), key=p_values.__getitem__)):
        previous = max(previous, min(1.0, (2-rank)*p_values[index]))
        adjusted[index] = previous
    for task, p in zip(("memory", "commander"), adjusted):
        if not math.isclose(p, stats["tasks"][task]["primary_ja"]["holm_adjusted_p_two_primary_tasks"], rel_tol=1e-10, abs_tol=1e-300):
            raise ValueError("Independent Holm adjustment disagrees")
    output = root / "Results"
    csv_path = output / "expanded-paired-outcomes.csv"
    fields = ["case_id", "world_id", "suite", "family_id", "mechanism_group", "language", "primary", "gold",
              "hindsight_status", "hindsight_prediction", "hindsight_correct", "tinyjev_status", "tinyjev_prediction", "tinyjev_correct"]
    with csv_path.open("w", newline="") as target:
        writer = csv.DictWriter(target, fieldnames=fields)
        writer.writeheader()
        for case in cases:
            row = {key: case.get(key, "") for key in ("case_id", "world_id", "suite", "family_id", "mechanism_group", "language")}
            row.update(primary=case["language"] == "ja", gold=json.dumps(case["expected"], ensure_ascii=False, separators=(",", ":")))
            for name in records:
                item = records[name][case["case_id"]]
                row[name+"_status"] = item["status"]
                row[name+"_prediction"] = json.dumps(item["predictions"], ensure_ascii=False, separators=(",", ":"))
                row[name+"_correct"] = item["status"] == "complete" and item["predictions"] == case["expected"]
            writer.writerow(row)
    command_cases = {case["case_id"]: case for case in read(artifacts / "commands.json")}
    needle_reports = {}
    needle_rows = []
    if "needle" not in stats:
        raise ValueError("The complete analysis must include the Needle outcomes")
    for language, size in (("ja", 96), ("en", 32)):
        path = artifacts / f"needle-{language}/result.json"
        report = read(path)
        suite = read(artifacts / f"needle-{language}.json")
        if (report["status"] != "complete" or len(report["results"]) != size
                or report["suite_sha256"] != sha(artifacts / f"needle-{language}.json")
                or report["system_text"] != suite["system"]
                or report["adapter_sha256"] != protocol["source_files"]["compare_needle3.py"]):
            raise ValueError("Incomplete or inconsistent Needle run")
        for item in report["results"]:
            case = command_cases[item["case_id"]]
            if case["language"] != language or item["expected_calls"] != case["expected_calls"]:
                raise ValueError("Needle case/gold mapping differs")
            needle_rows.append({"case_id": item["case_id"], "world_id": case["world_id"], "family_id": case["family_id"],
                                "language": language, "expected": json.dumps(case["expected_calls"], ensure_ascii=False),
                                "accepted": json.dumps(item["accepted_calls"], ensure_ascii=False),
                                **{key: item[key] for key in ("response_valid", "exact_match", "abstained", "expected_clarification", "issued_game_action")}})
        needle_reports[language] = {key: value for key, value in report.items() if key not in ("results", "memory", "tool_schemas", "system_text", "warmup")}
    if len({row["case_id"] for row in needle_rows}) != 128:
        raise ValueError("Duplicate Needle outcomes")
    needle_csv = output / "expanded-needle-outcomes.csv"
    with needle_csv.open("w", newline="") as target:
        writer = csv.DictWriter(target, fieldnames=list(needle_rows[0]))
        writer.writeheader()
        writer.writerows(needle_rows)
    evidence = [protocol_path, amendment_path, csv_path, needle_csv, *log_root.glob("*.log"), *log_root.glob("*.ndjson"),
                *log_root.glob("*.py"), *log_root.glob("*-requirements.txt")]
    evidence.extend(log_root/name for name in ("primary-independent-validation.json", "controller-status.json", "controller-audit.json")
                    if (log_root/name).is_file())
    for backend in ("hindsight", "tinyjev"):
        evidence.extend(artifacts / backend / name for name in ("manifest.json", "result.json", "results.ndjson", "requests.ndjson"))
    evidence.extend(artifacts / f"needle-{language}/result.json" for language in ("ja", "en"))
    stats["verification"] = {"independent_statistical_check": independent,
                             "frozen_protocol": descriptor(protocol_path), "analysis_repair": descriptor(amendment_path),
                             "paired_outcomes": descriptor(csv_path), "needle_outcomes": descriptor(needle_csv),
                             "all_planned_native_requests_and_full_journals_verified": True}
    stats["verification"]["analysis_sources"] = [descriptor(root/"Python"/name) for name in (
        "analyze_expanded_decisions_repaired.py", "collect_expanded_results.py", "plot_expanded_results.py")]
    test_logs = [(log_root/"expanded-preflight-tests.log", 48), (log_root/"expanded-repair-tests.log", 6)]
    for path, count in test_logs:
        text = path.read_text()
        if f"Ran {count} tests" not in text or not text.rstrip().endswith("OK"):
            raise ValueError("A required automated test log is incomplete")
    stats["verification"]["automated_tests"] = {"unique_passed": 54, "failed": 0,
                                                "logs": [descriptor(path) for path, _ in test_logs]}
    stats["run_summaries"] = {name: {"status": report["status"], "completed": report["completed_case_count"],
                                     "failures": report["failed_case_count"], "identity": report["identity"],
                                     "sessions": report["sessions"]} for name, report in reports.items()}
    stats["run_summaries"]["needle"] = needle_reports
    stats["evidence"] = [descriptor(path) for path in sorted(set(evidence))]
    stats["collected_utc"] = datetime.datetime.now(datetime.timezone.utc).isoformat()
    path = output / "expanded-model-statistics.json"
    path.write_text(json.dumps(stats, ensure_ascii=False, indent=2, allow_nan=False)+"\n")
    return path


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--experiment-root", type=Path, default=Path(__file__).resolve().parent.parent)
    parser.add_argument("--stats", type=Path, required=True)
    parser.add_argument("--logs", type=Path, required=True)
    args = parser.parse_args()
    print(collect(args.experiment_root, args.stats, args.logs))
