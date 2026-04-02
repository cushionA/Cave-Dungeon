# T2: AutoInput動作テスト

## 概要
AutoInputTesterコンポーネントがMovementInfoを送信し、PlayModeで入力→動作の統合フローを検証する。

## 実行手段
- テスト設定: `/unicli` Menu.Execute で CLIInternal コマンド実行（EditorModeで）
  - `Tools/CLIInternal/Run Auto Input All` — 全カテゴリ
  - `Tools/CLIInternal/Run Auto Input Combat` — 攻撃系のみ
  - `Tools/CLIInternal/Run Auto Input Movement` — 移動系のみ
- PlayMode: `/unicli` PlayMode.Enter → ポーリング → PlayMode.Exit
- ログ確認: `/unicli` Console.GetLog + `auto-input-test-log.txt`

## 対象
- 移動（歩行、ダッシュ、方向転換）
- ジャンプ（通常、二段、壁ジャンプ）
- 攻撃（弱、強、空中）
- コンボ（連続入力タイミング）
- ガード（構え、解除）
- 回避（ドッジ）
- チャージ攻撃（長押し→リリース）
- 武器切替

## テストステップ設計
```
TestStep {
  name: string         // テスト名
  duration: float      // 実行時間（秒）
  input: MovementInfo  // 送信する入力
  validation: string   // 検証コールバック名
}
```

### MovementInfo フィールド（参照: references/auto-input-patterns.md）
- moveAmount: Vector2（移動方向）
- jumpPressed / jumpReleased: bool
- actionPressed / actionReleased: bool
- guardPressed: bool
- dodgePressed: bool
- etc.

## 完了検出
AutoInputTesterは全周回終了時に以下のログを出力:
```
[AutoInputTester] 全{N}周完了: TOTAL PASS={X} TOTAL FAIL={Y}
```
`全` + `周完了` パターンでマッチする。

## ポーリング手順
1. PlayMode.Status → PlayMode中か確認
2. Console.GetLog → ログ確認
3. 10秒間隔、最大180秒
4. タイムアウト時: PlayMode.Exit → エラー報告

## 結果判定
- TOTAL FAIL=0 → Pass
- TOTAL FAIL>0 → Fail（該当テストステップの詳細をログから抽出）
