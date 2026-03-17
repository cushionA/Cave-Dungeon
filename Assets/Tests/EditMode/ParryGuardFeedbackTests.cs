using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class ParryGuardFeedbackTests
    {
        // --- NoGuard ---

        [Test]
        public void GuardFeedbackLogic_CreateFeedback_NoGuard_NoEffect()
        {
            GuardFeedbackData feedback = GuardFeedbackLogic.CreateFeedback(GuardResult.NoGuard);

            Assert.AreEqual(0f, feedback.hitStopDuration);
            Assert.AreEqual(0f, feedback.cameraShakeIntensity);
            Assert.IsFalse(feedback.playGuardEffect);
            Assert.IsFalse(feedback.playBreakEffect);
            Assert.AreEqual(GuardResult.NoGuard, feedback.result);
        }

        // --- 通常ガード ---

        [Test]
        public void GuardFeedbackLogic_CreateFeedback_Guarded_NormalEffect()
        {
            GuardFeedbackData feedback = GuardFeedbackLogic.CreateFeedback(GuardResult.Guarded);

            Assert.AreEqual(GuardFeedbackLogic.k_NormalHitStop, feedback.hitStopDuration);
            Assert.AreEqual(GuardFeedbackLogic.k_NormalShake, feedback.cameraShakeIntensity);
            Assert.IsTrue(feedback.playGuardEffect);
            Assert.IsFalse(feedback.playBreakEffect);
            Assert.AreEqual(GuardResult.Guarded, feedback.result);
        }

        // --- ジャストガード ---

        [Test]
        public void GuardFeedbackLogic_CreateFeedback_JustGuard_StrongEffect()
        {
            GuardFeedbackData feedback = GuardFeedbackLogic.CreateFeedback(GuardResult.JustGuard);

            Assert.AreEqual(GuardFeedbackLogic.k_JustGuardHitStop, feedback.hitStopDuration);
            Assert.AreEqual(GuardFeedbackLogic.k_JustGuardShake, feedback.cameraShakeIntensity);
            Assert.IsTrue(feedback.playGuardEffect);
            Assert.IsFalse(feedback.playBreakEffect);
            Assert.AreEqual(GuardResult.JustGuard, feedback.result);
        }

        // --- ガードブレイク ---

        [Test]
        public void GuardFeedbackLogic_CreateFeedback_GuardBreak_BreakEffect()
        {
            GuardFeedbackData feedback = GuardFeedbackLogic.CreateFeedback(GuardResult.GuardBreak);

            Assert.AreEqual(GuardFeedbackLogic.k_GuardBreakHitStop, feedback.hitStopDuration);
            Assert.AreEqual(GuardFeedbackLogic.k_GuardBreakShake, feedback.cameraShakeIntensity);
            Assert.IsFalse(feedback.playGuardEffect);
            Assert.IsTrue(feedback.playBreakEffect);
            Assert.AreEqual(GuardResult.GuardBreak, feedback.result);
        }
    }
}
