using System;
using System.IO;
using Game.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    public sealed class ExpeditionDeckPresetTests
    {
        [Test]
        public void Presets_AreIndependentSnapshotsAndCanExceedTheNormalSortieBudget()
        {
            ExpeditionSquad squad = CreateSquad(new FlagManager());
            ExpeditionDeckPresets presets = new ExpeditionDeckPresets(squad, 2);
            ExpeditionSquad.Selection[] normal = { new ExpeditionSquad.Selection("scout", 1) };
            ExpeditionSquad.Selection[] larger = { new ExpeditionSquad.Selection("shield", 3) };
            Assert.IsTrue(squad.TryConfirm(normal));
            Assert.IsTrue(presets.TrySave(0, normal));
            Assert.IsTrue(presets.TrySave(1, larger));
            larger[0] = new ExpeditionSquad.Selection("unlisted", 10);
            Assert.IsFalse(presets.TrySave(1, larger));
            Assert.IsFalse(presets.TrySave(2, normal));

            Assert.IsTrue(presets.TryGet(0, out var first));
            Assert.IsTrue(presets.TryGet(1, out var second));
            Assert.AreEqual("scout", first[0].SpeciesId);
            Assert.AreEqual("shield", second[0].SpeciesId);
            Assert.AreEqual(3, second[0].Count);
            Assert.IsFalse(squad.TryConfirm(second));
            Assert.AreEqual("scout", squad.Confirmed[0].SpeciesId);
        }

        [Test]
        public void Presets_RestoreEachSlotFromDiskAndClearForAnOlderSave()
        {
            string path = Path.Combine(Path.GetTempPath(), "Cave-Dungeon-decks-" + Guid.NewGuid().ToString("N"));
            try
            {
                FlagManager flags = new FlagManager();
                ExpeditionSquad squad = CreateSquad(flags);
                ExpeditionDeckPresets presets = new ExpeditionDeckPresets(squad, 4);
                Assert.IsTrue(presets.TrySave(0, new[] { new ExpeditionSquad.Selection("scout", 1) }));
                Assert.IsTrue(presets.TrySave(2, new[] { new ExpeditionSquad.Selection("shield", 3) }));
                SaveManager writer = new SaveManager();
                writer.Register(flags);
                writer.Register(presets);
                SaveDataStore store = new SaveDataStore(path);
                store.WriteToDisk(writer.Save(0));

                FlagManager loadedFlags = new FlagManager();
                ExpeditionDeckPresets restored = new ExpeditionDeckPresets(CreateSquad(loadedFlags), 3);
                SaveManager reader = new SaveManager();
                reader.Register(loadedFlags);
                reader.Register(restored);
                reader.SetSlotData(0, store.ReadFromDisk(0));
                Assert.IsTrue(reader.Load(0));
                Assert.IsTrue(restored.TryGet(0, out var first));
                Assert.IsFalse(restored.TryGet(1, out _));
                Assert.IsTrue(restored.TryGet(2, out var third));
                Assert.AreEqual("scout", first[0].SpeciesId);
                Assert.AreEqual(3, third[0].Count);

                reader.SetSlotData(1, new SaveSlotData(1));
                Assert.IsTrue(reader.Load(1));
                Assert.IsFalse(restored.TryGet(0, out _));
                Assert.IsFalse(restored.TryGet(2, out _));
            }
            finally
            {
                if (Directory.Exists(path)) { Directory.Delete(path, true); }
            }
        }

        [Test]
        public void Presets_AnInvalidSavedSlotLeavesOtherSlotsAvailable()
        {
            ExpeditionDeckPresets presets = new ExpeditionDeckPresets(CreateSquad(new FlagManager()), 3);
            JArray payload = JArray.Parse("[[{\"speciesId\":\"scout\",\"count\":\"bad\"}],"
                + "[{\"speciesId\":\"retired\",\"count\":1}],[{\"speciesId\":\"shield\",\"count\":2}]]");

            ((ISaveable)presets).Deserialize(payload);

            Assert.IsFalse(presets.TryGet(0, out _));
            Assert.IsFalse(presets.TryGet(1, out _));
            Assert.IsTrue(presets.TryGet(2, out var third));
            Assert.AreEqual(2, third[0].Count);
        }

        private static ExpeditionSquad CreateSquad(FlagManager flags)
        {
            ExpeditionAidUnlocks unlocks = new ExpeditionAidUnlocks(flags);
            ExpeditionAidUnlocks.Completion[] completions =
            {
                new ExpeditionAidUnlocks.Completion("scout.helped", "scout"),
                new ExpeditionAidUnlocks.Completion("shield.helped", "shield"),
            };
            unlocks.TryComplete("scout.helped", completions);
            unlocks.TryComplete("shield.helped", completions);
            ExpeditionSquad.Species[] species =
            {
                new ExpeditionSquad.Species("scout", 1),
                new ExpeditionSquad.Species("shield", 3),
            };
            return new ExpeditionSquad(unlocks, species, maxCount: 2, maxCost: 3);
        }
    }
}
