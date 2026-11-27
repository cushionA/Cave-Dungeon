using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Game.Core
{
    /// <summary>騎士の装備と魔法枠をセーブスロットに保存する。</summary>
    public sealed class PlayerCombatLoadoutSelection : ISaveableWithMissingEntry
    {
        public const int k_SpellSlotCount = 3;
        public const int k_EmptyItem = -1;

        [Serializable]
        private sealed class Snapshot
        {
            public bool initialized;
            public int weaponId = k_EmptyItem;
            public int coreId = k_EmptyItem;
            public int activeSpellSlot;
            public int[] spellIds;
            public bool cooperationInitialized;
            public int? cooperationId;
        }

        private readonly InventoryManager _inventory;
        private readonly BeltShortcut _spellSlots = new BeltShortcut(k_SpellSlotCount);
        public string SaveId => "PlayerCombatLoadout";
        public bool IsInitialized { get; private set; }
        public bool IsCooperationInitialized { get; private set; }
        public int WeaponId { get; private set; } = k_EmptyItem;
        public int CoreId { get; private set; } = k_EmptyItem;
        public int ActiveSpellSlot => _spellSlots.ActiveIndex;
        public int CooperationId { get; private set; } = k_EmptyItem;

        public PlayerCombatLoadoutSelection(InventoryManager inventory) =>
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));

        public bool TryInitialize()
        {
            if (IsInitialized) { return false; }
            IsInitialized = true;
            return true;
        }

        public bool TryInitializeCooperation()
        {
            if (IsCooperationInitialized) { return false; }
            IsCooperationInitialized = true;
            return true;
        }

        public bool TryEquipWeapon(int itemId)
        {
            if (!Owns(itemId, ItemCategory.Weapon)) { return false; }
            WeaponId = itemId;
            IsInitialized = true;
            return true;
        }

        public bool TryEquipCore(int itemId)
        {
            if (!Owns(itemId, ItemCategory.Core)) { return false; }
            CoreId = itemId;
            IsInitialized = true;
            return true;
        }

        public bool TryEquipCooperation(int itemId)
        {
            if (!Owns(itemId, ItemCategory.CompanionMagic)) { return false; }
            CooperationId = itemId;
            IsCooperationInitialized = true;
            return true;
        }

        public int GetSpellId(int slot) => IsValidSlot(slot) ? _spellSlots.GetItemId(slot) : k_EmptyItem;

        public bool TryAssignSpell(int slot, int itemId)
        {
            if (!IsValidSlot(slot) || !Owns(itemId, ItemCategory.PlayerMagic)) { return false; }
            _spellSlots.SetSlot(slot, itemId);
            IsInitialized = true;
            return true;
        }

        public bool TryClearSpell(int slot)
        {
            if (!IsValidSlot(slot)) { return false; }
            _spellSlots.ClearSlot(slot);
            IsInitialized = true;
            return true;
        }

        public bool TrySelectSpellSlot(int slot)
        {
            if (!IsValidSlot(slot)) { return false; }
            _spellSlots.Select(slot);
            IsInitialized = true;
            return true;
        }

        private bool Owns(int itemId, ItemCategory category) => itemId >= 0 && _inventory.GetCount(itemId, category) > 0;

        private static bool IsValidSlot(int slot) => (uint)slot < k_SpellSlotCount;

        object ISaveable.Serialize()
        {
            int[] spellIds = new int[k_SpellSlotCount];
            for (int i = 0; i < spellIds.Length; i++) { spellIds[i] = GetSpellId(i); }
            return new Snapshot
            {
                initialized = IsInitialized,
                weaponId = WeaponId,
                coreId = CoreId,
                activeSpellSlot = ActiveSpellSlot,
                spellIds = spellIds,
                cooperationInitialized = IsCooperationInitialized,
                cooperationId = CooperationId,
            };
        }

        void ISaveable.Deserialize(object data)
        {
            Snapshot snapshot = data as Snapshot;
            if (data is JObject json)
            {
                try { snapshot = json.ToObject<Snapshot>(); }
                catch (JsonException) { snapshot = null; }
            }
            Restore(snapshot, initialized: snapshot?.initialized ?? true);
        }

        void ISaveableWithMissingEntry.RestoreMissingEntry() => Restore(null, initialized: false);

        private void Restore(Snapshot snapshot, bool initialized)
        {
            // Legacy slots and campaigns that have never initialized the loadout can receive the starters.
            IsInitialized = initialized;
            WeaponId = snapshot != null && Owns(snapshot.weaponId, ItemCategory.Weapon) ? snapshot.weaponId : k_EmptyItem;
            CoreId = snapshot != null && Owns(snapshot.coreId, ItemCategory.Core) ? snapshot.coreId : k_EmptyItem;
            IsCooperationInitialized = snapshot?.cooperationInitialized == true;
            int cooperationId = snapshot?.cooperationId ?? k_EmptyItem;
            CooperationId = IsCooperationInitialized && Owns(cooperationId, ItemCategory.CompanionMagic) ? cooperationId : k_EmptyItem;
            int[] spellIds = snapshot?.spellIds ?? Array.Empty<int>();
            for (int i = 0; i < k_SpellSlotCount; i++)
            {
                _spellSlots.SetSlot(i, i < spellIds.Length && Owns(spellIds[i], ItemCategory.PlayerMagic) ? spellIds[i] : k_EmptyItem);
            }
            int activeSlot = snapshot?.activeSpellSlot ?? 0;
            _spellSlots.Select(IsValidSlot(activeSlot) ? activeSlot : 0);
        }
    }
}
