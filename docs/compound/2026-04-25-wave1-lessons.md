---
topic: Wave 1 実装で得た実測値と動作確認の結論
date: 2026-04-25
outcome: CLAUDE.md トークン実測 4958 / path-scoped CLAUDE.md 机上検証 / 公式 skill ラッパー方針の確立
related_pr: "#45 (feature/wave1-rules-activation)"
files_affected:
  - CLAUDE.md
  - Assets/MyAsset/CLAUDE.md
  - Assets/Tests/CLAUDE.md
  - Assets/Scenes/CLAUDE.md
  - .claude/rules/README.md
  - .claude/rules/lint-patterns.json
  - .claude/rules/wave0-audit.md
  - .claude/rules/security-patterns.json
  - .claude/rules/security-known.md
  - .claude/skills/security-review-local/SKILL.md
  - tools/pr-validate.py
  - docs/compound/
tags: [token-measurement, path-scoped-claude-md, security-wrapper, wave-completion]
---

## Context

Wave 1（Phase 4 前半 + Phase 8 手動版 + Phase 21）実装中に「WBS と比べて本当に完了？」というユーザー確認があり、精査した結果 4 タスクが未完了だった。残タスクを片付けた際に得た実測値・設計判断・動作確認結論を記録する。

## Pattern

### トークン実測は tiktoken で行う（概算は 30-50% ズレる）

- `wc -c / 4` の概算値: **3,100 tokens**（実測との誤差 -37%）
- tiktoken `cl100k_base` 実測: **4,958 tokens**（CLAUDE.md 8,252 文字、13,852 UTF-8 バイト）
- Boris Cherny の 2.5k トークン基準との比率: **1.98 倍**
- **結論**: 概算はディレクショナルにしか使えない。最終判定は tiktoken 必須

測定スクリプト（再実行可能、再開時も同じコマンド）:

```python
import tiktoken
enc = tiktoken.get_encoding('cl100k_base')
content = open('CLAUDE.md', encoding='utf-8').read()
tokens = enc.encode(content)
print(f'tokens: {len(tokens)}, chars: {len(content)}, bytes: {len(content.encode("utf-8"))}')
```

### path-scoped CLAUDE.md は 2 系統併用で堅牢にする

SisterGame の `architecture.md` には既に `paths:` frontmatter（独自仕様）が設定されていたが、Claude Code 公式が同 frontmatter をどう扱うか不明。対策として:

1. **公式機能**: `Assets/<dir>/CLAUDE.md` を配置（公式仕様で確実にロードされる）
2. **独自仕様**: rules ファイルの `paths:` frontmatter（既存動作を壊さないため維持）

両方を併用することで、公式動作保証が不明でも安全に動く。`.claude/rules/README.md` にこの方針を明文化。

### path-scoped CLAUDE.md の配置判断表

| 配置先 | CLAUDE.md 配置 | 根拠 |
|-------|---------------|------|
| `Assets/MyAsset/` | 済 | ゲームコード、architecture/unity-conventions/asset-workflow 参照 |
| `Assets/Tests/` | 済 | テストコード、test-driven 参照 |
| `Assets/Scenes/` | 済 | シーン編集、.meta セット + Addressable グループ |
| `Assets/Resources/` | 非推奨（そもそも使わない） | asset-workflow.md は Addressable 優先 |
| `Assets/Plugins/`, `Assets/ThirdParty/`, `Assets/AnyPortrait/` 等 | **配置しない** | 外部ライブラリ、触らない領域 |

### 公式スキルのラップ設計（委任パターン）

Anthropic 公式 `/security-review` が存在する場合、**置き換えず委任する**のが安全:

- **ローカルラッパー**: `/security-review-local`
  - 前段: `tools/pr-validate.py` でプロジェクト固有 prompt injection / CC 攻撃を検査
  - fail-closed: block 検出時は公式 skill 起動前に停止
  - 通過時: 公式 `/security-review` を呼ぶ指示を出す
- 利点: 公式 skill の更新を追従しつつ、プロジェクト固有要件を前処理できる

### WBS 自己検証は必須

Wave / Phase 完了を宣言する前に、**WBS と PR 変更内容を突合する checklist**を走らせる:

```
各タスク ID について:
  - 成果物が存在するか
  - WBS の "成果物" 列と一致するか
  - (PR 含む場合) PR の diff にその変更が含まれるか
```

自分の「完了した」感覚は 30% ほど楽観的（Wave 1 で 19 タスク中 16 完了、16/19 = 84% を「完了」と宣言してしまった）。

## Examples

### トークン実測（再実行可能）

```bash
python -c "import tiktoken; enc = tiktoken.get_encoding('cl100k_base'); content = open(r'CLAUDE.md', encoding='utf-8').read(); print(f'tokens: {len(enc.encode(content))}')"
# => tokens: 4958
```

### path-scoped CLAUDE.md の `@` インポート記法

```markdown
# Assets/MyAsset/CLAUDE.md

@../../.claude/rules/architecture.md
@../../.claude/rules/unity-conventions.md
@../../.claude/rules/asset-workflow.md
```

- `@` で始まる行は Claude Code が該当ファイルをロード
- 相対パス、プロジェクトルートからの絶対パスどちらも可
- ロード失敗時は無視される（エラーにならない）

### 動作確認観点（次セッションで実施）

path-scoped CLAUDE.md が実運用でロードされるかは本セッションで確認できなかった。次回確認ポイント:

1. `Assets/MyAsset/Core/` で作業中、Claude が `@architecture.md` の内容を把握しているか（SoA / GameManager 等に言及するか）
2. `Assets/Tests/` で新規テスト作成時、結合テスト 3 観点に自発的に触れるか
3. `Assets/Scenes/` でシーン編集時、`.meta` セット確認を自発的に行うか
4. 逆に、Assets/Plugins/ で作業時はこれらが**ロードされないこと**

これらを実運用 3-5 セッションで観察し、効果測定を Wave 2 着手前に実施。

## Anti-patterns

- **概算トークンで 2.5k 目標判定**: 37% の誤差があり、「もう少しで目標」と誤認する。tiktoken 実測を必ず
- **Wave 完了を WBS 未照合で宣言**: PR タイトル/本文に「完了」と書いたが実は 84%。PR マージ後に遺留発見で嫌な思いをする
- **公式 skill を置き換える独自 skill**: 公式がアップデートしても追従できない、権限境界が曖昧に。委任パターンで済むケースは委任する
- **path-scoped CLAUDE.md の動作保証を確認せず大量配置**: ロードされない仕様だった場合に work が無駄。段階的配置＋実運用観察が安全

## Related

- plan file Phase 4（CLAUDE.md 剪定 + path-scoped）/ Phase 21（Comment and Control 防御）
- `docs/compound/2026-04-24-pr44-pipeline-refactor.md` — PR #44 の learning（前エントリ）
- `.claude/rules/wave0-audit.md` — Wave 0 の精読成果
- `.claude/rules/lint-patterns.json` / `.claude/rules/security-patterns.json` — Phase 11 / 21 の source of truth
