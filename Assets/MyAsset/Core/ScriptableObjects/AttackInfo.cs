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

        [TitleGroup("モーション")]
        [Tooltip("Recovery中のキャンセル可能ポイント（0=Recovery開始直後、1=Recovery終了時、-1=キャンセル不可）。行動データ側に持たせることで全行動タイプで統一管理する。")]
        [Range(-1f, 1f)]
        public float cancelPoint = -1f;

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
        public int mpCost;

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

        [TitleGroup("攻撃移動")]
        [Tooltip("空中攻撃時の移動方向（正規化済み）。yを正にすると上昇")]
        public Vector2 aerialMoveDirection;

        [TitleGroup("空中攻撃")]
        [Tooltip("空中攻撃中の浮遊時間（秒）")]
        [MinValue(0)]
        public float airHangDuration = 0.3f;

        [TitleGroup("空中攻撃")]
        [Tooltip("空中攻撃中の重力スケール（通常に対する倍率）")]
        [MinValue(0)]
        public float airHangGravityScale = 0.1f;

        [TitleGroup("空中攻撃")]
        [Tooltip("空中移動速度のデフォルト値（attackMoveDistance未設定時）")]
        [MinValue(0)]
        public float aerialMoveSpeedDefault = 3f;

        [TitleGroup("落下攻撃")]
        [Tooltip("落下攻撃として使用する場合の重力倍率")]
        [MinValue(1)]
        public float divingGravityMultiplier = 3f;

        [TitleGroup("落下攻撃")]
        [Tooltip("落下攻撃中の前方移動速度")]
        [MinValue(0)]
        public float divingForwardSpeed = 1f;

        [TitleGroup("落下攻撃")]
        [Tooltip("落下攻撃の着地後リカバリー時間（秒）。0なら即完了")]
        [MinValue(0)]
        public float divingLandingRecoveryDuration = 0.3f;

        [TitleGroup("パリィ・ガード")]
        public bool isParriable;

        [TitleGroup("パリィ・ガード")]
        [MinValue(0)]
        public float armorBreakValue;

        [TitleGroup("攻撃特性")]
        [EnumToggleButtons]
        public AttackFeature feature;

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
        [FoldoutGroup("サウンド")]
        [Tooltip("落下攻撃着地時のSE")]
        public AudioClip landingSound;
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
