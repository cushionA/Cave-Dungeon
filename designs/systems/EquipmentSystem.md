# System: EquipmentSystem
Section: 1 — MVP

## 責務
装備の装着・脱着管理、ステータス再計算、AbilityFlag管理、重量比率計算。装備変更時に全関連ステータスを再計算してSoAコンテナに書き戻す。

## 依存
- 入力: EquipmentDefinition（ScriptableObject）/ IEquippable、任意の IEquipmentStatsProvider、DataContainer
- 出力: EquipmentStatus更新、OnEquipmentChangedイベント、OnAbilityFlagsChangedイベント

## 実装済みの補正入力と再計算（2026-10-26）

装備の変更入口は `GameManager.Equipment` の `EquipmentStore.Equip / Unequip`。能力の再導出・成長・恒久能力の正本は `GameManager.Stats` の `CharacterStatsSystem` と `CharacterStatCalculator.CalculateDerived`。旧 `EquipmentStore.Recalculate / GrantAbilityFlags` と `EquipmentStatCalculator.Calculate` は互換入口としてのみ委譲する。`GetHolder` は着脱操作を公開しない読取専用viewを返す。既存の装備定義を参照したまま、Weapon・Shield・Coreの全スロットから毎回再導出する。`IEquippable` だけを実装する既存装備は数値補正なしとして使用できる。数値補正を持つ装備は任意の `IEquipmentStatsProvider.StatModifiers` を供給し、`EquipmentDefinition` はInspectorの `_statModifiers` から供給する。各補正の初期値は0、ガード上書きは無効なので、既存アセットの数値は維持する。

`EquipmentStatModifiers` の入力は次のとおり。

| 入力 | 意味 |
|------|------|
| `attributes` | STR/DEX/INT/VIT/MND/ENDの加算・減算 |
| `attackBonus` / `defenseBonus` | キャラクター全体への7属性別攻防加算。技ごとの武器・盾の参照元選択とは別 |
| `strengthScaling` / `dexterityScaling` / `intelligenceScaling` | 当該装備の攻撃加算値に適用する係数。0なら能力スケーリングなし |
| `bonusHp` / `bonusMp` / `bonusStamina` / `bonusArmor` | 各最大値への明示的な加算・減算 |
| `overridesGuard` / `guardStats` | Shieldスロットだけが使用する明示的なガード性能上書き。未設定なら素体値を維持 |

再計算の原本は `CharacterBaseStats`・`CharacterGrowth`・全装備。前回の導出結果へ差分を足し引きしない。

1. 最終6能力 = 基礎6能力 + 成長配分 + 全装備の能力補正。
2. 最大HP = 基礎最大HP + (配分VIT + 装備VIT) × 既存の10 + 明示HP加算。最大MPも配分MND・装備MNDに既存の5を使用する。基礎VIT/MNDを重ねて加算しない。END等に未定義の倍率は追加しない。
3. 各装備の攻撃加算へ、既存式を拡張した `bonus × (1 + (STR係数×STR + DEX係数×DEX + INT係数×INT) / 100)` を適用し、素体攻撃へ合算する。単一係数の場合は既存APIの演算・切り捨てを維持する。防御は素体防御と全装備の防御加算を合算する。攻防は合算後の負値を0に制限する。
4. 現在HP/MP/SP/アーマー・位置を保持し、装備込みの最終上限に対して一度だけ制限する。上限を増やしても現在値は回復させず、死亡済みのHP0も復活させない。HPを持つキャラクターの最大HPは最低1とし、装備変更だけで死亡通知を迂回してHP0へ落とさない。
5. `CombatStats` と `EquipmentStatus.finalAttack / finalDefense / finalGuardStats` を同じ値へ更新し、装備の6能力加算は `EquipmentStatus.attributeModifiers` へ保持する。`GetAttributes` は同じ加算を参照する。防御状態は最大アーマーだけ同期し、ガード時間や回復待ち時間は維持する。
6. 書き戻し後に装備・能力フラグ・ステータス変更を通知する。成長変更も同じ再計算経路へ通す。

状態効果の攻防倍率は従来どおり命中時に適用し、装備の導出値へ焼き込まない。重量の合計・能力フラグの反映は既存の共通入口で継続する。重量上限の算出、持ち方の性能差、技ごとの攻撃入力契約（DF-03）、装備選択UI・見た目の切り替えはこの数値反映とは別の接続項目。

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
