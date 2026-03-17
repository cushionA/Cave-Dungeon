# Feature Spec 指示フォーマット

このフォーマットを使用して、ゲーム機能の仕様をAIに指示する。
TDDワークフローと連携し、テストケースとアセット要件を含む。

## フォーマット

```
# Feature: [機能名]

## Overview
[機能の概要と目的を1-3文で記述]

## Behavior
- [振る舞い1: 条件 → 結果]
- [振る舞い2: 条件 → 結果]
- ...

## Components
- [コンポーネント名]: [責務の説明]
  - Dependencies: [依存コンポーネント]
  - Inspector Properties:
    - [プロパティ名] ([型]): [説明] (default: [値])

## Test Cases

### Edit Mode
- [テスト名]: [テスト内容]

### Play Mode (必要な場合)
- [テスト名]: [テスト内容]

## Required Assets
[asset-request.md フォーマットを使用]

## Dependencies
- [依存する他の機能]
- [必要なパッケージ]
```

## 使用例

```
# Feature: PlayerMovement

## Overview
プレイヤーキャラクターの水平移動とジャンプを実装する。
入力に応じて左右移動し、地面にいる時のみジャンプできる。

## Behavior
- 左右入力がある時 → キャラクターが入力方向に moveSpeed で移動する
- 左右入力がない時 → キャラクターが停止する
- ジャンプ入力 + 地面にいる時 → jumpForce で上方向に力を加える
- ジャンプ入力 + 空中にいる時 → 何もしない
- 空中にいる時 → 重力が適用される
- 移動方向に応じてスプライトが左右反転する

## Components
- PlayerMovement: 水平移動とジャンプの制御
  - Dependencies: Rigidbody2D, BoxCollider2D, GroundCheck
  - Inspector Properties:
    - moveSpeed (float): 移動速度 (default: 5.0)
    - jumpForce (float): ジャンプ力 (default: 10.0)
    - groundCheckOffset (Vector2): 接地判定の位置オフセット (default: 0, -0.5)
    - groundCheckRadius (float): 接地判定の半径 (default: 0.2)
    - groundLayer (LayerMask): 地面レイヤー

## Test Cases

### Edit Mode
- PlayerMovement_DefaultValues_ShouldHaveCorrectDefaults:
  moveSpeed=5.0, jumpForce=10.0 であることを確認
- PlayerMovement_MoveSpeed_ShouldNotBeNegative:
  負の値を設定した場合0にクランプされることを確認

### Play Mode
- PlayerMovement_WhenHorizontalInput_ShouldMoveInDirection:
  右入力時にX座標が増加することを確認
- PlayerMovement_WhenNoInput_ShouldNotMove:
  入力なし時にX座標が変化しないことを確認
- PlayerMovement_WhenJumpOnGround_ShouldApplyUpwardForce:
  地面でジャンプ時にY速度が正になることを確認
- PlayerMovement_WhenJumpInAir_ShouldNotJump:
  空中でジャンプ入力時にY速度が変化しないことを確認

## Required Assets
(asset-request.md フォーマットで別途記述)

## Dependencies
- Unity Input System パッケージ (com.unity.inputsystem)
- Physics2D
```
