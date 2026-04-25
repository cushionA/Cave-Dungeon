# Postmortems — インシデント・障害の事後検証

**用途**: ビルド大破、テスト破壊、main 汚染、データロス等のインシデント事後検証。

## 入る内容例

- 本番デプロイ失敗の事後分析
- pre-commit hook で push が止まった時の原因究明
- Unity プロジェクトが開けなくなった時の復旧記録

## ファイル形式

- ファイル名: `YYYY-MM-DD_<slug>.md`
- フォーマット: タイムライン / 影響範囲 / 根本原因 / 再発防止策
- 再発防止策は `.claude/rules/` または `docs/FUTURE_TASKS.md` に展開
- 新規作成時は `_registry.md` の postmortems/ セクション最上部に 1 行追加
