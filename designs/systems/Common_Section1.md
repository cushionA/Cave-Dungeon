# Common Design: Section 1 — 共通設計

## 1. 共通インターフェース

### IAbility（アーキテクチャ02準拠）
```csharp
public interface IAbility
{
    AbilityType Type { get; }
    AbilityExclusiveGroup ExclusiveGroup { get; }
    bool CanExecute();
    bool IsExecuting { get; }
    void Initialize(BaseCharacter owner);
    void Execute(MovementInfo info);
    void Cancel();
    void Tick(float deltaTime);
}
```

### IDamageable
```csharp
public interface IDamageable
{
    int ObjectHash { get; }
    bool IsAlive { get; }
    DamageResult ReceiveDamage(DamageData data);
}
```

### IInteractable
```csharp
public interface IInteractable
{
    InteractionType InteractionType { get; }
    string InteractionPrompt { get; }
    bool CanInteract(int playerHash);
    void Interact(int playerHash);
}
```

### ISaveable
```csharp
public interface ISaveable
{
    string SaveId { get; }
    SaveData Serialize();
    void Deserialize(SaveData data);
}
```

### IEquippable
```csharp
public interface IEquippable
{
    EquipSlot Slot { get; }
    int Weight { get; }
    void OnEquip(int ownerHash);
    void OnUnequip(int ownerHash);
    AbilityFlag GrantedFlags { get; }
}
```

---

## 2. 共通Enum定義

### 属性 (Element)
```csharp
[Flags]
public enum Element : byte
{
    None      = 0,
    Fire      = 1 << 0,
    Thunder   = 1 << 1,
    Light     = 1 << 2,
    Dark      = 1 << 3,
    // 物理タイプは別enum（WeaponPhysicalType）で管理
}
```

### 物理タイプ (WeaponPhysicalType)
```csharp
public enum WeaponPhysicalType : byte
{
    Slash,    // 斬撃
    Pierce,   // 刺突
    Strike,   // 打撃
}
```

### 装備スロット (EquipSlot)
```csharp
public enum EquipSlot : byte
{
    Weapon,     // 右手: 武器
    Shield,     // 左手: 盾
    Core,       // コア: 魔物の核（1スロット）
}
```

### 武器グリップモード (GripMode)
```csharp
public enum GripMode : byte
{
    OneHanded,   // 片手持ち（盾と併用）
    TwoHanded,   // 両手持ち（盾スロット無効化）
}
```

### 攻撃入力タイプ (AttackInputType)
```csharp
public enum AttackInputType : byte
{
    LightAttack,       // 弱攻撃
    HeavyAttack,       // 強攻撃
    ChargeLight,       // 溜め弱
    ChargeHeavy,       // 溜め強
    DropAttack,        // 落下攻撃（旧strike）
    Skill,             // スキル（MP消費技）
    AerialLight,       // 空中弱
    AerialHeavy,       // 空中強
}
```

### 攻撃特性フラグ (AttackFeature)
```csharp
[Flags]
public enum AttackFeature : ushort
{
    None             = 0,
    Light            = 1 << 0,   // 弱攻撃
    Heavy            = 1 << 1,   // 強攻撃
    Unparriable      = 1 << 2,   // パリィ不可
    SelfRecover      = 1 << 3,   // 自己回復（HP吸収等）
    HitRecover       = 1 << 4,   // ヒット時回復
    SuperArmor       = 1 << 5,   // スーパーアーマー（怯まない）
    GuardAttack      = 1 << 6,   // ガード攻撃（ガード貫通）
    DropAttack       = 1 << 7,   // 落下攻撃
    PositiveEffect   = 1 << 8,   // バフ効果付き
    NegativeEffect   = 1 << 9,   // デバフ効果付き
    BackAttack       = 1 << 10,  // 背面攻撃
    JustGuardImmune  = 1 << 11,  // ジャストガード不可
}
```

### 攻撃接触タイプ (AttackContactType)
```csharp
public enum AttackContactType : byte
{
    PassThrough,    // 敵をすり抜ける
    StopOnHit,      // 敵に当たると停止
    Carry,          // 敵を運搬する
}
```

### Abilityフラグ (AbilityFlag)
```csharp
[Flags]
public enum AbilityFlag : uint
{
    None          = 0,
    WallKick      = 1 << 0,   // 壁蹴り
    WallCling     = 1 << 1,   // 壁張り付き
    DoubleJump    = 1 << 2,   // 二段ジャンプ
    AirDash       = 1 << 3,   // 空中ダッシュ
    Swim          = 1 << 4,   // 水中移動
    GlideFloat    = 1 << 5,   // 滞空
    // セクション2以降で追加
}
```

### 状態異常 (StatusEffectId)
```csharp
public enum StatusEffectId : byte
{
    None,
    // DoT系
    Poison,     // 毒
    Burn,       // 炎上
    Bleed,      // 出血
    // 行動阻害系
    Stun,       // スタン
    Freeze,     // 凍結
    Paralyze,   // 麻痺
    Slow,       // 鈍足
    // デバフ系
    Blind,      // 暗闇
    Silence,    // 沈黙（魔法使用不可）
    Weakness,   // 虚弱
    Curse,      // 呪い
}
```

### キャラクター所属 (CharacterBelong)
```csharp
[Flags]
public enum CharacterBelong : byte
{
    Ally    = 1 << 0,
    Enemy   = 1 << 1,
    Neutral = 1 << 2,
}
```

### キャラクター特徴 (CharacterFeature)
```csharp
[Flags]
public enum CharacterFeature : ushort
{
    Player    = 1 << 0,
    Companion = 1 << 1,
    Summon    = 1 << 2,
    NPC       = 1 << 3,
    Minion    = 1 << 4,
    MiniBoss  = 1 << 5,
    Boss      = 1 << 6,
    Ghost     = 1 << 7,
    Flying    = 1 << 8,
}
```

### 仲間スタンス (CompanionStance)
```csharp
public enum CompanionStance : byte
{
    Aggressive,   // 攻撃優先
    Defensive,    // 防御優先
    Supportive,   // 回復・バフ優先
    Passive,      // 戦闘しない（追従のみ）
}
```

### インタラクションタイプ (InteractionType)
```csharp
public enum InteractionType : byte
{
    SavePoint,
    Shop,
    NpcDialog,
    Chest,
    Door,
    Switch,
}
```

### ガードタイプ (GuardType)
```csharp
public enum GuardType : byte
{
    Small,    // 小盾: パリィ寄り
    Normal,   // 通常盾: バランス
    Tower,    // 大盾: ガード性能高
    Wall,     // 壁盾: 最高ガード、パリィ遅い
}
```

---

## 3. 共通データ構造

### ElementalStatus（属性ステータス）
```csharp
public struct ElementalStatus
{
    public int physical;   // 物理
    public int fire;
    public int thunder;
    public int light;
    public int dark;

    public int Get(Element element) { ... }
    public int Total => physical + fire + thunder + light + dark;
}
```

### DamageData（ダメージ情報パケット）
```csharp
public struct DamageData
{
    public int attackerHash;
    public int defenderHash;
    public ElementalDamage damage;         // 各属性の最終ダメージ
    public float motionValue;              // モーション値
    public float knockbackForce;
    public Vector2 knockbackDirection;
    public WeaponPhysicalType physicalType;
    public StatusEffectApply statusEffect; // 状態異常蓄積
    public AttackFeature feature;          // 攻撃特性フラグ
    public float armorBreakValue;          // アーマー削り値
    public float justGuardResistance;      // ジャストガード抵抗 (0-100)
}
```

### DamageResult（ダメージ結果）
```csharp
public struct DamageResult
{
    public int totalDamage;
    public GuardResult guardResult;
    public bool isCritical;
    public bool isKill;
    public float armorDamage;
    public StatusEffectId appliedEffect;
}
```

### GuardResult
```csharp
public enum GuardResult : byte
{
    NoGuard,        // ガードなし（フルダメージ）
    Guarded,        // 通常ガード（カット率適用）
    JustGuard,      // ジャストガード（弾き+反撃可能）
    GuardBreak,     // ガードブレイク（ガード崩壊）
    EnhancedGuard,  // 強化ガード（ガード性能1.3倍）
}
```

### StatusEffectApply（状態異常蓄積データ）
```csharp
public struct StatusEffectApply
{
    public StatusEffectId effectId;
    public float accumulateValue;   // 蓄積量（装備値+モーション値の合算）
}
```

---

## 4. 共通イベント定義

GameManager経由のC#イベントで疎結合通信を行う。

### 戦闘イベント
```csharp
// ダメージ処理完了時
public event Action<DamageResult, int attackerHash, int defenderHash> OnDamageDealt;

// キャラクター死亡時
public event Action<int deadHash, int killerHash> OnCharacterDeath;

// パリィ/ジャストガード成功時
public event Action<int defenderHash, int attackerHash, GuardResult> OnGuardEvent;

// 状態異常発症時
public event Action<int targetHash, StatusEffectId> OnStatusEffectApplied;
```

### 進行イベント
```csharp
// 経験値獲得時
public event Action<int characterHash, int expAmount> OnExpGained;

// レベルアップ時
public event Action<int characterHash, int newLevel> OnLevelUp;

// 通貨変動時
public event Action<int amount, int newTotal> OnCurrencyChanged;
```

### 装備イベント
```csharp
// 装備変更時（AbilityFlag再計算トリガー）
public event Action<int ownerHash, EquipSlot slot> OnEquipmentChanged;

// AbilityFlag変更時（移動システムが監視）
public event Action<int ownerHash, AbilityFlag newFlags> OnAbilityFlagsChanged;
```

### 探索イベント
```csharp
// エリア遷移時
public event Action<string fromAreaId, string toAreaId> OnAreaTransition;

// セーブポイント使用時
public event Action<string savePointId> OnSavePointUsed;

// アイテム取得時
public event Action<int characterHash, string itemId, int count> OnItemAcquired;
```

---

## 5. 装備重量システム

### 重量比率計算
```
weightRatio = totalEquipWeight / maxWeightCapacity   (0.0 ~ 1.0+)
```

### 重量による性能変動

| weightRatio | 回避速度 | スタミナ回復 | 攻撃速度 | 回避距離 |
|-------------|---------|------------|---------|---------|
| 0.0 - 0.3   | 100%    | 100%       | 100%    | 100%    |
| 0.3 - 0.5   | 90%     | 95%        | 100%    | 95%     |
| 0.5 - 0.7   | 75%     | 85%        | 95%     | 85%     |
| 0.7 - 1.0   | 55%     | 70%        | 90%     | 70%     |
| 1.0+        | 30%     | 50%        | 80%     | 50%     |

- `maxWeightCapacity` はレベルと能力値（STR）から算出
- weightRatio 1.0超過 = 過負荷（ペナルティ大だが装備自体は可能）
- AnimationCurveで滑らかに補間（段階的ではなく連続的）

---

## 6. 能力値定義

### 基本ステータス（レベルアップで振り分け）

| 能力値 | 略称 | 影響 |
|--------|------|------|
| 筋力 | STR | 物理攻撃力、光攻撃力（部分）、最大重量上限 |
| 技量 | DEX | 物理攻撃力（部分）、闇攻撃力（部分）、クリティカル |
| 知力 | INT | 火攻撃力、雷攻撃力、光攻撃力（部分）、闇攻撃力（部分）、最大MP |
| 体力 | VIT | 最大HP、物理防御 |
| 精神 | MND | 最大MP（部分）、魔法防御、状態異常耐性 |
| 持久 | END | 最大スタミナ、スタミナ回復速度、最大重量上限（部分） |

### 武器スケーリング（AnimationCurve）

各武器は以下のスケーリングカーブを持つ（武器レベル別）:
- `strCurve` → 物理攻撃力、光攻撃力に乗算
- `dexCurve` → 物理攻撃力、闇攻撃力に乗算
- `intCurve` → 火・雷攻撃力に乗算

スケーリング式:
```
最終攻撃力[属性] = 基礎値[武器Lv] + Σ(curve[武器Lv].Evaluate(能力値) × 対応係数)
```

---

## 7. ダメージ計算式（参考コード準拠）

### 基本式
```
属性ダメージ = (atk² × motionValue) / (atk + def) × 攻撃側倍率 × 防御側倍率
```

### 合計ダメージ
```
totalDamage = Σ(各属性ダメージ) × 全体攻撃倍率 × 全体防御倍率
スタン中: totalDamage × 1.2
最終: Mathf.CeilToInt(totalDamage)
```

### ガード時
```
ガードダメージ = 各属性ダメージ × (100 - カット率[属性]) × 0.01
```

### ジャストガード
```
if (ジャストガード成功 && !JustGuardImmune):
    armorDamage = shock × (1.0 - justGuardResistance / 100)
    if armorDamage >= remainingArmor:
        → 怯み（JustGuard抵抗0の攻撃は即怯み）
    else:
        → アーマー削りのみ
```

---

## 8. モーションデータ構造（リビルド版）

```csharp
[Serializable]
public struct AttackMotionData
{
    [Header("基本")]
    public float motionValue;              // ダメージ倍率
    public Element attackElement;          // 攻撃属性（武器の該当属性攻撃力を参照）
    public WeaponPhysicalType physicalType;
    public AttackFeature feature;          // 攻撃特性フラグ

    [Header("コスト")]
    public float staminaCost;              // スタミナ消費
    public float mpCost;                   // MP消費（スキル用）

    [Header("ヒット")]
    public int maxHitCount;                // 最大ヒット数
    public float armorBreakValue;          // アーマー削り値（shock）

    [Header("ノックバック")]
    public Vector2 knockbackForce;         // 吹き飛ばし方向・力

    [Header("状態異常")]
    public StatusEffectApply statusEffect; // 蓄積（装備値 + このモーション値の合算）

    [Header("移動")]
    public float attackMoveDistance;        // 攻撃時移動距離
    public float attackMoveDuration;       // 移動時間
    public AttackContactType contactType;  // 接触タイプ

    [Header("コンボ")]
    public bool isAutoChain;               // true: 入力なしで自動的に次モーションへ遷移
    public bool isChainEndPoint;           // true: 自動遷移チェーンの終端
    public float inputWindow;              // 次入力の受付時間（isAutoChain=falseの場合）

    [Header("ガード関連")]
    public float justGuardResistance;      // ジャストガード抵抗 (0-100)

    [Header("エフェクト")]
    public ProjectileConfig projectile;    // 飛翔体設定（nullなら近接のみ）
}
```

---

## 9. 装備データ構造（リビルド版）

### 武器 (WeaponData : ScriptableObject)
```csharp
public class WeaponData : ScriptableObject, IEquippable
{
    [Header("基本")]
    public string weaponName;
    public int weaponLevel;
    public int weight;
    public GripMode defaultGrip;          // デフォルトの持ち方

    [Header("攻撃力")]
    public ElementalStatus baseAttack;     // 基礎攻撃力（武器Lv別）

    [Header("スケーリング")]
    public AnimationCurve strCurve;
    public AnimationCurve dexCurve;
    public AnimationCurve intCurve;

    [Header("状態異常付与")]
    public StatusEffectApply inflictStatus; // 装備由来の状態異常蓄積

    [Header("モーション")]
    public AttackMotionData[] lightAttacks;     // 弱攻撃チェーン
    public AttackMotionData[] heavyAttacks;     // 強攻撃チェーン
    public AttackMotionData[] aerialAttacks;    // 空中攻撃
    public AttackMotionData dropAttack;         // 落下攻撃
    public AttackMotionData[] skillMotions;     // スキル（MP消費技）

    [Header("スキル")]
    public int[] skillMpCosts;                  // 各スキルのMP消費

    [Header("必要能力値")]
    public int requiredStr, requiredDex, requiredInt;

    [Header("AbilityFlag")]
    public AbilityFlag grantedFlags;            // この武器が付与するフラグ
}
```

### 盾 (ShieldData : ScriptableObject)
```csharp
public class ShieldData : ScriptableObject, IEquippable
{
    [Header("基本")]
    public string shieldName;
    public int weight;
    public GuardType guardType;
    public bool weaponArts;                // true: 武器スキルを優先使用

    [Header("ガード性能")]
    public GuardStats guardStats;

    [Header("攻撃力（盾スキル用）")]
    public ElementalStatus baseAttack;

    [Header("状態異常付与")]
    public StatusEffectApply inflictStatus;

    [Header("ジャストガード")]
    public float justGuardStartTime;       // ガード開始から何秒後にJG判定開始
    public float justGuardDuration;        // JG持続時間

    [Header("盾スキル")]
    public AttackMotionData[] shieldSkills; // 盾のスキルモーション
    public int[] skillMpCosts;

    [Header("スケーリング")]
    public AnimationCurve strCurve;
    public AnimationCurve dexCurve;
    public AnimationCurve intCurve;

    [Header("AbilityFlag")]
    public AbilityFlag grantedFlags;
}
```

### ガードステータス (GuardStats)
```csharp
[Serializable]
public struct GuardStats
{
    public float physicalCut;       // 物理カット率 (0-100)
    public float fireCut;
    public float thunderCut;
    public float lightCut;
    public float darkCut;
    public float guardStrength;     // ガード強度（スタミナ消費軽減）
    public float statusCut;         // 状態異常カット率 (0-100)
}
```

### コア (CoreData : ScriptableObject)
```csharp
public class CoreData : ScriptableObject, IEquippable
{
    [Header("基本")]
    public string coreName;
    public int weight;

    [Header("能力値補正")]
    public StatModifier statModifier;      // 各能力値の加算/減算

    [Header("追加HP/MP/スタミナ")]
    public int bonusHp, bonusMp;
    public float bonusStamina;

    [Header("攻撃/防御補正")]
    public ElementalStatus attackBonus;
    public ElementalStatus defenseBonus;

    [Header("特殊効果")]
    public AbilityFlag grantedFlags;       // 壁蹴り可能、等
    public PassiveEffect[] passiveEffects; // パッシブ効果リスト

    [Header("基礎アーマー")]
    public float baseArmor;
}
```

### StatModifier（能力値補正）
```csharp
[Serializable]
public struct StatModifier
{
    public int str, dex, intel, vit, mnd, end;
}
```

---

## 10. スキル優先ルール

```
if (GripMode == TwoHanded):
    → 武器スキルを使用
    → 武器の攻撃力を参照
elif (shield.weaponArts == true):
    → 武器スキルを使用
    → 武器の攻撃力を参照
else:
    → 盾スキルを使用
    → 盾の攻撃力を参照
```

---

## 11. 装備コンビネーション効果

特定の武器+盾+コアの組み合わせで発動するボーナス。

```csharp
[CreateAssetMenu]
public class EquipmentComboData : ScriptableObject
{
    public string comboName;
    public string description;

    [Header("発動条件")]
    public WeaponData requiredWeapon;      // null = 任意
    public ShieldData requiredShield;      // null = 任意
    public CoreData requiredCore;          // null = 任意

    [Header("効果")]
    public StatModifier bonusStats;
    public AbilityFlag bonusFlags;
    public PassiveEffect[] bonusEffects;
}
```

EquipmentManagerが装備変更時に全EquipmentComboDataを走査し、条件一致で効果を適用。
