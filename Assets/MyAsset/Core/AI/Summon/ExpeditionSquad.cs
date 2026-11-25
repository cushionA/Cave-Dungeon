using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Game.Core
{
    /// <summary>通常探索で呼べる種と一体のコストから、出発前の編成を確定する。</summary>
    public sealed class ExpeditionSquad : ISaveableWithMissingEntry
    {
        [Serializable]
        public struct Species
        {
            [Tooltip("解放イベントと同じ種ID")]
            [SerializeField] private string speciesId;
            [Min(1)]
            [SerializeField] private int costPerUnit;

            public string SpeciesId => speciesId;
            public int CostPerUnit => costPerUnit;

            public Species(string speciesId, int costPerUnit)
            {
                this.speciesId = speciesId;
                this.costPerUnit = costPerUnit;
            }
        }

        [Serializable]
        public readonly struct Selection
        {
            [JsonProperty("speciesId")]
            public string SpeciesId { get; }
            [JsonProperty("count")]
            public int Count { get; }

            [JsonConstructor]
            public Selection(string speciesId, int count)
            {
                SpeciesId = speciesId;
                Count = count;
            }
        }

        private readonly ExpeditionAidUnlocks _unlocks;
        private readonly Species[] _species;
        private readonly Dictionary<string, int> _costBySpecies = new Dictionary<string, int>(StringComparer.Ordinal);

        public int MaxCount { get; }
        public int MaxCost { get; }
        public IReadOnlyList<Selection> Confirmed { get; private set; } = Array.Empty<Selection>();
        public int ConfirmedCount { get; private set; }
        public int ConfirmedCost { get; private set; }
        public string SaveId => "ExpeditionSquad";

        public ExpeditionSquad(ExpeditionAidUnlocks unlocks, IReadOnlyList<Species> species, int maxCount, int maxCost)
        {
            _unlocks = unlocks ?? throw new ArgumentNullException(nameof(unlocks));
            if (species == null)
            {
                throw new ArgumentNullException(nameof(species));
            }
            if (maxCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxCount));
            }
            if (maxCost < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxCost));
            }

            MaxCount = maxCount;
            MaxCost = maxCost;
            _species = new Species[species.Count];
            for (int index = 0; index < species.Count; index++)
            {
                Species entry = species[index];
                if (string.IsNullOrWhiteSpace(entry.SpeciesId) || entry.CostPerUnit <= 0
                    || !_costBySpecies.TryAdd(entry.SpeciesId, entry.CostPerUnit))
                {
                    throw new ArgumentException("Each squad species needs a unique ID and a positive cost.", nameof(species));
                }
                _species[index] = entry;
            }
        }

        /// <summary>現在解放されている種を、設定アセットの順番で返す。</summary>
        public IEnumerable<Species> GetUnlockedSpecies()
        {
            foreach (Species entry in _species)
            {
                if (_unlocks.IsUnlocked(entry.SpeciesId))
                {
                    yield return entry;
                }
            }
        }

        /// <summary>候補の合計を計算する。上限を超える編成も、確定前の表示用に計算できる。</summary>
        public bool TryMeasure(IReadOnlyList<Selection> selections, out int totalCount, out int totalCost)
        {
            totalCount = 0;
            totalCost = 0;
            if (selections == null)
            {
                return false;
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            long count = 0;
            long cost = 0;
            foreach (Selection selection in selections)
            {
                string speciesId = selection.SpeciesId;
                if (selection.Count <= 0 || string.IsNullOrWhiteSpace(speciesId)
                    || !_costBySpecies.TryGetValue(speciesId, out int costPerUnit)
                    || !_unlocks.IsUnlocked(speciesId) || !seen.Add(speciesId))
                {
                    return false;
                }

                count += selection.Count;
                cost += (long)costPerUnit * selection.Count;
                if (count > int.MaxValue || cost > int.MaxValue)
                {
                    return false;
                }
            }

            totalCount = (int)count;
            totalCost = (int)cost;
            return true;
        }

        /// <summary>人数とコストの両方が上限内なら、受理した編成を入れ替える。</summary>
        public bool TryConfirm(IReadOnlyList<Selection> selections)
        {
            if (selections == null)
            {
                return false;
            }

            Selection[] snapshot = selections.ToArray();
            if (!TryMeasure(snapshot, out int count, out int cost) || count > MaxCount || cost > MaxCost)
            {
                return false;
            }

            Confirmed = Array.AsReadOnly(snapshot);
            ConfirmedCount = count;
            ConfirmedCost = cost;
            return true;
        }

        object ISaveable.Serialize() => Confirmed.ToArray();

        void ISaveable.Deserialize(object data)
        {
            Selection[] saved = data as Selection[];
            if (data is JArray json)
            {
                try
                {
                    saved = json.ToObject<Selection[]>();
                }
                catch (JsonException)
                {
                    // 読めない編成は、別スロットの確定値を引き継がずに初期化する。
                }
            }
            if (!TryConfirm(saved))
            {
                ClearConfirmedSelection();
            }
        }

        void ISaveableWithMissingEntry.RestoreMissingEntry() => ClearConfirmedSelection();

        private void ClearConfirmedSelection()
        {
            Confirmed = Array.Empty<Selection>();
            ConfirmedCount = 0;
            ConfirmedCost = 0;
        }
    }
}
