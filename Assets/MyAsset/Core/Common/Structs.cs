using System;
using UnityEngine;

namespace Game.Core
{
    [Serializable]
    public struct ElementalStatus
    {
        public int physical;
        public int fire;
        public int thunder;
        public int light;
        public int dark;

        public int Total => physical + fire + thunder + light + dark;

        public int Get(Element element)
        {
            switch (element)
            {
                case Element.Fire:    return fire;
                case Element.Thunder: return thunder;
                case Element.Light:   return light;
                case Element.Dark:    return dark;
                default:              return physical;
            }
        }
    }

    [Serializable]
    public struct GuardStats
    {
        public float physicalCut;
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

    [Serializable]
    public struct StatusEffectApply
    {
        public StatusEffectId effectId;
        public float accumulateValue;
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

    public struct DamageData
    {
        public int attackerHash;
        public int defenderHash;
        public ElementalStatus damage;
        public float motionValue;
        public float knockbackForce;
        public Vector2 knockbackDirection;
        public WeaponPhysicalType physicalType;
        public StatusEffectApply statusEffect;
        public AttackFeature feature;
        public float armorBreakValue;
        public float justGuardResistance;
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
        public WeaponPhysicalType physicalType;
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
        public StatusEffectApply statusEffect;

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
