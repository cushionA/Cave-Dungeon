using System;

namespace Game.Core
{
    public enum ExpeditionSlotStatus { Empty, Ready, Unavailable }

    /// <summary>タイトルから再開するために検証した保存先とスロット。</summary>
    public readonly struct ExpeditionSlot
    {
        internal SaveSlotData Data { get; }
        internal string SourceText { get; }
        public ExpeditionSlotStatus Status { get; }
        public string ScenePath { get; }
        public string SavePointId { get; }
        public int CycleNumber { get; }
        public string Timestamp => Data?.timestamp;

        internal ExpeditionSlot(ExpeditionSlotStatus status, SaveSlotData data = null, string scenePath = null,
            string savePointId = null, int cycleNumber = 0, string sourceText = null)
        {
            Status = status;
            Data = data;
            ScenePath = scenePath;
            SavePointId = savePointId;
            CycleNumber = cycleNumber;
            SourceText = sourceText;
        }
    }

    public static class ExpeditionLaunch
    {
        public static ExpeditionSlot ReadSlot(SaveDataStore store, int slot, Func<string, bool> isSupportedScene)
        {
            var unavailable = new ExpeditionSlot(ExpeditionSlotStatus.Unavailable);
            if (store == null || (uint)slot >= SaveManager.k_MaxSlots || isSupportedScene == null) { return unavailable; }
            if (!store.HasSlotFiles(slot)) { return new ExpeditionSlot(ExpeditionSlotStatus.Empty); }

            SaveSlotData saved = store.ReadFromDisk(slot, logFailures: false, out string sourceText);
            if (saved?.slotIndex != slot || saved.entries == null) { return unavailable; }
            int cycleNumber = 1;
            if (saved.entries.TryGetValue(ExpeditionPlaythrough.k_SaveId, out object cycle)
                && !ExpeditionPlaythrough.TryReadCycle(cycle, out cycleNumber)) { return unavailable; }

            string scenePath;
            string savePointId = null;
            bool valid = saved.entries.ContainsKey(ExpeditionCheckpoint.k_SaveId)
                ? ExpeditionCheckpoint.TryReadDestination(saved, out scenePath, out savePointId)
                : ExpeditionNextPlaythrough.TryReadStartScene(saved, out scenePath) && cycleNumber >= 2;
            return valid && isSupportedScene(scenePath)
                ? new ExpeditionSlot(ExpeditionSlotStatus.Ready, saved, scenePath, savePointId, cycleNumber, sourceText)
                : unavailable;
        }

        /// <summary>シーンの準備中に保存内容が変わっていない場合だけ、現在のスロットを返す。</summary>
        public static bool TryReadUnchangedSlot(SaveDataStore store, int slot, Func<string, bool> isSupportedScene,
            ExpeditionSlot selected, out ExpeditionSlot current)
        {
            current = ReadSlot(store, slot, isSupportedScene);
            if (selected.Status != current.Status) { return false; }
            return current.Status == ExpeditionSlotStatus.Empty
                || (current.Status == ExpeditionSlotStatus.Ready && selected.Data.slotIndex == slot
                    && string.Equals(current.SourceText, selected.SourceText, StringComparison.Ordinal));
        }

        public static bool TryRestoreCheckpoint(SaveManager saves, ExpeditionSlot launch, string scenePath, string savePointId,
            out ExpeditionCheckpoint.Resources resources, Func<ExpeditionCheckpoint.Resources, bool> placeKnight)
        {
            resources = default;
            return launch.Status == ExpeditionSlotStatus.Ready && saves != null
                && ExpeditionCheckpoint.TryRestoreSlot(saves, saves.ActiveSlotIndex, launch.Data, scenePath, savePointId,
                    out resources, placeKnight);
        }

        public static bool TryRestoreStart(SaveManager saves, ExpeditionSlot launch, string scenePath)
        {
            return launch.Status == ExpeditionSlotStatus.Ready && launch.SavePointId == null && launch.CycleNumber >= 2
                && saves != null && saves.ActiveSlotIndex == launch.Data.slotIndex
                && string.Equals(launch.ScenePath, scenePath, StringComparison.Ordinal)
                && saves.TryLoadSlot(saves.ActiveSlotIndex, launch.Data);
        }
    }
}
