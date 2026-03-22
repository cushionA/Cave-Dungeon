using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    [TestFixture]
    public class WakeUpLogicTests
    {
        // --- Knockbacked+着地→WakeUp遷移 ---

        [Test]
        public void WakeUpLogic_KnockbackedAndGrounded_TransitionsToWakeUp()
        {
            ActState current = ActState.Knockbacked;
            bool isGrounded = true;

            WakeUpLogic.Result result = WakeUpLogic.Evaluate(current, isGrounded, 0f, 0.5f);

            Assert.AreEqual(ActState.WakeUp, result.newState);
            Assert.IsTrue(result.isInvincible, "起き上がり中は無敵");
        }

        [Test]
        public void WakeUpLogic_KnockbackedAndNotGrounded_StaysKnockbacked()
        {
            ActState current = ActState.Knockbacked;
            bool isGrounded = false;

            WakeUpLogic.Result result = WakeUpLogic.Evaluate(current, isGrounded, 0f, 0.5f);

            Assert.AreEqual(ActState.Knockbacked, result.newState);
            Assert.IsFalse(result.isInvincible);
        }

        // --- WakeUp中ダメージ0（無敵） ---

        [Test]
        public void WakeUpLogic_DuringWakeUp_IsInvincible()
        {
            ActState current = ActState.WakeUp;
            float elapsed = 0.3f;
            float duration = 0.5f;

            WakeUpLogic.Result result = WakeUpLogic.Evaluate(current, true, elapsed, duration);

            Assert.AreEqual(ActState.WakeUp, result.newState);
            Assert.IsTrue(result.isInvincible, "起き上がり持続中は無敵");
        }

        // --- 持続時間経過後Neutral遷移 ---

        [Test]
        public void WakeUpLogic_AfterDuration_TransitionsToNeutral()
        {
            ActState current = ActState.WakeUp;
            float elapsed = 0.6f;
            float duration = 0.5f;

            WakeUpLogic.Result result = WakeUpLogic.Evaluate(current, true, elapsed, duration);

            Assert.AreEqual(ActState.Neutral, result.newState);
            Assert.IsFalse(result.isInvincible, "遷移後は無敵解除");
        }

        [Test]
        public void WakeUpLogic_ExactDuration_TransitionsToNeutral()
        {
            ActState current = ActState.WakeUp;
            float elapsed = 0.5f;
            float duration = 0.5f;

            WakeUpLogic.Result result = WakeUpLogic.Evaluate(current, true, elapsed, duration);

            Assert.AreEqual(ActState.Neutral, result.newState);
            Assert.IsFalse(result.isInvincible);
        }

        // --- 非関連状態はパススルー ---

        [Test]
        public void WakeUpLogic_NeutralState_NoChange()
        {
            ActState current = ActState.Neutral;

            WakeUpLogic.Result result = WakeUpLogic.Evaluate(current, true, 0f, 0.5f);

            Assert.AreEqual(ActState.Neutral, result.newState);
            Assert.IsFalse(result.isInvincible);
        }

        [Test]
        public void WakeUpLogic_AttackingState_NoChange()
        {
            ActState current = ActState.Attacking;

            WakeUpLogic.Result result = WakeUpLogic.Evaluate(current, true, 0f, 0.5f);

            Assert.AreEqual(ActState.Attacking, result.newState);
            Assert.IsFalse(result.isInvincible);
        }

        // --- WakeUp状態チェックヘルパー ---

        [Test]
        public void WakeUpLogic_IsWakeUpState_ReturnsCorrectly()
        {
            Assert.IsTrue(WakeUpLogic.IsWakeUpState(ActState.WakeUp));
            Assert.IsFalse(WakeUpLogic.IsWakeUpState(ActState.Neutral));
            Assert.IsFalse(WakeUpLogic.IsWakeUpState(ActState.Knockbacked));
        }
    }
}
