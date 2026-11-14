"""Render the completed, separately frozen Saif study; never run a model.

The statistics JSON is authoritative. Validation rejects incomplete samples and
inconsistent cells instead of silently repairing or recomputing observations.
Only the three files beneath --output-prefix are written.
"""
from __future__ import annotations

import argparse
import hashlib
import html
import json
import math
import os
from pathlib import Path
from urllib.parse import quote


MODEL_KEY = "saif-lightning"
MODEL_ID = "GyrTechnology/saif-0.8b-lightning"
REVISION = "0cc7d0954a7aa48e0ba7302c4839185eb5aa164d"
PROTOCOL_SHA256 = "d017040630b5e1b31597730ef40e9224d7b9b014ba1daf130121afa3cad1d35a"
BASELINES = ("hindsight", "tinyjev", "unee-0.8b", "unee-2b", "lfm25-jp", "lfm3")
LABELS = {MODEL_KEY: "Saif 0.8B Lightning", "hindsight": "Hindsight 0.8B",
          "tinyjev": "TinyJev", "unee-0.8b": "Unee 0.8B", "unee-2b": "Unee 2B",
          "lfm25-jp": "LFM2.5 JP 1.2B", "lfm3": "LFM3 1.7B (fast)"}
TASKS = {"memory": "記憶の関連判定", "commander": "提示された作戦の4択"}
TEMPERATURES = {"choice": 1.3492052555084229, "noul": 1.6690177917480469,
                "score": 0.5360531806945801}


def _unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("Duplicate JSON key: " + key)
        result[key] = value
    return result


def read_json(path):
    return json.loads(Path(path).read_text(encoding="utf-8"), object_pairs_hook=_unique_object,
                      parse_constant=lambda value: (_ for _ in ()).throw(ValueError("Non-finite JSON: " + value)))


def descriptor(path):
    path = Path(path).resolve()
    data = path.read_bytes()
    return {"path": str(path), "bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()}


def _integer(value, low=0, high=None):
    return type(value) is int and value >= low and (high is None or value <= high)


def _number(value, low=None, high=None):
    return (type(value) in (int, float) and math.isfinite(value)
            and (low is None or value >= low) and (high is None or value <= high))


def _close(first, second):
    return _number(first) and abs(first - second) <= 1e-10


def _interval(value, point, low, high):
    return (isinstance(value, list) and len(value) == 2
            and all(_number(item, low, high) for item in value) and value[0] <= point <= value[1])


def _wilson(correct, n):
    z = 1.959963984540054
    rate = correct / n
    denominator = 1 + z * z / n
    center = (rate + z * z / (2 * n)) / denominator
    half = z * math.sqrt(rate * (1 - rate) / n + z * z / (4 * n * n)) / denominator
    return [0.0 if correct == 0 else max(0.0, center - half),
            1.0 if correct == n else min(1.0, center + half)]


def validate_latency(value, expected_n=None):
    if not isinstance(value, dict) or not _integer(value.get("n")):
        raise ValueError("Latency must declare its measured sample size")
    if expected_n is not None and value["n"] != expected_n:
        raise ValueError("Latency sample size differs from its declared scope")
    values = [value.get(field) for field in ("mean_ms", "p50_ms", "p95_ms")]
    if value["n"] == 0:
        if values != [None, None, None]:
            raise ValueError("Zero-observation latency must not invent estimates")
    elif not all(_number(item, 0) for item in values) or values[1] > values[2]:
        raise ValueError("Latency estimates must be finite, nonnegative and ordered")


def validate_group(group, task, language, *, recorded_latency=True):
    n = 500 if language == "ja" else 100
    if not isinstance(group, dict) or group.get("n") != n or not _integer(group.get("correct"), 0, n):
        raise ValueError("Every task needs exactly JA500 and EN100 observations")
    correct = group["correct"]
    if not _close(group.get("accuracy"), correct / n):
        raise ValueError("Accuracy disagrees with the integer count")
    expected = _wilson(correct, n)
    if (not _interval(group.get("wilson_95"), group["accuracy"], 0, 1)
            or any(abs(a - b) > 1e-10 for a, b in zip(expected, group["wilson_95"]))):
        raise ValueError("Wilson interval disagrees with its exact count")
    failures = group.get("failures")
    if not _integer(failures, 0, n - correct) or not _close(group.get("coverage"), (n - failures) / n):
        raise ValueError("Failed observations must remain incorrect")
    # Historical CSVs retain quality only. Their regenerated summaries correctly
    # have zero timing observations rather than copied or imputed old latencies.
    validate_latency(group["latency"], n if recorded_latency else 0)
    if task == "memory":
        cells = group["memory_confusion"]
        keys = ("true_positive", "true_negative", "false_positive", "false_negative",
                "failed_expected_true", "failed_expected_false")
        if (any(not _integer(cells.get(key), 0, n) for key in keys)
                or sum(cells[key] for key in keys) != n
                or cells["true_positive"] + cells["true_negative"] != correct
                or cells["failed_expected_true"] + cells["failed_expected_false"] != failures
                or cells.get("actual_positive") != cells["true_positive"] + cells["false_negative"] + cells["failed_expected_true"]
                or cells.get("actual_negative") != cells["true_negative"] + cells["false_positive"] + cells["failed_expected_false"]):
            raise ValueError("Memory confusion cells disagree with counts or failures")
    else:
        cells = group["commander"]
        keys = ("correct", "known_infeasible", "feasible_but_not_best", "failed_or_invalid")
        if (any(not _integer(cells.get(key), 0, n) for key in keys)
                or sum(cells[key] for key in keys) != n or cells["correct"] != correct
                or cells["failed_or_invalid"] != failures
                or not _number(cells.get("mean_normalized_ordinal_rank_loss"), 0, 1)
                or not _close(cells.get("known_infeasible_rate"), cells["known_infeasible"] / n)
                or cells.get("unsafe_or_invalid") != cells["known_infeasible"] + failures
                or not _close(cells.get("unsafe_or_invalid_rate"), (cells["known_infeasible"] + failures) / n)):
            raise ValueError("Commander diagnostic cells disagree with counts or failures")


def validate_statistics(statistics):
    if (statistics.get("schema_version") != 1 or statistics.get("exploratory") is not True
            or set(statistics.get("models", {})) != {MODEL_KEY}
            or set(statistics.get("baseline_summary", {})) != set(BASELINES)):
        raise ValueError("Expected the frozen exploratory Saif study and all six baselines")
    model = statistics["models"][MODEL_KEY]
    identity = model["identity"]
    if (identity.get("model_id") != MODEL_ID or identity.get("revision") != REVISION
            or identity.get("license") != "CC-BY-NC-4.0"):
        raise ValueError("Checkpoint identity or noncommercial license changed")
    description = identity["run_identity"]["adapter_description"]
    if (description.get("decoder_dtype") != "torch.bfloat16" or description.get("head_dtype") != "torch.float32"
            or description.get("decoder_device") != "cpu" or description.get("head_device") != "cpu"
            or description.get("native_temperatures") != TEMPERATURES
            or description.get("torch_threads") != 4 or not _integer(description.get("torch_interop_threads"), 1)
            or description.get("reading") != "joint" or description.get("read_twice") is not False
            or description.get("fast_path") is not True
            or description.get("calibration_fit_on_this_corpus") is not False):
        raise ValueError("Native precision, CPU device or published temperatures changed")
    if set(model.get("tasks", {})) != set(TASKS):
        raise ValueError("Unexpected task coverage")
    for task in TASKS:
        if set(model["tasks"][task]) != {"ja", "en"}:
            raise ValueError("Japanese and English results must remain separate")
        for language in ("ja", "en"):
            group = model["tasks"][task][language]
            validate_group(group, task, language)
            paired = group.get("paired_baselines", {})
            if set(paired) != set(BASELINES):
                raise ValueError("All six prespecified paired comparisons are required")
            for name in BASELINES:
                baseline = statistics["baseline_summary"][name][task][language]
                validate_group(baseline, task, language, recorded_latency=False)
                cells = paired[name]
                keys = ("both_correct", "candidate_only", "baseline_only", "both_wrong")
                n = group["n"]
                if (cells.get("n") != n or any(not _integer(cells.get(key), 0, n) for key in keys)
                        or sum(cells[key] for key in keys) != n
                        or cells["both_correct"] + cells["candidate_only"] != group["correct"]
                        or cells["both_correct"] + cells["baseline_only"] != baseline["correct"]):
                    raise ValueError("Paired cells disagree with the two exact correct counts")
                delta = (cells["candidate_only"] - cells["baseline_only"]) / n
                if (not _close(cells.get("difference"), delta)
                        or not _close(cells.get("difference_percentage_points"), 100 * delta)
                        or not _interval(cells.get("conservative_95"), delta, -1, 1)):
                    raise ValueError("Paired difference or interval is invalid")
    overall = model["overall"]
    groups = [model["tasks"][task][language] for task in TASKS for language in ("ja", "en")]
    if (overall.get("n") != 1200 or overall.get("correct") != sum(group["correct"] for group in groups)
            or overall.get("failures") != sum(group["failures"] for group in groups)
            or any(key in overall for key in ("wilson_95", "paired_baselines", "conservative_95"))):
        raise ValueError("Overall is an attempt count, never a pooled-world interval")
    validate_latency(model["latency"], 1200)
    validate_latency(model["native_only_latency"], 1200 - overall["failures"])
    for field in ("input_tokens", "output_tokens"):
        tokens = model["tokens"][field]
        known = tokens.get("known_n")
        if (not _integer(known, 0, 1200) or tokens.get("missing_n") != 1200 - known
                or not _integer(tokens.get("total"))
                or (known and not _close(tokens.get("mean"), tokens["total"] / known))
                or (not known and tokens.get("mean") is not None)):
            raise ValueError("Token summary must distinguish actual counts from missing observations")
        if field == "output_tokens" and tokens["total"] != 0:
            raise ValueError("The native decision-head path must not report generated tokens")
    return model


def verify_protocol(statistics, path=None):
    saved = statistics["protocol"]
    path = Path(path or saved["path"])
    actual = descriptor(path)
    if (actual["sha256"] != PROTOCOL_SHA256 or saved.get("sha256") != actual["sha256"]
            or saved.get("bytes") != actual["bytes"]):
        raise ValueError("The rendering input must match the independently pinned Saif protocol")
    protocol = read_json(path)
    if protocol["model"]["revision"] != REVISION or protocol["model"]["temperatures"] != TEMPERATURES:
        raise ValueError("Native model protocol changed")
    if (statistics["models"][MODEL_KEY]["runtime"] != protocol["runtime"]
            or statistics["models"][MODEL_KEY]["identity"]["quantization"] != protocol["model"]["quantization"]):
        raise ValueError("Reported runtime or quantization differs from the frozen protocol")
    return protocol, actual


def read_controls(path, protocol):
    if path is None:
        return None
    cases_path = Path(protocol["controls_cases"]["path"])
    actual = descriptor(cases_path)
    if any(actual[key] != protocol["controls_cases"][key] for key in ("bytes", "sha256")):
        raise ValueError("The separately fixed eight control cases changed")
    cases, result = read_json(cases_path), read_json(path)
    rows = result.get("results", [])
    if (len(cases) != 8 or result.get("status") != "complete" or result.get("planned_cases") != 8
            or result.get("completed_cases") != 8 or len(rows) != 8
            or len({row.get("case_id") for row in rows}) != 8):
        raise ValueError("The separate control run must contain all eight fixed observations")
    model = result.get("identity", {}).get("adapter_description", {})
    if model.get("model_id") != MODEL_ID or model.get("revision") != REVISION:
        raise ValueError("Controls used another model identity")
    output = []
    for index, (case, row) in enumerate(zip(cases, rows)):
        if (row.get("case_index") != index or row.get("case_id") != case["case_id"]
                or row.get("suite") != "simple_controls" or row.get("language") != "ja"
                or row.get("expected") != case["expected"] or type(row.get("correct")) is not bool):
            raise ValueError("Control order, metadata or gold changed")
        status, predictions = row.get("status"), row.get("predictions")
        if status == "failed":
            if predictions != {} or row["correct"] is not False or not isinstance(row.get("error"), dict):
                raise ValueError("Failed controls must remain incorrect")
        elif status == "complete":
            if not isinstance(predictions, dict) or set(predictions) != set(case["expected"]) or row.get("error") is not None:
                raise ValueError("Invalid control answer coverage")
            for qid, expected in case["expected"].items():
                prediction = predictions[qid]
                if type(prediction) is not type(expected):
                    raise ValueError("Control booleans cannot be replaced by numeric values")
                if isinstance(expected, str) and prediction not in case["request"]["questions"][qid]["criteria"]:
                    raise ValueError("Control selected an unknown candidate")
            if row["correct"] != (predictions == case["expected"]):
                raise ValueError("Control correctness disagrees with its typed answer")
        else:
            raise ValueError("Unknown control status")
        output.append({"case_id": case["case_id"], "kind": case["kind"], "state": case["request"]["state"],
                       "expected": case["expected"], "predictions": predictions, "status": status, "correct": row["correct"]})
    return {"rows": output, "source": descriptor(path), "correct": sum(row["correct"] for row in output),
            "failed": sum(row["status"] == "failed" for row in output)}


def read_resources(path):
    if path is None:
        return None
    data = read_json(path)
    if (data.get("exit_code") != 0 or not _integer(data.get("max_vmrss_kib"))
            or not _integer(data.get("max_vmhwm_kib"))
            or data["max_vmhwm_kib"] < data["max_vmrss_kib"] or not isinstance(data.get("scope"), str)):
        raise ValueError("Resource summary must be complete with a successful worker exit")
    return {"data": data, "source": descriptor(path)}


def groups(statistics, task, language):
    return [(MODEL_KEY, statistics["models"][MODEL_KEY]["tasks"][task][language])] + [
        (name, statistics["baseline_summary"][name][task][language]) for name in BASELINES]


def draw(statistics, prefix, statistics_sha):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    from matplotlib.ticker import FuncFormatter, PercentFormatter

    plt.rcParams.update({"font.family": "DejaVu Sans", "font.size": 9.5,
                         "svg.hashsalt": statistics_sha, "axes.spines.top": False, "axes.spines.right": False})
    fig, axes = plt.subplots(2, 2, figsize=(13, 10.2), gridspec_kw={"height_ratios": [1.15, 1]})
    model = statistics["models"][MODEL_KEY]
    all_intervals = [pair["conservative_95"] for task in TASKS
                     for pair in model["tasks"][task]["ja"]["paired_baselines"].values()]
    bound = min(1, max(.15, max(abs(value) for interval in all_intervals for value in interval)) + .03)
    for column, (task, title) in enumerate((("memory", "Memory relevance"), ("commander", "Given-plan selection"))):
        ax = axes[0, column]
        for index, (name, result) in enumerate(groups(statistics, task, "ja")):
            score = result["accuracy"]
            low, high = result["wilson_95"]
            color = "#0369a1" if name == MODEL_KEY else "#64748b"
            ax.errorbar(score, index, xerr=[[max(0, score - low)], [max(0, high - score)]],
                        fmt="o" if name == MODEL_KEY else "s", color=color, capsize=3.5,
                        markersize=6, elinewidth=1.7)
            x, align = (high + .025, "left") if high < .82 else (low - .025, "right")
            ax.text(x, index, f'{result["correct"]}/500 ({score:.1%})', va="center", ha=align,
                    fontsize=8.5, color=color)
        ax.set_yticks(range(7), [LABELS[name] for name, _ in groups(statistics, task, "ja")])
        ax.invert_yaxis()
        ax.set_xlim(0, 1)
        ax.set_title(title + "\nJapanese: 500 worlds", loc="left", fontweight="bold")
        ax.xaxis.set_major_formatter(PercentFormatter(1))
        ax.set_xlabel("Exact accuracy; pointwise 95% Wilson interval")
        ax.grid(axis="x", alpha=.18)
        ax.set_axisbelow(True)
        ax = axes[1, column]
        for index, name in enumerate(BASELINES):
            pair = model["tasks"][task]["ja"]["paired_baselines"][name]
            delta = pair["difference"]
            low, high = pair["conservative_95"]
            ax.errorbar(delta, index, xerr=[[max(0, delta - low)], [max(0, high - delta)]],
                        fmt="o", color="#0369a1", capsize=3.5, markersize=5, elinewidth=1.7)
        ax.axvline(0, color="#475569", linestyle=":", linewidth=1.2)
        ax.set_yticks(range(6), ["Saif minus " + LABELS[name] for name in BASELINES])
        ax.invert_yaxis()
        ax.set_xlim(-bound, bound)
        ax.xaxis.set_major_formatter(FuncFormatter(lambda value, _: f"{100 * value:+.0f}"))
        ax.set_xlabel("Paired accuracy difference (percentage points)")
        ax.set_title("Same-case paired differences", loc="left", fontweight="bold")
        ax.grid(axis="x", alpha=.18)
        ax.set_axisbelow(True)
    fig.suptitle("Saif Lightning: exploratory CPU pipeline comparison", x=.015, ha="left", fontsize=16, fontweight="bold")
    fig.text(.015, .018,
             "Blue: native Saif BF16 decoder / FP32 head, original published temperatures. Gray: six frozen historical pipelines.\n"
             "Bottom: conservative paired 95% Clopper-Pearson projections; pointwise, not multiplicity-adjusted.\n"
             "Fixed synthetic tasks only. English translations excluded here. Prompt, precision and CPU histories differ; no Unity or live-game claim.",
             fontsize=8.3, color="#475569")
    fig.tight_layout(rect=(0, .105, 1, .95), h_pad=2.5, w_pad=2.5)
    fig.savefig(prefix.with_suffix(".png"), dpi=180, bbox_inches="tight")
    fig.savefig(prefix.with_suffix(".svg"), bbox_inches="tight", metadata={"Date": None})
    plt.close(fig)


def _escape(value):
    return html.escape(str(value), quote=True)


def _table(headers, rows):
    return ('<div class="scroll"><table><thead><tr>' + "".join("<th>" + _escape(item) + "</th>" for item in headers)
            + "</tr></thead><tbody>" + "".join("<tr>" + "".join("<td>" + cell + "</td>" for cell in row) + "</tr>" for row in rows)
            + "</tbody></table></div>")


def _quality_table(statistics, language):
    rows = []
    for name, _ in groups(statistics, "memory", language):
        row = [_escape(LABELS[name])]
        for task in TASKS:
            value = dict(groups(statistics, task, language))[name]
            low, high = value["wilson_95"]
            row.append(f'<strong>{value["correct"]}/{value["n"]} ({value["accuracy"]:.1%})</strong>'
                       f'<small>Wilson 95%: {low:.1%}〜{high:.1%}</small>')
        rows.append(row)
    return _table(["実装", "記憶の関連判定", "提示された作戦の4択"], rows)


def _paired_table(statistics, language):
    model = statistics["models"][MODEL_KEY]
    rows = []
    for task in TASKS:
        for name in BASELINES:
            pair = model["tasks"][task][language]["paired_baselines"][name]
            low, high = pair["conservative_95"]
            rows.append([_escape(TASKS[task]), _escape(LABELS[name]), str(pair["candidate_only"]), str(pair["baseline_only"]),
                         f'{pair["difference_percentage_points"]:+.1f} pt', f'{100 * low:+.1f}〜{100 * high:+.1f} pt'])
    return _table(["課題", "Saifの比較相手", "Saifだけ正解", "相手だけ正解", "正解率の差", "対応95%区間"], rows)


def _diagnostic_tables(statistics):
    memory, commander = [], []
    for name, group in groups(statistics, "memory", "ja"):
        cells = group["memory_confusion"]
        memory.append([_escape(LABELS[name])] + [str(cells[key]) for key in
                      ("true_positive", "true_negative", "false_positive", "false_negative")]
                      + [str(cells["failed_expected_true"]), str(cells["failed_expected_false"])])
    for name, group in groups(statistics, "commander", "ja"):
        cells = group["commander"]
        commander.append([_escape(LABELS[name])] + [str(cells[key]) for key in
                         ("correct", "known_infeasible", "feasible_but_not_best", "failed_or_invalid")]
                         + [f'{cells["mean_normalized_ordinal_rank_loss"]:.3f}'])
    return (_table(["記憶判定 / JA500", "TP", "TN", "FP", "FN", "失敗・正例", "失敗・負例"], memory)
            + "<p>TPは関連のある新情報を採用、TNは不要情報を除外、FPは不要情報を採用、FNは必要な情報を見落とした件数です。"
            "実行失敗は正解率では誤答に含め、二値の混同行列とは別に残します。</p>"
            + _table(["作戦4択 / JA500", "正解", "既知の制約違反", "実行可能だが最適でない", "失敗・不正", "平均順位損失"], commander)
            + "<p>順位損失は与えられた4候補の順位を0〜1にした値で、低いほど良好です。ゲーム内の損害や勝率を表す数値ではありません。"
            "制約違反の候補を後処理で実行可能な候補へ置き換えていません。</p>")


def _ms(value):
    return "未観測" if value is None else f"{value:,.1f} ms"


def _latency_table(model):
    rows = []
    for field, label in (("latency", "全adapter・全試行"), ("native_only_latency", "native API・記録のある成功試行")):
        value = model[field]
        rows.append([label, str(value["n"]), _ms(value["mean_ms"]), _ms(value["p50_ms"]), _ms(value["p95_ms"])])
    return _table(["時間の範囲", "件数", "平均", "中央値", "95パーセンタイル"], rows)


def _session_table(model):
    rows = []
    for index, session in enumerate(model.get("sessions", [])):
        warmup = session.get("warmup", {})
        details = (warmup.get("raw") or {}).get("details", {})
        rows.append([str(index + 1), _escape(session.get("resume_from", "未記録")),
                     _ms(details.get("model_initialization_ms")), _ms(warmup.get("duration_ms")), _escape(warmup.get("status", "未記録"))])
    return _table(["セッション", "開始時の完了件数", "adapter初期化", "固定1回のwarmup", "warmup状態"], rows) if rows else ""


def _tokens_table(model):
    rows = []
    for field, label in (("input_tokens", "実際のnative入力token ID数"), ("output_tokens", "生成token数")):
        value = model["tokens"][field]
        format_value = lambda number: "未観測" if number is None else f"{number:,.1f}"
        rows.append([label, str(value["known_n"]), str(value["missing_n"]), str(value["total"]),
                     format_value(value["mean"]), format_value(value.get("p50")), format_value(value.get("p95"))])
    return _table(["token数の範囲", "既知件数", "不明件数", "合計", "平均", "中央値", "95パーセンタイル"], rows)


def _local_link(source, output_dir):
    return _escape(quote(os.path.relpath(Path(source).resolve(), output_dir.resolve()), safe="/"))


def _control_section(controls, output_dir):
    if controls is None:
        return "<h2>別枠の単純事実チェック</h2><p>このHTMLには補助8問の結果ファイルが指定されていません。主試験へは加算していません。</p>"
    rows = [[_escape(row["case_id"]), _escape(row["state"]), _escape(json.dumps(row["expected"], ensure_ascii=False)),
             _escape(json.dumps(row["predictions"], ensure_ascii=False)), "正解" if row["correct"] else "失敗" if row["status"] == "failed" else "誤答"]
            for row in controls["rows"]]
    return ("<h2>別枠の単純事実チェック</h2><p>以前公開されたランプの色4問・引き出しの開閉4問を、Saifの主試験結果を見る前に固定しました。"
            f'8つの既知の設問に対する正解は{controls["correct"]}件、実行失敗は{controls["failed"]}件です。'
            "主試験と別run・別warmupで各1回実行しました。この8問から一般的な精度や信頼区間・有意差を推定せず、主1,200試行にも加算しません。</p>"
            + _table(["固定設問ID", "観測された事実", "期待される回答", "Saif回答", "判定"], rows)
            + '<p><a href="' + _local_link(controls["source"]["path"], output_dir) + '">8問の全応答JSON</a></p>')


def _resource_section(resources):
    if resources is None:
        return "<p>このHTMLにはworkerのメモリ測定ファイルが指定されていません。</p>"
    data = resources["data"]
    return (_table(["メモリ指標", "観測値"], [
        ["1秒間隔サンプルの最大VmRSS", f'{data["max_vmrss_kib"] / (1024 * 1024):.3f} GiB'],
        ["同サンプルで確認したVmHWM", f'{data["max_vmhwm_kib"] / (1024 * 1024):.3f} GiB']])
        + "<p>workerプロセス全体の観測で、import・モデルロード・warmupも含みます。サンプル間の最後の増加を取り逃す可能性があります。"
        "mmapしたチェックポイント、tmpfs上の重み、cgroup全体の使用量・ページキャッシュとは測定範囲が異なります。</p>")


def _evidence_table(statistics, statistics_source, protocol_source, protocol, controls, resources, output_dir):
    items = [("本レポートの統計JSON", statistics_source), ("推論前に固定したプロトコル", protocol_source),
             ("1,200件の固定入力・正解", statistics["sources"]["cases"])]
    for name in ("checkpoint_manifest", "source_manifest", "sdk_manifest"):
        items.append((name, protocol["model"][name]))
    for name in ("analysis_entrypoint", "saif_entrypoint", "statistical_helpers", "case_level_csv", "caller_facts"):
        if name in statistics["sources"]:
            items.append((name, statistics["sources"][name]))
    for label, value in (("補助8問の全応答", controls), ("workerメモリ測定", resources)):
        if value is not None:
            items.append((label, value["source"]))
    rows = []
    for label, item in items:
        rows.append([_escape(label), '<a href="' + _local_link(item["path"], output_dir) + '">' + _escape(Path(item["path"]).name) + "</a>",
                     '<code>' + _escape(item["sha256"]) + "</code>", _escape(item["bytes"])])
    return _table(["保存物", "ファイル", "SHA-256", "bytes"], rows)


def write_html(statistics, statistics_source, protocol, protocol_source, prefix, controls=None, resources=None, csv_name="saif-model-outcomes.csv"):
    model = statistics["models"][MODEL_KEY]
    description = model["identity"]["run_identity"]["adapter_description"]
    runtime = model["runtime"]
    memory, commander = [model["tasks"][task]["ja"] for task in TASKS]
    output_dir = prefix.parent
    score_link = "https://huggingface.co/" + MODEL_ID + "/tree/" + REVISION
    pieces = ['<!doctype html><html lang="ja"><head><meta charset="utf-8">',
        '<meta name="viewport" content="width=device-width,initial-scale=1">',
        '<title>Saif Lightning CPU比較</title><style>',
        'body{font:16px/1.8 system-ui,sans-serif;color:#172033;background:#f8fafc;margin:0}main{max-width:1180px;margin:28px auto;padding:28px;background:#fff;border:1px solid #e2e8f0;border-radius:12px}h1{line-height:1.4}h2{margin-top:32px}table{border-collapse:collapse;width:100%;margin:18px 0}th,td{text-align:left;vertical-align:top;padding:10px;border-bottom:1px solid #e2e8f0}th{background:#f1f5f9}small{display:block;font-size:13px;color:#475569}img{max-width:100%;height:auto}a{color:#0369a1}code{font-size:12px;overflow-wrap:anywhere}.note{background:#eff6ff;border-left:4px solid #0284c7;padding:14px 18px}.scroll{overflow-x:auto}summary{cursor:pointer}details{margin:18px 0}@media(max-width:650px){main{margin:0;padding:16px;border-radius:0}th,td{padding:8px;font-size:13px}}',
        '</style></head><body><main><h1>Saif LightningのCPU比較</h1>',
        f'<p class="note"><strong>日本語の正解率は、記憶判定{memory["correct"]}/500 ({memory["accuracy"]:.1%})、作戦4択{commander["correct"]}/500 ({commander["accuracy"]:.1%})でした。</strong> '
        f'Saifの主1,200試行に含まれる実行・形式の失敗は{model["overall"]["failures"]}件です。'
        '既存6実装と同じ固定入力で比較した、探索的なパイプライン測定です。</p>',
        '<p>主評価は日本語の各500場面です。同じ世界に対応する英語訳各100件は副評価として分けています。'
        '日本語・英語や異なる課題を合計した1,200件は推論試行数であり、独立した世界数や合算CIに置き換えていません。</p>',
        '<p>固定した合成ルールに沿った記憶の関連判定と、提示済み4作戦の選択を測りました。'
        '実ゲームの勝率、自由な作戦生成、将来予測、Unity・Sentis内の動作や速度は測っていません。'
        'native prompt・量子化・回答方法・過去のCPU割り当てが異なり、モデル本体だけの効果として扱えません。</p>',
        '<h2>日本語の主評価</h2>', _quality_table(statistics, "ja"),
        '<img src="' + _escape(prefix.with_suffix(".png").name) + '" alt="日本語各500場面におけるSaifと既存6実装の正解率・Wilson95%区間、および同じ入力に対する正解率の対応差。数値は前後の表にも掲載。">',
        '<p>上段は各実装・各課題の95% Wilson区間、下段は同じ設問の正誤を対応させた区間です。'
        '区間はこの合成問題の生成方法・構成に条件づけた値です。複数モデルを同時に95%で覆う区間ではありません。</p>',
        '<h2>Saifと既存6実装の対応比較</h2>', _paired_table(statistics, "ja"),
        '<p>差はSaif − 比較相手です。対応区間は事前に固定した保守的なClopper–Pearson長方形の射影を使います。'
        'すべてpointwiseの探索的95%区間で、多重比較補正は行っていません。新たな確証的p値や同等性の結論は出さず、以前のHolm補正2課題の解析は変更していません。</p>',
        '<h2>誤りの内容</h2>', _diagnostic_tables(statistics),
        '<h2>英語の副評価</h2><p>各100件は日本語世界の対応訳です。日本語500件の主評価に合算していません。</p>',
        _quality_table(statistics, "en"), '<details><summary>英語各100件の対応比較</summary>', _paired_table(statistics, "en"), '</details>',
        '<h2>CPUの応答時間とメモリ</h2>', _latency_table(model),
        '<p>全adapter時間は公式native推論に加え、呼び出し後のtoken layout・token数・出力の検査を含みます。'
        'native API時間は公式<code>SaifEngine.__call__</code>のwall timeです。失敗時にnative時間を得られなければ、その時間を作り足さず件数を示します。'
        'import・モデルロード・固定warmupは両方の応答時間から除外しています。request間のKV prefix再利用はありません。'
        'request内部の公式shared-prefix処理は保持しています。</p>',
    ]
    pieces += [_tokens_table(model), _session_table(model), _resource_section(resources),
        '<p>CPU: ' + _escape(runtime.get("cpu_model", "未記録")) + '。batch 1、モデルを直列実行。intra-op '
        + _escape(description.get("torch_threads", "未記録")) + '、inter-op ' + _escape(description.get("torch_interop_threads", "未記録"))
        + '、affinity ' + _escape(runtime.get("affinity", "未記録")) + '。過去実装の時間との比較にはCPU配分と入力形式の違いが含まれます。</p>',
        '<h2>native入力・採点・実行条件</h2><ul>',
        '<li>公式の専用decision headを用いて、元のstate/questionsを1回のAPI呼び出しに渡しました。'
        '正解・監査結果・設問IDをpromptへ加えず、一般的なchat生成や1文字grammarへ置き換えていません。</li>',
        '<li>decoderはBF16、readout headはFP32、無量子化です。公開済み温度はchoice '
        f'{TEMPERATURES["choice"]:.6f}、noul {TEMPERATURES["noul"]:.6f}（score {TEMPERATURES["score"]:.6f}）を保持しました。'
        'この問題への温度fit・追加softmax・確率floor・再正規化は行っていません。score型は主試験の対象外です。</li>',
        '<li>関連判定はnative p(true) ≥ 0.5、4択は元の候補順のnative argmaxで採点しました。'
        '公開温度を通った確率はゲーム内の成功確率を意味しません。実行失敗・形式不正は誤答に残しています。</li>',
        '<li>公式singlepassのsegmented bidirectional attention、joint reading、read_twice=false、fast_path=trueを保持。'
        'CUDA用causal_conv1d / flash-linear-attentionが使えないCPUでは、PyTorch reference fallbackを使用しました。公式sourceは未変更です。</li>',
        '<li>Python ' + _escape(runtime.get("python")) + '、Torch ' + _escape(runtime.get("torch"))
        + '、Transformers ' + _escape(runtime.get("transformers")) + '。model cardのTorch 2.8に対し、このCPU測定は2.9.1です。'
        '作者の公表18msはRTX 5070 Tiでの値で、このCPU測定の値ではありません。</li></ul>',
        _control_section(controls, output_dir),
        '<h2>出典と保存物</h2><p>モデル作者・出典: <a href="' + score_link + '">GyrTechnology / saif-0.8b-lightning（固定revision）</a>。'
        'ライセンスは<a href="https://creativecommons.org/licenses/by-nc/4.0/">CC-BY-NC-4.0</a>です。ユーザーが明示した非商用検証として実施しました。'
        'モデル本体・公式source・公開温度は変更していません。CPU fallbackと周辺adapter・集計・表示をこの検証で使用しています。</p>',
        '<p><a href="' + _escape(quote(csv_name)) + '">ケース別CSV</a> · <a href="'
        + _escape(prefix.with_suffix(".svg").name) + '">図のSVG</a>。以下のSHA-256は入力ファイルの保存時点を示します。</p>',
        _evidence_table(statistics, statistics_source, protocol_source, protocol, controls, resources, output_dir),
        '</main></body></html>']
    prefix.with_suffix(".html").write_text("\n".join(pieces) + "\n", encoding="utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--statistics", type=Path, required=True)
    parser.add_argument("--output-prefix", type=Path, required=True)
    parser.add_argument("--protocol", type=Path, help="Defaults to the pinned descriptor path in statistics")
    parser.add_argument("--controls", type=Path, help="Separate completed 8-case shared-runner result.json")
    parser.add_argument("--resources", type=Path, help="Completed whole-worker resource-summary.json")
    parser.add_argument("--csv-name", default="saif-model-outcomes.csv")
    args = parser.parse_args()
    statistics = read_json(args.statistics)
    validate_statistics(statistics)
    protocol, protocol_source = verify_protocol(statistics, args.protocol)
    controls, resources = read_controls(args.controls, protocol), read_resources(args.resources)
    statistics_source = descriptor(args.statistics)
    targets = [args.output_prefix.with_suffix(suffix).resolve() for suffix in (".png", ".svg", ".html")]
    inputs = {Path(path).resolve() for path in (args.statistics, protocol_source["path"], args.controls, args.resources) if path is not None}
    if any(path in inputs for path in targets):
        raise ValueError("Output paths must not overwrite input evidence")
    args.output_prefix.parent.mkdir(parents=True, exist_ok=True)
    draw(statistics, args.output_prefix, statistics_source["sha256"])
    write_html(statistics, statistics_source, protocol, protocol_source, args.output_prefix, controls, resources, args.csv_name)
    print("Saved PNG, SVG and HTML to", args.output_prefix)


if __name__ == "__main__":
    main()
