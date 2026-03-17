# System: WeaponSystem
Section: 1 — MVP

## 責務
武器の攻撃実行、コンボ管理、チャージ攻撃、スキル発動。AttackMotionDataに基づく攻撃フローの制御。

## 依存
- 入力: InputSystem（AttackInputType）、EquipmentSystem（WeaponData/ShieldData）、DataContainer
- 出力: DamageDealer（ヒットボックス生成）、アニメーション駆動

## コンポーネント構成
| コンポーネント | 責務 | MonoBehaviour? |
|--------------|------|---------------|
| WeaponHolder | 武器スロット管理・攻撃入力ルーティング | Yes (IAbility) |
| ComboTracker | コンボ進行管理（チェーン追跡） | No |
| ChargeHandler | チャージ攻撃の溜め時間管理 | No |
| DamageDealer | ヒットボックス生成・当たり判定・DamageData生成 | Yes |
| AttackMover | 攻撃時移動制御（突進/停止/すり抜け/運搬） | Yes |
| ProjectileEmitter | 飛翔体発射（エフェクト弾） | Yes |

## インタフェース
```csharp
public class WeaponHolder : MonoBehaviour, IAbility
{
    AbilityType Type => AbilityType.Attack;

    // 攻撃要求
    void RequestAttack(AttackInputType inputType);

    // 状態
    bool IsAttacking { get; }
    int CurrentComboStep { get; }
    AttackMotionData CurrentMotion { get; }
}
```

## コンボシステム詳細

### isAutoChain の動作
```
[モーション0: motionValue=0.3, isAutoChain=true]   ← 入力なしで自動遷移
    ↓ (自動)
[モーション1: motionValue=0.2, isAutoChain=true]   ← 自動遷移続行
    ↓ (自動)
[モーション2: motionValue=1.5, isChainEndPoint=true] ← チェーン終端（大技）
```

### 通常コンボ（isAutoChain=false）
```
[弱1: inputWindow=0.4s] → (弱入力) → [弱2: inputWindow=0.4s] → (弱入力) → [弱3]
                         → (強入力) → [派生強1]
```

### 攻撃フロー
```
1. RequestAttack(inputType)
2. ComboTracker.GetNextMotion(inputType) → AttackMotionData or null
3. スタミナチェック → Consume(staminaCost)
4. アニメーション再生
5. AttackMover.StartMove(distance, duration, contactType)
6. DamageDealer.Activate(motionData) → ヒットボックス有効化
7. ヒット時 → DamageData生成 → IDamageable.ReceiveDamage()
8. hitCount < maxHitCount でヒット制限
9. isAutoChain → 自動で次モーションへ / else → inputWindow内の入力待ち
10. コンボ終了 → リカバリーモーション → 行動可能に
```

## 攻撃移動 (AttackMover)
```csharp
public class AttackMover
{
    void StartMove(float distance, float duration, AttackContactType contactType);
    // contactType:
    //   PassThrough → 敵をすり抜けて移動継続
    //   StopOnHit   → 敵に接触で停止
    //   Carry       → 敵を押しながら移動（運搬）
}
```

## スキル実行
```
1. GetActiveSkillSource() → Weapon or Shield
2. if Weapon: weaponData.skillMotions[index] + weaponData.skillMpCosts[index]
   if Shield: shieldData.shieldSkills[index] + shieldData.skillMpCosts[index]
3. MP消費チェック → 攻撃実行（通常攻撃と同じフロー）
4. スキル攻撃力参照:
   - Weapon skill → 武器の攻撃力
   - Shield skill → 盾の攻撃力
```

## 機能分解
| 機能名 | 説明 | テスト種別 | 優先度 |
|--------|------|-----------|--------|
| 弱攻撃チェーン | lightAttacks配列を順に実行 | PlayMode | High |
| 強攻撃チェーン | heavyAttacks配列を順に実行 | PlayMode | High |
| コンボ派生 | 弱→強の派生ルート | PlayMode | High |
| isAutoChain自動遷移 | 入力なしで次モーションへ自動遷移 | PlayMode | High |
| ヒットボックス・当たり判定 | コライダーベースのダメージ判定 | PlayMode | High |
| 攻撃時移動 | 3種の接触タイプによる移動制御 | PlayMode | High |
| チャージ攻撃 | 溜め時間によるモーション切替 | PlayMode | Medium |
| スキル発動 | MP消費技、参照先武器/盾の切替 | PlayMode | Medium |
| 空中攻撃 | aerialAttacks配列の実行 | PlayMode | Medium |
| 落下攻撃 | 空中から急降下攻撃 | PlayMode | Medium |
| 飛翔体発射 | ProjectileConfig設定に基づく弾発射 | PlayMode | Medium |
| ヒット数制限 | maxHitCountによるマルチヒット管理 | EditMode | Medium |

## 設計メモ
- 参考コードのWeaponAbillity.csを大幅リビルド。ActType enumの冗長な分岐を排除
- isCombo → isAutoChain にリネーム（意味を明確化）
- 攻撃モーションはすべてAttackMotionData配列で統一管理
- DamageDealerはヒットした対象のhashをDictionaryで追跡し、同一対象への重複ヒットを防止
