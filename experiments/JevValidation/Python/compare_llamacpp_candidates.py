"""Evaluate LFM candidates through local llama.cpp in a fixed fast symbol mode.

The grammar constrains only the answer format. No feasibility filter, gold label,
answer repair, generated tool execution, or sampled-probability claim is added.
This deliberately does not measure unrestricted or full-thinking LFM3 behavior.
"""
from __future__ import annotations

import hashlib
import json
import math
from pathlib import Path
import time
import urllib.error
import urllib.parse
import urllib.request


DEFAULT_TIMEOUT_SECONDS = 300.0
SEED = 20261107
MAX_RESPONSE_BYTES = 4 * 1024 * 1024
SYSTEM_INSTRUCTION = (
    "提示された状況・規則・質問・候補に基づいて判断してください。"
    "候補の記号1文字のみを出力してください。説明や思考過程は出力しないでください。"
)


class CandidateProtocolError(ValueError):
    """A failed call, with full request/response evidence available to the runner."""
    def __init__(self, message, *, details=None, raw=None):
        super().__init__(message)
        self.details = details
        self.raw = raw


def describe():
    return {
        "adapter": "compare_llamacpp_candidates",
        "adapter_sha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
        "mode": "fast non-thinking, grammar-constrained answer symbol",
        "endpoint": "/v1/chat/completions on a local HTTP llama-server",
        "native_chat_template": "Server formats messages with the loaded model's native template.",
        "system_instruction": SYSTEM_INSTRUCTION,
        "decode": {"temperature": 0, "seed": SEED, "max_tokens": 4,
                   "chat_template_kwargs": {"enable_thinking": False}, "reasoning_format": "deepseek",
                   "cache_prompt": False},
        "native_reasoning_parser": "llama.cpp's deepseek parser separates the native template's empty thinking wrapper from content; nonempty reasoning is rejected. No client-side wrapper stripping or answer repair.",
        "grammar": "Exactly one of A..D for the complete original candidate list; no semantic filtering.",
        "boolean_mapping": {"A": False, "B": True},
        "choice_mapping": "Original criteria insertion order maps to A..D; IDs stay outside the model prompt.",
        "question_conditioning": "Each question is a separate stateless chat request, without other questions' sampled answers.",
        "state_format": "Strings are unchanged; objects use ensure_ascii=False JSON with all keys retained.",
        "failure_policy": "HTTP/native errors, truncation, extra reasoning, and non-single-symbol answers raise; no guessed answer.",
        "sources": {
            "LFM2.5-JP": "https://huggingface.co/LiquidAI/LFM2.5-1.2B-JP-202606",
            "LFM3": "https://huggingface.co/LiquidAI/LFM3-1.7B",
            "llama_server": "https://github.com/ggml-org/llama.cpp/tree/b12110/tools/server",
        },
        "limitation": "LFM3 documents that enable_thinking=False can be inconsistent. This evaluates the specified fast constrained mode, not full-thinking capability.",
        "http_timeout_seconds": DEFAULT_TIMEOUT_SECONDS,
    }


def _json_state(state):
    if isinstance(state, str):
        return state
    if not isinstance(state, dict):
        raise ValueError("state must be text or a JSON object")
    def check_keys(value):
        if isinstance(value, dict):
            if any(not isinstance(key, str) for key in value):
                raise ValueError("JSON state object keys must be strings")
            for item in value.values():
                check_keys(item)
        elif isinstance(value, list):
            for item in value:
                check_keys(item)
    check_keys(state)
    return json.dumps(state, ensure_ascii=False, allow_nan=False)


def prepare_tasks(request):
    if not isinstance(request, dict) or set(request) != {"state", "questions"}:
        raise ValueError("Pass only state/questions; benchmark IDs, gold and metadata are forbidden")
    state_text = _json_state(request["state"])
    questions = request["questions"]
    if not isinstance(questions, dict) or not questions:
        raise ValueError("questions must be a nonempty object")
    tasks = []
    for qid, question in questions.items():
        if (not isinstance(qid, str) or not qid or not isinstance(question, dict) or
                not {"type", "instructions"} <= set(question) or
                set(question) - {"type", "instructions", "criteria"}):
            raise ValueError("Question identity, instructions or allowed fields are invalid")
        instructions = question["instructions"]
        if not isinstance(instructions, str) or not instructions.strip():
            raise ValueError("Original question instructions must be nonempty text")
        kind, criteria = question["type"], question.get("criteria", {})
        if not isinstance(criteria, dict) or any(not isinstance(value, str) for value in criteria.values()):
            raise ValueError("Criteria must map original candidate IDs to text descriptions")
        if kind in {"noul", "boolean"}:
            if set(criteria) - {"false", "true"}:
                raise ValueError("Boolean criteria may contain only false/true descriptions")
            values = [False, True]
            descriptions = ["false（偽・いいえ）: " + criteria.get("false", ""),
                            "true（真・はい）: " + criteria.get("true", "")]
            ids = ["false", "true"]
        elif kind == "choice":
            if not 2 <= len(criteria) <= 4 or any(not isinstance(key, str) or not key for key in criteria):
                raise ValueError("Choice requires 2..4 distinct original candidate IDs")
            if any(not value.strip() for value in criteria.values()):
                raise ValueError("Every choice needs its original nonempty description")
            ids = list(criteria)
            values = list(ids)
            descriptions = list(criteria.values())
        else:
            raise ValueError("Only noul/boolean and 2..4-option choice questions are supported")
        symbols = list("ABCD"[:len(values)])
        user_text = ("【状況】\n" + state_text + "\n\n【質問】\n" + instructions + "\n\n【候補】\n" +
                     "\n".join(symbol + ": " + text for symbol, text in zip(symbols, descriptions)))
        body = {
            "messages": [{"role": "system", "content": SYSTEM_INSTRUCTION},
                         {"role": "user", "content": user_text}],
            "temperature": 0, "seed": SEED, "max_tokens": 4, "stream": False,
            "cache_prompt": False,
            "chat_template_kwargs": {"enable_thinking": False}, "reasoning_format": "deepseek",
            "grammar": "root ::= " + " | ".join(json.dumps(symbol) for symbol in symbols),
        }
        tasks.append({"question_id": qid, "body": body, "candidate_ids": ids,
                      "symbol_mapping": dict(zip(symbols, values))})
    return tasks


def _endpoint(server_url):
    parsed = urllib.parse.urlparse(server_url)
    if (parsed.scheme != "http" or parsed.hostname not in {"localhost", "127.0.0.1", "::1"} or
            parsed.username is not None or parsed.password is not None or parsed.query or parsed.fragment or
            parsed.path.rstrip("/") not in {"", "/v1"}):
        raise ValueError("Use a local HTTP llama-server base URL without credentials/query/fragment")
    return server_url.rstrip("/") + ("/chat/completions" if parsed.path.rstrip("/") == "/v1" else "/v1/chat/completions")


def _post_json(server_url, body, timeout_seconds):
    endpoint = _endpoint(server_url)
    data = json.dumps(body, ensure_ascii=False, allow_nan=False).encode("utf-8")
    request = urllib.request.Request(endpoint, data=data, headers={"Content-Type": "application/json"}, method="POST")
    # The explicit local backend bypasses only the outbound proxy for loopback.
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    try:
        with opener.open(request, timeout=timeout_seconds) as response:
            encoded = response.read(MAX_RESPONSE_BYTES + 1)
    except urllib.error.HTTPError as error:
        text = error.read(MAX_RESPONSE_BYTES + 1).decode("utf-8", "replace")
        raise CandidateProtocolError("llama-server HTTP error " + str(error.code),
                                     raw={"http_status": error.code, "body": text}) from error
    except (urllib.error.URLError, TimeoutError, OSError) as error:
        raise CandidateProtocolError("Local llama-server request failed: " + str(error),
                                     raw={"transport_error": type(error).__name__, "message": str(error)}) from error
    if len(encoded) > MAX_RESPONSE_BYTES:
        raise CandidateProtocolError("llama-server response exceeds the bounded response size")
    try:
        def invalid_constant(value):
            raise ValueError("Nonfinite JSON constant: " + value)
        return json.loads(encoded, parse_constant=invalid_constant)
    except (ValueError, UnicodeError) as error:
        raise CandidateProtocolError("llama-server returned invalid JSON", raw={"body": encoded.decode("utf-8", "replace")}) from error


def _decode(raw, symbols):
    if not isinstance(raw, dict) or raw.get("error") is not None:
        raise CandidateProtocolError("Native response contains an error or is not an object")
    choices = raw.get("choices")
    if not isinstance(choices, list) or len(choices) != 1 or not isinstance(choices[0], dict):
        raise CandidateProtocolError("Exactly one native answer is required")
    choice = choices[0]
    message = choice.get("message")
    if choice.get("finish_reason") != "stop":
        raise CandidateProtocolError("Native answer was truncated or did not finish normally")
    if (not isinstance(message, dict) or message.get("role", "assistant") != "assistant" or
            message.get("tool_calls") or message.get("reasoning_content") or message.get("reasoning")):
        raise CandidateProtocolError("Fast symbol mode returned unexpected reasoning or tool calls")
    answer = message.get("content")
    if not isinstance(answer, str) or len(answer) != 1 or answer not in symbols:
        raise CandidateProtocolError("Native answer must be exactly one permitted symbol without cleanup")
    return answer


def _token_counts(raw):
    usage = raw.get("usage", {})
    if not isinstance(usage, dict):
        raise CandidateProtocolError("Native usage is not an object")
    for key in ("prompt_tokens", "completion_tokens", "total_tokens"):
        value = usage.get(key)
        if value is not None and (type(value) is not int or value < 0):
            raise CandidateProtocolError("Native token usage must be nonnegative integers when present")
    return usage, usage.get("prompt_tokens"), usage.get("completion_tokens")


def predict(server_url, request, *, timeout_seconds=DEFAULT_TIMEOUT_SECONDS):
    if (type(timeout_seconds) not in (int, float) or not math.isfinite(timeout_seconds) or timeout_seconds <= 0):
        raise ValueError("timeout_seconds must be positive and finite")
    _endpoint(server_url)
    tasks = prepare_tasks(request)
    predictions, calls, by_question = {}, [], {}
    started = time.perf_counter()
    for task in tasks:
        t0 = time.perf_counter()
        detail = {"question_id": task["question_id"], "body": task["body"], "raw": None,
                  "candidate_ids": task["candidate_ids"], "symbol_mapping": task["symbol_mapping"],
                  "input_tokens": None, "output_tokens": None}
        calls.append(detail)
        by_question[task["question_id"]] = detail
        try:
            remaining = timeout_seconds - (time.perf_counter() - started)
            if remaining <= 0:
                raise CandidateProtocolError("The complete prediction request exceeded its wall-time budget")
            raw = _post_json(server_url, task["body"], remaining)
            detail["raw"] = raw
            symbol = _decode(raw, task["symbol_mapping"])
            usage, input_tokens, output_tokens = _token_counts(raw)
            detail.update({"usage": usage, "input_tokens": input_tokens, "output_tokens": output_tokens,
                           "timings": raw.get("timings"), "symbol": symbol})
            predictions[task["question_id"]] = task["symbol_mapping"][symbol]
        except CandidateProtocolError as error:
            if detail["raw"] is None:
                detail["raw"] = error.raw
            detail["error"] = str(error)
            error.details = {"calls": calls, "by_question": by_question, "timeout_seconds": timeout_seconds}
            raise
        finally:
            detail["elapsed_ms"] = (time.perf_counter() - t0) * 1000
    return {"predictions": predictions,
            "details": {"calls": calls, "by_question": by_question,
                        "http_elapsed_ms": (time.perf_counter() - started) * 1000,
                        "timeout_seconds": timeout_seconds}}
