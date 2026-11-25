using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Game.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.EditMode
{
    public sealed class ExpeditionNextPlaythroughTests
    {
        private const string k_StartScene = "Assets/Scenes/ExpeditionEntrance.unity";
        private const string k_SpeciesId = "goblin-engineer";
        private const string k_StoryFlag = "story.mine-survey";
        private const string k_CarryFlag = "achievement.first-clear";
        private string _directory;
        private SaveDataStore _store;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "ExpeditionNextPlaythrough-" + Guid.NewGuid().ToString("N"));
            _store = new SaveDataStore(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory)) { Directory.Delete(_directory, recursive: true); }
        }

        [Test]
        public void NextPlaythrough_RequiresTheCurrentClearedSlotAndRevision_AndConsumesTheClearOnce()
        {
            using var core = new GameManagerCore();
            ExpeditionNextPlaythrough next = core.NextPlaythrough;
            ulong beforeLoad = core.Saves.LoadRevision;
            Assert.IsFalse(next.CanBeginNext);
            Assert.IsFalse(next.TryBeginNextPlaythrough(0, beforeLoad, 1, k_StartScene, _store));
            Assert.IsFalse(_store.HasSlotFile(0));

            next.MarkCleared();
            next.MarkCleared();
            Assert.IsTrue(next.CanBeginNext);
            Assert.IsTrue(_store.TryWriteToDisk(core.Saves.Save(0)));
            Assert.IsTrue(core.Saves.Load(0));
            ulong revision = core.Saves.LoadRevision;
            Assert.IsTrue(next.CanBeginNext);
            Assert.AreNotEqual(beforeLoad, revision);
            Assert.IsFalse(next.TryBeginNextPlaythrough(0, beforeLoad, 1, k_StartScene, _store));
            Assert.IsFalse(next.TryBeginNextPlaythrough(1, revision, 1, k_StartScene, _store));
            Assert.IsFalse(next.TryBeginNextPlaythrough(0, revision, 2, k_StartScene, _store));
            Assert.AreEqual(1, core.Playthrough.CycleNumber);
            Assert.AreEqual(revision, core.Saves.LoadRevision);
            Assert.IsFalse(_store.HasSlotFile(1));

            Assert.IsTrue(next.TryBeginNextPlaythrough(0, revision, 1, k_StartScene, _store));
            Assert.AreEqual(2, core.Playthrough.CycleNumber);
            Assert.AreEqual(k_StartScene, next.StartScenePath);
            Assert.IsFalse(next.CanBeginNext);
            Assert.IsFalse(next.TryBeginNextPlaythrough(0, revision, 1, k_StartScene, _store));
            Assert.IsFalse(next.TryBeginNextPlaythrough(0, core.Saves.LoadRevision, 2, k_StartScene, _store));
            Assert.AreEqual(2, core.Playthrough.CycleNumber);
        }

        [Test]
        public void NextPlaythrough_WhenWritingFails_PreservesTheClearedRunAndCanBeRetried()
        {
            using var core = new GameManagerCore();
            ExpeditionNextPlaythrough next = core.NextPlaythrough;
            var story = new StorySave("native-story") { Value = 29 };
            core.Saves.Register(story);
            core.Flags.SetGlobalFlag(k_StoryFlag, true);
            next.MarkCleared();
            SaveSlotData original = core.Saves.Save(0);
            Assert.IsTrue(_store.TryWriteToDisk(original));
            Assert.IsTrue(core.Saves.Load(0));
            story.Value = 31;
            string path = SlotFile(0);
            string originalFile = File.ReadAllText(path);
            string blockedTemporaryPath = path + SaveDataStore.k_TempExtension;
            Directory.CreateDirectory(blockedTemporaryPath);

            ulong revision = core.Saves.LoadRevision;
            int notifications = 0;
            core.Saves.Loaded += _ => notifications++;
            LogAssert.Expect(LogType.Error, new Regex(@"\[SaveDataStore\].+一時ファイル書き込みに失敗しました"));
            Assert.IsFalse(next.TryBeginNextPlaythrough(0, revision, 1, k_StartScene, _store));
            Assert.AreSame(original, core.Saves.GetSlotData(0));
            Assert.AreSame(original, core.Saves.LastLoadedSlot);
            Assert.AreEqual(originalFile, File.ReadAllText(path));
            Assert.AreEqual(revision, core.Saves.LoadRevision);
            Assert.AreEqual(1, core.Playthrough.CycleNumber);
            Assert.IsTrue(core.Flags.GetGlobalFlag(k_StoryFlag));
            Assert.IsTrue(next.CanBeginNext);
            Assert.IsNull(next.StartScenePath);
            Assert.AreEqual(31, story.Value);
            Assert.AreEqual(0, notifications);

            Directory.Delete(blockedTemporaryPath);
            Assert.IsTrue(next.TryBeginNextPlaythrough(0, revision, 1, k_StartScene, _store));
            Assert.AreEqual(2, core.Playthrough.CycleNumber);
            Assert.AreEqual(0, story.Value);
            Assert.AreEqual(1, notifications);
            Assert.AreEqual(originalFile, File.ReadAllText(path + SaveDataStore.k_BackupExtension));
        }

        [Test]
        public void NextPlaythrough_WhenRestoringTheCandidateThrows_PreservesTheSaveAndOngoingEncounter()
        {
            using var core = new GameManagerCore();
            ExpeditionNextPlaythrough next = core.NextPlaythrough;
            Assert.IsTrue(core.Playthrough.TryBeginNextPlaythrough(1));
            var story = new StorySave("native-story") { Value = 29, RejectReset = true };
            core.Saves.Register(story);
            core.Flags.SetGlobalFlag(k_StoryFlag, true);
            next.MarkCleared();
            SaveSlotData original = core.Saves.Save(0);
            Assert.IsTrue(_store.TryWriteToDisk(original));
            Assert.IsTrue(core.Saves.Load(0));
            string path = SlotFile(0);
            string originalFile = File.ReadAllText(path);
            ulong revision = core.Saves.LoadRevision;
            int notifications = 0;
            core.Saves.Loaded += _ => notifications++;

            const string bossId = "sentinel";
            var boss = new BossControllerLogic(new[] { new BossPhaseData { phaseName = "Encounter" } }, bossId);
            var rules = new[] { new ExpeditionAidUnlocks.BossRule(bossId, k_SpeciesId, 2, k_StoryFlag) };
            using IDisposable watcher = core.ExpeditionAid.ObserveBossDefeats(boss, core.Playthrough, rules);
            boss.StartEncounter();

            LogAssert.Expect(LogType.Error, new Regex(@"\[SaveManager\].*スロットの復元に失敗しました"));
            Assert.IsFalse(next.TryBeginNextPlaythrough(0, revision, 2, k_StartScene, _store));

            Assert.AreSame(original, core.Saves.GetSlotData(0));
            Assert.AreSame(original, core.Saves.LastLoadedSlot);
            Assert.AreEqual(originalFile, File.ReadAllText(path));
            Assert.IsFalse(File.Exists(path + SaveDataStore.k_BackupExtension));
            Assert.AreEqual(0, core.Saves.ActiveSlotIndex);
            Assert.AreEqual(revision, core.Saves.LoadRevision);
            Assert.AreEqual(0, notifications);
            Assert.AreEqual(2, core.Playthrough.CycleNumber);
            Assert.AreEqual(29, story.Value);
            Assert.IsTrue(core.Flags.GetGlobalFlag(k_StoryFlag));
            Assert.IsTrue(next.CanBeginNext);
            Assert.IsNull(next.StartScenePath);
            boss.OnDefeated();
            Assert.IsTrue(core.ExpeditionAid.IsUnlocked(k_SpeciesId));
        }

        [Test]
        public void NextPlaythrough_WhenSavingToAnotherSlot_OnlyTheClearedSlotCanAdvance()
        {
            using var original = new GameManagerCore();
            original.Saves.SetActiveSlot(1);
            original.NextPlaythrough.MarkCleared();
            Assert.IsTrue(_store.TryWriteToDisk(original.Saves.Save(0)));
            original.Saves.SetActiveSlot(0);
            Assert.IsFalse(original.NextPlaythrough.CanBeginNext);
            Assert.IsTrue(_store.TryWriteToDisk(original.Saves.Save(1)));

            using var restored = new GameManagerCore();
            restored.Saves.SetSlotData(0, _store.ReadFromDisk(0));
            restored.Saves.SetSlotData(1, _store.ReadFromDisk(1));
            Assert.IsTrue(restored.Saves.Load(0));
            Assert.IsFalse(restored.NextPlaythrough.CanBeginNext);
            Assert.IsTrue(restored.Saves.Load(1));
            Assert.IsTrue(restored.NextPlaythrough.CanBeginNext);
            Assert.IsTrue(restored.Saves.Load(0));
            Assert.IsFalse(restored.NextPlaythrough.CanBeginNext);
        }

        [Test]
        public void NextPlaythrough_WhenOwningCoreIsDisposed_CannotMarkClearAdvanceOrRestore()
        {
            using var core = new GameManagerCore();
            ExpeditionNextPlaythrough next = core.NextPlaythrough;
            next.MarkCleared();
            Assert.IsTrue(next.TryBeginNextPlaythrough(0, core.Saves.LoadRevision, 1, k_StartScene, _store));
            next.MarkCleared();
            Assert.IsTrue(next.CanBeginNext);
            string committed = File.ReadAllText(SlotFile(0));

            core.Dispose();
            next.MarkCleared();

            Assert.IsFalse(next.CanBeginNext);
            Assert.IsFalse(next.TryBeginNextPlaythrough(0, core.Saves.LoadRevision, 2, k_StartScene, _store));
            Assert.IsFalse(next.TryRestoreStartSlot(0, k_StartScene, _store));
            Assert.IsFalse(core.Saves.HasSaveData(0));
            Assert.AreEqual(committed, File.ReadAllText(SlotFile(0)));
        }

        [Test]
        public void NextPlaythrough_ColdStartWithAMalformedCycle_DoesNotReplaceTheCurrentSlot()
        {
            using var original = new GameManagerCore();
            original.NextPlaythrough.MarkCleared();
            Assert.IsTrue(original.NextPlaythrough.TryBeginNextPlaythrough(0, original.Saves.LoadRevision, 1,
                k_StartScene, _store));
            SaveSlotData malformed = _store.ReadFromDisk(0);
            Assert.IsNotNull(malformed);
            malformed.entries[original.Playthrough.SaveId] = "2";
            Assert.IsTrue(_store.TryWriteToDisk(malformed));

            using var restored = new GameManagerCore();
            SaveSlotData otherSlot = restored.Saves.Save(1);
            Assert.IsTrue(restored.Saves.Load(1));
            restored.NextPlaythrough.MarkCleared();
            ulong revision = restored.Saves.LoadRevision;

            LogAssert.Expect(LogType.Error, new Regex(@"\[ExpeditionNextPlaythrough\].*周回番号が無効です"));
            Assert.IsFalse(restored.NextPlaythrough.TryRestoreStartSlot(0, k_StartScene, _store));

            Assert.AreEqual(1, restored.Playthrough.CycleNumber);
            Assert.IsTrue(restored.NextPlaythrough.CanBeginNext);
            Assert.IsNull(restored.NextPlaythrough.StartScenePath);
            Assert.IsFalse(restored.Saves.HasSaveData(0));
            Assert.AreEqual(1, restored.Saves.ActiveSlotIndex);
            Assert.AreSame(otherSlot, restored.Saves.LastLoadedSlot);
            Assert.AreEqual(revision, restored.Saves.LoadRevision);
        }

        [Test]
        public void NextPlaythrough_RestoresTheAuthoredStartWithCarryover_AndKeepsAnotherSlotSeparate()
        {
            using var original = new GameManagerCore();
            (ExpeditionSquad squad, ExpeditionDeckPresets presets) = RegisterRoster(original);
            var ordinary = new NumberSave("ordinary-progress") { Value = 13 };
            var story = new StorySave("native-story") { Value = 29 };
            original.Saves.Register(ordinary);
            original.Saves.Register(story);
            Assert.IsTrue(_store.TryWriteToDisk(original.Saves.Save(1)));
            string otherSlot = File.ReadAllText(SlotFile(1));

            Assert.IsTrue(original.ExpeditionAid.TryComplete("mine.helped",
                new[] { new ExpeditionAidUnlocks.Completion("mine.helped", k_SpeciesId) }));
            Assert.IsTrue(squad.TryConfirm(new[] { new ExpeditionSquad.Selection(k_SpeciesId, 2) }));
            Assert.IsTrue(presets.TrySave(1, new[] { new ExpeditionSquad.Selection(k_SpeciesId, 1) }));
            original.Flags.SetGlobalFlag(k_CarryFlag, true);
            original.Flags.SetGlobalFlag(k_StoryFlag, true);
            original.Flags.SwitchMap("entrance");
            original.Flags.SetLocalFlag("gate-open", true);
            original.Flags.SwitchMap("mine");
            original.Flags.SetLocalFlag("chest-open", true);
            original.NextPlaythrough.MarkCleared();
            Assert.IsTrue(_store.TryWriteToDisk(original.Saves.Save(0)));

            Assert.IsTrue(original.NextPlaythrough.TryBeginNextPlaythrough(0, original.Saves.LoadRevision, 1,
                k_StartScene, _store, new[] { k_CarryFlag }));
            Assert.AreEqual(0, story.Value);
            Assert.AreEqual(13, ordinary.Value);
            Assert.AreEqual(otherSlot, File.ReadAllText(SlotFile(1)));

            using var restored = new GameManagerCore();
            (ExpeditionSquad restoredSquad, ExpeditionDeckPresets restoredPresets) = RegisterRoster(restored);
            var restoredOrdinary = new NumberSave(ordinary.SaveId);
            var restoredStory = new StorySave(story.SaveId) { Value = 99 };
            restored.Saves.Register(restoredOrdinary);
            restored.Saves.Register(restoredStory);
            Assert.IsFalse(restored.NextPlaythrough.TryRestoreStartSlot(0, "Assets/Scenes/OtherEntrance.unity", _store));
            Assert.IsFalse(restored.NextPlaythrough.TryRestoreStartSlot(1, k_StartScene, _store));
            Assert.AreEqual(1, restored.Playthrough.CycleNumber);
            Assert.AreEqual(99, restoredStory.Value);

            Assert.IsTrue(restored.NextPlaythrough.TryRestoreStartSlot(0, k_StartScene, _store));
            Assert.AreEqual(0, restored.Saves.ActiveSlotIndex);
            Assert.AreEqual(2, restored.Playthrough.CycleNumber);
            Assert.AreEqual(k_StartScene, restored.NextPlaythrough.StartScenePath);
            Assert.IsFalse(restored.NextPlaythrough.CanBeginNext);
            Assert.IsTrue(restored.Flags.GetGlobalFlag(k_CarryFlag));
            Assert.IsFalse(restored.Flags.GetGlobalFlag(k_StoryFlag));
            restored.Flags.SwitchMap("entrance");
            Assert.IsFalse(restored.Flags.GetLocalFlag("gate-open"));
            restored.Flags.SwitchMap("mine");
            Assert.IsFalse(restored.Flags.GetLocalFlag("chest-open"));
            Assert.IsTrue(restored.ExpeditionAid.IsUnlocked(k_SpeciesId));
            Assert.AreEqual(k_SpeciesId, restoredSquad.Confirmed.Single().SpeciesId);
            Assert.AreEqual(2, restoredSquad.ConfirmedCount);
            Assert.AreEqual(4, restoredSquad.ConfirmedCost);
            Assert.IsTrue(restoredPresets.TryGet(1, out var carriedPreset));
            Assert.AreEqual(k_SpeciesId, carriedPreset.Single().SpeciesId);
            Assert.AreEqual(1, carriedPreset.Single().Count);
            Assert.AreEqual(13, restoredOrdinary.Value);
            Assert.AreEqual(0, restoredStory.Value);

            restored.Saves.SetSlotData(1, _store.ReadFromDisk(1));
            Assert.IsTrue(restored.Saves.Load(1));
            Assert.AreEqual(1, restored.Playthrough.CycleNumber);
            Assert.IsFalse(restored.ExpeditionAid.IsUnlocked(k_SpeciesId));
            Assert.IsFalse(restored.Flags.GetGlobalFlag(k_CarryFlag));
            Assert.IsFalse(restored.NextPlaythrough.CanBeginNext);
            Assert.IsEmpty(restoredSquad.Confirmed);
            Assert.IsFalse(restoredPresets.TryGet(1, out _));
            Assert.AreEqual(29, restoredStory.Value);
        }

        private string SlotFile(int slot) => Path.Combine(_directory, "saves", $"slot_{slot}.json");

        private static (ExpeditionSquad, ExpeditionDeckPresets) RegisterRoster(GameManagerCore core)
        {
            var squad = new ExpeditionSquad(core.ExpeditionAid,
                new[] { new ExpeditionSquad.Species(k_SpeciesId, 2) }, 3, 5);
            var presets = new ExpeditionDeckPresets(squad, 2);
            core.Saves.Register(squad);
            core.Saves.Register(presets);
            return (squad, presets);
        }

        private class NumberSave : ISaveable
        {
            public string SaveId { get; }
            public int Value { get; set; }

            public NumberSave(string saveId) { SaveId = saveId; }

            public object Serialize() => Value;

            public void Deserialize(object data) => Value = Convert.ToInt32(data);
        }

        private sealed class StorySave : NumberSave, IResetOnNextPlaythrough
        {
            public bool RejectReset { get; set; }

            public StorySave(string saveId) : base(saveId) { }

            public void RestoreMissingEntry()
            {
                Value = 0;
                if (RejectReset) { throw new InvalidOperationException("Rejected story reset"); }
            }
        }
    }
}
