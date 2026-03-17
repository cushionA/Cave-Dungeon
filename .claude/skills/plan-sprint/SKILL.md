---
name: plan-sprint
description: Break down a section's system designs into implementable features and register them in feature-db in dependency order.
user-invocable: true
argument-hint: <section-name or section-number>
---

# Plan Sprint: $ARGUMENTS

指定セクションのシステム設計書から実装可能な機能に分解し、依存順でfeature-dbに登録する。

## 前提
- `designs/game-design.md` が存在すること
- 対象セクションのシステム設計書が `designs/systems/` にあること（`/design-systems` で作成）
- `designs/dependency-graph.md` が存在すること
- `feature-log.db` が初期化済みであること

## 手順

### ステップ1: 対象確認

GDDから指定セクションに含まれるシステムを特定する。

### ステップ2: 既存機能の棚卸し（重複検出）

**新しい機能を登録する前に、既存機能との重複を必ずチェックする。**

```bash
python tools/feature-db.py list
python tools/feature-db.py summary
```

チェック項目:
- **完全重複**: 同じ機能が既に存在する → 登録しない
- **拡張で対応可能**: 既存機能に少し手を加えれば実現できる → 新規ではなく「拡張」として登録
- **依存先が既に完成済み**: テスト不要の依存関係 → 依存リストから除外

結果をユーザーに報告:
```
## 既存機能との照合結果
- InputSystem ✅ 既に完成済み → スキップ
- PlayerMovement_Dash → PlayerMovement を拡張して対応可能
- EnemyAI_Chase → 新規作成が必要
```

### ステップ3: 機能のカテゴリ分類

各機能を設計書の内容から以下の基準で分類する。

**システム系 (system)**:
- 入力処理、移動・物理、カメラ、衝突判定
- HP・ダメージ等のコアメカニクス
- UI基盤（HUD、メニューフレームワーク）
- データ管理（セーブ、フラグ、状態管理）
- オーディオ基盤、エフェクト基盤
- 判定基準: 他の機能から「使われる」側。ゲーム内容に依存しない汎用処理

**コンテンツ系 (content)**:
- ステージ構成、敵種・配置パターン
- アイテム・報酬設計、イベント・演出
- レベルデザイン固有のパラメータ調整
- 判定基準: システムを「使う」側。ゲーム内容に固有の具体的データ・設定

### ステップ4: 依存解決と実装順序決定

`dependency-graph.md` を参照し、実装順序を決定する。

```
実装順序の決定ルール:
- システム系機能をコンテンツ系機能より先に配置
- 各カテゴリ内では:
  - 依存なしの機能を先に
  - 同一システム内は優先度順
  - システム間依存がある場合は依存先を先に
```

### ステップ5: feature-db登録

各機能を依存順でDBに登録する。

```bash
python tools/feature-db.py add "SystemName_FeatureName" \
  --tests "Assets/Tests/EditMode/FeatureTests.cs" \
  --impl "Assets/Scripts/System/Feature.cs" \
  --category system \
  --section section-1 \
  --depends "DependencyFeature1" "DependencyFeature2"
```

### ステップ6: スプリント計画出力

`designs/sprints/[セクション名].md` に実装計画を記録する。

```markdown
# Sprint: [セクション名]

## 完了条件
[このセクションが完了した時に何が動くか]

## 既存機能の活用
| 既存機能 | 対応方法 | 備考 |
|----------|---------|------|
| [機能名] | スキップ/拡張 | [理由] |

## 実装順序 — システム系
| # | 機能名 | システム | カテゴリ | 依存 | 状態 |
|---|--------|---------|----------|------|------|
| 1 | InputSystem_Setup | Input | system | なし | pending |
| 2 | PlayerMovement_Horizontal | Player | system | Input | pending |

## 実装順序 — コンテンツ系
| # | 機能名 | システム | カテゴリ | 依存 | 状態 |
|---|--------|---------|----------|------|------|
| 3 | EnemyTypes_Slime | Enemy | content | PlayerMovement | pending |

## 動作確認手順
[全機能完了後にどうやってセクション完了を確認するか]
```

### ステップ7: GDD更新

`designs/game-design.md` のセクション状態を「計画済み」に更新する。

### ステップ8: ユーザー確認

実装順序をユーザーに提示し、以下を確認する:
- カテゴリ分類は正しいか
- 実装順序に問題はないか
- 既存機能の活用方針は妥当か
- 不要な機能はないか

## ルール
- **指定セクションの機能のみ**を登録する
- 1機能 = `/create-feature` で実装可能な粒度（テスト5個以内が目安）
- 大きすぎる機能は分割する
- 機能名は `[システム名]_[機能名]` フォーマット
- セクションの「完了条件」と「動作確認手順」を必ず定義する
- **既存機能の重複登録は絶対に避ける**

## 出力先
- `designs/sprints/[セクション名].md`（スプリント計画）
- `feature-log.db`（機能登録）
- `designs/game-design.md`（セクション状態更新）

## 次のステップ
スプリント計画の先頭から実装開始:
```
/create-feature [最初の機能名]
```
