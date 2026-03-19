using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class CooldownReward_FeedbackTests
    {
        [Test]
        public void CooldownFeedback_CooldownReady_FiresEvent()
        {
            CoopCooldownTracker tracker = new CoopCooldownTracker();
            CooldownRewardFeedback feedback = new CooldownRewardFeedback();
            bool readyFired = false;
            feedback.OnCooldownReady += () => readyFired = true;

            tracker.TryActivate(0f, 10, 50, 5f);
            feedback.Update(tracker, 3f);
            Assert.IsFalse(readyFired);

            feedback.Update(tracker, 6f);
            Assert.IsTrue(readyFired);
        }

        [Test]
        public void CooldownFeedback_FreeActivation_FiresEvent()
        {
            CooldownRewardFeedback feedback = new CooldownRewardFeedback();
            bool freeFired = false;
            feedback.OnFreeActivation += () => freeFired = true;

            feedback.NotifyFreeActivation();

            Assert.IsTrue(freeFired);
        }

        [Test]
        public void CooldownFeedback_NoCooldown_NoEvent()
        {
            CoopCooldownTracker tracker = new CoopCooldownTracker();
            CooldownRewardFeedback feedback = new CooldownRewardFeedback();
            bool readyFired = false;
            feedback.OnCooldownReady += () => readyFired = true;

            feedback.Update(tracker, 0f);

            Assert.IsFalse(readyFired);
        }
    }
}
