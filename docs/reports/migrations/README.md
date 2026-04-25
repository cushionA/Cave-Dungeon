# Migrations — データ・コード・ツールの移行作業ログ

**用途**: 一過性の移行作業（Unity バージョン上げ、依存ライブラリ更新、データスキーマ変更等）の作業ログ。

## 入る内容例

- Unity 6000.x → 6001.x へのアップグレード作業
- Newtonsoft.Json から System.Text.Json への移行
- pipeline-state.json スキーマ変更

## ファイル形式

- ファイル名: `YYYY-MM-DD_<slug>.md`
- 移行完了後も「次回同様の移行をする時の参考」として残す
- 新規作成時は `_registry.md` の migrations/ セクション最上部に 1 行追加
