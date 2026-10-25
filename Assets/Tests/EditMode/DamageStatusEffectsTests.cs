using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class DamageStatusEffectsTests
    {
        [Test]
        public void StatusEffectManager_AllEffectIds_CanBeActiveTogether()
        {
            StatusEffectManager manager = new StatusEffectManager();
            for (int i = 1; i < (int)StatusEffectId.Count; i++)
            {
                Assert.IsTrue(manager.Apply(new StatusEffectInfo
                {
                    effect = (StatusEffectId)i,
                    duration = 4f,
                }));
            }

            Assert.AreEqual((int)StatusEffectId.Count - 1, manager.ActiveCount);
            Assert.IsTrue(manager.IsActive(StatusEffectId.Confusion));
        }

        [Test]
        public void StatusEffectManager_LargeTick_OnlyDamagesBeforeExpiration()
        {
            StatusEffectManager manager = new StatusEffectManager();
            manager.Apply(new StatusEffectInfo
            {
                effect = StatusEffectId.Poison,
                duration = 2.5f,
                tickInterval = 1f,
                tickDamage = 7f,
            });

            Assert.AreEqual(14, manager.Tick(20f));
            Assert.AreEqual(0, manager.ActiveCount);
            Assert.AreEqual(0, manager.Tick(20f));
        }

        [Test]
        public void StatusEffectManager_Reapply_RefreshesAndCapsStacks()
        {
            StatusEffectManager manager = new StatusEffectManager();
            StatusEffectInfo info = new StatusEffectInfo
            {
                effect = StatusEffectId.Poison,
                duration = 4f,
                tickInterval = 1f,
                tickDamage = 3f,
                maxStack = 2,
            };
            manager.Apply(info);
            manager.Tick(1f);
            manager.Apply(info);
            manager.Apply(info);

            Assert.IsTrue(manager.TryGetEffect(info.effect, out ActiveEffect active));
            Assert.AreEqual(2, active.stackCount);
            Assert.AreEqual(4f, active.remainTime);
            Assert.AreEqual(1, manager.ActiveCount);
            Assert.AreEqual(6, manager.Tick(1f));
        }

        [Test]
        public void StatusEffectManager_NonStackingReapply_RefreshesWithoutExtraStacks()
        {
            StatusEffectManager manager = new StatusEffectManager();
            StatusEffectInfo info = new StatusEffectInfo
            {
                effect = StatusEffectId.Slow,
                duration = 5f,
                modifier = 0.5f,
            };
            manager.Apply(info);
            manager.Tick(1f);
            manager.Apply(info);

            Assert.IsTrue(manager.TryGetEffect(info.effect, out ActiveEffect active));
            Assert.AreEqual(1, active.stackCount);
            Assert.AreEqual(5f, active.remainTime);
            Assert.AreEqual(0.5f, manager.MoveSpeedMultiplier);
        }

        [Test]
        public void StatusEffectManager_ResistanceOutsideRange_ClampsAccumulation()
        {
            StatusEffectManager manager = new StatusEffectManager();
            manager.Accumulate(StatusEffectId.Poison, 25f, 2f);
            Assert.AreEqual(0f, manager.GetAccumulation(StatusEffectId.Poison));
            manager.Accumulate(StatusEffectId.Poison, 25f, -2f);
            Assert.AreEqual(25f, manager.GetAccumulation(StatusEffectId.Poison));
        }

        [Test]
        public void StatusEffectManager_RefreshWithShorterInterval_DoesNotCreateRetroactiveTicks()
        {
            StatusEffectManager manager = new StatusEffectManager();
            StatusEffectInfo info = new StatusEffectInfo
            {
                effect = StatusEffectId.Poison,
                duration = 20f,
                tickInterval = 10f,
                tickDamage = 5f,
            };
            manager.Apply(info);
            Assert.AreEqual(0, manager.Tick(9f));
            info.tickInterval = 1f;
            manager.Apply(info);

            Assert.AreEqual(5, manager.Tick(0.2f));
        }

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
