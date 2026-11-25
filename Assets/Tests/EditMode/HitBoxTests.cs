using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    [TestFixture]
    public class HitBoxTests
    {
        // --- maxHitCount制御 ---

        [Test]
        public void HitboxLogic_MaxHitCount1_StopsAfterOneHit()
        {
            HitboxLogic logic = new HitboxLogic(1);

            bool first = logic.TryRegisterHit(100);
            bool second = logic.TryRegisterHit(200);

            Assert.IsTrue(first, "最初のヒットは成功するべき");
            Assert.IsFalse(second, "maxHitCount=1で2ヒット目は失敗するべき");
            Assert.IsTrue(logic.IsExhausted);
        }

        [Test]
        public void HitboxLogic_MaxHitCount3_AllowsThreeHits()
        {
            HitboxLogic logic = new HitboxLogic(3);

            Assert.IsTrue(logic.TryRegisterHit(100));
            Assert.IsTrue(logic.TryRegisterHit(200));
            Assert.IsTrue(logic.TryRegisterHit(300));
            Assert.IsFalse(logic.TryRegisterHit(400));
            Assert.AreEqual(3, logic.HitCount);
        }

        // --- 同一ターゲット重複防止 ---

        [Test]
        public void HitboxLogic_SameTarget_BlocksDuplicate()
        {
            HitboxLogic logic = new HitboxLogic(5);

            bool first = logic.TryRegisterHit(100);
            bool duplicate = logic.TryRegisterHit(100);

            Assert.IsTrue(first);
            Assert.IsFalse(duplicate, "同一ターゲットは重複ヒットしない");
            Assert.AreEqual(1, logic.HitCount);
        }

        // --- Reset後の再攻撃 ---

        [Test]
        public void HitboxLogic_AfterReset_AllowsReHit()
        {
            HitboxLogic logic = new HitboxLogic(1);

            Assert.IsTrue(logic.TryRegisterHit(100));
            Assert.IsTrue(logic.IsExhausted);

            logic.Reset(2);

            Assert.IsFalse(logic.IsExhausted);
            Assert.AreEqual(0, logic.HitCount);
            Assert.IsTrue(logic.TryRegisterHit(100), "Reset後は同一ターゲットに再ヒット可能");
            Assert.IsTrue(logic.TryRegisterHit(200));
            Assert.IsFalse(logic.TryRegisterHit(300), "新maxHitCount=2で3ヒット目は失敗");
        }

        // --- 0ヒット上限 ---

        [Test]
        public void HitboxLogic_ZeroMaxHitCount_AlwaysExhausted()
        {
            HitboxLogic logic = new HitboxLogic(0);

            Assert.IsTrue(logic.IsExhausted);
            Assert.IsFalse(logic.TryRegisterHit(100));
        }

        // --- HitCount追跡 ---

        [Test]
        public void HitboxLogic_HitCount_TracksCorrectly()
        {
            HitboxLogic logic = new HitboxLogic(10);

            logic.TryRegisterHit(1);
            logic.TryRegisterHit(2);
            logic.TryRegisterHit(3);

            Assert.AreEqual(3, logic.HitCount);
        }

        [Test]
        public void HitboxLogic_ReusedTargetHash_CountsEachGenerationOnce()
        {
            HitboxLogic logic = new HitboxLogic(3);
            ActorRef first = new ActorRef(10, 1);
            ActorRef replacement = new ActorRef(10, 2);

            Assert.IsTrue(logic.TryRegisterHit(first));
            Assert.IsFalse(logic.TryRegisterHit(first));
            Assert.IsTrue(logic.TryRegisterHit(replacement));
            Assert.IsFalse(logic.TryRegisterHit(replacement));
            Assert.AreEqual(2, logic.HitCount);

            logic.Reset(1);
            Assert.IsTrue(logic.TryRegisterHit(replacement));
            Assert.IsTrue(logic.IsExhausted);
        }

        [Test]
        public void AttackHitTracker_WithTwoTargetSlots_ExhaustsAfterBothTargetsReachTheirOwnLimit()
        {
            HitRepeatSettings repeat = new HitRepeatSettings { maxHitsPerTarget = 2, interval = 0.25f };
            AttackHitTracker tracker = new AttackHitTracker(2, repeat);
            ActorRef first = new ActorRef(10, 1);
            ActorRef replacement = new ActorRef(10, 2);
            ActorRef other = new ActorRef(20, 1);

            Assert.IsTrue(tracker.CanRegisterHit(first, 1.0));
            Assert.AreEqual(0, tracker.HitCount);
            Assert.IsTrue(tracker.TryRegisterHit(first, 1.0));
            Assert.IsFalse(tracker.CanRegisterHit(first, 1.249));
            Assert.IsFalse(tracker.TryRegisterHit(first, 1.249));
            Assert.IsTrue(tracker.TryRegisterHit(replacement, 1.125));
            Assert.IsFalse(tracker.IsExhausted);
            Assert.IsFalse(tracker.CanRegisterHit(other, 1.5));
            Assert.IsFalse(tracker.TryRegisterHit(other, 1.5));
            Assert.IsTrue(tracker.TryRegisterHit(first, 1.25));
            Assert.IsFalse(tracker.TryRegisterHit(first, 1.5));
            Assert.IsFalse(tracker.IsExhausted);
            Assert.IsTrue(tracker.TryRegisterHit(replacement, 1.375));
            Assert.IsTrue(tracker.IsExhausted);
            Assert.AreEqual(4, tracker.HitCount);
        }

        [Test]
        public void AttackHitTracker_WithFloatFixedSteps_RecognizesTheAuthoredIntervalBoundary()
        {
            HitRepeatSettings repeat = new HitRepeatSettings { maxHitsPerTarget = 2, interval = 0.3f };
            AttackHitTracker tracker = new AttackHitTracker(1, repeat);
            ActorRef target = new ActorRef(10, 1);
            double step = 0.02f;

            Assert.IsTrue(tracker.TryRegisterHit(target, 4.0));
            Assert.IsFalse(tracker.TryRegisterHit(target, 4.0 + 14 * step));
            Assert.IsTrue(tracker.TryRegisterHit(target, 4.0 + 15 * step));
            Assert.IsTrue(tracker.IsExhausted);
        }

        [Test]
        public void AttackHitTracker_WhenTimeIsUnchangedOrRewinds_DoesNotCountAnotherContact()
        {
            HitRepeatSettings repeat = new HitRepeatSettings { maxHitsPerTarget = 3, interval = 1e-12f };
            AttackHitTracker tracker = new AttackHitTracker(1, repeat);
            ActorRef target = new ActorRef(10, 1);

            Assert.IsFalse(tracker.TryRegisterHit(target, double.NaN));
            Assert.IsTrue(tracker.TryRegisterHit(target, 1.0));
            Assert.IsFalse(tracker.TryRegisterHit(target, 1.0));
            Assert.IsFalse(tracker.TryRegisterHit(target, 0.999));
            Assert.IsFalse(tracker.TryRegisterHit(target, double.PositiveInfinity));
            Assert.IsTrue(tracker.TryRegisterHit(target, 1.001));
            Assert.AreEqual(2, tracker.HitCount);
        }

        [TestCase(2, 0f)]
        [TestCase(2, -0.25f)]
        [TestCase(2, float.NaN)]
        [TestCase(2, float.PositiveInfinity)]
        [TestCase(1, 0.25f)]
        public void AttackHitTracker_WhenRepeatSettingsAreDisabled_UsesOneHitPerTarget(int hitLimit, float interval)
        {
            HitRepeatSettings repeat = new HitRepeatSettings { maxHitsPerTarget = hitLimit, interval = interval };
            AttackHitTracker tracker = new AttackHitTracker(1, repeat);
            ActorRef target = new ActorRef(10, 1);

            Assert.IsFalse(repeat.IsEnabled);
            Assert.AreEqual(1, repeat.EffectiveMaxHitsPerTarget);
            Assert.IsTrue(tracker.TryRegisterHit(target));
            Assert.IsFalse(tracker.TryRegisterHit(target, 30.0));
            Assert.IsTrue(tracker.IsExhausted);
        }

        [Test]
        public void HitboxLogic_WhenReset_SwitchesBetweenLegacyAndRepeatPolicies()
        {
            HitRepeatSettings repeat = new HitRepeatSettings { maxHitsPerTarget = 2, interval = 0.25f };
            HitboxLogic logic = new HitboxLogic(-1);
            Assert.IsTrue(logic.IsExhausted);
            Assert.IsFalse(logic.TryRegisterHit(100));

            logic.Reset(1, repeat);
            Assert.IsTrue(logic.TryRegisterHit(100, 1.0));
            Assert.IsFalse(logic.IsExhausted);
            Assert.IsTrue(logic.TryRegisterHit(100, 1.25));
            Assert.IsTrue(logic.IsExhausted);

            logic.Reset(1);
            Assert.AreEqual(0, logic.HitCount);
            Assert.IsTrue(logic.TryRegisterHit(100));
            Assert.IsFalse(logic.TryRegisterHit(100, 1.25));
            Assert.IsTrue(logic.IsExhausted);
        }
    }
}
