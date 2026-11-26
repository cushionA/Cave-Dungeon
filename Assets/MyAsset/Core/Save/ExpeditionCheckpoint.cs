using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Game.Core
{
    /// <summary>通常の遠征セーブに、再開地点と騎士の資源を記録する。</summary>
    public static class ExpeditionCheckpoint
    {
        public const string k_SaveId = "ExpeditionCheckpoint";

        /// <summary>後続の保存・削除・スロット変更によって失効するオートセーブ予約。</summary>
        public readonly struct AutosaveTicket
        {
            private readonly SaveManager _saves;
            private readonly SaveDataStore _store;
            private readonly int _slot;
            private readonly ulong _loadRevision;
            private readonly ulong _selectionRevision;
            private readonly ulong _slotRevision;
            private readonly ulong _diskRevision;

            public AutosaveTicket(SaveManager saves, SaveDataStore store)
            {
                _saves = saves;
                _store = store;
                _slot = saves.ActiveSlotIndex;
                _loadRevision = saves.LoadRevision;
                _selectionRevision = saves.ActiveSlotRevision;
                _slotRevision = saves.GetSlotRevision(_slot);
                _diskRevision = store.GetSlotRevision(_slot);
            }

            public bool IsCurrent(SaveManager saves, SaveDataStore store) => _saves != null && _saves == saves
                && _store == store && !_saves.IsApplyingLoad && _saves.ActiveSlotIndex == _slot
                && _saves.LoadRevision == _loadRevision && _saves.ActiveSlotRevision == _selectionRevision
                && _saves.GetSlotRevision(_slot) == _slotRevision && _store.GetSlotRevision(_slot) == _diskRevision;

            public bool TryWrite(string scenePath, string savePointId, in CharacterVitals vitals, Func<bool> canCommit)
            {
                AutosaveTicket ticket = this;
                bool IsCurrent() => ticket.IsCurrent(ticket._saves, ticket._store) && canCommit?.Invoke() != false;
                return IsCurrent() && TryAutosaveCurrentSlot(_saves, _store, scenePath, savePointId, vitals, IsCurrent);
            }
        }

        public readonly struct Resources
        {
            public int Hp { get; }
            public float Mp { get; }
            public float Stamina { get; }
            public float Armor { get; }

            internal Resources(int hp, float mp, float stamina, float armor)
            {
                Hp = hp;
                Mp = mp;
                Stamina = stamina;
                Armor = armor;
            }

            public void ApplyRecovery(ref CharacterVitals vitals)
            {
                vitals.currentHp = Math.Min(Hp, Math.Max(1, vitals.maxHp));
                vitals.currentMp = Math.Min(Mp, Math.Max(0, vitals.maxMp));
                vitals.currentStamina = Math.Min(Stamina, Math.Max(0f, vitals.maxStamina));
            }

            public void Restore(ref CharacterVitals vitals)
            {
                ApplyRecovery(ref vitals);
                vitals.currentArmor = Math.Min(Armor, Math.Max(0f, vitals.maxArmor));
            }
        }

        [Serializable]
        private sealed class Snapshot
        {
            [JsonProperty(Required = Required.Always)] public string scenePath;
            [JsonProperty(Required = Required.Always)] public string savePointId;
            [JsonProperty(Required = Required.Always)] public int hp;
            [JsonProperty(Required = Required.Always)] public float mp;
            [JsonProperty(Required = Required.Always)] public float stamina;
            [JsonProperty(Required = Required.Always)] public float armor;
        }

        public static bool TrySaveCurrentSlot(SaveManager saves, SaveDataStore store, string scenePath, string savePointId,
            in CharacterVitals vitals, out Resources recovered)
            => TrySaveCurrentSlot(saves, store, scenePath, savePointId, vitals, out recovered, null, null);

        /// <summary>遠征のチェックポイントと転移地点の訪問記録を同じ保存に含める。</summary>
        public static bool TrySaveCurrentSlot(SaveManager saves, SaveDataStore store, string scenePath, string savePointId,
            in CharacterVitals vitals, out Resources recovered, FlagManager visitFlags, string visitKey)
        {
            recovered = default;
            if (!CanUse(saves, store, scenePath, savePointId) || vitals.currentHp <= 0 || vitals.maxHp <= 0
                || vitals.maxMp < 0 || !ValidResource(vitals.maxStamina) || !ValidResource(vitals.currentArmor))
            {
                return false;
            }

            var snapshot = new Snapshot
            {
                scenePath = scenePath,
                savePointId = savePointId,
                hp = vitals.maxHp,
                mp = vitals.maxMp,
                stamina = vitals.maxStamina,
                armor = vitals.currentArmor,
            };
            if (!TryWriteCheckpoint(saves, store, snapshot, visitFlags: visitFlags, visitKey: visitKey)) { return false; }
            recovered = ToResources(snapshot);
            return true;
        }

        public static bool TryAutosaveCurrentSlot(SaveManager saves, SaveDataStore store, string scenePath,
            string savePointId, in CharacterVitals vitals)
            => TryAutosaveCurrentSlot(saves, store, scenePath, savePointId, vitals, null);

        private static bool TryAutosaveCurrentSlot(SaveManager saves, SaveDataStore store, string scenePath,
            string savePointId, in CharacterVitals vitals, Func<bool> canCommit)
        {
            if (!CanUse(saves, store, scenePath, savePointId) || vitals.currentHp <= 0
                || !ValidResource(vitals.currentMp) || !ValidResource(vitals.currentStamina) || !ValidResource(vitals.currentArmor))
            {
                return false;
            }

            var snapshot = new Snapshot
            {
                scenePath = scenePath,
                savePointId = savePointId,
                hp = vitals.currentHp,
                mp = vitals.currentMp,
                stamina = vitals.currentStamina,
                armor = vitals.currentArmor,
            };
            return TryWriteCheckpoint(saves, store, snapshot, canCommit);
        }

        private static bool TryWriteCheckpoint(SaveManager saves, SaveDataStore store, Snapshot snapshot,
            Func<bool> canCommit = null, FlagManager visitFlags = null, string visitKey = null)
        {
            int slot = saves.ActiveSlotIndex;
            ulong revision = saves.LoadRevision;
            SaveSlotData candidate;
            try { candidate = saves.CaptureSlot(slot); }
            catch (Exception exception)
            {
                Debug.LogError($"[ExpeditionCheckpoint] セーブポイントの保存データを準備できませんでした: {exception.Message}");
                return false;
            }
            if (candidate == null || !IsCurrent(saves, slot, revision) || canCommit?.Invoke() == false) { return false; }

            if (visitKey != null)
            {
                // チェックポイントと訪問記録を同じディスク書き込みで確定する。
                if (visitFlags == null || !candidate.entries.TryGetValue(visitFlags.SaveId, out object entry)
                    || entry is not FlagSaveData flags) { return false; }
                flags.globalFlags[visitKey] = true;
            }
            candidate.entries[k_SaveId] = snapshot;
            return saves.TryWriteSlot(slot, candidate, store);
        }

        public static bool TryRestoreCurrentSlot(SaveManager saves, SaveDataStore store, string scenePath, string savePointId,
            out Resources restored, Func<Resources, bool> placeKnight = null)
        {
            restored = default;
            if (!CanUse(saves, store, scenePath, savePointId)) { return false; }

            int slot = saves.ActiveSlotIndex;
            ulong revision = saves.LoadRevision;
            SaveSlotData saved = store.ReadFromDisk(slot);
            return IsCurrent(saves, slot, revision)
                && TryRestoreSlot(saves, slot, saved, scenePath, savePointId, out restored, placeKnight);
        }

        internal static bool TryRestoreSlot(SaveManager saves, int slot, SaveSlotData saved, string scenePath, string savePointId,
            out Resources restored, Func<Resources, bool> placeKnight)
        {
            restored = default;
            Snapshot snapshot = ReadCheckpoint(saved);
            if (saves == null || saved?.slotIndex != slot || snapshot == null
                || !string.Equals(snapshot.scenePath, scenePath, StringComparison.Ordinal)
                || !string.Equals(snapshot.savePointId, savePointId, StringComparison.Ordinal)) { return false; }

            Resources resources = ToResources(snapshot);
            if (!saves.TryLoadSlot(slot, saved, placeKnight == null ? null : () => placeKnight(resources))) { return false; }
            restored = resources;
            return true;
        }

        internal static bool TryReadDestination(SaveSlotData saved, out string scenePath, out string savePointId)
        {
            Snapshot snapshot = ReadCheckpoint(saved);
            scenePath = snapshot?.scenePath;
            savePointId = snapshot?.savePointId;
            return snapshot != null;
        }

        private static bool CanUse(SaveManager saves, SaveDataStore store, string scenePath, string savePointId) =>
            saves != null && store != null && !string.IsNullOrWhiteSpace(scenePath) && !string.IsNullOrWhiteSpace(savePointId);

        private static bool IsCurrent(SaveManager saves, int slot, ulong revision) =>
            saves.ActiveSlotIndex == slot && saves.LoadRevision == revision;

        private static bool ValidResource(float value) => float.IsFinite(value) && value >= 0f;

        private static Resources ToResources(Snapshot snapshot) => new Resources(snapshot.hp, snapshot.mp, snapshot.stamina, snapshot.armor);

        private static Snapshot ReadCheckpoint(SaveSlotData saved)
        {
            if (saved?.entries == null || !saved.entries.TryGetValue(k_SaveId, out object entry)) { return null; }
            Snapshot snapshot = ReadSnapshot(entry);
            return snapshot != null && !string.IsNullOrWhiteSpace(snapshot.scenePath) && !string.IsNullOrWhiteSpace(snapshot.savePointId)
                && snapshot.hp > 0 && ValidResource(snapshot.mp) && ValidResource(snapshot.stamina) && ValidResource(snapshot.armor)
                ? snapshot : null;
        }

        private static Snapshot ReadSnapshot(object entry)
        {
            if (entry is Snapshot direct) { return direct; }
            if (entry is not JObject json) { return null; }
            try { return json.ToObject<Snapshot>(); }
            catch (JsonException) { return null; }
        }
    }
}
