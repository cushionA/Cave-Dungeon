# Handoffs — セッション間引き継ぎノート

**目的**: 1 セッションで終わらない作業を次セッションに引き継ぐためのノート。
Anthropic 公式の effective context 戦略に準拠し、context rot を防ぐ。

## ファイル形式

- ファイル名: `YYYY-MM-DD_<slug>.md`
- 自動生成: `/handoff-note` skill（セッション終了時 or 中断時）
- 自動読込: `/resume-handoff` skill（次セッション開始時）

## エントリ構造

```yaml
---
date: 2026-04-25
session_topic: <短いタイトル>
status: in-progress | paused | needs-review | resolved
branch: feature/xxx
related_pr: 50  # optional
---

## 現在地
（git diff main の要約 / 完了したタスク / 残タスク）

## 次セッションでやること
（具体的な手順）

## 注意点・ブロッカー
（実装中に気付いた制約、ハマり所、外部依存）

## 関連ファイル
- path/to/file.cs:123
```

## 運用ルール

- **毎セッション最後に作成**（dream skill の 24h 自動メモリ統合とは別軸）
- **新規 entry は `_registry.md` の handoffs/ セクション最上部にも 1 行追加**
- 解決した entry（status: resolved）は 1 ヶ月後に削除候補
