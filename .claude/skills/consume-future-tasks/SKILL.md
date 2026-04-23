---
name: consume-future-tasks
description: docs/FUTURE_TASKS.md の未完タスクを仕分けし、batch消化可能なものを worktree で並列実装 → メインでテスト → 1バッチ1PR を発行する
user-invocable: true
argument-hint: [並列数]
---

# Consume Future Tasks: $ARGUMENTS

`docs/FUTURE_TASKS.md` の未完タスクを仕分け、batch 消化可能なものを並列で実装・PR化する。

## ステップ0: 引数解析 + 事前チェック

### 引数
- `$ARGUMENTS` に並列数 N が指定されていればそれを採用（1-5）
- 省略時はステップ4で自動決定

### 事前チェック
- 現在のブランチが `main` かつ clean であることを確認
  - NG なら: ユーザーに「main に戻って clean 状態にしますか?」と確認
- `docs/FUTURE_TASKS.md` の存在を確認
- `git fetch origin` + `git pull origin main` で最新化

## ステップ1: タスク抽出

`docs/FUTURE_TASKS.md` を読み、未完タスク (`- [ ]` で始まる行) を列挙する。

各タスクについて以下を収集:
- **タスク名** (行頭の太字部分)
- **対象ファイル** (リスト内の `対象:` 行から抽出)
- **説明・仕様** (ネストされた詳細)
- **優先度タグ** (🔴/🟡/🟢)

## ステップ2: 仕分け (修正量 × 影響度)

各タスクに 2軸でタグ付けする。推定は対象ファイルの行数・構造を見て判断。

### 修正量
| タグ | 目安 |
|------|------|
| S | ≤50行 / ファイル1-2個 |
| M | ≤200行 / ファイル3-5個 |
| L | 200行超 or ファイル6+個 |

### 影響度 (強い方を採用)
| タグ | 条件 |
|------|------|
| 低 | 単一クラス内の変更、private追加、定数外出し、テスト追加のみ |
| 中 | public シグネチャ変更あり・呼出元 2-3 箇所、同一 asmdef 内 |
| 高 | enum/struct 定義変更、SoA/GameManager/Events/CharacterInfo 等の共通ハブ、asmdef 跨ぎ、破壊的変更 |

### 振り分けマトリクス

| | 低 | 中 | 高 |
|---|---|---|---|
| S | **batch** | **batch** | 単独PR (スキップ) |
| M | **batch** | 単独PR (スキップ) | create-feature (スキップ) |
| L | 単独PR (スキップ) | create-feature (スキップ) | create-feature (スキップ) |

**スキップ対象**: このスキルでは処理しない。サマリに「要別対応」として列挙のみ。

**仕様が未確定・「要検討」扱いのタスクはサイズに関わらず全てスキップ**。

## ステップ3: ファイル単位で融合

batch 対象タスク同士を以下ルールでグループ化:

1. **同一ファイルを触るタスクは同じ batch に寄せる** (衝突回避の最優先ルール)
2. 同一セクション (FUTURE_TASKS.md の見出し) のタスクは同じ batch に寄せやすい
3. 1 batch あたり **5-15タスク / 総行数 ≤500行** を上限目安

グループ化後、各 batch に名前を付ける (例: `batch-combat-config`、`batch-ui-cleanup`)。

## ステップ4: 並列数決定

- ユーザー指定があればそれを使用
- 未指定時:
  - batch 数 ≤ 3 → batch 数と同じ
  - batch 数 4-5 → 3
  - batch 数 6+ → 3 (残りは次回実行送り、スキルを複数回起動する想定)
- 上限 5

## ステップ5: ユーザー承認

以下をテキスト表示し、実行前に承認を取る:

```
## Batch 消化プラン

### 対象 batch (並列 N=3)
- batch-combat-config (タスク3件, 対象ファイル: DamageReceiver.cs 他)
  - [ ] Guard 系フラグ仕様整理 (S × 中)
  - [ ] Flinch 解除後 armor 復元仕様 (S × 低)
  ...
- batch-ui-cleanup (...)
- batch-save-robustness (...)

### スキップ (別対応必要)
- Addressable 導入 (L × 高) → create-feature 推奨
- 装備→SoA書き戻しパイプライン (L × 高) → create-feature 推奨
...

### リトライ候補
なし

実行しますか? (y/n)
```

ユーザーが `y` 以外を返したら中止。

## ステップ6: 並列実装 (worktree)

各 batch に対して git worktree を作成し、Agent を並列起動する。

### worktree 作成
```bash
BRANCH_NAME="feature/future-tasks-batch-$(date +%Y%m%d)-${BATCH_NAME}"
git worktree add .claude/worktrees/consume-${BATCH_NAME} -b ${BRANCH_NAME} main
```

### Agent 起動 (並列)
各 worktree に対して、Agent ツール (subagent_type: `general-purpose`) を `isolation: "worktree"` なしで直接起動。**並列数 N 個を同一メッセージで発行**する。

各 Agent への指示 (prompt) に含めるもの:
- このスキルの目的 (FUTURE_TASKS.md の消化)
- そのbatchに含まれるタスクの一覧と詳細（FUTURE_TASKS.md から抽出した内容）
- 実行すべき手順:
  1. 各タスクについて TDD: テスト追加 → 実装 → テスト追加確認 (CLI テストは不要、コンパイル通過まで)
  2. コンパイル確認: `python tools/unicli.py compile` 相当または Bash で検証
  3. FUTURE_TASKS.md の該当タスクに `- [x]` チェック + `✅` 完了メモ追記
  4. 小さな論理単位でコミット (`feat(scope): 日本語タイトル` + `Co-Authored-By`)
  5. **worktree 上でブランチにコミットまで**。Unity テスト・PR 作成はしない
- 準拠すべき規約: `.claude/rules/unity-conventions.md`、`.claude/rules/test-driven.md`、`CLAUDE.md`
- worktree パスと対象ブランチ名
- 完了時に報告: 成功タスク一覧、失敗タスク一覧 + 失敗要因

### Agent 完了待ち
全 Agent が完了するまで待つ (Agent ツールは foreground で起動)。

## ステップ7: 順次テスト + PR 作成

メインworktree (プロジェクトルート) で batch ごとに順次処理する。

### 各 batch の処理フロー

```bash
# 1. main をクリーン状態に戻す
git checkout main

# 2. batch ブランチをチェックアウト
git checkout ${BRANCH_NAME}

# 3. Unity CLI で EditMode テスト実行 (-quit なし)
"C:\Program Files\Unity\Hub\Editor\6000.3.9f1\Editor\Unity.exe" \
  -batchmode -nographics \
  -projectPath "C:\Users\tatuk\Desktop\GameDev\SisterGame" \
  -runTests -testPlatform EditMode \
  -testResults "TestResults/batch-${BATCH_NAME}.xml" \
  -logFile "TestResults/batch-${BATCH_NAME}.log"
```

### テスト結果判定

**OK (全 Pass)**:
1. PlayMode テストの追加/変更がある場合、MCP `run_tests` で PlayMode も実行
2. PR 作成
   ```bash
   gh pr create --title "[種類](future-tasks): ${batch名} 消化 (Nタスク)" \
     --body "## 消化タスク\n- ...\n\n## Test plan\n- [x] EditMode テスト\n- [ ] 人間レビュー"
   ```
3. PR 番号を取得、`/review` スキル実行（または `gh pr review` で自己レビュー）
4. 指摘を反映してコミット追加、同ブランチに push

**NG (Fail あり)**:
1. ブランチはそのまま残す (ユーザーが後で調査可能)
2. `docs/FUTURE_TASKS.md` の該当タスクは未消化のまま
3. **特筆すべき失敗要因があれば** FUTURE_TASKS.md に追記
   - 例: `<!-- 2026-04-23 consume-future-tasks: XXX が XXX に依存するため先行対応必要 -->`
4. worktree は残置 (次回再実行の材料にする)、ブランチも削除しない

### ステップ7 実行中の原則
- batch 間は独立している前提なので、並列でテストはせず **逐次実行** (Library/ ロック回避)
- 先行 batch が成功しても main にマージせず、各 PR は main を base にする
  - マージはユーザー手動
- テスト失敗が連続する場合はユーザーに報告して停止判断

## ステップ8: worktree 後処理

- **成功 batch の worktree**: `git worktree remove .claude/worktrees/consume-${BATCH_NAME}` で削除 (ブランチは残る)
- **失敗 batch の worktree**: 残置

## ステップ9: サマリ表示

最終報告:

```
## Consume Future Tasks 完了

### PR 作成済み (N件)
- [batch-combat-config](PR URL) — 3 タスク消化
- [batch-ui-cleanup](PR URL) — 5 タスク消化

### テスト失敗 (N件)
- batch-save-robustness — EditMode テスト 2 件失敗
  - 原因メモ: SaveDataStore.ResolveType の型解決順序変更で既存テストが影響

### スキップ (別対応)
- Addressable 導入 (L × 高)
- 装備→SoA書き戻しパイプライン (L × 高)

### 次回へ
- 失敗 batch のブランチと worktree は残置。再実行時に拾うか、create-feature で個別対応してください
```

## ルール・注意点

- **Unity CLI テスト時 `-quit` は絶対に付けない** (メモリ: Unity CLIバッチテスト注意点)
- **失敗 batch のブランチ・worktree は削除しない** — 次回再実行の材料
- **FUTURE_TASKS.md の編集は各 Agent に委ねる** — 本スキルは orchestration 専任
- **PR の base は常に main** — batch 間は独立前提
- **マージは常にユーザー手動** — スキルは作成まで
- コミットメッセージ規約: `[種類](範囲): 日本語タイトル` + `Co-Authored-By: Claude Opus 4.6 <noreply@anthropic.com>` (CLAUDE.md 準拠)
- 大きめ・高影響度タスクは **create-feature** を案内してスキップ

## 依存スキル・ツール

- `create-feature` — スキップ対象のタスクを個別消化する際に案内
- `/review` — PR 自己レビュー
- `git worktree` — 並列 Agent の隔離環境
- Unity CLI — EditMode テスト実行
- MCP `run_tests` — PlayMode テスト実行
