using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// 7属性（斬撃/打撃/刺突/炎/雷/聖/闇）のステータス値。
    /// 攻撃力・防御力・ダメージの各チャネルで使用する。
    /// </summary>
    [Serializable, InlineProperty]
    public struct ElementalStatus
    {
        [HorizontalGroup("Physical", LabelWidth = 40)]
        [LabelText("斬")]
        public int slash;
        [HorizontalGroup("Physical", LabelWidth = 40)]
        [LabelText("打")]
        public int strike;
        [HorizontalGroup("Physical", LabelWidth = 40)]
        [LabelText("突")]
        public int pierce;

        [HorizontalGroup("Magical", LabelWidth = 40)]
        [LabelText("炎")]
        public int fire;
        [HorizontalGroup("Magical", LabelWidth = 40)]
        [LabelText("雷")]
        public int thunder;
        [HorizontalGroup("Magical", LabelWidth = 40)]
        [LabelText("聖")]
        public int light;
        [HorizontalGroup("Magical", LabelWidth = 40)]
        [LabelText("闇")]
        public int dark;

        [ShowInInspector, ReadOnly, LabelText("合計")]
        [Tooltip("物理: 斬+打+突 / 魔法: 炎+雷+聖+闇")]
        public int Total => slash + strike + pierce + fire + thunder + light + dark;

        [ShowInInspector, ReadOnly, LabelText("物理計")]
        public int PhysicalTotal => slash + strike + pierce;

        [ShowInInspector, ReadOnly, LabelText("魔法計")]
        public int MagicalTotal => fire + thunder + light + dark;

        public int Get(Element element)
        {
            switch (element)
            {
                case Element.Slash:   return slash;
                case Element.Strike:  return strike;
                case Element.Pierce:  return pierce;
                case Element.Fire:    return fire;
                case Element.Thunder: return thunder;
                case Element.Light:   return light;
                case Element.Dark:    return dark;
                default:              return 0;
            }
        }
    }

    /// <summary>
    /// 7属性別のガードカット率。
    /// </summary>
    [Serializable, InlineProperty]
    public struct GuardStats
    {
        [HorizontalGroup("Cuts", LabelWidth = 40)]
        [LabelText("斬"), Range(0f, 1f)] public float slashCut;
        [HorizontalGroup("Cuts", LabelWidth = 40)]
        [LabelText("打"), Range(0f, 1f)] public float strikeCut;
        [HorizontalGroup("Cuts", LabelWidth = 40)]
        [LabelText("突"), Range(0f, 1f)] public float pierceCut;

        [HorizontalGroup("MagicCuts", LabelWidth = 40)]
        [LabelText("炎"), Range(0f, 1f)] public float fireCut;
        [HorizontalGroup("MagicCuts", LabelWidth = 40)]
        [LabelText("雷"), Range(0f, 1f)] public float thunderCut;
        [HorizontalGroup("MagicCuts", LabelWidth = 40)]
        [LabelText("聖"), Range(0f, 1f)] public float lightCut;
        [HorizontalGroup("MagicCuts", LabelWidth = 40)]
        [LabelText("闇"), Range(0f, 1f)] public float darkCut;

        [MinValue(0)] public float guardStrength;
        [Range(0f, 1f)] public float statusCut;
        [EnumToggleButtons] public GuardDirection guardDirection;
    }

    [Serializable]
    public struct StatModifier
    {
        public int str;
        public int dex;
        public int intel;
        public int vit;
        public int mnd;
        public int end;
    }

    /// <summary>
    /// 攻撃に付随する状態異常情報（蓄積モデル対応）。
    /// 1ヒットあたりの蓄積量を定義し、閾値超過で発症する。
    /// </summary>
    [Serializable]
    public struct StatusEffectInfo
    {
        public StatusEffectId effect;
        [MinValue(0)] public float accumulateValue;
        [MinValue(0)] public float duration;
        [MinValue(0)] public float tickDamage;
        [MinValue(0)] public float tickInterval;
        [Range(0f, 1f)] public float modifier;
        [Tooltip("0=スタック不可")]
        public byte maxStack;
    }

    /// <summary>
    /// 物理タイプ別耐性（斬撃/打撃/刺突）。
    /// </summary>
    [Serializable, InlineProperty]
    public struct PhysicalResistance
    {
        [HorizontalGroup("Resist", LabelWidth = 40)]
        [LabelText("斬"), Range(0f, 1f)] public float slashResist;
        [HorizontalGroup("Resist", LabelWidth = 40)]
        [LabelText("打"), Range(0f, 1f)] public float strikeResist;
        [HorizontalGroup("Resist", LabelWidth = 40)]
        [LabelText("突"), Range(0f, 1f)] public float pierceResist;
    }

    public struct MovementInfo
    {
        public Vector2 moveDirection;
        public bool jumpPressed;
        public bool jumpHeld;
        public bool dodgePressed;   // 単押し回避
        public bool sprintHeld;     // 長押しスプリント
        public AttackInputType? attackInput;
        public bool guardHeld;
        public bool interactPressed;
        public bool cooperationPressed;
        public bool weaponSwitchPressed;
        public bool gripSwitchPressed;
        public bool menuPressed;
        public bool mapPressed;
        public float chargeMultiplier;
    }

    /// <summary>
    /// 攻撃時に生成されるダメージ情報パケット。
    /// DamageDealerが生成し、DamageReceiverに渡す。
    /// </summary>
    public struct DamageData
    {
        public int attackerHash;
        public int defenderHash;
        public ElementalStatus damage;
        public float motionValue;
        public Vector2 knockbackForce;
        public Element attackElement;
        public StatusEffectInfo statusEffectInfo;
        public AttackFeature feature;
        public float armorBreakValue;
        public float justGuardResistance;
        public bool isProjectile;
    }

    public struct DamageResult
    {
        public int totalDamage;
        public GuardResult guardResult;
        public HitReaction hitReaction;
        public SituationalBonus situationalBonus;
        public bool isCritical;
        public bool isKill;
        public float armorDamage;
        public StatusEffectId appliedEffect;
    }

    /// <summary>
    /// 行動中に発生する特殊効果（アーマー、無敵、ダメージ軽減など）。
    /// 開始時間と持続時間で発生区間を制御する。
    /// </summary>
    [Serializable, InlineProperty]
    public struct ActionEffect
    {
        [EnumToggleButtons] public ActionEffectType type;
        [MinValue(0), LabelText("開始(秒)")] public float startTime;
        [MinValue(0), LabelText("持続(秒)")] public float duration;
        [LabelText("効果量")] public float value;

        public float EndTime => startTime + duration;

        public bool IsActive(float elapsedTime)
        {
            return elapsedTime >= startTime && elapsedTime < EndTime;
        }
    }

    [Serializable]
    public struct AttackMotionData
    {
        [FoldoutGroup("基本")]
        [LabelText("行動名")] public string actionName;
        [FoldoutGroup("基本")]
        [MinValue(0)] public float motionValue;
        [FoldoutGroup("基本")]
        [EnumToggleButtons] public Element attackElement;
        [FoldoutGroup("基本")]
        public AttackFeature feature;

        [FoldoutGroup("コスト")]
        [MinValue(0)] public float staminaCost;
        [FoldoutGroup("コスト")]
        [MinValue(0)] public float mpCost;

        [FoldoutGroup("ヒット")]
        [MinValue(1)] public int maxHitCount;
        [FoldoutGroup("ヒット")]
        [MinValue(0)] public float armorBreakValue;

        [FoldoutGroup("ノックバック")]
        public Vector2 knockbackForce;

        [FoldoutGroup("状態異常")]
        [InlineProperty] public StatusEffectInfo statusEffect;

        [FoldoutGroup("移動")]
        [MinValue(0)] public float attackMoveDistance;
        [FoldoutGroup("移動")]
        [MinValue(0)] public float attackMoveDuration;
        [FoldoutGroup("移動")]
        public AttackContactType contactType;

        [FoldoutGroup("コンボ")]
        public bool isAutoChain;
        [FoldoutGroup("コンボ")]
        public bool isChainEndPoint;
        [FoldoutGroup("コンボ")]
        [MinValue(0)] public float inputWindow;

        [FoldoutGroup("ガード関連")]
        [Range(0f, 100f)] public float justGuardResistance;

        [FoldoutGroup("行動特殊効果")]
        [ListDrawerSettings(ShowFoldout = true)]
        public ActionEffect[] actionEffects;
    }
}
