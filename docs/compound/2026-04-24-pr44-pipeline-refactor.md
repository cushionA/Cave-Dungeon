---
topic: スキル統廃合と pipeline-state 外在化で metadata 予算と中断耐性を確保
date: 2026-04-24
outcome: Primitive/Composition skill 分離原則 + 外部 state file による中断耐性
related_pr: "#44 (refactor/pipeline-skill-cleanup)"
files_affected:
  - .claude/skills/*/SKILL.md
  - .claude/skills/README.md
  - designs/pipeline-state.json
  - CLAUDE.md
  - docs/FUTURE_TASKS.md
  - docs/ARCHIVED_TASKS.md
tags: [skill-management, refactor, context-rot, state-externalization]
---

## Context

3 ヶ月運用で SisterGame の Claude Code スキルが 40+ に膨れ上がり、スキル間の役割重複・FUTURE_TASKS.md の肥大化（25k トークン超）・pipeline-state.json の未実装が顕在化。
リサーチで以下が判明したのが契機:
- Skill メタデータ予算は ~16K 文字（≒42 skills）で一部不可視化（[MindStudio: Context Rot](https://www.mindstudio.ai/blog/context-rot-claude-code-skills-bloated-files)）
- Claude Code `--resume` の JSONL 破損報告が多数（[Issue #39667](https://github.com/anthropics/claude-code/issues/39667)）→ 自前 state file 方針が正解
- 「20+ slash command はアンチパターン」（Shrivu Shankar）

## Pattern

### スキル設計
- **Primitive / Composition 分離原則**: primitive skill（run-tests 等）は他 skill を呼ばない、composition skill（build-pipeline, consume-future-tasks, playtest）のみが他 skill を呼ぶ
- **直線的に連なるスキル群は統合**: design-systems → plan-sprint → create-feature のような順序固定フローは 1 スキルに集約して往復コストを削減
- **独自スキルと既存 CLI の重複は削除**: `/list-assets` のような薄いラッパは `python tools/feature-db.py assets` に代替させて廃止

### 状態管理
- **pipeline-state.json は外部 state file として独立管理**: Claude Code `--resume` の JSONL とは分離、feature-db を Source of Truth とし state はキャッシュ
- **各 phase 遷移でタイムスタンプと lastAction を記録**: 中断再開時に「どこまでやったか」を復元可能に

### タスク管理
- **FUTURE_TASKS.md にタグ体系（優先度 + 仕様確定度）を制定**: 🔴🟡🟢 + ✓⚠🔶 で consume-future-tasks が自動仕分け可能に
- **ARCHIVED_TASKS.md で完了 6 ヶ月超のアーカイブ**: 本体のスキャン負荷を削減
- **エントリテンプレート標準化**: 「背景 / 仕様 / 対象ファイル / 関連 PR」のネスト構造

## Examples

```yaml
# designs/pipeline-state.json（初期値）
{
  "phase": "idle",
  "currentSection": null,
  "pendingFeatures": [],
  "completedFeatures": [],
  "lastUpdated": null
}
```

```markdown
# FUTURE_TASKS.md エントリテンプレート
- [ ] 🟡✓ **タイトル** — 一行要約
  - **背景**: なぜ必要か
  - **仕様**: 何をどうするか
  - **対象ファイル**: `path/to/file.cs:line`
  - **関連PR**: PR #NN
```

## Anti-patterns

- スキルを増やしてから「予算が足りない」と後で統廃合する → **認知負荷が高く、参照も散らばるので早期に棚卸しルーチン化**
- `~/.claude/projects/` の JSONL resume に依存した pipeline-state 管理 → **破損時に復旧不能、自前 JSON state を source of truth にする**
- Skill description を英語テンプレのまま放置 → **他の英語 skill と区別がつかず発火精度が下がる、日本語 or 独自要約を書く**
- rules ファイルを分割しただけで CLAUDE.md から参照しない → **「必要なら読め」状態で死蔵、path-scoped CLAUDE.md や `paths:` frontmatter で明示ロード**

## Related

- plan file Phase 1-3（完了）/ Phase 5（スキル棚卸しルーチン化）/ Phase 7（pipeline-state 拡張）
- [Context Rot in Claude Code Skills](https://www.mindstudio.ai/blog/context-rot-claude-code-skills-bloated-files)
- [Claude Code session .jsonl corruption Issue #39667](https://github.com/anthropics/claude-code/issues/39667)
