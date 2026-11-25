using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Game.Core
{
    /// <summary>所持している消耗品の割り当てと現在の選択枠をセーブスロットに保存する。</summary>
    public sealed class ConsumableBelt : ISaveableWithMissingEntry
    {
        public const int k_DefaultSlotCount = 4;

        [Serializable]
        private sealed class Snapshot
        {
            public int activeIndex;
            public int[] itemIds;
        }

        private readonly InventoryManager _inventory;
        private readonly BeltShortcut _belt;

        public string SaveId => "ConsumableBelt";
        public int SlotCount => _belt.SlotCount;
        public int ActiveIndex => _belt.ActiveIndex;
        public int ActiveItemId => _belt.ActiveItemId;

        public ConsumableBelt(InventoryManager inventory, int slotCount = k_DefaultSlotCount)
        {
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            if (slotCount <= 0) { throw new ArgumentOutOfRangeException(nameof(slotCount)); }
            _belt = new BeltShortcut(slotCount);
        }

        public int GetItemId(int slot) => IsValidSlot(slot) ? _belt.GetItemId(slot) : BeltShortcut.k_EmptySlot;

        public bool TryAssign(int slot, int itemId)
        {
            if (!IsValidSlot(slot) || _inventory.GetConsumableCount(itemId) <= 0) { return false; }
            _belt.SetSlot(slot, itemId);
            return true;
        }

        public bool TrySelect(int slot)
        {
            if (!IsValidSlot(slot)) { return false; }
            _belt.Select(slot);
            return true;
        }

        public void Next() => _belt.Next();
        public void Prev() => _belt.Prev();

        public bool TryClear(int slot)
        {
            if (!IsValidSlot(slot)) { return false; }
            _belt.ClearSlot(slot);
            return true;
        }

        private bool IsValidSlot(int slot) => (uint)slot < (uint)SlotCount;

        object ISaveable.Serialize()
        {
            int[] itemIds = new int[SlotCount];
            for (int i = 0; i < SlotCount; i++) { itemIds[i] = GetItemId(i); }
            return new Snapshot { activeIndex = ActiveIndex, itemIds = itemIds };
        }

        void ISaveable.Deserialize(object data)
        {
            Snapshot snapshot = data as Snapshot;
            if (data is JObject json)
            {
                try { snapshot = json.ToObject<Snapshot>(); }
                catch (JsonException) { snapshot = null; }
            }
            Restore(snapshot);
        }

        void ISaveableWithMissingEntry.RestoreMissingEntry() => Restore(null);

        private void Restore(Snapshot snapshot)
        {
            int[] itemIds = snapshot?.itemIds ?? Array.Empty<int>();
            for (int i = 0; i < SlotCount; i++)
            {
                if (i < itemIds.Length && itemIds[i] >= 0) { _belt.SetSlot(i, itemIds[i]); }
                else { _belt.ClearSlot(i); }
            }
            if (!TrySelect(snapshot?.activeIndex ?? 0)) { _belt.Select(0); }
        }
    }
}
