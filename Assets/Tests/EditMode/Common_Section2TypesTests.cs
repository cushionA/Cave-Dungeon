using NUnit.Framework;
using UnityEngine;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class Common_Section2TypesTests
    {
        // --- Enum Definitions ---

        [Test]
        public void ActionExecType_AllValues_AreDefined()
        {
            Assert.AreEqual(0, (int)ActionExecType.Attack);
            Assert.AreEqual(1, (int)ActionExecType.Cast);
            Assert.AreEqual(2, (int)ActionExecType.Instant);
            Assert.AreEqual(3, (int)ActionExecType.Sustained);
            Assert.AreEqual(4, (int)ActionExecType.Broadcast);
        }

        [Test]
        public void BulletFeature_Flags_CombineCorrectly()
        {
            BulletFeature flags = BulletFeature.Pierce | BulletFeature.Explode | BulletFeature.Knockback;

            Assert.IsTrue(flags.HasFlag(BulletFeature.Pierce));
            Assert.IsTrue(flags.HasFlag(BulletFeature.Explode));
            Assert.IsTrue(flags.HasFlag(BulletFeature.Knockback));
            Assert.IsFalse(flags.HasFlag(BulletFeature.Gravity));
            Assert.IsFalse(flags.HasFlag(BulletFeature.Shield));
        }

        [Test]
        public void GateType_AllValues_AreDefined()
        {
            Assert.AreEqual(0, (int)GateType.Clear);
            Assert.AreEqual(1, (int)GateType.Ability);
            Assert.AreEqual(2, (int)GateType.Key);
        }

        [Test]
        public void MagicType_AllValues_AreDefined()
        {
            Assert.AreEqual(0, (int)MagicType.Attack);
            Assert.AreEqual(1, (int)MagicType.Recover);
            Assert.AreEqual(2, (int)MagicType.Support);
        }

        // --- Struct Definitions ---

        [Test]
        public void ActionSlot_Fields_StoreCorrectly()
        {
            ActionSlot slot = new ActionSlot
            {
                execType = ActionExecType.Cast,
                paramId = 10,
                paramValue = 1.5f,
                displayName = "Fireball"
            };

            Assert.AreEqual(ActionExecType.Cast, slot.execType);
            Assert.AreEqual(10, slot.paramId);
            Assert.AreEqual(1.5f, slot.paramValue, 0.001f);
            Assert.AreEqual("Fireball", slot.displayName);
        }

        [Test]
        public void AICondition_Fields_StoreCorrectly()
        {
            AICondition condition = new AICondition
            {
                conditionType = AIConditionType.HpRatio,
                compareOp = CompareOp.Less,
                operandA = 30,
                filter = default
            };

            Assert.AreEqual(AIConditionType.HpRatio, condition.conditionType);
            Assert.AreEqual(CompareOp.Less, condition.compareOp);
            Assert.AreEqual(30, condition.operandA);
        }

        [Test]
        public void TargetFilter_Fields_StoreCorrectly()
        {
            TargetFilter filter = new TargetFilter
            {
                belong = CharacterBelong.Enemy,
                distanceRange = new Vector2(0f, 10f)
            };

            Assert.AreEqual(CharacterBelong.Enemy, filter.belong);
            Assert.AreEqual(0f, filter.distanceRange.x, 0.001f);
            Assert.AreEqual(10f, filter.distanceRange.y, 0.001f);
        }

        // --- ComboWindowTimer ---

        [Test]
        public void ComboWindowTimer_Open_SetsIsOpenTrue()
        {
            ComboWindowTimer timer = new ComboWindowTimer();

            timer.Open(1.0f, 3);

            Assert.IsTrue(timer.IsOpen);
            Assert.AreEqual(0, timer.CurrentStep);
        }

        [Test]
        public void ComboWindowTimer_Tick_ClosesOnExpiry()
        {
            ComboWindowTimer timer = new ComboWindowTimer();
            timer.Open(0.5f, 3);

            timer.Tick(0.6f);

            Assert.IsFalse(timer.IsOpen);
        }

        [Test]
        public void ComboWindowTimer_TryAdvance_IncrementsStep()
        {
            ComboWindowTimer timer = new ComboWindowTimer();
            timer.Open(1.0f, 3);

            bool advanced = timer.TryAdvance();

            Assert.IsTrue(advanced);
            Assert.AreEqual(1, timer.CurrentStep);
        }

        [Test]
        public void ComboWindowTimer_TryAdvance_ClosesAtMaxSteps()
        {
            ComboWindowTimer timer = new ComboWindowTimer();
            timer.Open(1.0f, 2);

            timer.TryAdvance(); // step 1
            bool advanced = timer.TryAdvance(); // step 2 = max, should close

            Assert.IsFalse(advanced);
            Assert.IsFalse(timer.IsOpen);
        }

        [Test]
        public void ComboWindowTimer_TryAdvance_WhenClosed_ReturnsFalse()
        {
            ComboWindowTimer timer = new ComboWindowTimer();

            bool advanced = timer.TryAdvance();

            Assert.IsFalse(advanced);
        }

        [Test]
        public void ComboWindowTimer_Reset_ClearsState()
        {
            ComboWindowTimer timer = new ComboWindowTimer();
            timer.Open(1.0f, 3);
            timer.TryAdvance();

            timer.Reset();

            Assert.IsFalse(timer.IsOpen);
            Assert.AreEqual(0, timer.CurrentStep);
        }

        // --- CooldownTracker ---

        [Test]
        public void CooldownTracker_IsReady_ReturnsTrueWhenNoCooldown()
        {
            CooldownTracker tracker = new CooldownTracker();

            Assert.IsTrue(tracker.IsReady(1, 0f));
        }

        [Test]
        public void CooldownTracker_Start_MakesNotReadyUntilExpiry()
        {
            CooldownTracker tracker = new CooldownTracker();

            tracker.Start(1, 5.0f, 0f);

            Assert.IsFalse(tracker.IsReady(1, 3.0f));
            Assert.IsTrue(tracker.IsReady(1, 5.0f));
            Assert.AreEqual(2.0f, tracker.GetRemaining(1, 3.0f), 0.001f);
        }

        [Test]
        public void CooldownTracker_GetRemaining_ReturnsZeroWhenExpired()
        {
            CooldownTracker tracker = new CooldownTracker();
            tracker.Start(1, 2.0f, 0f);

            float remaining = tracker.GetRemaining(1, 5.0f);

            Assert.AreEqual(0f, remaining, 0.001f);
        }

        [Test]
        public void CooldownTracker_GetRemaining_ReturnsZeroWhenNoEntry()
        {
            CooldownTracker tracker = new CooldownTracker();

            float remaining = tracker.GetRemaining(99, 0f);

            Assert.AreEqual(0f, remaining, 0.001f);
        }

        [Test]
        public void CooldownTracker_Clear_RemovesAllCooldowns()
        {
            CooldownTracker tracker = new CooldownTracker();
            tracker.Start(1, 10.0f, 0f);
            tracker.Start(2, 10.0f, 0f);

            tracker.Clear();

            Assert.IsTrue(tracker.IsReady(1, 0f));
            Assert.IsTrue(tracker.IsReady(2, 0f));
        }

        // --- ActionInterruptHandler ---

        [Test]
        public void ActionInterruptHandler_SaveRestore_RoundTrips()
        {
            ActionInterruptHandler handler = new ActionInterruptHandler();
            ActionSlot slot = new ActionSlot
            {
                execType = ActionExecType.Attack,
                paramId = 42,
                displayName = "TestAction"
            };

            handler.Save(slot, 999);
            Assert.IsTrue(handler.HasSavedState);

            (ActionSlot restored, int hash)? result = handler.Restore();

            Assert.IsNotNull(result);
            Assert.AreEqual(ActionExecType.Attack, result.Value.restored.execType);
            Assert.AreEqual(42, result.Value.restored.paramId);
            Assert.AreEqual(999, result.Value.hash);
            Assert.IsFalse(handler.HasSavedState);
        }

        [Test]
        public void ActionInterruptHandler_Restore_WhenNoSave_ReturnsNull()
        {
            ActionInterruptHandler handler = new ActionInterruptHandler();

            (ActionSlot slot, int hash)? result = handler.Restore();

            Assert.IsNull(result);
        }

        [Test]
        public void ActionInterruptHandler_Clear_RemovesSavedState()
        {
            ActionInterruptHandler handler = new ActionInterruptHandler();
            ActionSlot slot = new ActionSlot { execType = ActionExecType.Instant };
            handler.Save(slot, 100);

            handler.Clear();

            Assert.IsFalse(handler.HasSavedState);
            Assert.IsNull(handler.Restore());
        }
    }
}
