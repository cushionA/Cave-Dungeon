"""Pinned native Saif Lightning adapter; one official Engine call per request.

Importing the official engine installs process-global attention patches. Use a
dedicated sequential process, never an HTTP descriptor or a second model here.
"""
import copy
import hashlib
import importlib
import importlib.metadata
import json
import math
from pathlib import Path
import platform
import sys
import threading
import time

MODEL_ID = "GyrTechnology/saif-0.8b-lightning"
REVISION = "0cc7d0954a7aa48e0ba7302c4839185eb5aa164d"
SDK_REVISION = "25c6ec7b6b5f44a216c63c3a700de3a9c6d6699d"
ROOT = Path("/workspace/.cave-dungeon/unity/saif-lightning")
SOURCE_SHA256 = {
    "decision_index_engine.py": "7a18d7efe6a337df3fcafb7249d43fa031b8a76860df1625e211d930ff1f736e",
    "saif_v2_core.py": "091adf71ffe4266c916befde25f9c05cc3b2c98765e736a2a115a1b619eafe27",
    "singlepass.py": "cd147cacfa9ba1ca3e5647df1600d3e5e37261784a0f0a3834192e5c6e2701d3",
}
SDK_SHA256 = {
    "decision_index/__init__.py": "802fef8d0a617d28cd906facf8582b707ca7baa35475ce834ef8af41ab9aa54f",
    "decision_index/engines/__init__.py": "808a0971ad2ce9f9a1afc3c071691001375aa73b26f3ba891beb8364dcabba77",
    "decision_index/engines/base.py": "ae548636cfbd583fe774b67e4d3d2cdc6db10f580f47d9559d5de44373b79ca5",
}


def descriptor(path):
    path = Path(path).resolve()
    sha = hashlib.sha256()
    with path.open("rb") as source:
        for chunk in iter(lambda: source.read(1024 * 1024), b""):
            sha.update(chunk)
    return {"path": str(path), "bytes": path.stat().st_size, "sha256": sha.hexdigest()}


def verify_checkpoint(model_dir, manifest_path=ROOT / "model-manifest.json"):
    model_dir = Path(model_dir).resolve()
    manifest = json.loads(Path(manifest_path).read_text())
    if manifest.get("repo") != MODEL_ID or manifest.get("revision") != REVISION:
        raise ValueError("Checkpoint manifest does not identify the pinned Saif release")
    rows = manifest["files"]
    names = [row["filename"] for row in rows]
    required = set(SOURCE_SHA256) | {"config.json", "tokenizer.json", "tokenizer_config.json",
               "chat_template.jinja", "decoder.safetensors", "decision_rows.safetensors",
               "saif_v2_config.json", "saif_temperatures.json"}
    if len(names) != len(set(names)) or not required <= set(names):
        raise ValueError("Checkpoint manifest has duplicate or missing files")
    verified = []
    for row in rows:
        name = row["filename"]
        if Path(name).name != name or name in {"", ".", ".."}:
            raise ValueError("Unsafe checkpoint filename")
        actual = descriptor(model_dir / name)
        if actual["bytes"] != row["bytes"] or actual["sha256"] != row["sha256"]:
            raise ValueError("Checkpoint artifact changed: " + name)
        verified.append({"filename": name, **actual})
    # Native Saif conditionally loads this file; an unrecorded one would change semantics.
    calibration = model_dir / "saif_phishing_calibration.json"
    if calibration.exists() and calibration.name not in names:
        raise ValueError("Unrecorded native phishing calibration file")
    return {"repository": MODEL_ID, "revision": REVISION, "model_dir": str(model_dir),
            "manifest": descriptor(manifest_path), "files": verified}


def verify_sources(source_root=ROOT / "source", sdk_root=ROOT / "decision-index"):
    source_root, sdk_root = Path(source_root).resolve(), Path(sdk_root).resolve()
    sources = []
    for directory, expected in ((source_root, SOURCE_SHA256), (sdk_root, SDK_SHA256)):
        for name, sha in expected.items():
            row = descriptor(directory / name)
            if row["sha256"] != sha:
                raise ValueError("Pinned native source changed: " + str(directory / name))
            sources.append(row)
    return sources


def validate_request(request):
    if not isinstance(request, dict) or set(request) != {"state", "questions"}:
        raise ValueError("Only state/questions may cross the model boundary")
    if not isinstance(request["questions"], dict) or not request["questions"]:
        raise ValueError("Questions must be nonempty")
    for name, question in request["questions"].items():
        if (type(name) is not str or not name or not isinstance(question, dict)
                or set(question) - {"type", "instructions", "criteria"}
                or question.get("type") not in {"noul", "choice"}):
            raise ValueError("Only the original noul/choice schema is supported")
        criteria = question.get("criteria", {})
        if not isinstance(criteria, dict):
            raise ValueError("Criteria must be an object")
        if question["type"] == "noul" and set(criteria) - {"false", "true"}:
            raise ValueError("Noul criteria must use native false/true keys")
        if question["type"] == "choice" and (len(criteria) < 2 or any(type(key) is not str or not key for key in criteria)):
            raise ValueError("Choice needs distinct original option IDs")
    json.dumps(request, ensure_ascii=False, allow_nan=False)


def predictions_from_native(request, response):
    answers = response.get("answers") if isinstance(response, dict) else None
    if not isinstance(answers, dict) or set(answers) != set(request["questions"]):
        raise ValueError("Native answers do not cover the original questions")
    predictions = {}
    for name, question in request["questions"].items():
        answer = answers[name]
        if not isinstance(answer, dict) or answer.get("type") != question["type"]:
            raise ValueError("Native answer type changed")
        probabilities = answer.get("probabilities")
        keys = {"false", "true"} if question["type"] == "noul" else set(question["criteria"])
        if (not isinstance(probabilities, dict) or set(probabilities) != keys
                or any(type(value) not in (int, float) or not math.isfinite(value) or not 0 <= value <= 1
                       for value in probabilities.values())
                or abs(sum(probabilities.values()) - 1) > 1e-4):
            raise ValueError("Native probabilities are incomplete or invalid")
        if question["type"] == "noul":
            if type(answer.get("noul")) not in (int, float) or answer["noul"] != probabilities["true"]:
                raise ValueError("Native noul probability disagrees with the distribution")
            predictions[name] = probabilities["true"] >= 0.5
        else:
            if list(probabilities) != list(question["criteria"]):
                raise ValueError("Native choice probability order differs from original candidate order")
            selected = max(question["criteria"], key=probabilities.get)
            if answer.get("choice") != selected:
                raise ValueError("Native choice disagrees with its own argmax")
            predictions[name] = selected
    return predictions


class NativeBackend:
    __file__ = __file__

    def __init__(self, engine, core, description, initialization_ms):
        self.engine, self.core = engine, core
        self.description = copy.deepcopy(description)
        self.initialization_ms = initialization_ms
        self._lock = threading.Lock()

    def describe(self):
        return copy.deepcopy(self.description)

    def predict(self, backend_url, request, *, timeout_seconds=300.0):
        if backend_url != "native://cpu":
            raise ValueError("This adapter is an in-process CPU engine, not an HTTP server")
        if type(timeout_seconds) not in (int, float) or not math.isfinite(timeout_seconds) or timeout_seconds <= 0:
            raise ValueError("Timeout must be finite and positive")
        validate_request(request)
        if not self._lock.acquire(blocking=False):
            raise RuntimeError("Saif's global attention layout forbids concurrent calls")
        body = copy.deepcopy(request)
        call = {"request": body, "input_tokens": None, "output_tokens": 0}
        try:
            started = time.perf_counter()
            # Exactly one official API call. No separate logits() call or temperature rewrite.
            raw, auxiliary = self.engine(body["state"], body["questions"])
            call.update(native_elapsed_ms=(time.perf_counter() - started) * 1000,
                        raw=raw, native_auxiliary=auxiliary)
            predictions = predictions_from_native(body, raw)
            model = self.engine.model
            # Count AFTER native inference to avoid pre-warming its tokenizer/label caches.
            layouts = list(self.core.layouts(model.tokenizer, body["state"], body["questions"],
                           model.reading, model.max_length, model.read_twice))
            if not layouts or sorted(name for layout in layouts for name in layout.fields) != sorted(body["questions"]):
                raise ValueError("Native layout question coverage changed")
            call["layouts"] = [{"input_tokens": len(layout.ids), "prefix_tokens": layout.prefix,
                                "input_ids_sha256": hashlib.sha256(json.dumps(layout.ids, separators=(",", ":")).encode()).hexdigest(),
                                "fields": layout.fields, "slots": layout.slots,
                                "blocks": layout.blocks, "label_token_ids": layout.label_ids}
                               for layout in layouts]
            call["input_tokens"] = sum(len(layout.ids) for layout in layouts)
            call["probabilities"] = {name: answer["probabilities"] for name, answer in raw["answers"].items()}
            call["native_answers"] = raw["answers"]
            call["native_temperatures"] = dict(model.temperatures)
            return {"predictions": predictions, "details": {"calls": [call],
                    "model_initialization_ms": self.initialization_ms,
                    "initialization_excluded_from_request": True,
                    "raw_logits": None, "raw_logits_note": "Official __call__ returns calibrated probabilities; no second forward is used to obtain logits.",
                    "token_count_scope": "Sum of actual token IDs in native layouts, recomputed purely after the one native API call; no truncation or character proxy."}}
        except Exception as error:
            error.details = {"calls": [call]}
            raise
        finally:
            self._lock.release()

    def close(self):
        self.engine.close()


def initialize(model_dir, threads=4, *, source_root=ROOT / "source", sdk_root=ROOT / "decision-index"):
    """Load once before the durable runner starts its fixed warmup/request timers."""
    if type(threads) is not int or threads < 1:
        raise ValueError("Thread count must be positive")
    sources = verify_sources(source_root, sdk_root)
    for directory in (Path(sdk_root).resolve(), Path(source_root).resolve()):
        if str(directory) not in sys.path:
            sys.path.insert(0, str(directory))
    for name in ("decision_index_engine", "saif_v2_core", "singlepass"):
        if name in sys.modules and Path(sys.modules[name].__file__).resolve().parent != Path(source_root).resolve():
            raise ValueError("Another native source with the same module name is already imported")
    started = time.perf_counter()
    native = importlib.import_module("decision_index_engine")
    core = importlib.import_module("saif_v2_core")
    singlepass = importlib.import_module("singlepass")
    base = importlib.import_module("decision_index.engines.base")
    for module in (native, core, singlepass):
        if Path(module.__file__).resolve().parent != Path(source_root).resolve():
            raise ValueError("Native module was loaded from another directory")
    if Path(base.__file__).resolve() != Path(sdk_root).resolve() / "decision_index/engines/base.py":
        raise ValueError("Decision Index base was shadowed by another installation")
    engine = native.SaifEngine(model=str(Path(model_dir).resolve()), device="cpu", threads=threads)
    initialization_ms = (time.perf_counter() - started) * 1000
    import torch
    model = engine.model
    description = {"adapter": "compare_saif_lightning", "model_id": MODEL_ID, "revision": REVISION,
        "native_engine": "decision_index_engine.SaifEngine", "backend_url": "native://cpu",
        "native_source_files": sources, "sdk_revision": SDK_REVISION,
        "native_provenance": copy.deepcopy(engine.provenance), "native_runtime": engine.runtime(),
        "native_serving_path": engine.provenance.get("serving_path"),
        "native_temperatures": dict(model.temperatures), "native_logits_available": False,
        "decoder_dtype": str(next(model.language_model.parameters()).dtype),
        "decoder_parameter_dtypes": sorted({str(parameter.dtype) for parameter in model.language_model.parameters()}),
        "decoder_device": str(next(model.language_model.parameters()).device),
        "head_dtype": str(model.head.weight.dtype), "head_device": str(model.head.weight.device),
        "attention": model.attention, "reading": model.reading, "read_twice": model.read_twice,
        "fast_path": model.fast_path, "max_context_tokens": model.max_length,
        "python": platform.python_version(), "python_executable": str(Path(sys.executable).resolve()),
        "packages": {name: importlib.metadata.version(name) for name in ("torch", "transformers", "accelerate", "safetensors", "huggingface-hub")},
        "torch_threads": torch.get_num_threads(), "torch_interop_threads": torch.get_num_interop_threads(),
        "license": "CC-BY-NC-4.0", "calibration_fit_on_this_corpus": False,
        "prediction_rule": "Native calibrated probabilities unchanged; noul p(true)>=0.5, choice native argmax.",
        "request_timing_scope": "Full adapter call includes native inference and pure post-call token-layout/count validation. Native-only wall time is also saved. Imports/model initialization and fixed warmup are excluded from quality timings.",
        "execution": "One official __call__ per request in a dedicated sequential CPU process; native global attention patches unchanged."}
    return NativeBackend(engine, core, description, initialization_ms)
