using System;
using System.IO;
using System.Linq;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    public sealed class ExpeditionPlaythroughTests
    {
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
        public void Playthrough_DiskRestoreKeepsEligibleAidAndSquad_AndDefaultsOlderOrMalformedSlots()
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

                Assert.IsTrue(restored.Saves.Load(0));
                Assert.AreEqual(2, restored.Playthrough.CycleNumber);
                Assert.IsTrue(restored.ExpeditionAid.IsUnlocked("sentinel.aid"));
                Assert.AreEqual(2, restoredSquad.ConfirmedCount);
                Assert.AreEqual(4, restoredSquad.ConfirmedCost);
                Assert.IsTrue(restored.Saves.Load(1));
                Assert.AreEqual(1, restored.Playthrough.CycleNumber);
                Assert.IsFalse(restored.ExpeditionAid.IsUnlocked("sentinel.aid"));
                Assert.IsEmpty(restoredSquad.Confirmed);
                Assert.IsTrue(restored.Saves.Load(0));
                Assert.IsTrue(restored.Saves.Load(2));
                Assert.AreEqual(1, restored.Playthrough.CycleNumber);
            }
            finally
            {
                if (Directory.Exists(temporaryDirectory)) { Directory.Delete(temporaryDirectory, recursive: true); }
            }
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
