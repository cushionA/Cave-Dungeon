# settings.json コスト最適化 変更案 (2026-04-25 スペック)

**目的**: Wave 4 Phase 15 P15-T5 / P15-T6 の settings 変更を「やる方向」で確定（2026-04-25 ユーザー確認）。**実反映タイミングはコスト測定 1 週間後**。
**前提**: Phase 15 PR #56 で `tools/cost-report.py` + Stop hook 設置済。本セッションで `stop-cost-log.sh` を transcript 解析方式に拡張。

## 変更案 1: `/model opusplan` デフォルト化（P15-T5）

### 対象ファイル

`~/.claude/settings.json`（**ユーザーグローバル設定**、プロジェクト共有 settings ではない）

### 変更前

```jsonc
{
  // model 未指定 → セッションごとにユーザー選択
}
```

### 変更後

```jsonc
{
  "model": "opusplan",
  // ※ "opusplan" は計画フェーズ Opus / 実装フェーズ Sonnet の自動切替モード
  //   (Anthropic 公式モデル選択戦略の 1 つ)
}
```

### 効果（推定）

- 計画 / 設計判断 → Opus、実装 / リファクタ → Sonnet で自動振り分け
- Advisor Strategy（design-systems = Opus / create-feature = Sonnet 固定）と整合
- 実測値は 1 週間運用後に `python tools/cost-report.py --period 7d` で比較

### リスクと対策

| リスク | 対策 |
|-------|------|
| 計画/実装の境界が曖昧で誤切替 | 1 週間試用、`docs/compound/` に observation を残す |
| Opus が利用不可 / 高コスト | Anthropic plan を再確認（2026-04-25 ユーザー確認: Opus 利用可） |
| 既存セッションのモデル設定が上書き | `~/.claude/settings.local.json` で個別 override 可能 |

## 変更案 2: 環境変数による thinking 制限（P15-T6）

### 対象ファイル

選択肢:
- **A. `~/.claude/settings.json` (env セクション)** — グローバル
- **B. `.claude/settings.local.json` (env)** — プロジェクト固有 + 開発者固有
- **C. `.claude/settings.json` (env、共有)** — プロジェクト全体に強制

**推奨**: **B**（開発者固有）。共有 settings に強制すると他開発者に影響するため。

### 変更内容

```jsonc
{
  "env": {
    "DISABLE_NON_ESSENTIAL_MODEL_CALLS": "1",
    "MAX_THINKING_TOKENS": "8000"
  }
}
```

### 各変数の効果

| 変数 | 効果 | 副作用 |
|------|------|-------|
| `DISABLE_NON_ESSENTIAL_MODEL_CALLS=1` | 補助的なモデル呼出（auto title 生成、メモ自動生成等）を抑止 | UI の自動補完が減る |
| `MAX_THINKING_TOKENS=8000` | thinking depth に上限を設定 | 一部 skill (design-systems の重い思考) が打ち切られる可能性 |

### リスクと対策

- **`MAX_THINKING_TOKENS=8000` は強気**: 設計フェーズで枯渇する可能性あり
  - **段階的に下げる**: 16000 → 12000 → 8000 で 2 週間ずつ試す案も検討
- **副作用検出**: `docs/compound/` に「8000 で打ち切られた skill」を記録、必要なら巻き戻し

## 実反映タイミング

### Phase A（**現在**）: 雛形配置のみ

- 本仕様書を `docs/reports/specs/` に保存（このファイル）
- `docs/FUTURE_TASKS.md` の P15-T5 / P15-T6 エントリを「ユーザー意思確定済 / コスト測定 1 週間後に反映」に更新
- settings.json は**触らない**

### Phase B（**2026-05-02 以降**）: コスト測定後に反映判断

- `python tools/cost-report.py --period 7d --output docs/reports/analysis/2026-05-02_cost.md`
- 測定結果を見て:
  - 月額予算超過リスクあり → 即反映
  - 余裕あり → MAX_THINKING_TOKENS を 12000 等に緩和
  - 削減効果不明 → 16000 で 2 週目試用

### Phase C: 反映実施

```bash
# /model opusplan
# (~/.claude/settings.json を update-config skill 経由で書き換え)

# 環境変数（プロジェクト固有）
# .claude/settings.local.json に env セクション追記
```

## 反映時の自動化（オプション）

ユーザーから「Phase B 完了、反映 OK」の合図を受けたら、Claude が `update-config` skill 経由で settings.json を書き換える。手順:

1. `python tools/cost-report.py --period 7d` の結果をユーザーに提示
2. ユーザー判断（GO / 見送り / パラメータ調整）
3. GO の場合、`update-config` skill で対象ファイル変更 + 動作確認

## ロールバック手順

設定が問題を起こした場合:

```bash
# 環境変数の即座ロールバック
unset DISABLE_NON_ESSENTIAL_MODEL_CALLS
unset MAX_THINKING_TOKENS

# settings 復元
git checkout HEAD -- .claude/settings.local.json   # プロジェクト固有
# または、~/.claude/settings.json をユーザー手動で戻す
```

## 関連

- `tools/cost-report.py` (PR #56) — 測定ツール
- `.claude/hooks/stop-cost-log.sh` (PR #56 + 本セッション拡張) — ログ追記
- `docs/reports/research/2026-04-25_claude-code-cost-hook.md` — transcript 解析方式の詳細
- WAVE_PLAN.md L808-820 (Phase 15 P15-T5/T6/T8)
- ユーザー意思確認: 2026-04-25「いいよ」（やる方向、タイミングは別途）
