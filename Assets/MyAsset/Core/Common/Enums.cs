using System;

namespace Game.Core
{
    // ===== 属性・物理タイプ =====

    [Flags]
    public enum Element : byte
    {
        None    = 0,
        Fire    = 1 << 0,
        Thunder = 1 << 1,
        Light   = 1 << 2,
        Dark    = 1 << 3,
    }

    public enum WeaponPhysicalType : byte
    {
        Slash,
        Pierce,
        Strike,
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
}
