using System;

namespace Game.Core
{
    /// <summary>装備定義が供給する加算値。未設定はすべて補正なしとして扱う。</summary>
    [Serializable]
    public struct EquipmentStatModifiers
    {
        public StatModifier attributes;
        public ElementalStatus attackBonus;
        public ElementalStatus defenseBonus;
        public int bonusHp;
        public int bonusMp;
        public float bonusStamina;
        public float bonusArmor;
        public float strengthScaling;
        public float dexterityScaling;
        public float intelligenceScaling;
        public bool overridesGuard;
        public GuardStats guardStats;
    }

    /// <summary>補正を持たない既存IEquippableも引き続き使用できる任意の入力契約。</summary>
    public interface IEquipmentStatsProvider
    {
        EquipmentStatModifiers StatModifiers { get; }
    }

    public struct EquipmentDerivedStats
    {
        public CharacterVitals vitals;
        public CombatStats combat;
        /// <summary>素体・成長を含まない、全装備による6能力の加算合計。</summary>
        public StatModifier attributeModifiers;
    }
}
