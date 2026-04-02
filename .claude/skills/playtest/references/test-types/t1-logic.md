# T1: EditModeロジックテスト

## 概要
純粋ロジックをEditModeで高速に検証する。PlayMode不要のため最も実行コストが低い。

## 実行手段
- `/unicli`: `TestRunner.RunEditMode`
- フィルタ付き: `TestRunner.RunEditMode --filter "機能名"`

## 対象
- ダメージ計算（HpArmorLogic, DamageCalculator）
- AI条件評価（ConditionEvaluator）
- 入力変換（MovementInfo → Action）
- 状態遷移ロジック（ステートマシン判定）
- データ変換・シリアライゼーション
- 数学ユーティリティ

## テスト設計指針
- 1テスト = 1条件 × 1期待結果（`[機能名]_[条件]_[期待結果]`）
- 境界値テスト必須: 0, 負数, 最大値, null
- 既存ユーティリティ経由の検証: 「HPが減った」ではなく「HP >= 0 にクランプされる」
- 連続操作テスト: A→B→A実行後の状態が正しいか

## テストファイル配置
- `Tests/EditMode/[機能名]Tests.cs`
- 結合テスト: `Tests/EditMode/Integration_[テスト名]Tests.cs`

## 結果判定
- 全テストPass → feature-db: complete
- 1つでもFail → feature-db: in_progress
