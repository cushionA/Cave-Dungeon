using Game.Core;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    public sealed class ExpeditionAidUnlockTests
    {
        private static readonly ExpeditionAidUnlocks.Completion[] s_Completions =
        {
            new ExpeditionAidUnlocks.Completion("quest.completed", "engineer"),
            new ExpeditionAidUnlocks.Completion("story.resolved", "sentinel")
        };

        [Test]
        public void Completion_OnlyConfiguredIdentityUnlocksItsSpeciesOnce()
        {
            ExpeditionAidUnlocks aid = new ExpeditionAidUnlocks(new FlagManager());

            Assert.IsFalse(aid.TryComplete("QUEST.COMPLETED", s_Completions));
            Assert.IsFalse(aid.TryComplete("quest.started", s_Completions));
            Assert.IsFalse(aid.TryComplete(null, s_Completions));
            Assert.IsFalse(aid.IsUnlocked("engineer"));
            Assert.IsTrue(aid.TryComplete("quest.completed", s_Completions));
            Assert.IsFalse(aid.TryComplete("quest.completed", s_Completions));
            Assert.IsTrue(aid.IsUnlocked("engineer"));
            Assert.IsFalse(aid.IsUnlocked("sentinel"));
            Assert.IsTrue(aid.TryComplete("story.resolved", s_Completions));
            Assert.IsTrue(aid.IsUnlocked("sentinel"));
        }

        [Test]
        public void Completion_AmbiguousAuthorMappingLeavesBothSpeciesLocked()
        {
            ExpeditionAidUnlocks aid = new ExpeditionAidUnlocks(new FlagManager());
            ExpeditionAidUnlocks.Completion[] ambiguous =
            {
                new ExpeditionAidUnlocks.Completion("quest.completed", "engineer"),
                new ExpeditionAidUnlocks.Completion("quest.completed", "sentinel")
            };

            Assert.IsFalse(aid.TryComplete("quest.completed", ambiguous));
            Assert.IsFalse(aid.IsUnlocked("engineer"));
            Assert.IsFalse(aid.IsUnlocked("sentinel"));
        }

        [Test]
        public void Completion_LoadingLegacySlotClearsPriorUnlock()
        {
            using GameManagerCore world = new GameManagerCore();
            world.Saves.SetSlotData(0, new SaveSlotData(0));
            Assert.IsTrue(world.ExpeditionAid.TryComplete("quest.completed", s_Completions));
            world.Saves.Save(1);

            Assert.IsTrue(world.Saves.Load(0));
            Assert.IsFalse(world.ExpeditionAid.IsUnlocked("engineer"));
            Assert.IsTrue(world.Saves.Load(1));
            Assert.IsTrue(world.ExpeditionAid.IsUnlocked("engineer"));
        }
    }
}
