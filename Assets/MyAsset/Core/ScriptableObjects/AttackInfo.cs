using System;
using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// 攻撃・魔法・スキルを1つのアセットで完全に定義するScriptableObject。
    /// </summary>
    [CreateAssetMenu(fileName = "NewAttackInfo", menuName = "Game/AttackInfo")]
    public class AttackInfo : ScriptableObject
    {
        [Header("基本情報")]
        public string attackName;
        public AttackCategory category;

        [Header("モーション")]
        public MotionInfo motionInfo;

        [Header("ダメージ")]
        public ElementalStatus baseDamage;
        public float damageMultiplier;

        [Header("エフェクト・サウンド")]
        public EffectSoundInfo effectSoundInfo;

        [Header("飛翔体")]
        public ProjectileInfo projectileInfo;

        [Header("吹き飛ばし")]
        public KnockbackInfo knockbackInfo;

        [Header("状態異常")]
        public StatusEffectInfo statusEffectInfo;

        [Header("コスト")]
        public float mpCost;
        public float staminaCost;

        [Header("AI認識用")]
        public AIAttackEvaluation aiEvaluation;

        [Header("攻撃移動")]
        public float attackMoveDistance;
        public float attackMoveDuration;
        public AttackContactType contactType;

        [Header("パリィ・ガード")]
        public bool isParriable;
        public float armorBreakValue;

        [Header("属性")]
        public Element attackElement;

        [Header("コンボ")]
        public bool isAutoChain;
        public bool isChainEndPoint;
        public float inputWindow;
        public float justGuardResistance;
    }

    [Serializable]
    public struct MotionInfo
    {
        public float preMotionDuration;
        public float activeMotionDuration;
        public float recoveryDuration;
        public float chargeTimeMin;
        public float chargeTimeMax;
        public float chargeDamageMultiplier;
        public AnimationClip preMotionClip;
        public AnimationClip activeClip;
        public AnimationClip chargeLoopClip;
        public AnimationClip recoveryClip;
    }

    [Serializable]
    public struct EffectSoundInfo
    {
        public GameObject hitEffect;
        public GameObject castEffect;
        public GameObject chargeEffect;
        public GameObject trailEffect;
        public AudioClip attackSound;
        public AudioClip hitSound;
        public AudioClip chargeSound;
        public AudioClip chargeCompleteSound;
    }

    [Serializable]
    public struct ProjectileInfo
    {
        public bool hasProjectile;
        public GameObject projectilePrefab;
        public BulletMoveType trajectory;
        public float speed;
        public float lifetime;
        public float homingStrength;
        public int pierceCount;
        public float aoeRadius;
        public int projectileCount;
        public float spreadAngle;
    }

    [Serializable]
    public struct KnockbackInfo
    {
        public bool hasKnockback;
        public Vector2 force;
        public float stunDuration;
        public bool groundBounce;
        public bool wallBounce;
        public float gravityScale;
    }

    [Serializable]
    public struct AIAttackEvaluation
    {
        public float effectiveRangeMin;
        public float effectiveRangeMax;
        public float threatLevel;
        public bool isBlockable;
        public bool isParriable;
        public float totalWindupTime;
        public float preferredUseHpThreshold;
        public byte priority;
    }
}
