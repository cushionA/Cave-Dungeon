using System;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    public class InputSamplingClockTests
    {
        [Test]
        public void InputClock_UpdateAndFixedAtSameTime_AdvancesChargeOnlyOnce()
        {
            InputSamplingClock clock = new InputSamplingClock();
            clock.Reset(1f);
            ChargeInputHandler charge = new ChargeInputHandler(new ChargeAttackLogic());
            charge.BeginHold(0);

            charge.UpdateHold(clock.Advance(1.3f));
            charge.UpdateHold(clock.Advance(1.3f));
            charge.EndHold(0);

            Assert.IsFalse(charge.IsCharging);
            Assert.AreEqual(1f, charge.ChargeMultiplier);
        }

        [Test]
        public void InputClock_OlderFixedAfterUpdate_DoesNotRewindOrAddTimeTwice()
        {
            InputSamplingClock clock = new InputSamplingClock();
            clock.Reset(1f);

            Assert.AreEqual(0.05f, clock.Advance(1.05f), 0.00001f);
            Assert.AreEqual(0f, clock.Advance(1.02f));
            Assert.AreEqual(0f, clock.Advance(1.04f));
            Assert.AreEqual(0.03f, clock.Advance(1.08f), 0.00001f);
        }

        [Test]
        public void InputClock_MultipleFixedSamplesWithinFrame_PreserveChargeDurationAndMultiplier()
        {
            InputSamplingClock clock = new InputSamplingClock();
            clock.Reset(1f);
            ChargeAttackLogic logic = new ChargeAttackLogic();
            ChargeInputHandler charge = new ChargeInputHandler(logic);
            charge.BeginHold(0);

            charge.UpdateHold(clock.Advance(1.2f));
            charge.UpdateHold(clock.Advance(1.4f));
            charge.UpdateHold(clock.Advance(1.6f));
            charge.UpdateHold(clock.Advance(1.6f));

            Assert.AreEqual(0.6f, logic.ChargeTime, 0.00001f);
            charge.EndHold(0);
            Assert.AreEqual(ChargeAttackLogic.k_ChargeLevel1Multiplier, charge.ChargeMultiplier);
        }

        [Test]
        public void InputClock_ResetAfterPoolReuse_DiscardsPreviousLifetime()
        {
            InputSamplingClock clock = new InputSamplingClock();
            clock.Reset(1f);
            clock.Advance(10f);
            clock.Reset(50f);

            Assert.AreEqual(0f, clock.Advance(50f));
            Assert.AreEqual(0.1f, clock.Advance(50.1f), 0.00001f);
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void InputClock_InvalidTime_DoesNotPoisonNextValidSample(float invalid)
        {
            InputSamplingClock clock = new InputSamplingClock();
            clock.Reset(1f);

            Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(invalid));
            Assert.AreEqual(0.25f, clock.Advance(1.25f));
        }
    }
}
