# `docs/compound/` — Compound Engineering Learnings

実装・レビュー・運用で得られた**再利用可能な教訓**を YAML frontmatter 付き markdown として蓄積するディレクトリ。

Every 社 Kieran Klaassen の [Compound Engineering 4 ステップループ](https://github.com/EveryInc/compound-engineering-plugin)（Plan → Work → Review → **Compound**）の最終ステップの成果物置き場。

## 目的

- **50% 新機能開発 / 50% institutional knowledge 蓄積**の時間配分を実現
- コンテキストが**新鮮なうち**に compound することで、`/compact` や会話終了時に失われる具体情報を保存
- 将来の Claude セッションが参照することで、同じ失敗の繰り返し・再学習コストを削減

## ファイル命名規則

```
YYYY-MM-DD-<slug>.md
```

- `YYYY-MM-DD` — 学びが発生した日付
- `<slug>` — ハイフン区切りの短い要約（英語・ローマ字可）

例: `2026-04-24-pr44-pipeline-refactor.md`

## フォーマット（`_template.md` 参照）

```yaml
---
topic: <1 行要約>
date: YYYY-MM-DD
outcome: <パターン名 or 結論>
related_pr: <PR 番号 or リンク>
files_affected:
  - <path1>
  - <path2>
tags: [<tag1>, <tag2>]
---

## Context
（なぜこの学びが発生したか、状況の説明）

## Pattern
（再利用可能な抽出された教訓・原則）

## Examples
（具体的な実例、コード片や設定）

## Anti-patterns
（避けるべきこと、ハマったポイント）

## Related
（他の関連する compound エントリへのリンク）
```

## 運用

### 当面は手動運用（Phase 8 手動版）
- PR マージ直後 or 大きな学びを得た時点で手動で新規エントリを作成
- 月に 1-3 エントリを目安
- CLAUDE.md の「Compound 運用」節も参照

### 将来の自動化（Phase 24）
- Stop hook で session 終了時に自動抽出
- `/compound-learn` スキルが learning を YAML frontmatter 付きで起草
- 月次 review 会で CLAUDE.md / `Architect/` / FUTURE_TASKS.md への昇格を判定

## 昇格ルール（月次 review）

1. **複数エントリで同じパターンが出現** → `.claude/rules/` or `Architect/` 内のドキュメントに昇格
2. **パイプライン全体に関わる気づき** → CLAUDE.md or plan file に反映
3. **機能 TODO として残っている** → `docs/FUTURE_TASKS.md` に移動
4. **古くなった・無関係になった** → 削除 or アーカイブ

## 参考

- [Every Inc: Compound Engineering](https://github.com/EveryInc/compound-engineering-plugin)
- [plan file Phase 8 / Phase 24](../../../.claude/plans/humming-dancing-conway.md) — Compound 恒常化の設計
