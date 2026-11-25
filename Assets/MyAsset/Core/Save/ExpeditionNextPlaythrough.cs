using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Game.Core
{
    /// <summary>新しい周回では保存値を使わず、未保存時の初期状態を復元する。</summary>
    public interface IResetOnNextPlaythrough : ISaveableWithMissingEntry { }

    /// <summary>クリア記録を持ち、確定した次周回を保存してから現在のスロットへ反映する。</summary>
    public sealed class ExpeditionNextPlaythrough : ISaveableWithMissingEntry
    {
        public const string k_SaveId = "ExpeditionNextPlaythrough";

        [Serializable]
        private sealed class Progress
        {
            public int clearedCycle;
            public string startScenePath;
        }

        private readonly SaveManager _saves;
        private readonly FlagManager _flags;
        private readonly ExpeditionPlaythrough _playthrough;
        private int _clearedCycle;
        private int _clearedSlot = -1;
        private bool _committing;

        public string SaveId => k_SaveId;
        public string StartScenePath { get; private set; }
        public bool CanBeginNext => !_committing && _clearedSlot == _saves.ActiveSlotIndex
            && _clearedCycle == _playthrough.CycleNumber && _clearedCycle < int.MaxValue;

        internal ExpeditionNextPlaythrough(SaveManager saves, FlagManager flags, ExpeditionPlaythrough playthrough)
        {
            _saves = saves;
            _flags = flags;
            _playthrough = playthrough;
            _saves.Loaded += OnSlotLoaded;
        }

        public void MarkCleared()
        {
            if (_committing) { return; }
            _clearedSlot = _saves.ActiveSlotIndex;
            _clearedCycle = _playthrough.CycleNumber;
        }

        public bool TryBeginNextPlaythrough(int expectedSlot, ulong expectedLoadRevision, int expectedCycle,
            string startScenePath, SaveDataStore store, IReadOnlyList<string> carryGlobalFlags = null)
        {
            if (!CanBeginNext || expectedSlot != _saves.ActiveSlotIndex || expectedLoadRevision != _saves.LoadRevision
                || expectedCycle != _playthrough.CycleNumber || string.IsNullOrWhiteSpace(startScenePath) || store == null)
            {
                return false;
            }

            _committing = true;
            try
            {
                SaveSlotData nextSlot;
                try { nextSlot = _saves.CaptureSlot(expectedSlot, forNextPlaythrough: true); }
                catch (Exception exception)
                {
                    Debug.LogError($"[ExpeditionNextPlaythrough] 周回開始の保存データを準備できませんでした: {exception.Message}");
                    return false;
                }

                FlagSaveData flags = (FlagSaveData)nextSlot.entries[_flags.SaveId];
                nextSlot.entries[_flags.SaveId] = CreateStartingFlags(flags, carryGlobalFlags);
                nextSlot.entries[_playthrough.SaveId] = expectedCycle + 1;
                nextSlot.entries[SaveId] = new Progress { clearedCycle = expectedCycle, startScenePath = startScenePath };
                if (!store.TryWriteToDisk(nextSlot)) { return false; }

                _saves.SetSlotData(expectedSlot, nextSlot);
                return _saves.Load(expectedSlot);
            }
            finally { _committing = false; }
        }

        /// <summary>この開始シーンに対応する保存済み周回をコールドスタートから再開する。</summary>
        public bool TryRestoreStartSlot(int slot, string expectedStartScenePath, SaveDataStore store)
        {
            if (_committing || slot < 0 || slot >= SaveManager.k_MaxSlots || store == null
                || string.IsNullOrWhiteSpace(expectedStartScenePath)) { return false; }

            SaveSlotData saved = store.ReadFromDisk(slot);
            if (saved == null || saved.slotIndex != slot || !saved.entries.TryGetValue(SaveId, out object data)
                || !string.Equals(ReadProgress(data)?.startScenePath, expectedStartScenePath, StringComparison.Ordinal)
                || !saved.entries.TryGetValue(_playthrough.SaveId, out object cycle) || ExpeditionPlaythrough.ReadCycle(cycle) < 2)
            {
                return false;
            }

            _saves.SetSlotData(slot, saved);
            return _saves.Load(slot);
        }

        object ISaveable.Serialize() => new Progress
        {
            clearedCycle = _clearedSlot == _saves.ActiveSlotIndex ? _clearedCycle : 0,
            startScenePath = StartScenePath,
        };

        void ISaveable.Deserialize(object data) => Restore(ReadProgress(data));

        void ISaveableWithMissingEntry.RestoreMissingEntry() => Restore(null);

        private void Restore(Progress progress)
        {
            _clearedCycle = progress?.clearedCycle ?? 0;
            StartScenePath = progress?.startScenePath;
        }

        private void OnSlotLoaded(int slot) => _clearedSlot = slot;

        private static Progress ReadProgress(object data)
        {
            if (data is Progress direct) { return direct; }
            if (data is JObject json)
            {
                try { return json.ToObject<Progress>(); }
                catch (Newtonsoft.Json.JsonException) { return null; }
            }
            return null;
        }

        private static FlagSaveData CreateStartingFlags(FlagSaveData flags, IReadOnlyList<string> carryGlobalFlags)
        {
            HashSet<string> allowed = new HashSet<string>(carryGlobalFlags ?? Array.Empty<string>(), StringComparer.Ordinal);
            FlagSaveData starting = new FlagSaveData
            {
                globalFlags = new Dictionary<string, bool>(),
                mapLocalFlags = new Dictionary<string, Dictionary<string, bool>>(),
            };
            foreach (KeyValuePair<string, bool> entry in flags.globalFlags)
            {
                if (entry.Key.StartsWith(ExpeditionAidUnlocks.k_UnlockFlagPrefix, StringComparison.Ordinal) || allowed.Contains(entry.Key))
                {
                    starting.globalFlags.Add(entry.Key, entry.Value);
                }
            }
            return starting;
        }
    }
}
