using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class CoopAction_CooldownTrackerTests
    {
        [Test]
        public void CoopCooldown_NoCooldown_IsFree()
        {
            CoopCooldownTracker tracker = new CoopCooldownTracker();

            CoopCooldownTracker.ActivationResult result = tracker.TryActivate(0f, 10, 50, 15f);

            Assert.IsTrue(result.success);
            Assert.IsTrue(result.isFree);
            Assert.AreEqual(0, result.mpConsumed);
        }

        [Test]
        public void CoopCooldown_DuringCooldown_CostsMp()
        {
            CoopCooldownTracker tracker = new CoopCooldownTracker();
            tracker.TryActivate(0f, 10, 50, 15f);

            CoopCooldownTracker.ActivationResult result = tracker.TryActivate(5f, 10, 50, 15f);

            Assert.IsTrue(result.success);
            Assert.IsFalse(result.isFree);
            Assert.AreEqual(10, result.mpConsumed);
        }

        [Test]
        public void CoopCooldown_DuringCooldown_TimerUnchanged()
        {
            CoopCooldownTracker tracker = new CoopCooldownTracker();
            tracker.TryActivate(0f, 10, 50, 15f);
            tracker.TryActivate(5f, 10, 50, 15f);

            Assert.AreEqual(10f, tracker.GetRemaining(5f), 0.01f);
        }

        [Test]
        public void CoopCooldown_AfterExpiry_FreeAgain()
        {
            CoopCooldownTracker tracker = new CoopCooldownTracker();
            tracker.TryActivate(0f, 10, 50, 15f);

            CoopCooldownTracker.ActivationResult result = tracker.TryActivate(16f, 10, 50, 15f);

            Assert.IsTrue(result.success);
            Assert.IsTrue(result.isFree);
        }

        [Test]
        public void CoopCooldown_InsufficientMp_Fails()
        {
            CoopCooldownTracker tracker = new CoopCooldownTracker();
            tracker.TryActivate(0f, 10, 50, 15f);

            CoopCooldownTracker.ActivationResult result = tracker.TryActivate(5f, 10, 5, 15f);

            Assert.IsFalse(result.success);
        }
    }
}
