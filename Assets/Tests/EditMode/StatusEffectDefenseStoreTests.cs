using System;
using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    public class StatusEffectDefenseStoreTests
    {
        private static StatusEffectInfo Effect(float amount = 100f, float duration = 5f)
            => new StatusEffectInfo
            {
                effect = StatusEffectId.Poison, accumulateValue = amount,
                duration = duration, tickInterval = 1f, tickDamage = 1f,
            };

        [Test]
        public void StatusDefense_DefaultValue_KeepsHundredPointThreshold()
        {
            StatusEffectStore store = new StatusEffectStore();
            StatusEffectDefense defense = default;
            Assert.IsFalse(store.Accumulate(20, Effect(60f), defense));
            Assert.IsTrue(store.Accumulate(20, Effect(40f), defense));
            Assert.IsTrue(store.IsActive(20, StatusEffectId.Poison));
        }

        [Test]
        public void StatusDefense_HigherThreshold_CombinesAccumulationAcrossSources()
        {
            StatusEffectStore store = new StatusEffectStore();
            StatusEffectDefense defense = new StatusEffectDefense { threshold = 150f };
            Assert.IsFalse(store.Accumulate(20, Effect(100f), defense, 1));
            Assert.IsFalse(store.IsActive(20, StatusEffectId.Poison));
            Assert.AreEqual(100f, store.GetAccumulation(20, StatusEffectId.Poison));
            Assert.IsTrue(store.Accumulate(20, Effect(50f), defense, 2));
            Assert.AreEqual(0f, store.GetAccumulation(20, StatusEffectId.Poison));
        }

        [Test]
        public void StatusDefense_LowerThreshold_TriggersAtConfiguredBoundary()
        {
            StatusEffectStore store = new StatusEffectStore();
            StatusEffectDefense defense = new StatusEffectDefense { threshold = 25f };
            Assert.IsFalse(store.Accumulate(20, Effect(24f), defense));
            Assert.IsTrue(store.Accumulate(20, Effect(1f), defense));
        }

        [Test]
        public void StatusDefense_Immunity_BlocksBuildupWithoutAddingRowsOrPublishing()
        {
            StatusEffectStore store = new StatusEffectStore();
            int changes = 0;
            store.Changed += (hash, id, kind) => changes++;
            StatusEffectDefense defense = new StatusEffectDefense { immune = true };

            Assert.IsFalse(store.Accumulate(20, Effect(), defense, 1));

            Assert.AreEqual(0, store.Count);
            Assert.AreEqual(0, store.ActiveCount);
            Assert.AreEqual(0, changes);
        }

        [Test]
        public void StatusDefense_ImmediateImmunity_BlocksApplyAndRefresh()
        {
            StatusEffectStore store = new StatusEffectStore();
            StatusEffectDefense immune = new StatusEffectDefense { immune = true };
            Assert.IsFalse(store.Apply(20, Effect(), immune));
            Assert.AreEqual(0, store.Count);
            Assert.IsTrue(store.Apply(20, Effect()));
            store.TickAll(1f, new List<StatusEffectTick>());

            Assert.IsFalse(store.Apply(20, Effect(duration: 20f), immune));
            Assert.AreEqual(4f, store.GetRemainingTime(20, StatusEffectId.Poison));
        }

        [Test]
        public void StatusDefense_ImmediateApply_IgnoresReductionAndThreshold()
        {
            StatusEffectStore store = new StatusEffectStore();
            StatusEffectDefense defense = new StatusEffectDefense { accumulationReduction = 1f, threshold = 999f };
            Assert.IsTrue(store.Apply(20, Effect(), defense));
            Assert.IsTrue(store.IsActive(20, StatusEffectId.Poison));
        }

        [Test]
        public void StatusDefense_CurrentPolicyOnlyAffectsNewApplication_PreservesPriorState()
        {
            StatusEffectStore store = new StatusEffectStore();
            Assert.IsFalse(store.Accumulate(20, Effect(60f), 0f));
            StatusEffectDefense immune = new StatusEffectDefense { immune = true };
            Assert.IsFalse(store.Accumulate(20, Effect(100f), immune));
            Assert.AreEqual(60f, store.GetAccumulation(20, StatusEffectId.Poison));

            StatusEffectDefense lowered = new StatusEffectDefense { threshold = 50f };
            Assert.IsFalse(store.IsActive(20, StatusEffectId.Poison));
            Assert.IsTrue(store.Accumulate(20, Effect(1f), lowered));
        }

        [Test]
        public void StatusDefense_GenerationOverloads_KeepOriginalSourceAndTarget()
        {
            StatusEffectStore store = new StatusEffectStore();
            ActorRef target = new ActorRef(20, 3);
            ActorRef source = new ActorRef(10, 8);
            StatusEffectDefense defense = new StatusEffectDefense { threshold = 50f };
            Assert.IsTrue(store.Accumulate(target, Effect(50f), defense, source));
            Assert.IsTrue(store.TryGetEffect(target.Hash, StatusEffectId.Poison, out ActiveEffect effect));
            Assert.AreEqual(target, effect.targetRef);
            Assert.AreEqual(source, effect.sourceRef);
        }

        [TestCase(float.NaN, 100f)]
        [TestCase(float.PositiveInfinity, 100f)]
        [TestCase(0f, float.NaN)]
        [TestCase(0f, float.PositiveInfinity)]
        public void StatusDefense_NonFiniteDefense_RejectsBeforeWriting(float reduction, float threshold)
        {
            StatusEffectStore store = new StatusEffectStore();
            StatusEffectDefense defense = new StatusEffectDefense
            {
                accumulationReduction = reduction, threshold = threshold,
            };
            Assert.Throws<ArgumentOutOfRangeException>(() => store.Accumulate(20, Effect(), defense));
            Assert.AreEqual(0, store.Count);
        }
    }
}
