using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// 攻撃・魔法・スキルを1つのアセットで完全に定義するScriptableObject。
    /// </summary>
    [CreateAssetMenu(fileName = "NewAttackInfo", menuName = "Game/AttackInfo")]
    public class AttackInfo : ScriptableObject
    {
        [TitleGroup("基本情報")]
        public string attackName;

        [TitleGroup("基本情報")]
        [EnumToggleButtons]
        public AttackCategory category;

        [TitleGroup("モーション")]
        [InlineProperty]
        public MotionInfo motionInfo;

        [TitleGroup("ダメージ")]
        public ElementalStatus baseDamage;

        [TitleGroup("ダメージ")]
        [MinValue(0)]
        public float damageMultiplier;

        [TitleGroup("エフェクト・サウンド")]
        [InlineProperty]
        public EffectSoundInfo effectSoundInfo;

        [TitleGroup("飛翔体")]
        [InlineProperty]
        public ProjectileInfo projectileInfo;

        [TitleGroup("吹き飛ばし")]
        [InlineProperty]
        public KnockbackInfo knockbackInfo;

        [TitleGroup("状態異常")]
        [InlineProperty]
        public StatusEffectInfo statusEffectInfo;

        [TitleGroup("コスト")]
        [MinValue(0)]
        public float mpCost;

        [TitleGroup("コスト")]
        [MinValue(0)]
        public float staminaCost;

        [TitleGroup("AI認識用")]
        [InlineProperty]
        public AIAttackEvaluation aiEvaluation;

        [TitleGroup("攻撃移動")]
        [MinValue(0)]
        public float attackMoveDistance;

        [TitleGroup("攻撃移動")]
        [MinValue(0)]
        public float attackMoveDuration;

        [TitleGroup("攻撃移動")]
        public AttackContactType contactType;

        [TitleGroup("パリィ・ガード")]
        public bool isParriable;

        [TitleGroup("パリィ・ガード")]
        [MinValue(0)]
        public float armorBreakValue;

        [TitleGroup("属性")]
        [EnumToggleButtons]
        public Element attackElement;

        [TitleGroup("コンボ")]
        public bool isAutoChain;

        [TitleGroup("コンボ")]
        public bool isChainEndPoint;

        [TitleGroup("コンボ")]
        [MinValue(0)]
        public float inputWindow;

        [TitleGroup("コンボ")]
        [Range(0f, 100f)]
        public float justGuardResistance;
    }

    [Serializable]
    public struct MotionInfo
    {
        [FoldoutGroup("タイミング")]
        [MinValue(0)] public float preMotionDuration;
        [FoldoutGroup("タイミング")]
        [MinValue(0)] public float activeMotionDuration;
        [FoldoutGroup("タイミング")]
        [MinValue(0)] public float recoveryDuration;

        [FoldoutGroup("チャージ")]
        [MinValue(0)] public float chargeTimeMin;
        [FoldoutGroup("チャージ")]
        [MinValue(0)] public float chargeTimeMax;
        [FoldoutGroup("チャージ")]
        [MinValue(0)] public float chargeDamageMultiplier;

        [FoldoutGroup("アニメーション")]
        public AnimationClip preMotionClip;
        [FoldoutGroup("アニメーション")]
        public AnimationClip activeClip;
        [FoldoutGroup("アニメーション")]
        public AnimationClip chargeLoopClip;
        [FoldoutGroup("アニメーション")]
        public AnimationClip recoveryClip;

        [FoldoutGroup("行動特殊効果")]
        [ListDrawerSettings(ShowFoldout = true)]
        public ActionEffect[] actionEffects;
    }

    [Serializable]
    public struct EffectSoundInfo
    {
        [FoldoutGroup("エフェクト")]
        public GameObject hitEffect;
        [FoldoutGroup("エフェクト")]
        public GameObject castEffect;
        [FoldoutGroup("エフェクト")]
        public GameObject chargeEffect;
        [FoldoutGroup("エフェクト")]
        public GameObject trailEffect;

        [FoldoutGroup("サウンド")]
        public AudioClip attackSound;
        [FoldoutGroup("サウンド")]
        public AudioClip hitSound;
        [FoldoutGroup("サウンド")]
        public AudioClip chargeSound;
        [FoldoutGroup("サウンド")]
        public AudioClip chargeCompleteSound;
    }

    [Serializable]
    public struct ProjectileInfo
    {
        [ToggleGroup("hasProjectile", "飛翔体設定")]
        public bool hasProjectile;

        [ToggleGroup("hasProjectile")]
        public GameObject projectilePrefab;
        [ToggleGroup("hasProjectile")]
        public BulletMoveType trajectory;
        [ToggleGroup("hasProjectile")]
        [MinValue(0)] public float speed;
        [ToggleGroup("hasProjectile")]
        [MinValue(0)] public float lifetime;
        [ToggleGroup("hasProjectile")]
        [MinValue(0)] public float homingStrength;
        [ToggleGroup("hasProjectile")]
        [MinValue(0)] public int pierceCount;
        [ToggleGroup("hasProjectile")]
        [MinValue(0)] public float aoeRadius;
        [ToggleGroup("hasProjectile")]
        [MinValue(1)] public int projectileCount;
        [ToggleGroup("hasProjectile")]
        [Range(0f, 360f)] public float spreadAngle;
    }

    [Serializable]
    public struct KnockbackInfo
    {
        [ToggleGroup("hasKnockback", "ノックバック設定")]
        public bool hasKnockback;

        [ToggleGroup("hasKnockback")]
        public Vector2 force;
        [ToggleGroup("hasKnockback")]
        [MinValue(0)] public float stunDuration;
        [ToggleGroup("hasKnockback")]
        public bool groundBounce;
        [ToggleGroup("hasKnockback")]
        public bool wallBounce;
        [ToggleGroup("hasKnockback")]
        [MinValue(0)] public float gravityScale;
    }

    [Serializable]
    public struct AIAttackEvaluation
    {
        [MinValue(0)] public float effectiveRangeMin;
        [MinValue(0)] public float effectiveRangeMax;
        [Range(0f, 10f)] public float threatLevel;
        public bool isBlockable;
        public bool isParriable;
        [MinValue(0)] public float totalWindupTime;
        [Range(0f, 1f)] public float preferredUseHpThreshold;
        public byte priority;
    }
}
