using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Game.Core
{
    /// <summary>出撃時に選び直せる召喚デッキを、通常編成と別に保持する。</summary>
    public sealed class ExpeditionDeckPresets : ISaveableWithMissingEntry
    {
        private readonly ExpeditionSquad _squad;
        private readonly IReadOnlyList<ExpeditionSquad.Selection>[] _slots;

        public ExpeditionDeckPresets(ExpeditionSquad squad, int capacity)
        {
            _squad = squad ?? throw new ArgumentNullException(nameof(squad));
            if (capacity < 0) { throw new ArgumentOutOfRangeException(nameof(capacity)); }
            _slots = new IReadOnlyList<ExpeditionSquad.Selection>[capacity];
        }

        public string SaveId => "ExpeditionDeckPresets";
        public int Capacity => _slots.Length;

        public bool TryGet(int index, out IReadOnlyList<ExpeditionSquad.Selection> selection)
        {
            selection = (uint)index < (uint)Capacity ? _slots[index] : null;
            return selection != null;
        }

        /// <summary>共通の種・解放条件を照合する。人数とコストの上限は選んだ出撃先で確認する。</summary>
        public bool TrySave(int index, IReadOnlyList<ExpeditionSquad.Selection> selection)
        {
            if ((uint)index >= (uint)Capacity || selection == null) { return false; }

            ExpeditionSquad.Selection[] snapshot = selection.ToArray();
            if (!_squad.TryMeasure(snapshot, out _, out _)) { return false; }
            _slots[index] = Array.AsReadOnly(snapshot);
            return true;
        }

        object ISaveable.Serialize()
        {
            ExpeditionSquad.Selection[][] snapshot = new ExpeditionSquad.Selection[Capacity][];
            for (int index = 0; index < snapshot.Length; index++) { snapshot[index] = _slots[index]?.ToArray(); }
            return snapshot;
        }

        void ISaveable.Deserialize(object data)
        {
            Clear();
            if (data is ExpeditionSquad.Selection[][] slots)
            {
                for (int index = 0; index < Math.Min(slots.Length, Capacity); index++) { TrySave(index, slots[index]); }
            }
            else if (data is JArray json)
            {
                for (int index = 0; index < Math.Min(json.Count, Capacity); index++)
                {
                    if (json[index] is not JArray slot) { continue; }
                    try { TrySave(index, slot.ToObject<ExpeditionSquad.Selection[]>()); }
                    catch (JsonException) { /* 他のスロットを復元する。 */ }
                }
            }
        }

        void ISaveableWithMissingEntry.RestoreMissingEntry() => Clear();

        private void Clear() => Array.Clear(_slots, 0, _slots.Length);
    }
}
