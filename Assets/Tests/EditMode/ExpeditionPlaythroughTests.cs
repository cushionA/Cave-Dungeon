using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Game.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.EditMode
{
    public sealed class ExpeditionPlaythroughTests
    {
        private const string k_InvalidCycleLogPattern = @"\[SaveManager\].*スロットの事前検証に失敗しました.*周回番号";

        [Test]
        public void Playthrough_AdvanceOnlyWhenTheExpectedCycleMatches_DoesNotRepeatStart()
        {
            ExpeditionPlaythrough progress = new ExpeditionPlaythrough();
            Assert.AreEqual(1, progress.CycleNumber);
            Assert.IsFalse(progress.TryBeginNextPlaythrough(2));
            Assert.IsTrue(progress.TryBeginNextPlaythrough(1));
            Assert.IsFalse(progress.TryBeginNextPlaythrough(1));
            Assert.AreEqual(2, progress.CycleNumber);

            ((ISaveable)progress).Deserialize(int.MaxValue);
            Assert.IsFalse(progress.TryBeginNextPlaythrough(int.MaxValue));
            Assert.AreEqual(int.MaxValue, progress.CycleNumber);
        }

        [Test]
        public void Playthrough_DiskRestoreDefaultsMissingCycleAndRejectsMalformedCycle_WithoutLosingProgress()
        {
            string temporaryDirectory = Path.Combine(Path.GetTempPath(), "ExpeditionCycle-" + Guid.NewGuid().ToString("N"));
            try
            {
                SaveDataStore disk = new SaveDataStore(temporaryDirectory);
                using GameManagerCore original = new GameManagerCore();
                ExpeditionSquad squad = CreateSquad(original.ExpeditionAid);
                original.Saves.Register(squad);
                BossControllerLogic boss = CreateBoss("sentinel");
                ExpeditionAidUnlocks.BossRule[] rules = { new ExpeditionAidUnlocks.BossRule("sentinel", "sentinel.aid", 2) };
                using IDisposable watcher = original.ExpeditionAid.ObserveBossDefeats(boss, original.Playthrough, rules);

                Assert.IsTrue(original.Playthrough.TryBeginNextPlaythrough(1));
                Defeat(boss);
                Assert.IsTrue(squad.TryConfirm(new[] { new ExpeditionSquad.Selection("sentinel.aid", 2) }));
                Assert.AreEqual(4, squad.ConfirmedCost);
                disk.WriteToDisk(original.Saves.Save(0));
                disk.WriteToDisk(new SaveSlotData(1));
                SaveSlotData malformed = new SaveSlotData(2);
                malformed.entries[original.Playthrough.SaveId] = "2";
                disk.WriteToDisk(malformed);

                using GameManagerCore restored = new GameManagerCore();
                ExpeditionSquad restoredSquad = CreateSquad(restored.ExpeditionAid);
                restored.Saves.Register(restoredSquad);
                for (int slot = 0; slot < SaveManager.k_MaxSlots; slot++)
                {
                    SaveSlotData saved = disk.ReadFromDisk(slot);
                    Assert.IsNotNull(saved);
                    restored.Saves.SetSlotData(slot, saved);
                }

                SaveSlotData current = restored.Saves.GetSlotData(0);
                SaveSlotData rejected = restored.Saves.GetSlotData(2);
                Assert.IsTrue(restored.Saves.Load(0));
                Assert.AreEqual(2, restored.Playthrough.CycleNumber);
                Assert.IsTrue(restored.ExpeditionAid.IsUnlocked("sentinel.aid"));
                Assert.AreEqual(2, restoredSquad.ConfirmedCount);
                Assert.AreEqual(4, restoredSquad.ConfirmedCost);

                ulong revision = restored.Saves.LoadRevision;
                int notifications = 0;
                restored.Saves.Loaded += _ => notifications++;
                LogAssert.Expect(LogType.Error, new Regex(k_InvalidCycleLogPattern));
                Assert.IsFalse(restored.Saves.Load(2));
                Assert.AreEqual(2, restored.Playthrough.CycleNumber);
                Assert.IsTrue(restored.ExpeditionAid.IsUnlocked("sentinel.aid"));
                Assert.AreEqual(2, restoredSquad.ConfirmedCount);
                Assert.AreEqual(4, restoredSquad.ConfirmedCost);
                Assert.AreEqual(0, restored.Saves.ActiveSlotIndex);
                Assert.AreSame(current, restored.Saves.LastLoadedSlot);
                Assert.AreSame(rejected, restored.Saves.GetSlotData(2));
                Assert.AreEqual(revision, restored.Saves.LoadRevision);
                Assert.AreEqual(0, notifications);

                Assert.IsTrue(restored.Saves.Load(1));
                Assert.AreEqual(1, restored.Playthrough.CycleNumber);
                Assert.IsFalse(restored.ExpeditionAid.IsUnlocked("sentinel.aid"));
                Assert.IsEmpty(restoredSquad.Confirmed);
                Assert.AreEqual(1, notifications);
            }
            finally
            {
                if (Directory.Exists(temporaryDirectory)) { Directory.Delete(temporaryDirectory, recursive: true); }
            }
        }

        [TestCase(null)]
        [TestCase(0)]
        [TestCase(-1)]
        [TestCase((long)int.MaxValue + 1)]
        [TestCase(2.0)]
        public void Playthrough_Load_WhenAnInvalidCycleEntryIsPresent_RejectsTheSlot(object invalidCycle)
        {
            using var core = new GameManagerCore();
            Assert.IsTrue(core.Playthrough.TryBeginNextPlaythrough(1));
            var rejected = new SaveSlotData(1);
            rejected.entries[core.Playthrough.SaveId] = invalidCycle;
            core.Saves.SetSlotData(1, rejected);

            LogAssert.Expect(LogType.Error, new Regex(k_InvalidCycleLogPattern));
            Assert.IsFalse(core.Saves.Load(1));
            Assert.AreEqual(2, core.Playthrough.CycleNumber);
            Assert.IsNull(core.Saves.LastLoadedSlot);
        }

        [TestCase(1, 1, false)]
        [TestCase(2L, 2, false)]
        [TestCase((long)int.MaxValue, int.MaxValue, true)]
        public void Playthrough_Load_WhenTheCycleIsAnInRangeInteger_RestoresExactValue(object value, int expected, bool asJson)
        {
            using var core = new GameManagerCore();
            var slot = new SaveSlotData(1);
            slot.entries[core.Playthrough.SaveId] = asJson ? new JValue(value) : value;
            core.Saves.SetSlotData(1, slot);

            Assert.IsTrue(core.Saves.Load(1));
            Assert.AreEqual(expected, core.Playthrough.CycleNumber);
        }

        [Test]
        public void BossDefeat_MatchesAuthoredCycleAndPrerequisite_UnlocksOneCostedSquadSpecies()
        {
            FlagManager flags = new FlagManager();
            ExpeditionAidUnlocks unlocks = new ExpeditionAidUnlocks(flags);
            ExpeditionPlaythrough progress = new ExpeditionPlaythrough();
            ExpeditionSquad squad = CreateSquad(unlocks);
            BossControllerLogic boss = CreateBoss("sentinel");
            BossControllerLogic unlisted = CreateBoss("other");
            ExpeditionAidUnlocks.BossRule[] rules =
            {
                new ExpeditionAidUnlocks.BossRule("sentinel", "sentinel.aid", 2, "story.sentinel"),
            };
            using IDisposable watcher = unlocks.ObserveBossDefeats(boss, progress, rules);
            using IDisposable otherWatcher = unlocks.ObserveBossDefeats(unlisted, progress, rules);

            flags.SetGlobalFlag("story.sentinel", true);
            Defeat(boss);
            Assert.IsEmpty(squad.GetUnlockedSpecies());
            boss.StartEncounter();
            Assert.IsTrue(progress.TryBeginNextPlaythrough(1));
            boss.OnDefeated();
            Assert.IsEmpty(squad.GetUnlockedSpecies(), "A previous cycle's encounter cannot unlock aid.");

            flags.SetGlobalFlag("story.sentinel", false);
            Defeat(boss);
            Assert.IsEmpty(squad.GetUnlockedSpecies());
            flags.SetGlobalFlag("story.sentinel", true);
            Defeat(unlisted);
            Assert.IsEmpty(squad.GetUnlockedSpecies());
            Defeat(boss);
            boss.OnDefeated();
            Assert.IsFalse(unlocks.TryCompleteBoss("sentinel", progress, rules));
            Assert.AreEqual("sentinel.aid", squad.GetUnlockedSpecies().Single().SpeciesId);
            Assert.IsFalse(squad.TryConfirm(new[] { new ExpeditionSquad.Selection("sentinel.aid", 3) }));
            Assert.IsTrue(squad.TryConfirm(new[] { new ExpeditionSquad.Selection("sentinel.aid", 2) }));
            Assert.AreEqual(4, squad.ConfirmedCost);
        }

        [Test]
        public void BossDefeat_LoadingAnotherSlotAtTheSameCycleRejectsTheOldEncounter()
        {
            using GameManagerCore core = new GameManagerCore();
            Assert.IsTrue(core.Playthrough.TryBeginNextPlaythrough(1));
            core.Saves.Save(0);
            core.Saves.Save(1);
            BossControllerLogic boss = CreateBoss("sentinel");
            ExpeditionAidUnlocks.BossRule[] rules = { new ExpeditionAidUnlocks.BossRule("sentinel", "sentinel.aid", 2) };
            using IDisposable watcher = core.ExpeditionAid.ObserveBossDefeats(boss, core.Playthrough, rules);

            boss.StartEncounter();
            Assert.IsTrue(core.Saves.Load(1));
            Assert.AreEqual(2, core.Playthrough.CycleNumber);
            boss.OnDefeated();
            Assert.IsFalse(core.ExpeditionAid.IsUnlocked("sentinel.aid"));

            Defeat(boss);
            Assert.IsTrue(core.ExpeditionAid.IsUnlocked("sentinel.aid"));
        }

        [Test]
        public void BossDefeat_ConflictingMappingsOrUnsubscribedEncounter_LeaveAidLocked()
        {
            ExpeditionAidUnlocks unlocks = new ExpeditionAidUnlocks(new FlagManager());
            ExpeditionPlaythrough progress = new ExpeditionPlaythrough();
            Assert.IsTrue(progress.TryBeginNextPlaythrough(1));
            BossControllerLogic boss = CreateBoss("sentinel");
            ExpeditionAidUnlocks.BossRule[] ambiguous =
            {
                new ExpeditionAidUnlocks.BossRule("sentinel", "sentinel.aid", 2),
                new ExpeditionAidUnlocks.BossRule("sentinel", "rival.aid", 2),
            };
            using (unlocks.ObserveBossDefeats(boss, progress, ambiguous))
            {
                Defeat(boss);
                Assert.IsFalse(unlocks.IsUnlocked("sentinel.aid"));
                Assert.IsFalse(unlocks.IsUnlocked("rival.aid"));
            }

            IDisposable watcher = unlocks.ObserveBossDefeats(boss, progress,
                new[] { new ExpeditionAidUnlocks.BossRule("sentinel", "sentinel.aid", 2) });
            boss.StartEncounter();
            watcher.Dispose();
            boss.OnDefeated();
            Assert.IsFalse(unlocks.IsUnlocked("sentinel.aid"));
        }

        private static BossControllerLogic CreateBoss(string id)
            => new BossControllerLogic(new[] { new BossPhaseData { phaseName = "Encounter" } }, id);

        private static void Defeat(BossControllerLogic boss)
        {
            boss.StartEncounter();
            boss.OnDefeated();
        }

        private static ExpeditionSquad CreateSquad(ExpeditionAidUnlocks unlocks)
            => new ExpeditionSquad(unlocks, new[] { new ExpeditionSquad.Species("sentinel.aid", 2) }, 3, 4);
    }
}
