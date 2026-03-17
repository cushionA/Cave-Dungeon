# System: PlayerMovement
Section: 1 — MVP

## 責務
プレイヤーキャラクターの移動制御。AbilityFlagに基づいて使用可能な移動手段を動的に変更する。

## 依存
- 入力: InputSystem（MovementInfo）、DataContainer（MoveStatus, EquipmentStatus）
- 出力: キャラクター位置・速度の更新

## コンポーネント構成
| コンポーネント | 責務 | MonoBehaviour? |
|--------------|------|---------------|
| BaseCharacter | Ability管理・フレーム更新 | Yes |
| WalkAbility | 地上移動 | Yes (IAbility) |
| JumpAbility | ジャンプ（可変高度） | Yes (IAbility) |
| DashAbility | 地上/空中ダッシュ | Yes (IAbility) |
| WallKickAbility | 壁蹴り（AbilityFlag.WallKick必要） | Yes (IAbility) |
| WallClingAbility | 壁張り付き（AbilityFlag.WallCling必要） | Yes (IAbility) |
| DropAttackAbility | 落下攻撃（空中から急降下） | Yes (IAbility) |
| GroundDetector | 接地判定（Raycast） | Yes |
| WallDetector | 壁接触判定（Raycast） | Yes |

## インタフェース
各Abilityは `IAbility` を実装。BaseCharacterが毎フレームMovementInfoを配信。

```csharp
// AbilityFlagチェック
bool CanExecute()
{
    AbilityFlag flags = GameManager.Instance.Data
        .GetEquipmentStatus(owner.ObjectHash).activeFlags;
    return flags.HasFlag(AbilityFlag.WallKick);
}
```

## データフロー
```
MovementInfo → BaseCharacter → 各IAbility.Execute(info)
                                    ↓
                              Rigidbody2D.velocity更新
                                    ↓
                       GroundDetector/WallDetector → IsGrounded/IsTouchingWall更新
```

## 機能分解
| 機能名 | 説明 | テスト種別 | 優先度 |
|--------|------|-----------|--------|
| 地上移動 | 左右移動、速度はMoveStatus依存 | PlayMode | High |
| ジャンプ | 可変高度ジャンプ（ボタン押し時間で変化） | PlayMode | High |
| 接地判定 | Raycastベースの地面検出 | PlayMode | High |
| ダッシュ | 地上ダッシュ（スタミナ消費） | PlayMode | High |
| 壁蹴り | AbilityFlag依存、壁接触+ジャンプ入力で発動 | PlayMode | Medium |
| 壁張り付き | AbilityFlag依存、壁接触中に滑り落ち減速 | PlayMode | Medium |
| 重量ペナルティ反映 | weightRatio → 移動速度/ダッシュ速度補正 | EditMode | Medium |
| 落下攻撃遷移 | 空中+下入力+攻撃 → DropAttackAbilityへ遷移 | PlayMode | Medium |

## 設計メモ
- 壁蹴り/壁張り付きのAbility自体はSection1で実装するが、それを有効化する装備はSection2
- GroundDetectorは複数Raycast（足元3点）で安定した接地判定
- 重量ペナルティはEquipmentManager.OnWeightChanged → MoveStatus再計算で反映
- Abilityの排他グループ: Movement（Walk/Dash）、Aerial（Jump/WallKick）、Combat（Attack/Guard）
