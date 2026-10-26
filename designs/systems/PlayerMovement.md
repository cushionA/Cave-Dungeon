# System: PlayerMovement
Section: 1 — MVP

## 責務
プレイヤーキャラクターの移動制御。AbilityFlagに基づいて使用可能な移動手段を動的に変更する。

## 依存
- 入力: InputSystem（MovementInfo）、DataContainer（MoveStatus, EquipmentStatus）
- 出力: キャラクター位置・速度の更新

## 現在の物理境界と固定更新（2026-10-26）

現在の共通基底は`BaseCharacter`、Abilityの契約は`IAbility`。以下のWalkAbility等の表は設計案を含み、実際の移動はPlayer/Enemy/Companionと共通Coreロジックで実行する。

物理の入口は`BaseCharacter.Physics.cs`へ集約する。速度・重力・位置・力積・移動リセット・衝突レイヤー・一時停止・接地/壁/床すり抜けは`ICharacterPhysics2D`へ委譲し、`RigidbodyCharacterPhysics2D`が既存Physics2Dを操作する。`CreateCharacterPhysics()`が差替え口。既存Rigidbody getterと`ApplyMovement(owner, Rigidbody2D)`は互換入口。Projectileと未接続legacy SimpleEnemyBrainは今回の境界の対象外。

`GameManager.FixedUpdate`から`SimulationTickRunner`を一度呼び、以下の順に全キャラを進める。派生キャラの独立したFixedUpdateは廃止する。

1. ActorRefで対象世代を固定し、前の物理ステップの位置・接地を取得。
2. 処理済み入力を全キャラへ公開。
3. 状態効果・共有行動・弾・資源を更新し、共通AI観測を確定。
4. 全キャラの`SimulateCharacterTick`で判断・移動計算。
5. 全キャラの`ApplySimulationPhysics`で共通行動の最終移動補正・衝突モードを反映。
6. Unityの通常のPhysics2Dが移動・衝突を解決し、次tickでその結果を取得。

各段階で世代とGameObject生存を再確認する。componentが無効でも物理観測は取得し、入力・判断・最終補正は有効なcomponentだけ実行する。死亡時の派生cleanupも維持する。

制御中の物理setterはread-after-write互換のため即時反映する。全書込みを最後まで遅延するコマンドバッファではなく、Unity物理前に判断と最終補正を完了する契約。明示速度入力があったtickだけ実速度から共通移動へのfallbackを更新し、受理済みHeld移動要求を旧速度0で上書きしない。

通常ジャンプ・壁蹴り・床すり抜けの受理時に押下だけを消費し、拒否時の先行入力と長押しは保持する。現行ステージとPhysics2Dは維持し、3D/DOTS Physics移行時には座標・Collider・接地/壁クエリ・床すり抜け・接触イベントとステージを併せて対応する。

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
