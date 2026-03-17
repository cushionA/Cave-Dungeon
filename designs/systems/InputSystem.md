# System: InputSystem
Section: 1 — MVP

## 責務
Unity Input Systemを使用した入力管理。キーボード/ゲームパッドの入力をゲームアクションに変換し、MovementInfoとして各システムに配信する。

## 依存
- 入力: Unity Input System（com.unity.inputsystem）
- 出力: MovementInfo（各フレームの入力結果）

## コンポーネント構成
| コンポーネント | 責務 | MonoBehaviour? |
|--------------|------|---------------|
| InputManager | InputActionAssetの管理・入力読取 | Yes |
| InputActionMap_Player | プレイヤー用アクションマップ | No (Asset) |
| MovementInfo | 入力結果を格納する構造体 | No |

## インタフェース
```csharp
public struct MovementInfo
{
    public Vector2 moveDirection;       // 移動方向
    public bool jumpPressed;
    public bool jumpHeld;
    public bool dashPressed;
    public AttackInputType? attackInput; // null = 攻撃入力なし
    public bool guardHeld;
    public bool interactPressed;
    public bool cooperationPressed;     // 連携ボタン
    public bool weaponSwitchPressed;
    public bool gripSwitchPressed;      // 持ち替え（片手⇔両手）
    public bool menuPressed;
    public bool mapPressed;
}
```

## データフロー
```
ハードウェア入力 → Unity Input System → InputManager → MovementInfo
                                                        ↓
                                        BaseCharacter.Execute(info) → IAbility群
```

## 機能分解
| 機能名 | 説明 | テスト種別 | 優先度 |
|--------|------|-----------|--------|
| アクションマップ定義 | Player/UI/Menuのマップ切替 | EditMode | High |
| 移動入力変換 | スティック/WASD → Vector2正規化 | EditMode | High |
| 戦闘入力変換 | 攻撃/ガード/連携ボタン変換 | EditMode | High |
| デバイス自動切替 | キーボード⇔ゲームパッド | PlayMode | Medium |
| 入力バッファ | 先行入力（攻撃コンボ用に数フレーム保持） | EditMode | Medium |

## 設計メモ
- プレイヤー専用。AI仲間はMovementInfoをAIBrainが生成する
- InputBufferは先行入力を3-5フレーム保持し、コンボの入力落ちを防ぐ
- UIモード時はPlayerアクションマップを無効化
