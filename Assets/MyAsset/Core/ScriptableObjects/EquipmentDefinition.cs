using System;
using UnityEngine;

namespace Game.Core
{
    [CreateAssetMenu(fileName = "Equipment", menuName = "Game/Equipment Definition")]
    public sealed class EquipmentDefinition : ScriptableObject, IEquippable, IEquipmentStatsProvider
    {
        [SerializeField] private EquipSlot _slot;
        [SerializeField, Min(0)] private int _weight;
        [SerializeField] private AbilityFlag _grantedFlags;
        [SerializeField] private EquipmentStatModifiers _statModifiers;
        [SerializeField] private StatusEffectApplication[] _onHitStatusEffects = Array.Empty<StatusEffectApplication>();

        public EquipSlot Slot => _slot;
        public int Weight => _weight;
        public AbilityFlag GrantedFlags => _grantedFlags;
        public EquipmentStatModifiers StatModifiers => _statModifiers;
        public StatusEffectApplicationData[] OnHitEffects => StatusEffectApplicationLogic.Resolve(_onHitStatusEffects);

        public void OnEquip(int ownerHash) { }
        public void OnUnequip(int ownerHash) { }
    }
}
