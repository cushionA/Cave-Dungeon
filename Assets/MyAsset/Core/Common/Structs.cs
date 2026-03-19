using System;
using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// 7属性（斬撃/打撃/刺突/炎/雷/聖/闇）のステータス値。
    /// 攻撃力・防御力・ダメージの各チャネルで使用する。
    /// </summary>
    [Serializable]
    public struct ElementalStatus
    {
        public int slash;
        public int strike;
        public int pierce;
        public int fire;
        public int thunder;
        public int light;
        public int dark;

        public int Total => slash + strike + pierce + fire + thunder + light + dark;
        public int PhysicalTotal => slash + strike + pierce;

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
    [Serializable]
    public struct GuardStats
    {
        public float slashCut;
        public float strikeCut;
        public float pierceCut;
        public float fireCut;
        public float thunderCut;
        public float lightCut;
        public float darkCut;
        public float guardStrength;
        public float statusCut;
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
        public float accumulateValue;   // 1ヒット蓄積量
        public float duration;          // 発症持続時間(秒)
        public float tickDamage;        // tick毎ダメージ(DoT)
        public float tickInterval;      // tick間隔(秒)
        public float modifier;          // 効果量(速度低下率等)
        public byte maxStack;           // 最大スタック数(0=スタック不可)
    }

    /// <summary>
    /// 物理タイプ別耐性（斬撃/打撃/刺突）。
    /// </summary>
    [Serializable]
    public struct PhysicalResistance
    {
        public float slashResist;
        public float strikeResist;
        public float pierceResist;
    }

    public struct MovementInfo
    {
        public Vector2 moveDirection;
        public bool jumpPressed;
        public bool jumpHeld;
        public bool dashPressed;
        public AttackInputType? attackInput;
        public bool guardHeld;
        public bool interactPressed;
        public bool cooperationPressed;
        public bool weaponSwitchPressed;
        public bool gripSwitchPressed;
        public bool menuPressed;
        public bool mapPressed;
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
        public Vector2 knockbackForce;          // XY軸別の吹き飛ばし強度
        public Element attackElement;           // 主属性
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
        public bool isCritical;
        public bool isKill;
        public float armorDamage;
        public StatusEffectId appliedEffect;
    }

    [Serializable]
    public struct AttackMotionData
    {
        [Header("基本")]
        public float motionValue;
        public Element attackElement;
        public AttackFeature feature;

        [Header("コスト")]
        public float staminaCost;
        public float mpCost;

        [Header("ヒット")]
        public int maxHitCount;
        public float armorBreakValue;

        [Header("ノックバック")]
        public Vector2 knockbackForce;

        [Header("状態異常")]
        public StatusEffectInfo statusEffect;

        [Header("移動")]
        public float attackMoveDistance;
        public float attackMoveDuration;
        public AttackContactType contactType;

        [Header("コンボ")]
        public bool isAutoChain;
        public bool isChainEndPoint;
        public float inputWindow;

        [Header("ガード関連")]
        public float justGuardResistance;
    }
}
