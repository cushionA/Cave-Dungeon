# Claude Code Stop hook で /cost 相当データを取得する方法 (2026-04-25 調査)

**調査対象**: Wave 4 Phase 15 P15-T2 拡張（FUTURE_TASKS 既登録）
**調査結論**: 公式の `/cost` 出力 stdin 引き渡しは**未実装**。`transcript_path` 経由で transcript ファイル解析する方式が王道。

## 公式仕様の現状

- Stop hook の stdin には以下が渡される（公式 docs `code.claude.com/docs/en/hooks`）:
  - `session_id`
  - `transcript_path` ← セッションの transcript JSONL ファイルのパス
  - 他、event 固有の情報
- **token usage の直接渡しは未実装**
- Feature request: [Issue #52089](https://github.com/anthropics/claude-code/issues/52089) で「Stop hook stdin に `usage: { input_tokens, output_tokens, cache_read_tokens, cache_write_tokens }` を含めて欲しい」が議論中
- 2026-04 時点で merge されておらず、当面は **transcript_path 経由が唯一の方法**

## transcript_path 解析方式

### transcript JSONL 構造（推定）

各行は 1 イベント。`type` が `"assistant"` の場合に `message.usage` フィールドが含まれる:

```jsonc
{
  "type": "assistant",
  "message": {
    "id": "...",
    "model": "claude-opus-4-7",
    "usage": {
      "input_tokens": 12345,
      "output_tokens": 6789,
      "cache_read_input_tokens": 50000,
      "cache_creation_input_tokens": 1000
    }
  }
}
```

### 集計ロジック（Python 擬似コード）

```python
import json
from pathlib import Path

def aggregate_transcript(transcript_path: Path) -> dict:
    totals = {"input_tokens": 0, "output_tokens": 0, "cache_read": 0, "cache_creation": 0}
    model = None
    for line in transcript_path.read_text(encoding="utf-8").splitlines():
        try:
            entry = json.loads(line)
        except json.JSONDecodeError:
            continue
        if entry.get("type") != "assistant":
            continue
        usage = entry.get("message", {}).get("usage", {})
        totals["input_tokens"] += int(usage.get("input_tokens", 0))
        totals["output_tokens"] += int(usage.get("output_tokens", 0))
        totals["cache_read"] += int(usage.get("cache_read_input_tokens", 0))
        totals["cache_creation"] += int(usage.get("cache_creation_input_tokens", 0))
        model = entry.get("message", {}).get("model", model)
    return {**totals, "model": model}
```

### コスト推定

公式料金（2026-04 時点、`code.claude.com/docs/en/pricing` 想定）:
- Opus 4.7: input $15/MTok / output $75/MTok / cache_read $1.50/MTok / cache_creation $18.75/MTok
- Sonnet 4.6: input $3/MTok / output $15/MTok / cache_read $0.30/MTok / cache_creation $3.75/MTok
- Haiku 4.5: input $0.80/MTok / output $4/MTok / cache_read $0.08/MTok / cache_creation $1/MTok

```python
PRICING = {
    "claude-opus-4-7": (15.0, 75.0, 1.50, 18.75),
    "claude-sonnet-4-6": (3.0, 15.0, 0.30, 3.75),
    "claude-haiku-4-5": (0.80, 4.0, 0.08, 1.0),
}
def estimate_cost(usage: dict) -> float:
    p = PRICING.get(usage.get("model", "").lower())
    if not p: return 0.0
    return (usage["input_tokens"] * p[0] +
            usage["output_tokens"] * p[1] +
            usage["cache_read"] * p[2] +
            usage["cache_creation"] * p[3]) / 1_000_000
```

## 推奨実装

`stop-cost-log.sh` の改修方針:
1. stdin の JSON から `transcript_path` を抽出
2. Python で `aggregate_transcript()` を呼ぶ
3. 集計結果 + 推定コストを `.claude/cost-log.jsonl` に追記

## ccusage との比較

[ccusage](https://github.com/) が同様に transcript 解析を行っているが:
- Node.js 依存
- branch 軸の集計なし
- SisterGame の `cost-report.py` (P15-T1) は branch 軸を持つので**自前実装を継続**する判断は維持

## アクション

- [x] 調査結果を本ドキュメントに保存
- [ ] `stop-cost-log.sh` を transcript_path 解析方式に拡張（本 PR で実装）
- [ ] Issue #52089 の進捗監視（公式実装されたら本ドキュメント側を更新）

## 出典

- [Hooks reference - Claude Code Docs](https://code.claude.com/docs/en/hooks)
- [Issue #52089: Feature: expose session token usage to hooks](https://github.com/anthropics/claude-code/issues/52089)
- [Claude Code Hooks: Complete Guide to All 12 Lifecycle Events](https://claudefa.st/blog/tools/hooks/hooks-guide)
