using Game.Core;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    public class ExpeditionSquadSaveTests
    {
        [Test]
        public void ExpeditionSquad_LoadOverCurrentLimits_ClearsInsteadOfRestoringPartialSelection()
        {
            ExpeditionAidUnlocks unlocks = new ExpeditionAidUnlocks(new FlagManager());
            Assert.That(unlocks.TryComplete("scout.helped", new[]
            {
                new ExpeditionAidUnlocks.Completion("scout.helped", "scout")
            }), Is.True);
            ExpeditionSquad.Species[] species = { new ExpeditionSquad.Species("scout", 1) };
            ExpeditionSquad previous = new ExpeditionSquad(unlocks, species, 3, 3);
            ExpeditionSquad current = new ExpeditionSquad(unlocks, species, 1, 1);
            Assert.That(previous.TryConfirm(new[] { new ExpeditionSquad.Selection("scout", 2) }), Is.True);
            Assert.That(current.TryConfirm(new[] { new ExpeditionSquad.Selection("scout", 1) }), Is.True);

            SaveManager saves = new SaveManager();
            saves.Register(previous);
            saves.Save(0);
            saves.Unregister(previous);
            saves.Register(current);

            Assert.That(saves.Load(0), Is.True);
            Assert.That(current.Confirmed, Is.Empty);
            Assert.That(current.ConfirmedCount, Is.Zero);
            Assert.That(current.ConfirmedCost, Is.Zero);
        }
    }
}
