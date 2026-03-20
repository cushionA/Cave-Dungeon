using System;

namespace Game.Core
{
    // ===== 属性（物理3種 + 属性4種 = 7属性） =====

    [Flags]
    public enum Element : byte
    {
        None    = 0,
        Slash   = 1 << 0,  // 斬撃
        Strike  = 1 << 1,  // 打撃
        Pierce  = 1 << 2,  // 刺突
        Fire    = 1 << 3,  // 炎
        Thunder = 1 << 4,  // 雷
        Light   = 1 << 5,  // 聖
        Dark    = 1 << 6,  // 闇
    }

    // ===== 装備 =====

    public enum EquipSlot : byte
    {
        Weapon,
        Shield,
        Core,
    }

    public enum GripMode : byte
    {
        OneHanded,
        TwoHanded,
    }

    public enum SkillSource : byte
    {
        Weapon,
        Shield,
    }

    public enum PerformanceType : byte
    {
        DodgeSpeed,
        StaminaRecovery,
        AttackSpeed,
        DodgeDistance,
    }

    [Flags]
    public enum AbilityFlag : uint
    {
        None       = 0,
        WallKick   = 1 << 0,
        WallCling  = 1 << 1,
        DoubleJump = 1 << 2,
        AirDash    = 1 << 3,
        Swim       = 1 << 4,
        GlideFloat = 1 << 5,
    }

    // ===== 攻撃 =====

    public enum AttackInputType : byte
    {
        LightAttack,
        HeavyAttack,
        ChargeLight,
        ChargeHeavy,
        DropAttack,
        Skill,
        AerialLight,
        AerialHeavy,
    }

    [Flags]
    public enum AttackFeature : ushort
    {
        None           = 0,
        Light          = 1 << 0,
        Heavy          = 1 << 1,
        Unparriable    = 1 << 2,
        SelfRecover    = 1 << 3,
        HitRecover     = 1 << 4,
        SuperArmor     = 1 << 5,
        GuardAttack    = 1 << 6,
        DropAttack     = 1 << 7,
        PositiveEffect = 1 << 8,
        NegativeEffect = 1 << 9,
        BackAttack     = 1 << 10,
        JustGuardImmune = 1 << 11,
    }

    public enum AttackContactType : byte
    {
        PassThrough,
        StopOnHit,
        Carry,
    }

    // ===== ガード =====

    public enum GuardType : byte
    {
        Small,
        Normal,
        Tower,
        Wall,
    }

    public enum GuardResult : byte
    {
        NoGuard,
        Guarded,
        JustGuard,
        GuardBreak,
        EnhancedGuard,
    }

    // ===== キャラクター =====

    [Flags]
    public enum CharacterBelong : byte
    {
        Ally    = 1 << 0,
        Enemy   = 1 << 1,
        Neutral = 1 << 2,
    }

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

    public enum CompanionStance : byte
    {
        Aggressive,
        Defensive,
        Supportive,
        Passive,
    }

    // ===== Ability =====

    public enum AbilityType : byte
    {
        Movement,
        Attack,
        Guard,
        Dodge,
        Interaction,
        Skill,
    }

    public enum AbilityExclusiveGroup : byte
    {
        None,
        Movement,
        Aerial,
        Combat,
    }

    // ===== キャラクターランク =====

    public enum CharacterRank : byte
    {
        F, E, D, C, B, A, S, SS,
    }

    // ===== 行動状態 =====

    public enum ActState : byte
    {
        Neutral,
        Running,
        Jumping,
        Falling,
        OnGround,
        Guarding,
        AttackPrep,
        Attacking,
        AttackRecovery,
        Knockbacked,
        Stunned,
        Dead,
        Custom,
    }

    // ===== 攻撃カテゴリ =====

    public enum AttackCategory : byte
    {
        Melee,
        Ranged,
        Magic,
        Skill,
        Support,
        Summon,
    }

    // ===== 状態異常 =====

    public enum StatusEffectId : byte
    {
        None,
        Poison,
        Burn,
        Bleed,
        Stun,
        Freeze,
        Paralyze,
        Slow,
        Blind,
        Silence,
        Weakness,
        Curse,
        Confusion,
    }

    // ===== インタラクション・アイテム =====

    public enum InteractionType : byte
    {
        SavePoint,
        Shop,
        NpcDialog,
        Chest,
        Door,
        Switch,
    }

    public enum ItemCategory : byte
    {
        Weapon,
        Shield,
        Core,
        Consumable,
        PlayerMagic,
        CompanionMagic,
        Material,
        KeyItem,
        Flavor,
    }

    // ===== Section 2: AI・魔法・ゲート =====

    public enum ActionExecType : byte
    {
        Attack,
        Cast,
        Instant,
        Sustained,
        Broadcast,
    }

    public enum InstantAction : byte
    {
        Dodge,
        WarpToTarget,
        WarpBehind,
        UseItem,
        InteractObject,
        BodySlam,
    }

    public enum SustainedAction : byte
    {
        MoveToTarget,
        Follow,
        Retreat,
        Flee,
        Patrol,
        Guard,
        Flank,
        ShieldDeploy,
        Decoy,
        Cover,
        Stealth,
        Orbit,
        MpRecover,
    }

    public enum BroadcastAction : byte
    {
        DesignateTarget,
        Rally,
        Scatter,
        Taunt,
        FocusFire,
        Disengage,
        ModeSync,
    }

    public enum GateType : byte
    {
        Clear,
        Ability,
        Key,
        Elemental,
    }

    public enum MagicType : byte
    {
        Attack,
        Recover,
        Support,
        Summon,
    }

    public enum CastType : byte
    {
        None,
        Short,
        Normal,
        Long,
    }

    public enum FireType : byte
    {
        None,
        Short,
        Normal,
        Swing,
        Special,
    }

    public enum BulletMoveType : byte
    {
        Straight,
        Homing,
        Angle,
        Rain,
        Set,
        Stop,
    }

    [Flags]
    public enum BulletFeature : ushort
    {
        None        = 0,
        Pierce      = 1 << 0,
        Explode     = 1 << 1,
        Reflect     = 1 << 2,
        Gravity     = 1 << 3,
        Platform    = 1 << 4,
        Shield      = 1 << 5,
        AreaEffect  = 1 << 6,
        Knockback   = 1 << 7,
    }

    public enum ChildBulletTrigger : byte
    {
        None,
        OnActivate,
        OnTimer,
        OnHit,
        OnDestroy,
    }

    public enum AIConditionType : byte
    {
        None = 0,
        Count,
        HpRatio,
        MpRatio,
        StaminaRatio,
        ArmorRatio,
        Distance,
        NearbyFaction,
        ProjectileNear,
        ObjectNearby,
        DamageScore,
        EventFired,
        SelfActState,
    }

    public enum TargetSortKey : byte
    {
        Distance,
        HpRatio,
        HpValue,
        AttackPower,
        DefensePower,
        TargetingCount,
        LastAttacker,
        DamageScore,
        Self,
        Player,
        Sister,
    }

    public enum CompareOp : byte
    {
        Less,
        LessEqual,
        Equal,
        GreaterEqual,
        Greater,
        NotEqual,
        InRange,    // operandA <= 値 <= operandB
        HasFlag,    // (値 & operandA) == operandA（AND判定）
        HasAny,     // (値 & operandA) != 0（OR判定）
    }

    // ===== Section 3: ボス・召喚・混乱・属性ゲート・バックトラック =====

    /// <summary>
    /// ボスフェーズ遷移条件の種類。
    /// </summary>
    public enum PhaseConditionType : byte
    {
        HpThreshold,       // HP割合が閾値以下
        Timer,             // 経過時間
        ActionCount,       // 特定行動の累計回数
        AllAddsDefeated,   // 雑魚全滅
        Custom,            // スクリプトで定義
    }

    /// <summary>
    /// 召喚獣のタイプ。
    /// </summary>
    public enum SummonType : byte
    {
        Combat,      // 戦闘用召喚獣
        Utility,     // 探索補助（足場、照明等）
        Decoy,       // 囮（敵ヘイトを集める）
    }

    /// <summary>
    /// 属性ゲートが要求する属性。
    /// ElementalRequirement → Element のマッピングは ElementalRequirementMapper で行う。
    /// </summary>
    public enum ElementalRequirement : byte
    {
        Fire,        // 炎で点火/溶解
        Thunder,     // 雷で通電/起動
        Light,       // 聖で浄化/照射
        Dark,        // 闇で暗幕/隠し通路
        Slash,       // 斬撃で切断
        Strike,      // 打撃で破壊
        Pierce,      // 刺突で穿孔
    }

    /// <summary>
    /// バックトラック報酬の種別。
    /// </summary>
    public enum BacktrackRewardType : byte
    {
        Item,         // アイテム
        Currency,     // 通貨
        AbilityOrb,   // 新能力
        Shortcut,     // ショートカット開通
        Lore,         // 世界観テキスト
    }

    /// <summary>
    /// ボスアリーナの状態。
    /// </summary>
    public enum ArenaState : byte
    {
        Open,        // 通常通行可能
        Locked,      // 戦闘中ロック
        Cleared,     // クリア済み（永続開放）
    }

    // ===== Section 4: チャレンジ・テンプレート・リーダーボード =====

    /// <summary>
    /// チャレンジモードの種別。
    /// </summary>
    public enum ChallengeType : byte
    {
        BossRush,
        TimeAttack,
        Survival,
        Restriction,
        ScoreAttack,
    }

    /// <summary>
    /// チャレンジの達成ランク。
    /// </summary>
    public enum ChallengeRank : byte
    {
        None,
        Bronze,
        Silver,
        Gold,
        Platinum,
    }

    /// <summary>
    /// チャレンジの進行状態。
    /// </summary>
    public enum ChallengeState : byte
    {
        Ready,
        Running,
        Completed,
        Failed,
    }

    /// <summary>
    /// AIテンプレートのカテゴリ。
    /// </summary>
    public enum AITemplateCategory : byte
    {
        General,
        BossFight,
        MobClear,
        SupportFocus,
        Aggressive,
        Defensive,
        Custom,
    }
}
