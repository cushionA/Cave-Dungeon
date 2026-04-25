# ccusage 導入評価メモ (Wave 4 Phase 15 P15-T4)

**作成日**: 2026-04-25
**評価対象**: [ccusage](https://github.com/) (Claude Code session cost aggregator)

## 評価軸

| 軸 | 自前 `tools/cost-report.py` | ccusage |
|----|-----------------------------|---------|
| ログ取得 | `.claude/cost-log.jsonl` (Stop hook で追記) | Claude Code transcript 直接解析 |
| 依存 | Python 標準ライブラリのみ | Node.js / npm |
| 集計粒度 | session / model / branch / day | session / model / project / time |
| 閾値アラート | `--threshold` で exit 1 | (未確認、要 README) |
| カスタマイズ | コード直接編集 | 設定ファイル / プラグイン |
| プロジェクト固有列 | branch を SisterGame 用に組込み | 汎用、branch 概念なし |

## 検討結果

### ✅ 自前実装を継続する理由

1. **branch 軸の集計が必要**: SisterGame は feature/wave* 単位でコストを見たい。ccusage は project 単位なので情報が足りない
2. **依存の簡素さ**: Python 標準ライブラリのみで動くので CI / 他開発者環境への展開が楽
3. **拡張容易性**: feature-db / pipeline-state.json と連携した分析を入れる予定（将来）。自前ツールなら統合が容易

### 🟡 ccusage を併用する場面

1. **transcript 直接解析が必要な時**: cost-log.jsonl が破損 / 欠落しているケースの再構築
2. **比較検証**: cost-report.py の集計値が正しいか cross-check したい時
3. **公式 Claude Code 機能との同期**: Anthropic 側で transcript フォーマットが変わった時の追従

### ❌ 導入見送り

ccusage を**メイン**に据えることは見送る。理由:

1. branch 軸のレポートが出ない（SisterGame の運用に合わない）
2. Node.js 依存が新規追加になる（プロジェクトが Python 中心）
3. ライフサイクル: Anthropic が公式の `/cost` 機能を拡張すると ccusage が陳腐化するリスク

## 結論

- **メイン**: `tools/cost-report.py`（本 PR で導入）
- **補助**: ccusage は `docs/FUTURE_TASKS.md` に「必要時の cross-check 手段として導入検討」として登録
- **将来**: Claude Code 公式の `/cost` フックが充実したら本ツールも段階的に統合する

## 関連

- `tools/cost-report.py` (P15-T1)
- `.claude/hooks/stop-cost-log.sh` (P15-T2)
- `.claude/skills/cost-report/SKILL.md` (P15-T3)
- WAVE_PLAN.md L815 (P15-T4)
