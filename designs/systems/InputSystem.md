# System: InputSystem
Section: 1 — MVP

## 責務
Unity Input Systemを使用した入力管理。キーボード/ゲームパッドの入力をゲームアクションに変換し、MovementInfoとして各システムに配信する。

## 依存
- 入力: Unity Input System（com.unity.inputsystem）
- 出力: MovementInfo（各フレームの入力結果）

## 現在の入力公開契約（2026-10-26）

`PlayerInputHandler`は`InputSystem.onAfterUpdate`のDynamic/Fixed更新内でだけInputActionをポーリングする。入力イベント処理後に公開し、中央固定tickの`PrepareSimulationInput(currentTime)`で最新接地に応じた入力を同じtickの制御へ渡す。Update・callback・固定tickは`InputSamplingClock`の単調時計を共有し、バッファとチャージを二重に進めない。過去のfixedTimeで時計を巻き戻さない。

Attack/Dodge/Jumpの押下は受理後に消費する。Jump長押しは別に保持し、拒否中の先行入力は有効期限まで残す。DisableでInputSystem購読・保持・公開/override入力を解除し、disabledなcallbackは受け付けない。

global updateModeの変更や手動`InputSystem.Update()`は追加しない。処理済み入力から物理への追加tick待ちを作らない構成であり、入力到着から表示までの遅延ゼロは保証しない。実機でupdateMode・固定刻み・描画補間を含めて計測する。

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
