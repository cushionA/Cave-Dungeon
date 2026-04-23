using System;
using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// HP/MP/stamina/armor/position - per-character vitals stored in SoA container.
    /// </summary>
    [Serializable]
    public struct CharacterVitals
    {
        public int currentHp;
        public int maxHp;
        public int currentMp;
        public int maxMp;
        public float currentStamina;
        public float maxStamina;
        public float currentArmor;
        public float maxArmor;
        public Vector2 position;
        public int level;
        public float staminaRecoveryRate;     // スタミナ回復速度(/秒)
        public float staminaRecoveryDelay;    // 消費後回復開始遅延(秒)
        public byte hpRatio;                  // HP割合(0-100) AI判定用キャッシュ
        public byte mpRatio;                  // MP割合(0-100) AI判定用キャッシュ
    }

    /// <summary>
    /// 7属性別の攻撃力・防御力 + ガード + ノックバック耐性。
    /// 弱点ダメージボーナス・クリティカルヒット機構は仕様外のため非搭載
    /// (弱点は defense の属性別低さで表現)。
    /// </summary>
    [Serializable]
    public struct CombatStats
    {
        public ElementalStatus attack;    // 7属性別攻撃力
        public ElementalStatus defense;   // 7属性別防御力
        public GuardStats guardStats;

        /// <summary>
        /// ノックバック耐性（0.0-1.0）。HpArmorLogic.CalculateKnockback で
        /// knockbackForce に (1 - resistance) を乗じる。1.0 で完全無効化。
        /// </summary>
        public float knockbackResistance;
    }

    /// <summary>
    /// All character state flags bit-packed into a single ulong.
    /// Layout:
    ///   [0-2]   CharacterBelong (3 bits)
    ///   [3-8]   ActState (6 bits)
    ///   [9-17]  CharacterFeature (9 bits)
    ///   [18-23] Reserved/SpecialEffect (6 bits)
    ///   [24-35] RecognizeObjectType (12 bits)
    ///   [36-43] BrainEventFlagType (8 bits)
    ///   [44-59] AbilityFlag (16 bits)
    ///   [60-63] Reserved (4 bits)
    /// </summary>
    public struct CharacterFlags
    {
        public ulong flags;

        // Belong: bits 0-2 (3 bits)
        public CharacterBelong Belong
        {
            get => (CharacterBelong)(flags & 0x7UL);
            set => flags = (flags & ~0x7UL) | ((ulong)value & 0x7UL);
        }

        // ActState: bits 3-8 (6 bits)
        public ActState ActState
        {
            get => (ActState)((flags >> 3) & 0x3FUL);
            set => flags = (flags & ~(0x3FUL << 3)) | (((ulong)value & 0x3FUL) << 3);
        }

        // Feature: bits 9-17 (9 bits)
        public CharacterFeature Feature
        {
            get => (CharacterFeature)((flags >> 9) & 0x1FFUL);
            set => flags = (flags & ~(0x1FFUL << 9)) | (((ulong)value & 0x1FFUL) << 9);
        }

        // RecognizeObjectType: bits 24-35 (12 bits)
        public ushort RecognizeObjectType
        {
            get => (ushort)((flags >> 24) & 0xFFFUL);
            set => flags = (flags & ~(0xFFFUL << 24)) | (((ulong)value & 0xFFFUL) << 24);
        }

        // BrainEventFlagType: bits 36-43 (8 bits)
        public byte BrainEventFlags
        {
            get => (byte)((flags >> 36) & 0xFFUL);
            set => flags = (flags & ~(0xFFUL << 36)) | (((ulong)value & 0xFFUL) << 36);
        }

        // AbilityFlags: bits 44-59 (16 bits)
        public AbilityFlag AbilityFlags
        {
            get => (AbilityFlag)((flags >> 44) & 0xFFFFUL);
            set => flags = (flags & ~(0xFFFFUL << 44)) | (((ulong)value & 0xFFFFUL) << 44);
        }

        public static CharacterFlags Pack(CharacterBelong belong, CharacterFeature feature, AbilityFlag ability)
        {
            CharacterFlags f = default;
            f.Belong = belong;
            f.Feature = feature;
            f.AbilityFlags = ability;
            return f;
        }

        public static CharacterFlags Pack(CharacterBelong belong, CharacterFeature feature,
            ActState actState, AbilityFlag ability)
        {
            CharacterFlags f = default;
            f.Belong = belong;
            f.Feature = feature;
            f.ActState = actState;
            f.AbilityFlags = ability;
            return f;
        }
    }

    /// <summary>
    /// Movement parameters for a character.
    /// </summary>
    [Serializable]
    public struct MoveParams
    {
        public float moveSpeed;
        public float jumpForce;
        public float dashSpeed;
        public float dashDuration;
        public float gravityScale;
        public float weightRatio;
        public float jumpStaminaCost;
        public float dodgeStaminaCost;
        public float sprintStaminaPerSecond;
        /// <summary>回避（ドッジ）持続時間（秒）。0 以下は GroundMovementLogic のデフォルト値を使用。</summary>
        public float dodgeDuration;
        /// <summary>回避時の moveSpeed 倍率。0 以下は GroundMovementLogic のデフォルト値を使用。</summary>
        public float dodgeSpeedMultiplier;
        /// <summary>スプリント時の moveSpeed 倍率。0 以下は GroundMovementLogic のデフォルト値を使用。</summary>
        public float sprintSpeedMultiplier;
    }

    /// <summary>
    /// 装備由来のステータス合計。SoAコンテナで管理。
    /// </summary>
    [Serializable]
    public struct EquipmentStatus
    {
        public int weaponId;
        public int shieldId;
        public int coreId;
        public GripMode gripMode;
        public ElementalStatus finalAttack;
        public ElementalStatus finalDefense;
        public GuardStats finalGuardStats;
        public AbilityFlag activeFlags;
        public float weightRatio;
        public float totalWeight;
        public float maxWeightCapacity;
        public float justGuardStartTime;
        public float justGuardDuration;
    }

    /// <summary>
    /// キャラクターの状態異常蓄積・アクティブ効果。SoAコンテナで管理。
    /// </summary>
    [Serializable]
    public struct CharacterStatusEffects
    {
        // 各状態異常の蓄積値（11種）
        public float poisonAccum;
        public float burnAccum;
        public float bleedAccum;
        public float stunAccum;
        public float freezeAccum;
        public float paralyzeAccum;
        public float slowAccum;
        public float blindAccum;
        public float silenceAccum;
        public float weaknessAccum;
        public float curseAccum;

        // アクティブ効果スロット（最大3）
        public StatusEffectId activeSlot0;
        public float remainTime0;
        public StatusEffectId activeSlot1;
        public float remainTime1;
        public StatusEffectId activeSlot2;
        public float remainTime2;
    }

    /// <summary>
    /// アニメーション状態。AI判定でAction Maskingとframe advantage計算に使用。
    /// BaseCharacterのアニメーションコントローラから毎フレーム更新される。
    /// フィールド順序はメモリアライメント最適化済み（サイズ降順）。
    /// </summary>
    public struct AnimationStateData
    {
        /// <summary>フェーズ内の正規化時間（0.0〜1.0）</summary>
        public float normalizedTime;          // 4 bytes, offset 0
        /// <summary>アクション可能になるまでのフレーム数</summary>
        public short framesUntilActionable;   // 2 bytes, offset 4
        /// <summary>現在のアニメーションフェーズ</summary>
        public AnimationPhase currentPhase;   // 1 byte,  offset 6
        /// <summary>攻撃モーションID（0 = 攻撃中でない）</summary>
        public byte currentMoveId;            // 1 byte,  offset 7
        /// <summary>キャンセル可能か</summary>
        public bool isCancelable;             // 1 byte,  offset 8
        /// <summary>キャンセル不能な攻撃フェーズか</summary>
        public bool isCommitted;              // 1 byte,  offset 9
    }
}
