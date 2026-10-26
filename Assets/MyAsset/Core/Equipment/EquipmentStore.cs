using System;
using System.Collections.Generic;

namespace Game.Core
{
    public enum AttackEquipmentSource : byte
    {
        None,
        Weapon,
        Shield,
    }

    public sealed class EquipmentStore : IDisposable
    {
        private readonly Dictionary<int, EquipmentHolder> _holders = new Dictionary<int, EquipmentHolder>();
        private readonly GameEvents _events;
        public CharacterStatsSystem Stats { get; }

        public EquipmentStore(GameEvents events = null, IEquipmentWorld world = null)
        {
            _events = events;
            Stats = new CharacterStatsSystem(world, this, events);
        }

        public void Register(int ownerHash)
        {
            if (!_holders.ContainsKey(ownerHash))
            {
                EquipmentHolder holder = new EquipmentHolder(ownerHash);
                _holders.Add(ownerHash, holder);
                Stats.Register(ownerHash);
            }
        }

        public void Unregister(int ownerHash)
        {
            if (!_holders.Remove(ownerHash, out EquipmentHolder holder))
            {
                return;
            }
            Stats.Unregister(ownerHash);
            holder.Unequip(EquipSlot.Weapon);
            holder.Unequip(EquipSlot.Shield);
            holder.Unequip(EquipSlot.Core);
            if (!_holders.ContainsKey(ownerHash))
            {
                Stats.ClearEquipment(ownerHash);
            }
        }

        /// <summary>着脱を公開しない装備の読み取り専用view。</summary>
        public IReadOnlyEquipmentHolder GetHolder(int ownerHash) => _holders[ownerHash];

        public IEquippable Equip(int ownerHash, IEquippable item)
        {
            EquipmentHolder holder = _holders[ownerHash];
            IEquippable previous = holder.Equip(item);
            if (!IsCurrentHolder(ownerHash, holder)) { return previous; }
            Stats.Recalculate(ownerHash, false);
            _events?.FireEquipmentChanged(ownerHash, item.Slot);
            Stats.PublishChanges(ownerHash, holder);
            return previous;
        }

        public IEquippable Unequip(int ownerHash, EquipSlot slot)
        {
            EquipmentHolder holder = _holders[ownerHash];
            IEquippable removed = holder.Unequip(slot);
            if (removed != null && IsCurrentHolder(ownerHash, holder))
            {
                Stats.Recalculate(ownerHash, false);
                _events?.FireEquipmentChanged(ownerHash, slot);
                Stats.PublishChanges(ownerHash, holder);
            }
            return removed;
        }

        /// <summary>互換入口。恒久能力の原本と再導出はStatsが所有する。</summary>
        public bool GrantAbilityFlags(int ownerHash, AbilityFlag flags) => Stats.GrantAbilityFlags(ownerHash, flags);

        /// <summary>互換入口。キャラ能力の再計算はStatsへ委譲する。</summary>
        public void Recalculate(int ownerHash)
        {
            _ = _holders[ownerHash];
            Stats.Recalculate(ownerHash);
        }

        internal bool TryGetMutableHolder(int ownerHash, out EquipmentHolder holder) =>
            _holders.TryGetValue(ownerHash, out holder);

        internal bool IsCurrentHolder(int ownerHash, EquipmentHolder holder) =>
            _holders.TryGetValue(ownerHash, out EquipmentHolder current) && ReferenceEquals(current, holder);

        public StatusEffectApplicationData[] GetOnHitEffects(int ownerHash, AttackEquipmentSource source)
        {
            return _holders.TryGetValue(ownerHash, out EquipmentHolder holder)
                ? holder.GetOnHitEffects(source) : Array.Empty<StatusEffectApplicationData>();
        }

        public void Clear()
        {
            List<int> owners = new List<int>(_holders.Keys);
            for (int i = 0; i < owners.Count; i++)
            {
                Unregister(owners[i]);
            }
        }

        public void Dispose() => Clear();
    }
}
