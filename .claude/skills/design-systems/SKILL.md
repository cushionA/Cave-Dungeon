---
name: design-systems
description: Design systems for a specific section of the GDD. Creates system architecture, interfaces, and component structure.
user-invocable: true
argument-hint: <section-name or section-number>
---

# Design Systems: $ARGUMENTS

GDDの指定セクションに含まれるシステムの設計書を作成する。

## 前提
- `designs/game-design.md` が存在すること（`/design-game` で作成）

## 手順

### ステップ1: GDD読込と対象確認

`designs/game-design.md` から指定セクションのシステム一覧を取得し、設計対象を確認する。

### ステップ2: 既存機能との照合

feature-dbに既存機能がある場合、重複を避ける。

```bash
python tools/feature-db.py list
python tools/feature-db.py summary
```

- 既存機能で流用・拡張できるものがあれば、新規作成ではなく拡張として設計する
- 完全に新しいシステムのみ新規設計する
- 照合結果をユーザーに報告: 「XXXは既存のYYYを拡張して対応できます」

### ステップ3: 共通設計の抽出

セクション内の複数システムにまたがる共通パターンを抽出する。

確認ポイント:
- **共通インターフェース**: 複数システムが実装すべき共通契約（IDamageable, IInteractable等）
- **共通基盤クラス**: 似た処理パターンの抽象化（EntityBase, StateMachineBase等）
- **共有データ**: 複数システムが参照するScriptableObject
- **イベント定義**: システム間通信のイベント一覧
- **定数・Enum**: 共通で使う列挙型や定数

→ 共通要素は `designs/systems/Common_[セクション名].md` に記載

### ステップ4: asmdef設計

プロジェクトのアセンブリ構成を設計・更新する。

```markdown
## Assembly Definitions

| asmdef | 含むスクリプト | 参照先asmdef | 用途 |
|--------|-------------|-------------|------|
| Game.Runtime | Assets/Scripts/ | Unity.InputSystem | ランタイムコード |
| Game.Editor | Assets/Editor/ | Game.Runtime | Editor拡張 |
| Game.Tests.EditMode | Assets/Tests/EditMode/ | Game.Runtime | EditModeテスト |
| Game.Tests.PlayMode | Assets/Tests/PlayMode/ | Game.Runtime | PlayModeテスト |
```

- 新しいシステムが既存asmdefに収まるか確認
- 大規模システムは独立asmdefを検討（循環参照を避ける）
- テスト用asmdefの参照設定を確認

### ステップ5: システム設計書作成

各システムについて `designs/systems/[システム名].md` を作成する。

```markdown
# System: [システム名]
Section: [所属セクション名]

## 責務
[このシステムが何をするか、1-2文]

## 依存
- 入力: [このシステムが必要とするもの]
- 出力: [このシステムが提供するもの]

## コンポーネント構成
| コンポーネント | 責務 | MonoBehaviour? |
|--------------|------|---------------|
| [名前] | [役割] | Yes/No |

## インタフェース
[他システムとの接続点。イベント、publicメソッド、ScriptableObject等]

## データフロー
[入力 → 処理 → 出力 の流れ]

## 機能分解
| 機能名 | 説明 | テスト種別 | 優先度 |
|--------|------|-----------|--------|
| [名前] | [何をするか] | EditMode/PlayMode | High/Medium/Low |

## 設計メモ
[判断の理由、代替案、注意点]
```

### ステップ6: 依存グラフ更新

`designs/dependency-graph.md` にこのセクションのシステム依存関係を追記する。

```markdown
# System Dependency Graph

## Section 1: [セクション名]
### 実装順序
1. [依存なし] InputSystem
2. [依存なし] EventBus
3. [← InputSystem] PlayerMovement
4. [← PlayerMovement, EventBus] CombatSystem

### システム間通信
| 発信 | → | 受信 | 方式 | 内容 |
|------|---|------|------|------|
| CombatSystem | → | UISystem | C# event | ダメージ表示 |
```

### ステップ7: アセット仕様の拡充

システム設計で具体値が確定したら `designs/asset-spec.json` を更新する。

- PlayerMovementシステム設計時 → `world.maxJumpHeight`, `world.maxJumpWidth` を設定
- スプライトカテゴリが明確になったら `sprites.categories` に追加
- まだ不明な項目はnullのまま残す

### ステップ8: アーキテクチャルール更新

設計から導出されるプロジェクト固有ルールを `.claude/rules/architecture.md` に追記する。

### ステップ9: GDD更新

`designs/game-design.md` のセクション状態を「設計済み」に更新する。

## ルール
- **指定セクションのシステムのみ**を設計する。他セクションには触れない
- 1システム = 1設計書
- システム間の直接参照は禁止。イベントまたはインタフェース経由
- 前セクションで作成済みのシステムは依存先として参照可能
- 各システムの「機能分解」は `/plan-sprint` の入力になる
- ScriptableObjectをデータ共有・設定値管理に活用する
- **既存機能の重複作成に注意**。拡張で済む場合は新規作成しない

## 出力先
- `designs/systems/[システム名].md`（システム別）
- `designs/systems/Common_[セクション名].md`（共通設計）
- `designs/dependency-graph.md`（追記）
- `.claude/rules/architecture.md`（追記）
- `designs/game-design.md`（セクション状態更新）
- `designs/asset-spec.json`（更新）

## 次のステップ
設計したセクションの実装計画を立てる:
```
/plan-sprint [セクション名]
```
