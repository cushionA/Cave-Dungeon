using System;
using System.Linq;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    public class ExpeditionSquadTests
    {
        private static readonly ExpeditionAidUnlocks.Completion[] s_Completions =
        {
            new ExpeditionAidUnlocks.Completion("scout.helped", "scout"),
            new ExpeditionAidUnlocks.Completion("guard.helped", "guard"),
            new ExpeditionAidUnlocks.Completion("mage.helped", "mage"),
            new ExpeditionAidUnlocks.Completion("other.helped", "absent-from-catalog")
        };

        [Test]
        public void ExpeditionSquad_AtBothLimits_ConfirmsMultipleOfOneSpecies()
        {
            ExpeditionSquad squad = CreateSquad(3, 5, out _);
            ExpeditionSquad.Selection[] selection =
            {
                new ExpeditionSquad.Selection("scout", 2),
                new ExpeditionSquad.Selection("guard", 1)
            };

            Assert.That(squad.TryMeasure(selection, out int count, out int cost), Is.True);
            Assert.That(count, Is.EqualTo(3));
            Assert.That(cost, Is.EqualTo(5));
            Assert.That(squad.Confirmed, Is.Empty);
            Assert.That(squad.TryConfirm(selection), Is.True);

            selection[0] = new ExpeditionSquad.Selection("scout", 1);
            Assert.That(squad.Confirmed[0].Count, Is.EqualTo(2));
            Assert.That(squad.Confirmed[1].SpeciesId, Is.EqualTo("guard"));
            Assert.That(squad.ConfirmedCount, Is.EqualTo(3));
            Assert.That(squad.ConfirmedCost, Is.EqualTo(5));
        }

        [Test]
        public void ExpeditionSquad_OverCostLimit_RetainsLastConfirmedSelection()
        {
            ExpeditionSquad squad = CreateSquad(5, 5, out _);
            Assert.That(squad.TryConfirm(new[] { new ExpeditionSquad.Selection("guard", 1) }), Is.True);

            ExpeditionSquad.Selection[] overBudget = { new ExpeditionSquad.Selection("guard", 2) };
            Assert.That(squad.TryMeasure(overBudget, out int count, out int cost), Is.True);
            Assert.That(count, Is.EqualTo(2));
            Assert.That(cost, Is.EqualTo(6));
            Assert.That(squad.TryConfirm(overBudget), Is.False);
            Assert.That(squad.ConfirmedCount, Is.EqualTo(1));
            Assert.That(squad.ConfirmedCost, Is.EqualTo(3));
        }

        [Test]
        public void ExpeditionSquad_OverHeadcountLimit_RetainsLastConfirmedSelection()
        {
            ExpeditionSquad squad = CreateSquad(2, 10, out _);
            Assert.That(squad.TryConfirm(new[] { new ExpeditionSquad.Selection("scout", 2) }), Is.True);

            Assert.That(squad.TryConfirm(new[] { new ExpeditionSquad.Selection("scout", 3) }), Is.False);
            Assert.That(squad.ConfirmedCount, Is.EqualTo(2));
            Assert.That(squad.ConfirmedCost, Is.EqualTo(2));
        }

        [Test]
        public void ExpeditionSquad_UnlockChanges_CatalogAndConfirmationSeeNewSpecies()
        {
            ExpeditionSquad squad = CreateSquad(5, 10, out ExpeditionAidUnlocks unlocks);
            CollectionAssert.AreEqual(new[] { "scout", "guard" }, squad.GetUnlockedSpecies().Select(species => species.SpeciesId));
            ExpeditionSquad.Selection[] selection = { new ExpeditionSquad.Selection("mage", 1) };

            Assert.That(squad.TryConfirm(selection), Is.False);
            Assert.That(unlocks.TryComplete("mage.helped", s_Completions), Is.True);
            Assert.That(unlocks.TryComplete("other.helped", s_Completions), Is.True);
            CollectionAssert.AreEqual(new[] { "scout", "guard", "mage" }, squad.GetUnlockedSpecies().Select(species => species.SpeciesId));
            Assert.That(squad.TryConfirm(selection), Is.True);
            Assert.That(squad.ConfirmedCost, Is.EqualTo(4));
            Assert.That(squad.TryConfirm(new[] { new ExpeditionSquad.Selection("absent-from-catalog", 1) }), Is.False);
        }

        [Test]
        public void ExpeditionSquad_InvalidOrOverflowingCounts_CannotReplaceConfirmedSelection()
        {
            ExpeditionSquad squad = CreateSquad(int.MaxValue, int.MaxValue, out _);
            Assert.That(squad.TryConfirm(new[] { new ExpeditionSquad.Selection("scout", 1) }), Is.True);

            Assert.That(squad.TryConfirm(new[] { new ExpeditionSquad.Selection("scout", 0) }), Is.False);
            Assert.That(squad.TryConfirm(new[] { new ExpeditionSquad.Selection("guard", int.MaxValue) }), Is.False);
            Assert.That(squad.TryConfirm(new[]
            {
                new ExpeditionSquad.Selection("scout", 1),
                new ExpeditionSquad.Selection("scout", 1)
            }), Is.False);
            Assert.That(squad.ConfirmedCount, Is.EqualTo(1));

            Assert.That(squad.TryConfirm(Array.Empty<ExpeditionSquad.Selection>()), Is.True);
            Assert.That(squad.Confirmed, Is.Empty);
            Assert.That(squad.ConfirmedCost, Is.Zero);
        }

        private static ExpeditionSquad CreateSquad(int maxCount, int maxCost, out ExpeditionAidUnlocks unlocks)
        {
            unlocks = new ExpeditionAidUnlocks(new FlagManager());
            unlocks.TryComplete("scout.helped", s_Completions);
            unlocks.TryComplete("guard.helped", s_Completions);
            ExpeditionSquad.Species[] species =
            {
                new ExpeditionSquad.Species("scout", 1),
                new ExpeditionSquad.Species("guard", 3),
                new ExpeditionSquad.Species("mage", 4)
            };
            return new ExpeditionSquad(unlocks, species, maxCount, maxCost);
        }
    }
}
