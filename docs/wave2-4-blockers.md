# Wave 2-4 ブロッカー集約レポート

**作成日**: 2026-04-25
**対象**: Wave 2 / Wave 3 / Wave 4 で出した 8 PR の自律実装中に蓄積されたブロッカー・実時間タスク・ユーザー判断要素
**目的**: 全 PR マージ後に「次に何を / 誰が / いつ」着手すべきかを 1 ファイルに集約

## 1. PR 一覧（マージ済 + 出した PR）

| PR | Wave | Phase | 状態 |
|----|------|-------|------|
| #50 | 2 | Phase 10 (Two-layer skill) | ✅ マージ済 (10:21) |
| #51 | 2 | Phase 17 (Registry handoff) | ✅ マージ済 |
| #52 | 2 | Phase 11 P11-T7 (lint.md) | ✅ マージ済 |
| #53 | 3 | Phase 5 (skill lifecycle) | ✅ マージ済 |
| #54 | 3 | Phase 13 (TDD 3 agent) | ✅ マージ済 |
| #55 | 3 | Phase 22 (Unity hooks) | ✅ マージ済 |
| #56 | 4 | Phase 15 (cost / Advisor) | ⏳ レビュー待ち |
| #57 | 4 | Phase 12 (Adversarial Review) | ⏳ レビュー待ち |

## 2. ブロッカー区分

### 🟡 ユーザー判断必須（マージ後に着手判断）

| 項目 | 関連 Phase | 理由 | 推奨アクション |
|------|-----------|------|---------------|
| **TDD-Guard 本格導入** | Phase 6 | 外部リポジトリ依存 (`nizos/tdd-guard`) + 既存編集ワークフローの破壊的変更 | 1 週間試用 → 採否判断 |
| **`/model opusplan` デフォルト化** | Phase 15 P15-T5 | settings.json の model 設定を破壊的変更 | コスト測定 1 週間後に判断 |
| **環境変数 thinking 制限** | Phase 15 P15-T6 | `DISABLE_NON_ESSENTIAL_MODEL_CALLS=1` / `MAX_THINKING_TOKENS=8000`、副作用が一部 skill に影響する可能性 | P15-T5 の試用後 |
| **外部 skill 輸入 (Phase 22)** | Phase 22 P22-T5〜T7 | Unity App UI Plugin / TheOne Studio skills のライセンス + Architect/ 整合性確認 | 採否判断 |
| **Opus model 利用可否確認** | Phase 12 reviewer-skeptic | Anthropic plan 依存。利用不可なら Sonnet フォールバック | Phase 12 PR 試用時に判明 |
| **`lint phase: error` 昇格** | Phase 11 P11-T8 | warn → error の段階昇格、観察期間後 | 2026-05-02 以降の判定 |

### ⏳ 実時間が必要（経過後に消化）

| 項目 | 関連 Phase | 期間 | 完了予定 |
|------|-----------|------|---------|
| **lint hook 1 週間誤検知観察** | Phase 11 P11-T6 | 7 日 | 2026-05-02 |
| **handoff registry 1 週間運用試験** | Phase 17 P17-T9 | 7 日 | 2026-05-02 |
| **コスト計測 1 週間** | Phase 15 P15-T8 | 7 日 | 2026-05-02 |
| **TDD 3 分割で 1 機能実装試験** | Phase 13 P13-T7 | 2-3 時間 | PR #54 マージ後 |
| **Adversarial Review リトロスペクト試験** | Phase 12 P12-T7 | 2 時間 | PR #57 マージ後 |
| **半年運用後の skill 棚卸し** | Phase 5 P5-T4/T5 | 1 時間 | 2026-07-25 (3 ヶ月後) or 2026-10-25 |

### 🔧 技術的ブロッカー（仕組み整備が必要）

| 項目 | 関連 Phase | 必要なもの |
|------|-----------|-----------|
| **Claude Code `/cost` hook 仕様** | Phase 15 P15-T2 | 公式 hook の stdin 仕様確立、または ccusage 経由に切替 |
| **claude-mcp CLI 整備** | Phase 22 P22-T1 | unity-console-check.sh の MCP backend (現状 UniCli フォールバック動作) |
| **skill-usage.py deprecated 誤検知** | Phase 5 P5-T2 改善 | regex 厳格化 or `deprecated: true` frontmatter キー導入 |

### 🐛 既知の警告（無害だが将来対応）

| 項目 | 詳細 | 対応 |
|------|------|------|
| `MD-EMOJI-001` regex error | Python re で `[\u{1F300}...]` Unicode escape 不正 | catch 済、WARN として skip 継続 |
| Windows cp932 出力 | `tools/*.py` で UTF-8 文字が直接出せない | `sys.stdout/stderr` 再ラップで対応済 |

## 3. Wave 5 着手前のチェックリスト

Wave 5 (Phase 7 / 14 / 16 / 18 / 19 / 23 / 24) に進む前に以下を確認:

- [ ] PR #56 / #57 がマージされている
- [ ] Phase 11 P11-T6 観察結果に基づき lint phase=error 昇格 PR が出ている
- [ ] Phase 17 P17-T9 運用試験で handoff registry の効果が `docs/compound/` に記録されている
- [ ] Phase 15 P15-T8 コスト計測で Advisor Strategy の効果が定量化されている
- [ ] Phase 12 P12-T7 で実 PR にリトロスペクトレビューを実施し有効性確認

## 4. 自律実装で踏まなかった地雷

ユーザーへの透明性のため、本セッション中で**意図的に避けた領域**を記録:

1. **`.claude/settings.json` の model 設定変更**: Phase 15 P15-T5/T6 は破壊的変更なので雛形のみ
2. **外部リポジトリの clone**: Phase 6 の `nizos/tdd-guard`、Phase 22 の Unity App UI Plugin は実物を引き込まず、設計のみ
3. **`unity-mcp read_console` の本物呼び出し**: Unity Editor 起動環境がないため、UniCli フォールバックの設計のみ
4. **既存 PR への `/adversarial-review` 試行**: 自分が書いた skill を自己実行検証するのは circular なので避けた
5. **main 直 push**: 1 度試行して permission 拒否 → 全変更を feature ブランチ経由に統一

## 5. ユーザー学習用ハイライト

「私勉強したいので」というユーザー方針に対し、以下が学習素材として有用:

| ファイル | 学べること |
|---------|----------|
| `.claude/skills/_two-layer-design.md` | Two-layer skill の設計判断基準と適用判定表 |
| `.claude/skills/handoff-note/SKILL.md` ほか | Registry-based handoff の三本柱 (state / compound / reports) |
| `.claude/agents/tdd-test-writer/AGENT.md` ほか | TDD 3 サブエージェント分離の実装パターン |
| `.claude/skills/adversarial-review/SKILL.md` | スコア閾値による段階起動 + cross-model consensus |
| `tools/cost-report.py` | branch 軸を入れた集計ツールの設計 (ccusage との比較含む) |
| `docs/SKILL_LIFECYCLE.md` | 四半期棚卸しの運用ルーチン (Shrivu Shankar 「20+ skill アンチパターン」対応) |
| 全 handoff note 3 本 | 「セッション圧縮の代替」を実演した記録 |

## 6. 関連リソース

- `docs/WAVE_PLAN.md` — Wave 計画 source of truth (Wave 2/3/4 PR で削除なく蓄積)
- `docs/FUTURE_TASKS.md` — 各 Wave 残タスクセクション (PR ごとに統合済)
- `docs/reports/_registry.md` — 本レポート + handoff note の索引
- `docs/reports/handoffs/` — 3 本の handoff note (本セッション中の作業スナップショット)
- `docs/reports/analysis/2026-04-25_ccusage-evaluation.md` — ccusage 評価 (Phase 15 補助)

## 7. 次セッションでの推奨アクション

1. **PR #56 / #57 のレビュー & マージ判断** (Wave 4 完了)
2. **`/registry-check`** を実行して直近 handoff の確認
3. **Phase 11 P11-T6 観察ログ**を `lint-observation-log.md` に記入開始 (本セッション中の lint 出力を素材に)
4. **Wave 5 着手判断** — ブロッカー解消状況を見て Phase 7 (Effective Harnesses) から開始するか検討
5. 必要なら **`/adversarial-review`** を本 PR (#56 / #57) 自身に試行して有効性確認
