# System: AICore
Section: 1 — MVP

## 責務
AIBrainの基盤。モードシステム、知覚（センサー）、行動選択のフレームワーク。敵AIと仲間AIの両方がこの基盤上で動作する。

## 依存
- 入力: DataContainer（RecognitionData, AIBrainData）、物理衝突（センサー）
- 出力: MovementInfo（BaseCharacterに渡す行動決定）

## コンポーネント構成
| コンポーネント | 責務 | MonoBehaviour? |
|--------------|------|---------------|
| AIBrain | 行動選択メインループ | Yes |
| SensorSystem | 視野・聴覚・近接検知 | Yes |
| ModeController | CharacterMode遷移管理 | No |
| ActSelector | ActData配列からの行動選択 | No |
| DeliberationBuffer | 意図的な判断遅延（難易度調整用） | No |

## AIBrain メインループ
```
毎フレーム:
1. SensorSystem.Update() → RecognitionData更新
2. ModeController.Evaluate() → モード遷移チェック
3. DeliberationBuffer.Tick() → 遅延中なら保留
4. ActSelector.Select(currentMode, recognitionData) → ActData
5. ActData → MovementInfo変換
6. BaseCharacter.Execute(movementInfo)
```

## モードシステム（アーキテクチャ05準拠）
```
Idle → [敵検知] → Alert → [接敵] → Combat
  ↑                                    ↓
  ←──── [敵消失] ←── Retreat ←── [HP低下]

Support（仲間AI専用）: 味方のHP低下で遷移
Patrol（巡回）: Idle中に巡回ルート移動
```

## 行動選択 (ActSelector)
```csharp
ActData? Select(CharacterMode mode, ref RecognitionData recognition,
                ref CharacterBaseInfo baseInfo, ref StaminaInfo stamina)
{
    // 1. 現在モードで使用可能なActData配列を取得
    // 2. 各ActDataのTriggerJudge → 発動条件チェック
    // 3. 各ActDataのActJudge → リソース条件チェック（MP, スタミナ, HP範囲）
    // 4. 各ActDataのTargetJudge → ターゲット選択
    // 5. 条件を満たすActDataの中からweight（重み）でランダム選択
    // 6. クールタイムチェック
    // 7. 選択されたActDataを返す（なければnull → 待機）
}
```

## DeliberationBuffer（難易度調整）
```csharp
// Easy: 12フレーム遅延 + 0-6ランダム → 0.3-0.6秒の判断遅延
// Normal: 6フレーム + 0-4ランダム → 0.1-0.33秒
// Hard: 2フレーム + 0-2ランダム → 0.03-0.13秒
```

## 機能分解
| 機能名 | 説明 | テスト種別 | 優先度 |
|--------|------|-----------|--------|
| AIBrainメインループ | 毎フレームの行動選択サイクル | PlayMode | High |
| センサーシステム | 視野角・距離による敵味方検知 | PlayMode | High |
| モード遷移 | 条件に基づくモード自動切替 | EditMode | High |
| 行動選択（重み付き） | ActData配列からの確率的選択 | EditMode | High |
| TriggerJudge評価 | 発動条件の真偽判定 | EditMode | High |
| TargetJudge評価 | ターゲット選択ロジック | EditMode | Medium |
| ActJudge評価 | リソース条件チェック | EditMode | Medium |
| クールタイム管理 | 行動別・グローバルクールタイム | EditMode | Medium |
| DeliberationBuffer | 難易度別の判断遅延 | EditMode | Medium |

## 設計メモ
- アーキテクチャ05に準拠。BrainStatus.csの巨大enum群をリファクタ
- AIInfo（ScriptableObject）にActData配列、モード設定、クールタイム設定を持つ
- Section2でAIRuleBuilderがActData配列を動的に構築する拡張ポイント
- PersonalityData（10D性格ベクトル）はSection1ではプリセット値を使用
