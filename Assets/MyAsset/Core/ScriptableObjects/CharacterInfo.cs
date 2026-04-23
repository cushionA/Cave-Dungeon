using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// キャラクターの「何者であるか」と「何ができるか」を定義するScriptableObject。
    /// ボス・雑魚・NPC・プレイヤーなど、すべてのキャラクターがこのクラスで定義される。
    /// </summary>
    [CreateAssetMenu(fileName = "NewCharacterInfo", menuName = "Game/CharacterInfo")]
    public class CharacterInfo : ScriptableObject
    {
        // ─────────────────────────────────────────────
        //  キャラクター識別
        // ─────────────────────────────────────────────
        [TitleGroup("キャラクター識別")]
        [EnumToggleButtons]
        public CharacterFeature feature;

        [TitleGroup("キャラクター識別")]
        [EnumToggleButtons]
        public CharacterBelong belong;

        [TitleGroup("キャラクター識別")]
        public CharacterRank rank;

        [TitleGroup("キャラクター識別")]
        public bool canFly;

        [TitleGroup("キャラクター識別")]
        [Tooltip("同時にターゲットされる上限数")]
        [MinValue(0)]
        public int targetingLimit;

        // ─────────────────────────────────────────────
        //  基礎ステータス
        // ─────────────────────────────────────────────
        [TitleGroup("基礎ステータス")]
        [MinValue(1)]
        public int maxHp;

        [TitleGroup("基礎ステータス")]
        [MinValue(0)]
        public int maxMp;

        [TitleGroup("基礎ステータス")]
        [MinValue(0)]
        public float maxStamina;

        [TitleGroup("基礎ステータス")]
        [Tooltip("スタミナ回復速度（/秒）")]
        [MinValue(0)]
        public float staminaRecoveryRate;

        [TitleGroup("基礎ステータス")]
        [Tooltip("消費後の回復開始までの遅延（秒）")]
        [MinValue(0)]
        public float staminaRecoveryDelay;

        // ─────────────────────────────────────────────
        //  属性攻撃力・防御力（7属性）
        // ─────────────────────────────────────────────
        [TitleGroup("属性攻撃力")]
        public ElementalStatus baseAttack;

        [TitleGroup("属性防御力")]
        public ElementalStatus baseDefense;

        // ─────────────────────────────────────────────
        //  弱点・攻撃属性
        // ─────────────────────────────────────────────
        [TitleGroup("弱点・属性")]
        [EnumToggleButtons]
        public Element weakPoint;

        [TitleGroup("弱点・属性")]
        [EnumToggleButtons]
        public Element attackElement;

        // ─────────────────────────────────────────────
        //  アクション設定
        // ─────────────────────────────────────────────
        [TitleGroup("アクション設定")]
        [MinValue(0)]
        public float moveSpeed;

        [TitleGroup("アクション設定")]
        [MinValue(0)]
        public float walkSpeed;

        [TitleGroup("アクション設定")]
        [MinValue(0)]
        public float dashSpeed;

        [TitleGroup("アクション設定")]
        [MinValue(0)]
        public float jumpHeight;

        // ─────────────────────────────────────────────
        //  行動スタミナコスト
        // ─────────────────────────────────────────────
        [TitleGroup("行動スタミナコスト")]
        [Tooltip("ジャンプ時のスタミナ消費")]
        [MinValue(0)]
        public float jumpStaminaCost;

        [TitleGroup("行動スタミナコスト")]
        [Tooltip("回避（単押し）時のスタミナ消費")]
        [MinValue(0)]
        public float dodgeStaminaCost = 15f;

        [TitleGroup("行動スタミナコスト")]
        [Tooltip("スプリント時のスタミナ消費（/秒）")]
        [MinValue(0)]
        public float sprintStaminaPerSecond = 10f;

        // ─────────────────────────────────────────────
        //  移動パラメータ（キャラ別チューニング値）
        // ─────────────────────────────────────────────
        [TitleGroup("移動パラメータ")]
        [Tooltip("回避（ドッジ）の持続時間（秒）")]
        [MinValue(0)]
        public float dodgeDuration = 0.25f;

        [TitleGroup("移動パラメータ")]
        [Tooltip("回避中の移動速度倍率（moveSpeed に対する倍率）。MoveParams.dashSpeed が設定されている場合はそちらを優先")]
        [MinValue(0)]
        public float dodgeSpeedMultiplier = 2.5f;

        [TitleGroup("移動パラメータ")]
        [Tooltip("スプリント時の移動速度倍率（moveSpeed に対する倍率）")]
        [MinValue(0)]
        public float sprintSpeedMultiplier = 1.6f;

        [TitleGroup("移動パラメータ")]
        [Tooltip("接地から離れた後でもジャンプ入力を許容する猶予時間（秒）。プラットフォーマのゲームフィール補正（Coyote Time）")]
        [MinValue(0)]
        public float coyoteTime = 0.1f;

        // ─────────────────────────────────────────────
        //  アーマー
        // ─────────────────────────────────────────────
        [TitleGroup("アーマー")]
        [Tooltip("ベースアーマー最大値")]
        [MinValue(0)]
        public float maxArmor;

        [TitleGroup("アーマー")]
        [Tooltip("アーマー自然回復速度（/秒）")]
        [MinValue(0)]
        public float armorRecoveryRate;

        [TitleGroup("アーマー")]
        [Tooltip("被弾後のアーマー回復開始までの遅延（秒）")]
        [MinValue(0)]
        public float armorRecoveryDelay;

        // ─────────────────────────────────────────────
        //  耐性
        // ─────────────────────────────────────────────
        [TitleGroup("耐性")]
        [Tooltip("基礎状態異常耐性（蓄積閾値のグローバル倍率）")]
        [Range(0f, 1f)]
        public float statusResistance;

        [TitleGroup("耐性")]
        public PhysicalResistance physicalResistance;

        [TitleGroup("耐性")]
        [Tooltip("ノックバック耐性（0=無耐性、1=完全無効化）")]
        [Range(0f, 1f)]
        public float knockbackResistance;

        // ─────────────────────────────────────────────
        //  スタミナ詳細
        // ─────────────────────────────────────────────
        [TitleGroup("スタミナ詳細")]
        [Tooltip("スタミナ0時の追加回復遅延（秒）")]
        [MinValue(0)]
        public float staminaExhaustionPenalty = 2f;

        [TitleGroup("スタミナ詳細")]
        [Tooltip("スタミナ枯渇解除閾値（最大スタミナに対する割合 0-1）")]
        [Range(0f, 1f)]
        public float staminaExhaustionRecoveryRatio = 0.3f;

        // ─────────────────────────────────────────────
        //  ジャンプ詳細
        // ─────────────────────────────────────────────
        [TitleGroup("ジャンプ詳細")]
        [Tooltip("ジャンプ入力バッファ時間（秒）")]
        [MinValue(0)]
        public float jumpBufferTime = 0.1f;

        [TitleGroup("ジャンプ詳細")]
        [Tooltip("ジャンプ早期リリース時のY速度減衰率（0-1）")]
        [Range(0f, 1f)]
        public float jumpReleaseVelocityDamping = 0.5f;

        // ─────────────────────────────────────────────
        //  入力設定
        // ─────────────────────────────────────────────
        [TitleGroup("入力設定")]
        [Tooltip("移動入力デッドゾーン")]
        [Range(0f, 0.5f)]
        public float moveInputDeadzone = 0.1f;

        [TitleGroup("入力設定")]
        [Tooltip("入力バッファ持続時間（秒）")]
        [MinValue(0)]
        public float inputBufferDuration = 0.15f;

        [TitleGroup("入力設定")]
        [Tooltip("ため攻撃判定のホールド閾値（秒）")]
        [MinValue(0)]
        public float chargeThreshold = 0.3f;

        [TitleGroup("入力設定")]
        [Tooltip("スプリント/回避を分離するホールド閾値（秒）")]
        [MinValue(0)]
        public float sprintHoldThreshold = 0.25f;

        // ─────────────────────────────────────────────
        //  初期状態
        // ─────────────────────────────────────────────
        [TitleGroup("初期状態")]
        public ActState initialActState;

        // ─────────────────────────────────────────────
        //  ステータス上限値（Str/Dex/Intel/Vit/Mnd/End の順）
        // ─────────────────────────────────────────────
        [TitleGroup("ステータス上限値")]
        [Tooltip("各ステータス (Str/Dex/Intel/Vit/Mnd/End) の個別上限値。動的最大レベル算出にも使用。要素数は 6 固定。")]
        [MinValue(0)]
        public int[] statCaps = new int[] { 99, 99, 99, 99, 99, 99 };
    }
}
