using System;
using System.IO;
using System.Text.RegularExpressions;
using Game.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.EditMode
{
    public sealed class ExpeditionCheckpointTests
    {
        private const string k_Scene = "Assets/Scenes/ExpeditionMock3DScene.unity";
        private const string k_Point = "expedition.entry-camp";
        private const string k_Flag = "survey.completed";
        private string _directory;
        private SaveDataStore _store;

        [SetUp]
        public void CreateStore()
        {
            _directory = Path.Combine(Path.GetTempPath(), "ExpeditionCheckpoint-" + Guid.NewGuid().ToString("N"));
            _store = new SaveDataStore(_directory);
        }

        [TearDown]
        public void RemoveStore()
        {
            if (Directory.Exists(_directory)) { Directory.Delete(_directory, recursive: true); }
        }

        [Test]
        public void SaveAndRestore_ActiveSlotPreservesProgressAndRestoresResourcesWithinCurrentCaps()
        {
            using var original = new GameManagerCore();
            original.Saves.SetActiveSlot(1);
            original.Flags.SetGlobalFlag(k_Flag, true);
            int notifications = 0;
            original.Saves.Loaded += _ => notifications++;
            CharacterVitals vitals = InjuredKnight();

            Assert.IsTrue(ExpeditionCheckpoint.TrySaveCurrentSlot(original.Saves, _store, k_Scene, k_Point, vitals, out var recovery));
            Assert.AreEqual(35, vitals.currentHp);
            Assert.AreEqual(0, notifications);
            Assert.IsNull(original.Saves.LastLoadedSlot);
            Assert.AreEqual(0UL, original.Saves.LoadRevision);
            Assert.IsTrue(original.Saves.GetSlotData(1).entries.ContainsKey(ExpeditionCheckpoint.k_SaveId));
            Assert.IsFalse(_store.HasSlotFile(0));
            recovery.ApplyRecovery(ref vitals);
            Assert.AreEqual(100, vitals.currentHp);
            Assert.AreEqual(40f, vitals.currentMp);
            Assert.AreEqual(70f, vitals.currentStamina);
            Assert.AreEqual(12f, vitals.currentArmor);

            using var resumed = new GameManagerCore();
            resumed.Saves.SetActiveSlot(1);
            Assert.IsTrue(ExpeditionCheckpoint.TryRestoreCurrentSlot(resumed.Saves, _store, k_Scene, k_Point, out var restored));
            Assert.IsTrue(resumed.Flags.GetGlobalFlag(k_Flag));
            Assert.AreEqual(1, resumed.Saves.LastLoadedSlot.slotIndex);
            Assert.AreEqual(1UL, resumed.Saves.LoadRevision);
            vitals = new CharacterVitals { maxHp = 80, maxMp = 50, maxStamina = 55f, maxArmor = 20f };
            restored.Restore(ref vitals);
            Assert.AreEqual(80, vitals.currentHp);
            Assert.AreEqual(40f, vitals.currentMp);
            Assert.AreEqual(55f, vitals.currentStamina);
            Assert.AreEqual(12f, vitals.currentArmor);
        }

        [Test]
        public void Save_WhenDiskWriteFails_KeepsThePreviousSlotAndRecoveryUncommitted()
        {
            using var world = new GameManagerCore();
            CharacterVitals vitals = InjuredKnight();
            Assert.IsTrue(ExpeditionCheckpoint.TrySaveCurrentSlot(world.Saves, _store, k_Scene, k_Point, vitals, out _));
            SaveSlotData original = world.Saves.GetSlotData(0);
            string path = Path.Combine(_directory, "saves", "slot_0.json");
            string originalDisk = File.ReadAllText(path);
            Directory.CreateDirectory(path + SaveDataStore.k_TempExtension);

            world.Flags.SetGlobalFlag(k_Flag, true);
            LogAssert.Expect(LogType.Error, new Regex(@"\[SaveDataStore\].+一時ファイル書き込みに失敗しました"));
            Assert.IsFalse(ExpeditionCheckpoint.TrySaveCurrentSlot(world.Saves, _store, k_Scene, k_Point, vitals, out var recovery));

            Assert.AreSame(original, world.Saves.GetSlotData(0));
            Assert.AreEqual(originalDisk, File.ReadAllText(path));
            Assert.AreEqual(35, vitals.currentHp);
            Assert.AreEqual(0, recovery.Hp);
            Assert.AreEqual(0UL, world.Saves.LoadRevision);
            Assert.IsTrue(world.Flags.GetGlobalFlag(k_Flag));
        }

        [Test]
        public void Restore_WhenSceneOrPointDoesNotMatch_DoesNotPublishOrApplySavedState()
        {
            using var original = new GameManagerCore();
            original.Flags.SetGlobalFlag(k_Flag, true);
            Assert.IsTrue(ExpeditionCheckpoint.TrySaveCurrentSlot(original.Saves, _store, k_Scene, k_Point, InjuredKnight(), out _));

            using var resumed = new GameManagerCore();
            Assert.IsFalse(ExpeditionCheckpoint.TryRestoreCurrentSlot(resumed.Saves, _store, "Assets/Scenes/Other.unity", k_Point, out _));
            Assert.IsFalse(ExpeditionCheckpoint.TryRestoreCurrentSlot(resumed.Saves, _store, k_Scene, "expedition.other", out _));
            Assert.IsFalse(resumed.Flags.GetGlobalFlag(k_Flag));
            Assert.IsNull(resumed.Saves.GetSlotData(0));
            Assert.IsNull(resumed.Saves.LastLoadedSlot);
            Assert.AreEqual(0UL, resumed.Saves.LoadRevision);
        }

        [Test]
        public void NextCycle_WhenOrdinaryCheckpointExists_UsesCheckpointOnlyUntilNextCycleBegins()
        {
            using var original = new GameManagerCore();
            original.NextPlaythrough.MarkCleared();
            Assert.IsTrue(original.NextPlaythrough.TryBeginNextPlaythrough(0, original.Saves.LoadRevision, 1, k_Scene, _store));
            Assert.IsTrue(ExpeditionCheckpoint.TrySaveCurrentSlot(original.Saves, _store, k_Scene, k_Point, InjuredKnight(), out _));

            using var resumed = new GameManagerCore();
            Assert.IsFalse(resumed.NextPlaythrough.TryRestoreStartSlot(0, k_Scene, _store));
            Assert.AreEqual(1, resumed.Playthrough.CycleNumber);
            Assert.IsTrue(ExpeditionCheckpoint.TryRestoreCurrentSlot(resumed.Saves, _store, k_Scene, k_Point, out _));
            Assert.AreEqual(2, resumed.Playthrough.CycleNumber);
            resumed.NextPlaythrough.MarkCleared();
            Assert.IsTrue(resumed.NextPlaythrough.TryBeginNextPlaythrough(0, resumed.Saves.LoadRevision, 2, k_Scene, _store));
            Assert.IsFalse(resumed.Saves.LastLoadedSlot.entries.ContainsKey(ExpeditionCheckpoint.k_SaveId));
            Assert.IsFalse(ExpeditionCheckpoint.TryRestoreCurrentSlot(resumed.Saves, _store, k_Scene, k_Point, out _));
        }

        private static CharacterVitals InjuredKnight() => new CharacterVitals
        {
            currentHp = 35, maxHp = 100,
            currentMp = 5f, maxMp = 40,
            currentStamina = 10f, maxStamina = 70f,
            currentArmor = 12f, maxArmor = 20f,
        };
    }
}
