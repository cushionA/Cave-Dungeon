---
name: create-feature
description: Create a new game feature using TDD workflow (test first, then implement, then record)
user-invocable: true
argument-hint: <FeatureName> [description]
model: sonnet
---

<!-- Wave 4 Phase 15 P15-T7 (Advisor Strategy): create-feature は Sonnet 固定。理由: TDD サイクル / Unity 規約適用 / feature-db 操作はパターンマッチ性が高い実行型タスク。Opus は overkill -->


# Create Feature: $ARGUMENTS

TDDワークフローで新しいゲーム機能を作成する。

## ステップ0: ブランチ作成 + 事前チェック

### ブランチ作成（標準動作）

**mainブランチから新しいfeatureブランチを切る。**

```bash
git checkout main
git pull
git checkout -b feature/<機能名>
```

- 機能名は引数から推定（日本語OK）
- 既にfeatureブランチにいる場合はユーザーに確認: 「現在 `feature/XXX` にいます。新しいブランチを切りますか？」
- ユーザーが明示的にブランチ不要と言った場合のみスキップ

### 将来タスクからの開始

引数が `docs/FUTURE_TASKS.md` の項目を指している場合:
1. `docs/FUTURE_TASKS.md` を読み、該当タスクの詳細（説明、対象ファイル、依存関係）を取得
2. タスクの情報を仕様整理のインプットとして活用
3. 実装完了後にステップ7でタスクにチェックを入れる

### 重複検出

**実装を始める前に、既存機能との重複を確認する。**

```bash
python tools/feature-db.py list
```

チェック項目:
- **同名・類似機能が存在するか**: 存在する場合はユーザーに報告
  - 「この機能は既存の XXX と重複しています。拡張で対応しますか？」
- **拡張で済むか**: 既存機能のテストファイル・実装ファイルを確認し、変更範囲を見積もる
- **新規作成が妥当か**: 完全に新しい場合のみ新規作成に進む

重複が見つかった場合:
- **拡張の場合**: 既存のテストファイルにテストケースを追加 → 既存の実装を拡張 → feature-dbは `update` で更新
- **新規の場合**: 通常のTDDフローへ進む

## ステップ1: 仕様確認

`instruction-formats/feature-spec.md` フォーマットに基づいて機能仕様を整理する。

設計書がある場合（`designs/systems/` 配下）:
- 該当システムの設計書から機能仕様を読み取る
- コンポーネント構成、インタフェース、データフローを確認

## ステップ2: テスト作成 (Red)

`.claude/rules/test-driven.md` に従いテストを先に作成する。

- `Tests/EditMode/{FeatureName}Tests.cs`（必須）
- `Tests/PlayMode/{FeatureName}PlayTests.cs`（ゲームプレイ機能の場合）

テスト命名: `[機能名]_[条件]_[期待結果]`

## ステップ3: Red確認

テストが全てFailすることを確認する。

テスト実行方法（優先順位）:
1. **MCP経由**: `run_tests` ツール（Unityエディタが起動中の場合）
2. **CLI**: Unity `-runTests -batchmode`（エディタが閉じている場合）

## ステップ4: 実装 (Green)

`.claude/rules/unity-conventions.md` に従い実装する。

- テンプレート確認: `template-registry.json`
- 必要アセットは `[PLACEHOLDER]` で仮配置（`.claude/rules/asset-workflow.md` 参照）
- 既存コードとの整合性を確認（アーキテクチャルールに従う）

## ステップ5: Green確認

テストが全てPassすることを確認する。

MCP経由でテスト実行した場合:
- `read_console` でエラーが出ていないかも確認する

## ステップ5.5: 結合テスト作成

`.claude/rules/test-driven.md` の「結合テスト（Cross-System Testing）」セクションに従い、
この機能に必要な結合テストを追加する。

**テスト設計チェックリスト**を確認し、該当する項目があれば結合テストを作成:
- `Tests/EditMode/Integration_{機能名}Tests.cs` に配置
- 既存ロジック呼び出し検証、状態シーケンス検証、境界値・不変条件検証の観点

結合テストが不要なケース（純粋なデータ構造体、他システムへの依存がない独立ロジック）はスキップ可。

## ステップ6: コードレビュー（自己チェック）

実装完了後、以下を自己チェックする:
- [ ] Unity規約に準拠しているか（命名、フォーマット、パフォーマンス）
- [ ] 既存コードとの重複はないか
- [ ] publicインタフェースは最小限か
- [ ] ScriptableObjectで設定値を外出ししているか
- [ ] マジックナンバーはないか
- [ ] 既存ユーティリティを正しく経由しているか（ロジック直書きでバイパスしていないか）
- [ ] イベント購読と解除が対称か（OnEnable/OnDisable、Subscribe/Dispose）
- [ ] リソース（Addressableハンドル、マテリアル等）の確保と解放が対になっているか

## ステップ7: 記録

feature-db に完了記録を追加する。

```bash
# 新規作成の場合
python tools/feature-db.py add "$0" --tests テストファイルパス --impl 実装ファイルパス
python tools/feature-db.py update "$0" --status complete --test-passed N --test-failed 0

# 拡張の場合
python tools/feature-db.py update "既存機能名" --status complete --test-passed N --test-failed 0
```

### FUTURE_TASKS.md 更新

将来タスクから開始した場合:
- `docs/FUTURE_TASKS.md` の該当タスクに `[x]` チェックを入れる
- 完了メモ（✅ で始まる1行）を追記する

## ステップ8: Gitコミット

テスト全Pass・feature-db記録後にコミット+プッシュ。

- ブランチ確認: ステップ0で作成したfeature/ブランチにいることを確認
- ステージング: テストファイルと実装ファイルをgit add
- コミット: `feat(scope): 機能名を実装` 形式で日本語コミットメッセージ
- プッシュ: `git push origin <branch>`
- Co-Authored-By: Claude Opus 4.6 <noreply@anthropic.com> を付与

## ステップ9: アセット要求

必要アセットがある場合は `asset-request.md` フォーマットでリストアップし、feature-dbにも登録する。

```bash
python tools/feature-db.py add-asset <id> "$0" <type> "<description>" --priority <high|medium|low>
```

## ルール
- テスト規約は `.claude/rules/test-driven.md`、コード規約は `.claude/rules/unity-conventions.md` に従う
- アセット管理は `.claude/rules/asset-workflow.md` に従う
- **既存機能の重複作成は絶対に避ける**。拡張で済む場合は拡張する
- 実装が大きくなりすぎた場合は分割を提案する
