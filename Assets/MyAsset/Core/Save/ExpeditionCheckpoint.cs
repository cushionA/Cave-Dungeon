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
        {
            recovered = default;
            if (!CanUse(saves, store, scenePath, savePointId) || vitals.currentHp <= 0 || vitals.maxHp <= 0
                || vitals.maxMp < 0 || !ValidResource(vitals.maxStamina) || !ValidResource(vitals.currentArmor))
            {
                return false;
            }

            int slot = saves.ActiveSlotIndex;
            ulong revision = saves.LoadRevision;
            SaveSlotData candidate;
            try { candidate = saves.CaptureSlot(slot); }
            catch (Exception exception)
            {
                Debug.LogError($"[ExpeditionCheckpoint] セーブポイントの保存データを準備できませんでした: {exception.Message}");
                return false;
            }
            if (candidate == null || !IsCurrent(saves, slot, revision)) { return false; }

            var snapshot = new Snapshot
            {
                scenePath = scenePath,
                savePointId = savePointId,
                hp = vitals.maxHp,
                mp = vitals.maxMp,
                stamina = vitals.maxStamina,
                armor = vitals.currentArmor,
            };
            candidate.entries[k_SaveId] = snapshot;
            if (!saves.TryWriteSlot(slot, candidate, store)) { return false; }
            recovered = ToResources(snapshot);
            return true;
        }

        public static bool TryRestoreCurrentSlot(SaveManager saves, SaveDataStore store, string scenePath, string savePointId,
            out Resources restored)
        {
            restored = default;
            if (!CanUse(saves, store, scenePath, savePointId)) { return false; }

            int slot = saves.ActiveSlotIndex;
            ulong revision = saves.LoadRevision;
            SaveSlotData saved = store.ReadFromDisk(slot);
            if (saved == null || saved.slotIndex != slot || !saved.entries.TryGetValue(k_SaveId, out object entry)) { return false; }
            Snapshot snapshot = ReadSnapshot(entry);
            if (snapshot == null || !string.Equals(snapshot.scenePath, scenePath, StringComparison.Ordinal)
                || !string.Equals(snapshot.savePointId, savePointId, StringComparison.Ordinal)
                || snapshot.hp <= 0 || !ValidResource(snapshot.mp) || !ValidResource(snapshot.stamina) || !ValidResource(snapshot.armor)
                || !IsCurrent(saves, slot, revision) || !saves.TryLoadSlot(slot, saved)) { return false; }

            restored = ToResources(snapshot);
            return true;
        }

        private static bool CanUse(SaveManager saves, SaveDataStore store, string scenePath, string savePointId) =>
            saves != null && store != null && !string.IsNullOrWhiteSpace(scenePath) && !string.IsNullOrWhiteSpace(savePointId);

        private static bool IsCurrent(SaveManager saves, int slot, ulong revision) =>
            saves.ActiveSlotIndex == slot && saves.LoadRevision == revision;

        private static bool ValidResource(float value) => float.IsFinite(value) && value >= 0f;

        private static Resources ToResources(Snapshot snapshot) => new Resources(snapshot.hp, snapshot.mp, snapshot.stamina, snapshot.armor);

        private static Snapshot ReadSnapshot(object entry)
        {
            if (entry is Snapshot direct) { return direct; }
            if (entry is not JObject json) { return null; }
            try { return json.ToObject<Snapshot>(); }
            catch (JsonException) { return null; }
        }
    }
}
