using System;
using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    public class StatusEffectStoreBoundaryTests
    {
        private static StatusEffectInfo Info()
            => new StatusEffectInfo
            {
                effect = StatusEffectId.Poison, duration = 1f, accumulateValue = 100f,
                tickDamage = 1f, tickInterval = 1f, modifier = 0.25f,
            };

        [TestCase("duration", float.NaN)]
        [TestCase("duration", float.PositiveInfinity)]
        [TestCase("duration", float.NegativeInfinity)]
        [TestCase("accumulation", float.NaN)]
        [TestCase("accumulation", float.PositiveInfinity)]
        [TestCase("accumulation", float.NegativeInfinity)]
        [TestCase("damage", float.NaN)]
        [TestCase("damage", float.PositiveInfinity)]
        [TestCase("damage", float.NegativeInfinity)]
        [TestCase("interval", float.NaN)]
        [TestCase("interval", float.PositiveInfinity)]
        [TestCase("interval", float.NegativeInfinity)]
        [TestCase("modifier", float.NaN)]
        [TestCase("modifier", float.PositiveInfinity)]
        [TestCase("modifier", float.NegativeInfinity)]
        public void StatusStore_NonFiniteInfo_RejectsBeforeRowsAndNotifications(string field, float value)
        {
            StatusEffectStore store = new StatusEffectStore();
            StatusEffectInfo info = Info();
            switch (field)
            {
                case "duration": info.duration = value; break;
                case "accumulation": info.accumulateValue = value; break;
                case "damage": info.tickDamage = value; break;
                case "interval": info.tickInterval = value; break;
                case "modifier": info.modifier = value; break;
                default: Assert.Fail("Unknown fixture field."); break;
            }
            int changes = 0;
            store.Changed += (hash, effect, kind) => changes++;

            Assert.IsFalse(store.Apply(20, info, 10));
            Assert.IsFalse(store.Accumulate(20, info, 0f, 10));
            Assert.IsFalse(store.Apply(new ActorRef(20, 2), info, default(StatusEffectDefense), new ActorRef(10, 1)));
            Assert.IsFalse(store.Accumulate(new ActorRef(20, 2), info, default(StatusEffectDefense), new ActorRef(10, 1)));

            Assert.AreEqual(0, store.Count);
            Assert.AreEqual(0, store.ActiveCount);
            Assert.AreEqual(0, changes);
            Assert.AreEqual(0f, store.GetAccumulation(20, info.effect));
        }

        [Test]
        public void StatusStore_FiniteNegativeValues_PreserveClampsAndIntervalFallback()
        {
            StatusEffectStore store = new StatusEffectStore();
            StatusEffectInfo info = Info();
            info.accumulateValue = -5f;
            info.tickDamage = -5f;
            info.tickInterval = -2f;
            info.modifier = -1f;

            Assert.IsFalse(store.Accumulate(20, info, 0f));
            Assert.AreEqual(0, store.Count);
            Assert.IsTrue(store.Apply(20, info));
            Assert.IsTrue(store.TryGetEffect(20, info.effect, out ActiveEffect effect));
            Assert.AreEqual(0, effect.tickDamage);
            Assert.AreEqual(1f, effect.tickInterval);
            Assert.AreEqual(0f, effect.modifier);
        }

        [Test]
        public void StatusStore_ZeroInterval_PreservesOneSecondFallback()
        {
            StatusEffectStore store = new StatusEffectStore();
            StatusEffectInfo info = Info();
            info.tickInterval = 0f;

            Assert.IsTrue(store.Apply(20, info));
            Assert.IsTrue(store.TryGetEffect(20, info.effect, out ActiveEffect effect));
            Assert.AreEqual(1f, effect.tickInterval);
        }

        [Test]
        public void StatusStore_TickAllReentry_RejectsBeforeClearingResultsAndKeepsOuterExpiryNotifications()
        {
            StatusEffectStore store = new StatusEffectStore();
            store.Apply(20, Info());
            store.Apply(30, Info());
            List<StatusEffectTick> outer = new List<StatusEffectTick>();
            List<StatusEffectTick> inner = new List<StatusEffectTick>
            {
                new StatusEffectTick { targetHash = 999 },
            };
            int expired = 0;
            bool rejected = false;
            store.Changed += (hash, effect, kind) =>
            {
                if (kind != StatusEffectChangeKind.Expired) { return; }
                expired++;
                if (expired != 1) { return; }
                try { store.TickAll(0.1f, inner); }
                catch (InvalidOperationException) { rejected = true; }
            };

            store.TickAll(1f, outer);

            Assert.AreEqual(2, expired);
            Assert.IsTrue(rejected);
            Assert.AreEqual(1, inner.Count);
            Assert.AreEqual(999, inner[0].targetHash);
            Assert.AreEqual(2, outer.Count);
            Assert.AreEqual(0, store.ActiveCount);
        }

        [Test]
        public void StatusStore_TickAllCallbackThrows_ReleasesReentryGuard()
        {
            StatusEffectStore store = new StatusEffectStore();
            store.Apply(20, Info());
            Action<int, StatusEffectId, StatusEffectChangeKind> fail = (hash, effect, kind) =>
            {
                if (kind == StatusEffectChangeKind.Expired) { throw new InvalidOperationException("consumer failure"); }
            };
            store.Changed += fail;
            List<StatusEffectTick> results = new List<StatusEffectTick>();

            Assert.Throws<InvalidOperationException>(() => store.TickAll(1f, results));
            store.Changed -= fail;
            Assert.IsTrue(store.Apply(30, Info()));
            Assert.DoesNotThrow(() => store.TickAll(1f, results));
            Assert.AreEqual(0, store.ActiveCount);
        }

        [Test]
        public void StatusStore_TickAllEarlyReturn_ReleasesReentryGuard()
        {
            StatusEffectStore store = new StatusEffectStore();
            List<StatusEffectTick> results = new List<StatusEffectTick>();
            store.TickAll(0f, results);
            Assert.DoesNotThrow(() => store.TickAll(0f, results));
        }
    }
}
