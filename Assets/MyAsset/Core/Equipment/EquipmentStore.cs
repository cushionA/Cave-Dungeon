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

        public EquipmentStore(GameEvents events = null)
        {
            _events = events;
        }

        public void Register(int ownerHash)
        {
            if (!_holders.ContainsKey(ownerHash))
            {
                _holders.Add(ownerHash, new EquipmentHolder(ownerHash));
            }
        }

        public void Unregister(int ownerHash)
        {
            if (!_holders.Remove(ownerHash, out EquipmentHolder holder))
            {
                return;
            }
            holder.Unequip(EquipSlot.Weapon);
            holder.Unequip(EquipSlot.Shield);
            holder.Unequip(EquipSlot.Core);
        }

        public EquipmentHolder GetHolder(int ownerHash) => _holders[ownerHash];

        public IEquippable Equip(int ownerHash, IEquippable item)
        {
            IEquippable previous = GetHolder(ownerHash).Equip(item);
            _events?.FireEquipmentChanged(ownerHash, item.Slot);
            return previous;
        }

        public IEquippable Unequip(int ownerHash, EquipSlot slot)
        {
            IEquippable removed = GetHolder(ownerHash).Unequip(slot);
            if (removed != null)
            {
                _events?.FireEquipmentChanged(ownerHash, slot);
            }
            return removed;
        }

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
