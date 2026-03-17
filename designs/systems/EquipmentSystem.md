# System: EquipmentSystem
Section: 1 — MVP

## 責務
装備の装着・脱着管理、ステータス再計算、AbilityFlag管理、重量比率計算。装備変更時に全関連ステータスを再計算してSoAコンテナに書き戻す。

## 依存
- 入力: WeaponData/ShieldData/CoreData（ScriptableObject）、DataContainer
- 出力: EquipmentStatus更新、OnEquipmentChangedイベント、OnAbilityFlagsChangedイベント

## コンポーネント構成
| コンポーネント | 責務 | MonoBehaviour? |
|--------------|------|---------------|
| EquipmentManager | 装備スロット管理・再計算 | No (GameManager保持) |
| EquipmentSlots | キャラクターごとの装備状態 | No (SoA EquipmentStatus内) |
| WeaponData | 武器定義（ScriptableObject） | No |
| ShieldData | 盾定義（ScriptableObject） | No |
| CoreData | コア定義（ScriptableObject） | No |
| EquipmentComboData | 装備組み合わせ効果（ScriptableObject） | No |
| EquipmentComboRegistry | 全コンビネーション効果の登録・検索 | No |

## インタフェース
```csharp
public class EquipmentManager
{
    // 装備操作
    void Equip(int ownerHash, IEquippable item);
    void Unequip(int ownerHash, EquipSlot slot);
    void SwitchGrip(int ownerHash);  // 片手⇔両手持ち切替

    // 参照
    WeaponData GetWeapon(int ownerHash);
    ShieldData GetShield(int ownerHash);
    CoreData GetCore(int ownerHash);
    GripMode GetGripMode(int ownerHash);

    // スキル優先判定
    SkillSource GetActiveSkillSource(int ownerHash);
    // → TwoHanded or shield.weaponArts → Weapon, else → Shield

    // 重量
    float GetWeightRatio(int ownerHash);
    float GetPerformanceMultiplier(int ownerHash, PerformanceType type);
}

public enum SkillSource { Weapon, Shield }
public enum PerformanceType { DodgeSpeed, StaminaRecovery, AttackSpeed, DodgeDistance }
```

## データフロー
```
装備変更要求 → EquipmentManager.Equip()
    ↓
1. スロットにアイテム設定
2. 攻撃力再計算（武器基礎値 + スケーリング(能力値)）
3. 防御力再計算（基礎防御 + 盾カット率 + コア補正）
4. 重量比率再計算（全装備重量 / 最大重量）
5. AbilityFlag再計算（武器+盾+コア+コンビネーション効果）
6. SoAコンテナに書き戻し
7. アニメーション切替（AnimatorController / AnimatorOverride 差し替え）
8. OnEquipmentChanged発火
9. OnAbilityFlagsChanged発火（フラグ変化時のみ）
```

## 装備変更アニメーション

### AnimatorController切替
武器タイプごとにAnimatorOverrideControllerを保持し、装備変更時に差し替える。
```csharp
// WeaponDataに保持
public RuntimeAnimatorController oneHandedController;  // 片手持ちモーションセット
public RuntimeAnimatorController twoHandedController;  // 両手持ちモーションセット
```

### 切替フロー
```
装備変更 → WeaponData.GetController(gripMode) → Animator.runtimeAnimatorController に設定
    → 待機・移動・攻撃・ガードモーションが武器に応じて変化
```

### 盾装備変更
```
盾変更 → ガードモーション差し替え（AnimatorOverrideController のガードクリップ置換）
     → 盾のスプライト変更（SpriteRenderer or 子オブジェクト）
```

### コア装備変更
コアはビジュアルエフェクト（パーティクル色変化等）で表現。アニメーションは変更なし。

## EquipmentStatus（SoA構造体）
```csharp
public struct EquipmentStatus
{
    // 装備参照（ScriptableObjectへのindex or ID）
    public int weaponId;
    public int shieldId;
    public int coreId;
    public GripMode gripMode;

    // 計算済みステータス
    public ElementalStatus finalAttack;    // 武器+スケーリング+コア補正
    public ElementalStatus finalDefense;   // 基礎防御+コア補正
    public GuardStats finalGuardStats;     // 盾ガード性能
    public AbilityFlag activeFlags;        // 全装備の合算フラグ
    public float weightRatio;              // 重量比率 (0.0~1.0+)
    public int totalWeight;
    public int maxWeightCapacity;

    // ジャストガード
    public float justGuardStartTime;
    public float justGuardDuration;
}
```

## 機能分解
| 機能名 | 説明 | テスト種別 | 優先度 |
|--------|------|-----------|--------|
| 装備装着・脱着 | スロット管理と検証（必要能力値チェック） | EditMode | High |
| 攻撃力スケーリング計算 | 基礎値 + AnimationCurve(能力値) | EditMode | High |
| 重量比率計算 | totalWeight / maxCapacity(STR, END依存) | EditMode | High |
| 性能倍率算出 | weightRatio → AnimationCurve → 各性能倍率 | EditMode | High |
| AbilityFlag合算 | 武器+盾+コア+コンビネーション効果 | EditMode | High |
| 持ち替え（GripMode切替） | OneHanded⇔TwoHanded、スキル参照先変更 | EditMode | Medium |
| スキル優先判定 | GripMode+shield.weaponArts → SkillSource | EditMode | Medium |
| コンビネーション効果 | 装備組み合わせボーナス判定 | EditMode | Medium |
| アニメーション切替 | 武器変更時のAnimatorController差し替え | PlayMode | High |
| 盾スプライト切替 | 盾変更時のビジュアル差し替え | PlayMode | Medium |

## 素手（デフォルト装備）

何も装備していないスロットには素手が自動適用される。

```csharp
// EquipmentManagerが保持する不変のデフォルト装備
public WeaponData bareHandWeapon;   // 素手（武器）: 物理攻撃力のみ、打撃タイプ
public ShieldData bareHandShield;   // 素手（盾）: カット率0、ガード不可（JGも不可）
```

| スロット | 素手の性能 |
|---------|-----------|
| 武器 | 低物理攻撃力、打撃タイプ、弱/強攻撃のみ（スキルなし）、weight=0 |
| 盾 | カット率0%、guardStrength=0（ガード不成立）、weight=0 |

- コアスロットが空の場合はボーナスなし（nullチェックで処理スキップ）
- 素手はインベントリに表示されず、売却・破棄不可
- 素手武器にもAnimatorControllerを持たせ、格闘モーションを設定

## 設計メモ
- 装備変更は頻繁ではないので、変更時に全再計算しても問題なし
- 重量性能カーブはAnimationCurveで設定（Common_Section1 §5参照）
- コンビネーション効果は線形走査（データ量が少ないため十分高速）
- 必要能力値を満たさない装備も装着可能だが、大幅なペナルティ（攻撃力半減等）
