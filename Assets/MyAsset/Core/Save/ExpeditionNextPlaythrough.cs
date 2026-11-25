using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Game.Core
{
    /// <summary>新しい周回では保存値を使わず、未保存時の初期状態を復元する。</summary>
    public interface IResetOnNextPlaythrough : ISaveableWithMissingEntry { }

    /// <summary>クリア記録を持ち、確定した次周回を保存してから現在のスロットへ反映する。</summary>
    public sealed class ExpeditionNextPlaythrough : ISaveableWithMissingEntry, ISaveableForDestinationSlot,
        ISaveableWithLoadRollback, IDisposable
    {
        public const string k_SaveId = "ExpeditionNextPlaythrough";

        [Serializable]
        private sealed class Progress
        {
            public int clearedCycle;
            public string startScenePath;
        }

        private readonly struct LiveProgress
        {
            public readonly int ClearedCycle;
            public readonly int ClearedSlot;
            public readonly string StartScenePath;

            public LiveProgress(int clearedCycle, int clearedSlot, string startScenePath)
            {
                ClearedCycle = clearedCycle;
                ClearedSlot = clearedSlot;
                StartScenePath = startScenePath;
            }
        }

        private readonly SaveManager _saves;
        private readonly FlagManager _flags;
        private readonly ExpeditionPlaythrough _playthrough;
        private int _clearedCycle;
        private int _clearedSlot = -1;
        private bool _committing;
        private bool _disposed;

        public string SaveId => k_SaveId;
        public string StartScenePath { get; private set; }
        public bool CanBeginNext => !_disposed && !_committing && _clearedSlot == _saves.ActiveSlotIndex
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
            if (_disposed || _committing) { return; }
            _clearedSlot = _saves.ActiveSlotIndex;
            _clearedCycle = _playthrough.CycleNumber;
        }

        public bool TryBeginNextPlaythrough(int expectedSlot, ulong expectedLoadRevision, int expectedCycle,
            string startScenePath, SaveDataStore store, IReadOnlyList<string> carryGlobalFlags = null)
        {
            if (!CanBeginNext || !MatchesConfirmation(expectedSlot, expectedLoadRevision, expectedCycle)
                || string.IsNullOrWhiteSpace(startScenePath) || store == null)
            {
                return false;
            }

            _committing = true;
            try
            {
                SaveSlotData nextSlot;
                try
                {
                    nextSlot = _saves.CaptureSlot(expectedSlot, forNextPlaythrough: true);
                    if (nextSlot == null) { return false; }

                    FlagSaveData flags = (FlagSaveData)nextSlot.entries[_flags.SaveId];
                    nextSlot.entries[_flags.SaveId] = CreateStartingFlags(flags, carryGlobalFlags);
                    nextSlot.entries[_playthrough.SaveId] = expectedCycle + 1;
                    nextSlot.entries[SaveId] = new Progress { clearedCycle = expectedCycle, startScenePath = startScenePath };
                }
                catch (Exception exception)
                {
                    Debug.LogError($"[ExpeditionNextPlaythrough] 周回開始の保存データを準備できませんでした: {exception.Message}");
                    return false;
                }

                return !_disposed && MatchesConfirmation(expectedSlot, expectedLoadRevision, expectedCycle)
                    && _saves.TryLoadSlot(expectedSlot, nextSlot, () => store.TryWriteToDisk(nextSlot));
            }
            finally { _committing = false; }
        }

        /// <summary>この開始シーンに対応する保存済み周回をコールドスタートから再開する。</summary>
        public bool TryRestoreStartSlot(int slot, string expectedStartScenePath, SaveDataStore store)
        {
            if (_disposed || _committing || slot < 0 || slot >= SaveManager.k_MaxSlots || store == null
                || string.IsNullOrWhiteSpace(expectedStartScenePath)) { return false; }

            _committing = true;
            try
            {
                SaveSlotData saved = store.ReadFromDisk(slot);
                if (saved == null || saved.slotIndex != slot || saved.entries.ContainsKey(ExpeditionCheckpoint.k_SaveId)
                    || !saved.entries.TryGetValue(SaveId, out object data)
                    || !string.Equals(ReadProgress(data)?.startScenePath, expectedStartScenePath, StringComparison.Ordinal)
                    || !saved.entries.TryGetValue(_playthrough.SaveId, out object cycle))
                {
                    return false;
                }
                if (!ExpeditionPlaythrough.TryReadCycle(cycle, out int cycleNumber))
                {
                    Debug.LogError($"[ExpeditionNextPlaythrough] 保存した周回番号が無効です (slot={slot})。");
                    return false;
                }

                return cycleNumber >= 2 && !_disposed && _saves.TryLoadSlot(slot, saved);
            }
            finally { _committing = false; }
        }

        private bool MatchesConfirmation(int slot, ulong loadRevision, int cycle) => slot == _saves.ActiveSlotIndex
            && loadRevision == _saves.LoadRevision && cycle == _playthrough.CycleNumber;

        object ISaveable.Serialize() => SerializeForSlot(_saves.ActiveSlotIndex);

        object ISaveableForDestinationSlot.SerializeForSlot(int slotIndex) => SerializeForSlot(slotIndex);

        private Progress SerializeForSlot(int slotIndex) => new Progress
        {
            clearedCycle = _clearedSlot == slotIndex ? _clearedCycle : 0,
            startScenePath = StartScenePath,
        };

        object ISaveableWithLoadRollback.CaptureLoadRollback() => new LiveProgress(_clearedCycle, _clearedSlot, StartScenePath);

        void ISaveableWithLoadRollback.RestoreLoadRollback(object snapshot)
        {
            LiveProgress live = (LiveProgress)snapshot;
            _clearedCycle = live.ClearedCycle;
            _clearedSlot = live.ClearedSlot;
            StartScenePath = live.StartScenePath;
        }

        void ISaveable.Deserialize(object data) => Restore(ReadProgress(data));

        void ISaveableWithMissingEntry.RestoreMissingEntry() => Restore(null);

        private void Restore(Progress progress)
        {
            _clearedCycle = progress?.clearedCycle ?? 0;
            StartScenePath = progress?.startScenePath;
        }

        private void OnSlotLoaded(int slot) => _clearedSlot = slot;

        public void Dispose()
        {
            if (_disposed) { return; }
            _disposed = true;
            _saves.Loaded -= OnSlotLoaded;
        }

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
