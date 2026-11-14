"""Render completed exploratory candidate estimates without changing any scores."""
from __future__ import annotations

import argparse
import hashlib
import html
import json
from pathlib import Path


MODEL_LABELS = {
    "uneeverse/unee-0.8b": "Unee 0.8B",
    "uneeverse/unee-2b": "Unee 2B",
    "LiquidAI/LFM2.5-1.2B-JP-202606": "LFM2.5 JP 1.2B",
    "LiquidAI/LFM3-1.7B": "LFM3 1.7B (fast)",
}


def _identity(node, key):
    identity = node.get("identity", {})
    model_id = identity.get("model_id", key)
    return model_id, MODEL_LABELS.get(model_id, key)


def plot_groups(statistics, baseline):
    groups = []
    for key, slot, label in (("hindsight", "first", "Hindsight 0.8B"),
                             ("tinyjev", "second", "TinyJev")):
        groups.append({"key": key, "label": label, "added": False, "tasks": {
            task: baseline["tasks"][task]["primary_ja"][slot]
            for task in ("memory", "commander")}})
    for key, node in statistics["models"].items():
        _, label = _identity(node, key)
        tasks = {task: node["tasks"][task]["ja"] for task in ("memory", "commander")}
        if any(value["n"] != 500 for value in tasks.values()):
            raise ValueError("The figure requires all 500 Japanese worlds per task")
        groups.append({"key": key, "label": label, "added": True, "tasks": tasks})
    if len(groups) != 6:
        raise ValueError("Exactly the two existing and four preselected added pipelines are required")
    return groups


def draw(groups, prefix, statistics_sha):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    from matplotlib.ticker import PercentFormatter

    plt.rcParams.update({"font.family": "DejaVu Sans", "font.size": 10,
                         "svg.hashsalt": statistics_sha, "axes.spines.top": False,
                         "axes.spines.right": False})
    fig, axes = plt.subplots(1, 2, figsize=(12, 5.5), sharey=True)
    colors = ["#64748b", "#64748b", "#2563eb", "#2563eb", "#2563eb", "#2563eb"]
    for ax, task, title, reference in zip(
            axes, ("memory", "commander"), ("Memory relevance", "Given-plan selection"), (.5, .25)):
        for index, (group, color) in enumerate(zip(groups, colors)):
            result = group["tasks"][task]
            score = result["accuracy"]
            low, high = result["wilson_95"]
            if not 0 <= low <= score <= high <= 1:
                raise ValueError("Invalid confidence interval")
            ax.errorbar(score, index, xerr=[[score - low], [high - score]],
                        fmt="o" if group["added"] else "s", color=color,
                        capsize=4, markersize=6, elinewidth=1.8)
            ax.annotate(f"{100 * score:.1f}%", (high, index), xytext=(7, 0),
                        textcoords="offset points", va="center", color=color, fontsize=9)
        ax.axvline(reference, color="#94a3b8", linestyle=":", linewidth=1.3)
        ax.set_title(title + "\nJapanese; 500 worlds", loc="left", fontweight="bold")
        ax.set_xlim(0, 1)
        ax.xaxis.set_major_formatter(PercentFormatter(1))
        ax.set_xlabel("Exact accuracy with 95% Wilson interval")
        ax.grid(axis="x", alpha=.18)
        ax.set_axisbelow(True)
        ax.set_yticks(range(len(groups)), [group["label"] for group in groups])
    axes[0].invert_yaxis()
    fig.suptitle("Local decision candidates: exploratory comparison", x=.02, ha="left",
                 fontsize=16, fontweight="bold")
    fig.text(.02, .025,
             "Blue: added Q4_K_M pipelines. Gray: previous native BF16/FP32 pipelines. "
             "Dotted: constant-choice reference.\n"
             "Intervals condition on this synthetic generator/family mix; "
             "not live-game performance or simultaneous model-comparison intervals.",
             fontsize=8.5, color="#475569")
    fig.tight_layout(rect=(0, .11, 1, .92))
    prefix.parent.mkdir(parents=True, exist_ok=True)
    fig.savefig(prefix.with_suffix(".png"), dpi=180, bbox_inches="tight")
    fig.savefig(prefix.with_suffix(".svg"), bbox_inches="tight", metadata={"Date": None})
    plt.close(fig)


def write_html(statistics, groups, path, image_name, statistics_name, csv_name, controls=None, controls_name=None):
    escape = html.escape
    models = list(statistics["models"].values())
    best_memory = max(node["tasks"]["memory"]["ja"]["accuracy"] for node in models)
    best_commander = max(node["tasks"]["commander"]["ja"]["accuracy"] for node in models)
    violations = [node["tasks"]["commander"]["ja"]["commander"]["known_infeasible_rate"] for node in models]
    failures = sum(node["overall"]["failures"] for node in models)
    positive_interval = any(
        value["conservative_95"][0] > 0
        for node in models for task in ("memory", "commander")
        for value in node["tasks"][task]["ja"]["paired_baselines"].values())
    headline = ("探索的な対応区間に改善を示す比較があります。" if positive_interval else
                "今回の高速設定では、追加候補に既存2実装を明確に上回る結果は得られませんでした。")
    outcome_summary = ("<p class=\"note\"><strong>" + headline + "</strong>追加4候補の日本語正解率は、"
               f"記憶判定が最大{best_memory:.1%}、作戦選択が最大{best_commander:.1%}。"
               f"明示された制約に違反する作戦の選択は{min(violations):.1%}〜{max(violations):.1%}でした。"
               f"本測定4,800件の実行失敗は{failures}件です。</p>")
    rows = []
    for group in groups:
        cells = ["<td>" + escape(group["label"]) + "</td>"]
        for task in ("memory", "commander"):
            value = group["tasks"][task]
            lo, hi = value["wilson_95"]
            cells.append(f'<td><strong>{value["accuracy"]:.1%}</strong>'
                         f'<small>95%区間 {lo:.1%}〜{hi:.1%}</small></td>')
        cells.append("<td>" + ("追加・Q4_K_M" if group["added"] else "前回・native") + "</td>")
        rows.append("<tr>" + "".join(cells) + "</tr>")
    latency_rows = []
    for key, node in statistics["models"].items():
        _, label = _identity(node, key)
        latency = node.get("latency", {})
        p50, p95 = latency.get("p50_ms"), latency.get("p95_ms")
        text50 = "未記載" if p50 is None else f"{p50 / 1000:.2f}秒"
        text95 = "未記載" if p95 is None else f"{p95 / 1000:.2f}秒"
        latency_rows.append(f"<tr><td>{escape(label)}</td><td>{text50}</td><td>{text95}</td></tr>")
    control_section = ""
    if controls is not None:
        if (controls.get("status") != "complete" or controls.get("post_hoc") is not True or
                controls.get("completed_calls") != 32 or set(controls["models"]) != set(statistics["models"])):
            raise ValueError("All 32 separate, post-hoc controls must be complete")
        control_rows = []
        for key, node in controls["models"].items():
            _, label = _identity(statistics["models"][key], key)
            summary = node["summary"]
            if node["status"] != "complete" or summary["completed_controls"] != 8:
                raise ValueError("Each control pipeline needs all eight observations")
            control_rows.append(f"<tr><td>{escape(label)}</td><td>{summary['correct']}/8</td>"
                                f"<td>{summary['colors']['correct']}/4</td>"
                                f"<td>{summary['booleans']['correct']}/4</td>"
                                f"<td>{summary['failed']}</td></tr>")
        control_section = (
            "<h2>単純な事実確認の補助診断</h2><p>日本語版LFMが主試験の記憶を全件「関連あり」と答えた後、"
            "色4問と引き出しの開閉4問を固定して、4候補で各1回ずつ確認しました。"
            "この8問は事後に選んだ対照です。主試験へ加算せず、信頼区間・有意差・一般的な精度は推定しません。</p>"
            "<table><thead><tr><th>実装</th><th>正解</th><th>色</th><th>開閉の二択</th><th>実行失敗</th>"
            "</tr></thead><tbody>" + "".join(control_rows) + "</tbody></table><p>"
            '<a href="' + escape(controls_name, quote=True) + '">補助診断の全応答JSON</a> · '
            '<a href="additional-controls-protocol.json">補助診断の固定条件</a></p>')
    page = """<!doctype html><html lang="ja"><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>追加小型モデルの探索的比較</title>
<style>
body{font:16px/1.8 system-ui,sans-serif;color:#172033;background:#f8fafc;margin:0}
main{max-width:1040px;margin:32px auto;padding:28px;background:white;border:1px solid #e2e8f0;border-radius:12px}
h1{line-height:1.4}h2{margin-top:32px}table{border-collapse:collapse;width:100%;margin:20px 0}
th,td{text-align:left;vertical-align:top;padding:12px;border-bottom:1px solid #e2e8f0}th{background:#f1f5f9}
small{display:block;font-size:13px;color:#475569}img{max-width:100%;height:auto}a{color:#1d4ed8}
.note{background:#eff6ff;border-left:4px solid #3b82f6;padding:14px 18px}.scroll{overflow-x:auto}
@media(max-width:600px){main{margin:0;padding:18px;border-radius:0}th,td{padding:9px;font-size:14px}}
</style><main><h1>追加小型モデルの探索的比較</h1>__SUMMARY__
<p>4候補を事前に固定し、それぞれ同じ1,200入力を評価しました。主評価は日本語の記憶判定500場面と
作戦選択500場面、英語の対応訳各100件は補助評価です。翻訳やモデル間の繰り返しを独立した場面数へ加えていません。</p>
<p class="note">これは固定した合成問題と各実装設定での比較です。実戦勝率、自由な作戦生成、
新しい問題構造への一般化、Unity・Sentis内の動作や速度は測っていません。
前回の確証的なHolm補正2課題の解析は変更していません。</p>
<h2>日本語の主評価</h2><div class="scroll"><table><thead><tr><th>モデル・実装</th>
<th>記憶判定 / 500</th><th>作戦4択 / 500</th><th>実行条件</th></tr></thead><tbody>__ROWS__</tbody></table></div>
<img src="__IMAGE__" alt="日本語各500場面の正解率と95% Wilson区間。追加4実装と前回2実装を比較。">
<p>区間は固定した生成方法・分野構成に条件づけた、課題・実装ごとの区間です。
複数モデルの比較を同時に95%で覆う区間ではありません。native prompt、量子化、
回答方法が異なるため、モデル本体やパラメータ数だけの効果とは解釈しません。</p>
<h2>CPUの応答時間</h2><table><thead><tr><th>追加候補</th><th>中央値</th><th>95パーセンタイル</th>
</tr></thead><tbody>__LATENCY__</tbody></table>
<p>全1,200試行（日本語・英語、成功・失敗を含む）のHTTP・前後処理を含む所要時間です。
同じLinux CPU環境、推論4スレッド、1モデルずつ直列実行、入力キャッシュ再利用なし。
重みの初回ロードと固定warmupは別記録です。入力長とnative promptは実装ごとに違います。
前回の並列CPU測定値との速度比較には使いません。</p>
<h2>設定と採点</h2><ul>
<li>Uneeは公式のcompact prompt・parser・date factsを使用。生成温度0、確率の補正温度1。
公式どおりtop-20 logprobに含まれない候補へfloorを置くため、その発生件数も集計しています。</li>
<li>LFMはnative chat templateと1文字の形式制約を使う低遅延向け設定です。
LFM3の思考を有効にした能力は評価していません。native parserで空のthinking wrapperを扱い、
クライアント側で回答を書き直す処理はありません。</li>
<li>正解・監査情報はモデル入力へ渡さず、制約違反の選択肢も後処理で修正しません。
失敗や形式不正は誤答に残します。Uneeの例外時にはraw応答が残らない場合があります。</li>
<li>LFM3は実行環境の準備切替後、保存済み1,050件を保持して未完了150件だけ再開しました。
モデル・入力・source・CPU設定を再照合し、完了回答は再実行していません。
応答時間は保存された各試行の時間です。LFM3の最初のprocess RSS記録は失われているため、全実行のRSS最大値は示しません。</li>
</ul>__CONTROLS__<h2>保存物</h2><p>
<a href="__STATS__">統計値・制約違反・対応差・実行来歴JSON</a> ·
<a href="__CSV__">ケース別CSV</a> ·
<a href="additional-candidates-protocol.json">推論前の固定プロトコル</a> ·
<a href="additional-analysis-notes.json">集計・再開の補足記録</a></p>
<p>公式資料：<a href="https://huggingface.co/uneeverse/unee-0.8b">Unee 0.8B</a> ·
<a href="https://huggingface.co/uneeverse/unee-2b">Unee 2B</a> ·
<a href="https://huggingface.co/LiquidAI/LFM2.5-1.2B-JP-202606">LFM2.5日本語版</a> ·
<a href="https://huggingface.co/LiquidAI/LFM3-1.7B">LFM3 1.7B</a>。
UneeはApache 2.0、LFMはLFM Open License 1.0です。</p></main></html>"""
    for key, value in {"__SUMMARY__": outcome_summary, "__ROWS__": "".join(rows), "__LATENCY__": "".join(latency_rows), "__CONTROLS__": control_section,
                       "__IMAGE__": escape(image_name, quote=True),
                       "__STATS__": escape(statistics_name, quote=True),
                       "__CSV__": escape(csv_name, quote=True)}.items():
        page = page.replace(key, value)
    path.write_text(page + "\n", encoding="utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--statistics", type=Path, required=True)
    parser.add_argument("--baseline-statistics", type=Path, required=True)
    parser.add_argument("--output-prefix", type=Path, required=True)
    parser.add_argument("--csv-name", default="additional-model-outcomes.csv")
    parser.add_argument("--controls", type=Path)
    args = parser.parse_args()
    raw = args.statistics.read_bytes()
    statistics = json.loads(raw)
    baseline = json.loads(args.baseline_statistics.read_text())
    groups = plot_groups(statistics, baseline)
    draw(groups, args.output_prefix, hashlib.sha256(raw).hexdigest())
    write_html(statistics, groups, args.output_prefix.with_suffix(".html"),
               args.output_prefix.with_suffix(".png").name,
               args.statistics.name, args.csv_name,
               json.loads(args.controls.read_text()) if args.controls else None,
               args.controls.name if args.controls else None)
    print("Saved PNG, SVG and HTML to", args.output_prefix)


if __name__ == "__main__":
    main()
