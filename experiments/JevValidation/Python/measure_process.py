"""Sample a live Editor's process tree while a command runs (CPU RAM only)."""
import argparse
import datetime
import json
from pathlib import Path
import subprocess
import time

import psutil


def cgroup_memory():
    root = Path("/sys/fs/cgroup")
    result = {}
    try:
        result["current_bytes"] = int((root / "memory.current").read_text())
        limit = (root / "memory.max").read_text().strip()
        result["limit_bytes"] = None if limit == "max" else int(limit)
        result["events"] = {key: int(value) for key, value in
                            (line.split() for line in (root / "memory.events").read_text().splitlines())}
    except (OSError, ValueError):
        pass
    return result


def sample(process, started):
    rows = []
    try:
        processes = [process] + process.children(recursive=True)
    except psutil.Error:
        processes = []
    for item in processes:
        try:
            memory = item.memory_info()
            rows.append({"pid": item.pid, "name": item.name(), "rss": memory.rss})
        except psutil.Error:
            pass
    return {"elapsed_seconds": time.monotonic() - started,
            "editor_rss_bytes": next((row["rss"] for row in rows if row["pid"] == process.pid), 0),
            "tree_rss_bytes": sum(row["rss"] for row in rows), "processes": rows,
            "cgroup_current_bytes": cgroup_memory().get("current_bytes")}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--pid", type=int, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--interval", type=float, default=0.1)
    parser.add_argument("--timeout", type=float, default=1800)
    parser.add_argument("command", nargs=argparse.REMAINDER)
    args = parser.parse_args()
    if args.interval <= 0 or args.timeout <= 0:
        parser.error("interval and timeout must be positive")
    command = args.command[1:] if args.command[:1] == ["--"] else args.command
    if not command:
        parser.error("a command is required after --")
    process = psutil.Process(args.pid)
    initial_cgroup = cgroup_memory()
    started = time.monotonic()
    samples = [sample(process, started)]
    args.output.parent.mkdir(parents=True, exist_ok=True)
    timed_out = False
    with args.output.with_suffix(".stdout.log").open("w") as stdout, \
            args.output.with_suffix(".stderr.log").open("w") as stderr:
        with subprocess.Popen(command, stdout=stdout, stderr=stderr) as child:
            while child.poll() is None:
                samples.append(sample(process, started))
                if time.monotonic() - started > args.timeout:
                    timed_out = True
                    child.terminate()
                    try:
                        child.wait(timeout=5)
                    except subprocess.TimeoutExpired:
                        child.kill()
                    break
                time.sleep(args.interval)
            exit_code = child.wait()
    samples.append(sample(process, started))
    baseline = samples[0]["tree_rss_bytes"]
    peak = max(row["tree_rss_bytes"] for row in samples)
    report = {"created_utc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
              "editor_pid": args.pid, "command": command,
              "scope": "Sampled sum of current RSS of Editor and recursive child processes; shared pages may be counted more than once. CPU RAM only; not VRAM or an allocation-size measurement.",
              "interval_seconds": args.interval, "elapsed_seconds": time.monotonic() - started,
              "baseline_tree_rss_bytes": baseline, "peak_sampled_tree_rss_bytes": peak,
              "peak_above_baseline_bytes": peak - baseline,
              "baseline_editor_rss_bytes": samples[0]["editor_rss_bytes"],
              "peak_sampled_editor_rss_bytes": max(row["editor_rss_bytes"] for row in samples),
              "peak_sampled_cgroup_current_bytes": max((row["cgroup_current_bytes"] or 0) for row in samples),
              "cgroup_initial": initial_cgroup, "cgroup_final": cgroup_memory(),
              "command_exit_code": exit_code, "timed_out": timed_out, "samples": samples}
    args.output.write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps({key: value for key, value in report.items() if key != "samples"}, indent=2))
    raise SystemExit(exit_code if not timed_out else 124)


if __name__ == "__main__":
    main()
