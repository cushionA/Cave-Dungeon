"""Use Unee's pinned native prompt/parser and llama.cpp decision probability rule.

Only ``request = {state, questions}`` crosses the model boundary.  The original
Unee code stays outside the repository; no benchmark labels or examples are
inserted by this adapter.  Probabilities reproduce the official top-20/floor
approximation, rather than pretending to be full-vocabulary logits.
"""
from __future__ import annotations

import copy
import hashlib
import importlib
import json
import math
from pathlib import Path
import sys
import time
import urllib.parse
import urllib.request


NATIVE_REPOSITORY = "https://github.com/uneeverse-hq/unee"
NATIVE_REVISION = "adaad0bc5e481d49044ff591762e700fe6acebfe"
NATIVE_VERSION = "0.3.2"
DEFAULT_NATIVE_ROOT = Path("/workspace/.cave-dungeon/unity/additional-candidates/unee-source")
NATIVE_SHA256 = {
    "unee/prompt.py": "8161d3ece231f1b2088e86ee0480fcaab2befc73ee407b261293af3786b687a9",
    "unee/facts.py": "0060459bbc4267b754ed5b4aadaed9316624a91c25bb42e6a53101b98589f1e3",
    "unee/systemone.py": "8f12ea919869169d4826ee430811d0c0dbb883bf26539077962ad8bf4aca96de",
    "unee/server.py": "39b2258e29ebc842f416f59214dc9aa735003adf91e392b7c1dc85f0311b7580",
}
CALIBRATION_TEMPERATURE = 1.0
DEFAULT_TIMEOUT_SECONDS = 300.0


def describe():
    return {
        "adapter": "compare_unee_candidates",
        "native_repository": NATIVE_REPOSITORY,
        "native_revision": NATIVE_REVISION,
        "native_version": NATIVE_VERSION,
        "native_source_sha256": dict(NATIVE_SHA256),
        "license": "Apache-2.0",
        "prompt_format": "native compact build_messages; date_facts enabled",
        "decode": {"max_tokens": 1, "temperature": 0, "logprobs": True,
                   "top_logprobs": 20, "chat_template_kwargs": {"enable_thinking": False}},
        "calibration_temperature": CALIBRATION_TEMPERATURE,
        "missing_letter_rule": "official min(observed token probability) * 0.5, then option normalization",
        "boolean_prediction": "unrounded native p_true >= 0.5; API rounded value also retained",
        "http_timeout_seconds": DEFAULT_TIMEOUT_SECONDS,
        "supports": ["noul", "choice with 2..8 options"],
    }


def _native(source_root=None):
    root = Path(source_root or DEFAULT_NATIVE_ROOT).resolve()
    for relative, expected in NATIVE_SHA256.items():
        path = root / relative
        if not path.is_file() or hashlib.sha256(path.read_bytes()).hexdigest() != expected:
            raise ValueError("Pinned Unee source is missing or changed: " + str(path))
    if str(root) not in sys.path:
        sys.path.insert(0, str(root))
    prompt = importlib.import_module("unee.prompt")
    systemone = importlib.import_module("unee.systemone")
    for module in (prompt, systemone):
        if not Path(module.__file__).resolve().is_relative_to(root):
            raise ValueError("Another Unee installation is already imported: " + str(module.__file__))
    return prompt, systemone


def _validate_request(request):
    if not isinstance(request, dict) or set(request) != {"state", "questions"}:
        raise ValueError("Pass only state and questions, never an annotated benchmark case")
    questions = request["questions"]
    if not isinstance(questions, dict) or not questions:
        raise ValueError("questions must be a nonempty object")
    for qid, question in questions.items():
        if (not isinstance(qid, str) or not qid or not isinstance(question, dict)
                or not {"type", "instructions"} <= set(question)
                or set(question) - {"type", "instructions", "criteria"}
                or question["type"] not in {"noul", "choice"}):
            raise ValueError("Only original noul/choice instructions and criteria are supported")
        if question["type"] == "choice" and not 2 <= len(question.get("criteria", {})) <= 8:
            raise ValueError("This adapter deliberately excludes Unee's multi-round tournament path")
    # Reject non-JSON and nonfinite values before making any HTTP request.
    json.dumps(request, ensure_ascii=False, allow_nan=False)


def prepare_tasks(request, *, source_root=None):
    """Return the exact native HTTP bodies without any benchmark bookkeeping."""
    _validate_request(request)
    prompt, systemone = _native(source_root)
    state, tasks = systemone.parse(copy.deepcopy(request))
    state = systemone.state_value(state)
    prepared = []
    for task in tasks:
        body = {
            "messages": prompt.build_messages(state, task.question, task.options, task.examples),
            "max_tokens": 1, "temperature": 0, "logprobs": True, "top_logprobs": 20,
            "chat_template_kwargs": {"enable_thinking": False},
        }
        prepared.append((task, body))
    return prepared


def native_probabilities(raw, options):
    """Mirror pinned ``unee.server.Engine.decide``; retain missing-letter evidence."""
    top = raw["choices"][0]["logprobs"]["content"][0]["top_logprobs"]
    if not isinstance(top, list):
        raise ValueError("Unee requires native top_logprobs entries")
    seen = {}
    for token in top:
        text, value = token["token"], token["logprob"]
        if (not isinstance(text, str) or isinstance(value, bool)
                or not isinstance(value, (int, float)) or not math.isfinite(value) or value > 0):
            raise ValueError("Nonfinite or invalid native token logprob")
        key = text.strip()
        seen[key] = seen.get(key, 0.0) + math.exp(value)
    floor = min(seen.values(), default=1e-6) * 0.5
    letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ"
    values = {o["key"]: seen.get(letters[i], floor) ** (1 / CALIBRATION_TEMPERATURE)
              for i, o in enumerate(options)}
    z = sum(values.values())
    if not math.isfinite(z) or z <= 0:
        raise ValueError("Native option probabilities have zero/nonfinite total mass")
    return ({key: value / z for key, value in values.items()},
            [letters[i] for i in range(len(options)) if letters[i] not in seen], floor)


def _post_json(server_url, body, timeout_seconds):
    parsed = urllib.parse.urlparse(server_url)
    if parsed.scheme != "http" or parsed.hostname not in {"127.0.0.1", "localhost", "::1"}:
        raise ValueError("The candidate backend must be a local HTTP llama-server")
    endpoint = server_url.rstrip("/") + "/v1/chat/completions"
    data = json.dumps(body, ensure_ascii=False, allow_nan=False).encode("utf-8")
    req = urllib.request.Request(endpoint, data=data, headers={"Content-Type": "application/json"}, method="POST")
    # This is loopback, so skip the inherited outbound HTTP proxy only for this local connection.
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    with opener.open(req, timeout=timeout_seconds) as response:
        return json.load(response)


def predict(server_url, request, *, timeout_seconds=DEFAULT_TIMEOUT_SECONDS, source_root=None):
    if (isinstance(timeout_seconds, bool) or not isinstance(timeout_seconds, (int, float))
            or not math.isfinite(timeout_seconds) or timeout_seconds <= 0):
        raise ValueError("timeout_seconds must be a positive finite number")
    prepared = prepare_tasks(request, source_root=source_root)
    _, systemone = _native(source_root)
    predictions, calls = {}, []
    started = time.perf_counter()
    for task, body in prepared:
        t0 = time.perf_counter()
        raw = _post_json(server_url, body, timeout_seconds)
        elapsed_ms = (time.perf_counter() - t0) * 1000
        probabilities, missing, floor = native_probabilities(raw, task.options)
        native_answer = systemone.answer(task, probabilities)
        predictions[task.qid] = (probabilities["true"] >= 0.5 if task.kind == "noul"
                                 else native_answer["choice"])
        usage = raw.get("usage", {})
        calls.append({
            "question_id": task.qid, "body": body, "raw": raw,
            "candidate_ids": [o["key"] for o in task.options],
            "probabilities": probabilities, "native_answer": native_answer,
            "missing_option_letters": missing, "native_floor_probability": floor,
            "input_tokens": usage.get("prompt_tokens"), "usage": usage,
            "elapsed_ms": elapsed_ms,
        })
    return {"predictions": predictions,
            "details": {"calls": calls, "http_elapsed_ms": (time.perf_counter() - started) * 1000,
                        "timeout_seconds": timeout_seconds}}
