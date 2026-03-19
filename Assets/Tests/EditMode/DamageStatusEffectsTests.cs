using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class DamageStatusEffectsTests
    {
        [Test]
        public void StatusEffectManager_Accumulate_TriggersAtThreshold()
        {
            StatusEffectManager manager = new StatusEffectManager();

            bool firstResult = manager.Accumulate(StatusEffectId.Poison, 50f, 0f);
            Assert.IsFalse(firstResult, "50 accumulation should not trigger (threshold=100)");

            bool secondResult = manager.Accumulate(StatusEffectId.Poison, 60f, 0f);
            Assert.IsTrue(secondResult, "50+60=110 should exceed threshold and trigger");
            Assert.IsTrue(manager.IsActive(StatusEffectId.Poison), "Poison should be active after triggering");
        }

        [Test]
        public void StatusEffectManager_Accumulate_WithResistance_ReducesValue()
        {
            StatusEffectManager manager = new StatusEffectManager();

            // statusCut=0.5 means 50% reduction: 80 * (1 - 0.5) = 40
            bool result = manager.Accumulate(StatusEffectId.Burn, 80f, 0.5f);
            Assert.IsFalse(result, "80 * 0.5 = 40 accumulation should not reach threshold=100");
            Assert.IsFalse(manager.IsActive(StatusEffectId.Burn), "Burn should not be active");
        }

        [Test]
        public void StatusEffectManager_Tick_DealsDamageAtInterval()
        {
            StatusEffectManager manager = new StatusEffectManager();

            // Force trigger poison (accumulate past threshold)
            manager.Accumulate(StatusEffectId.Poison, 100f, 0f);
            Assert.IsTrue(manager.IsActive(StatusEffectId.Poison), "Poison should be active");

            // Tick exactly one interval (default tickInterval=2.0)
            int damage = manager.Tick(2.0f);
            // Legacy Accumulate overload uses tickDamage=5, tickInterval=2.0
            Assert.AreEqual(5, damage,
                "Should deal tick damage after one interval");
        }

        [Test]
        public void StatusEffectManager_Tick_ExpiresAfterDuration()
        {
            StatusEffectManager manager = new StatusEffectManager();

            // Force trigger poison
            manager.Accumulate(StatusEffectId.Poison, 100f, 0f);
            Assert.AreEqual(1, manager.ActiveCount, "Should have 1 active effect");

            // Tick the full duration (default duration=10.0)
            manager.Tick(10.0f);
            Assert.AreEqual(0, manager.ActiveCount, "Effect should expire after full duration");
            Assert.IsFalse(manager.IsActive(StatusEffectId.Poison), "Poison should no longer be active");
        }
    }
}
