"""Durable Saif-native entrypoint using unchanged exploratory journal machinery."""
import argparse
import math
import platform
from pathlib import Path
import sys

import compare_saif_lightning as native
import run_additional_candidates as shared
from run_expanded_benchmark import apply_affinity


def run(args, adapter=None):
    if type(args.threads) is not int or args.threads < 1:
        raise ValueError("Thread count must be positive")
    if type(args.timeout_seconds) not in (int, float) or not math.isfinite(args.timeout_seconds) or args.timeout_seconds <= 0:
        raise ValueError("Timeout must be finite and positive")
    cases = shared.load_cases(args.cases)
    if args.protocol is None or not Path(args.protocol).is_file():
        raise ValueError("A frozen protocol file is required")
    affinity = apply_affinity(args.affinity)
    output = Path(args.output_dir)
    output.mkdir(parents=True, exist_ok=True)
    with shared.output_lock(output):
        checkpoint = native.verify_checkpoint(args.model_dir)
        sources = native.verify_sources()
        backend = adapter or native.initialize(args.model_dir, args.threads)
        try:
            identity = {"schema_version": shared.SCHEMA_VERSION, "adapter": "saif-lightning-native",
                "adapter_source": shared.file_descriptor(native.__file__), "adapter_description": backend.describe(),
                "runner_source": shared.file_descriptor(shared.__file__), "entrypoint_source": shared.file_descriptor(__file__),
                "affinity_helper_source": shared.file_descriptor(sys.modules[apply_affinity.__module__].__file__),
                "model_id": native.MODEL_ID, "model_revision": native.REVISION,
                "model_sha256": shared.digest(checkpoint), "model_verification": "Every checkpoint file independently SHA256/size verified against the pinned local model manifest before loading.",
                "checkpoint": checkpoint, "native_sources": sources,
                "source_manifest": shared.file_descriptor(native.ROOT / "source-manifest.json"),
                "sdk_manifest": shared.file_descriptor(native.ROOT / "sdk-manifest.json"),
                "server_url": "native://cpu", "cases_source": shared.file_descriptor(args.cases),
                "case_order": [case["case_id"] for case in cases], "total_corpus_cases": len(cases), "limit": None,
                "timeout_seconds": args.timeout_seconds, "threads": args.threads, "affinity": affinity,
                "requests_sha256": shared.digest([case["request"] for case in cases]),
                "gold_sha256": shared.digest([case["expected"] for case in cases]),
                "warmup_request_sha256": shared.digest(shared.WARMUP_REQUEST),
                "protocol": shared.file_descriptor(args.protocol),
                "python": platform.python_version(), "python_executable": str(Path(sys.executable).resolve()),
                "execution": "In-process native CPU engine loaded before the unchanged fixed one-call nonbenchmark warmup. One quality call at a time, all cases in order; errors remain incorrect observations.",
                "scope": "Exploratory reuse of the published synthetic corpus; no previous registered result or analysis is amended."}
            shared.encoded(identity)
            return shared._run_locked(args, backend, cases, identity, output)
        finally:
            backend.close()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-dir", type=Path, required=True)
    parser.add_argument("--cases", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--protocol", type=Path, required=True)
    parser.add_argument("--threads", type=int, default=4)
    parser.add_argument("--affinity", default="0,1,2,3")
    parser.add_argument("--timeout-seconds", type=float, default=300.0)
    parser.add_argument("--resume", action="store_true")
    args = parser.parse_args()
    result = run(args)
    print(shared.encoded({"status": result["status"], "completed_cases": result["completed_cases"],
                          "summary": result["summary"]["overall"]}).decode(), flush=True)


if __name__ == "__main__":
    main()
