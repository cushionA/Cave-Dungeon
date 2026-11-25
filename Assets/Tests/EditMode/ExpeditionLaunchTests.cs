using System;
using System.IO;
using Game.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    public sealed class ExpeditionLaunchTests
    {
        private const string k_CampScene = "Assets/Scenes/ExpeditionMock3DScene.unity";
        private const string k_NextScene = "Assets/Scenes/NextExpeditionStart.unity";
        private const string k_SavePoint = "expedition.entry-camp";
        private const string k_CycleEntry = "ExpeditionPlaythrough";

        private string _directory;
        private SaveDataStore _store;

        [SetUp]
        public void CreateStore()
        {
            _directory = Path.Combine(Path.GetTempPath(), "ExpeditionLaunch-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_directory, "saves"));
            _store = new SaveDataStore(_directory);
        }

        [TearDown]
        public void RemoveStore() => Directory.Delete(_directory, recursive: true);

        [Test]
        public void ReadSlot_OnlyAbsentPrimaryAndBackupIsEmpty()
        {
            Assert.AreEqual(ExpeditionSlotStatus.Empty, Read(0).Status);
            File.WriteAllText(SlotFile(0) + SaveDataStore.k_TempExtension, "Interrupted write");
            Assert.AreEqual(ExpeditionSlotStatus.Empty, Read(0).Status);

            Assert.IsTrue(_store.TryWriteToDisk(new SaveSlotData(0)));
            Assert.AreEqual(ExpeditionSlotStatus.Unavailable, Read(0).Status);
            Assert.AreEqual(ExpeditionSlotStatus.Empty, Read(1).Status);
            Assert.AreEqual(ExpeditionSlotStatus.Unavailable, Read(-1).Status);
            Assert.AreEqual(ExpeditionSlotStatus.Unavailable, Read(SaveManager.k_MaxSlots).Status);
        }

        [Test]
        public void ReadSlot_UsesRecordedCheckpointAheadOfEarlierCycleStart()
        {
            using var world = new GameManagerCore();
            world.Saves.SetActiveSlot(1);
            StartNextCycle(world);
            SaveCheckpoint(world);
            string timestamp = _store.ReadFromDisk(1).timestamp;
            string contents = File.ReadAllText(SlotFile(1));

            ExpeditionSlot slot = Read(1);

            Assert.AreEqual(ExpeditionSlotStatus.Ready, slot.Status);
            Assert.AreEqual(k_CampScene, slot.ScenePath);
            Assert.AreEqual(k_SavePoint, slot.SavePointId);
            Assert.AreEqual(2, slot.CycleNumber);
            Assert.AreEqual(timestamp, slot.Timestamp);
            Assert.AreEqual(contents, File.ReadAllText(SlotFile(1)));
        }

        [Test]
        public void ReadSlot_RequiresAnExactEnabledSceneForNextCycle()
        {
            using var world = new GameManagerCore();
            StartNextCycle(world);

            ExpeditionSlot slot = Read(0);
            Assert.AreEqual(ExpeditionSlotStatus.Ready, slot.Status);
            Assert.AreEqual(k_NextScene, slot.ScenePath);
            Assert.IsTrue(string.IsNullOrEmpty(slot.SavePointId));
            Assert.AreEqual(2, slot.CycleNumber);
            Assert.AreEqual(ExpeditionSlotStatus.Unavailable,
                ExpeditionLaunch.ReadSlot(_store, 0, path => path == "Assets/Scenes/Other/NextExpeditionStart.unity").Status);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ReadSlot_PresentInvalidCheckpointPreventsFallbackToNextCycle(bool nullEntry)
        {
            using var world = new GameManagerCore();
            StartNextCycle(world);
            SaveSlotData saved = _store.ReadFromDisk(0);
            saved.entries[ExpeditionCheckpoint.k_SaveId] = nullEntry ? null : new JObject
            {
                ["scenePath"] = k_CampScene,
                ["savePointId"] = k_SavePoint,
                ["hp"] = 0,
                ["mp"] = 20,
                ["stamina"] = 30,
                ["armor"] = 0,
            };
            Assert.IsTrue(_store.TryWriteToDisk(saved));

            Assert.AreEqual(ExpeditionSlotStatus.Unavailable, Read(0).Status);
        }

        [Test]
        public void ReadSlot_LegacyCheckpointUsesCycleOneButMalformedCycleIsUnavailable()
        {
            using var world = new GameManagerCore();
            SaveCheckpoint(world);
            SaveSlotData saved = _store.ReadFromDisk(0);
            Assert.IsTrue(saved.entries.Remove(k_CycleEntry));
            Assert.IsTrue(_store.TryWriteToDisk(saved));
            ExpeditionSlot legacy = Read(0);
            Assert.AreEqual(ExpeditionSlotStatus.Ready, legacy.Status);
            Assert.AreEqual(1, legacy.CycleNumber);

            saved.entries[k_CycleEntry] = "2";
            Assert.IsTrue(_store.TryWriteToDisk(saved));
            Assert.AreEqual(ExpeditionSlotStatus.Unavailable, Read(0).Status);
        }

        [Test]
        public void ReadSlot_BackupOnlyCanContinueAndCorruptionNeverMarksItEmpty()
        {
            using var world = new GameManagerCore();
            SaveCheckpoint(world);
            File.Copy(SlotFile(0), SlotFile(1));
            Assert.AreEqual(ExpeditionSlotStatus.Unavailable, Read(1).Status, "The embedded slot must match.");

            string backup = SlotFile(0) + SaveDataStore.k_BackupExtension;
            File.Move(SlotFile(0), backup);
            Assert.AreEqual(ExpeditionSlotStatus.Ready, Read(0).Status);
            File.WriteAllText(backup, "{");
            Assert.AreEqual(ExpeditionSlotStatus.Unavailable, Read(0).Status);
            Assert.AreEqual("{", File.ReadAllText(backup));
        }

        private ExpeditionSlot Read(int slot) => ExpeditionLaunch.ReadSlot(_store, slot,
            scene => scene == k_CampScene || scene == k_NextScene);

        private string SlotFile(int slot) => Path.Combine(_directory, "saves", $"slot_{slot}.json");

        private void StartNextCycle(GameManagerCore world)
        {
            world.NextPlaythrough.MarkCleared();
            Assert.IsTrue(world.NextPlaythrough.TryBeginNextPlaythrough(world.Saves.ActiveSlotIndex,
                world.Saves.LoadRevision, 1, k_NextScene, _store));
        }

        private void SaveCheckpoint(GameManagerCore world)
        {
            CharacterVitals vitals = new CharacterVitals
            {
                currentHp = 30, maxHp = 100, maxMp = 50, maxStamina = 60f,
            };
            Assert.IsTrue(ExpeditionCheckpoint.TrySaveCurrentSlot(world.Saves, _store, k_CampScene, k_SavePoint,
                vitals, out _));
        }
    }
}
