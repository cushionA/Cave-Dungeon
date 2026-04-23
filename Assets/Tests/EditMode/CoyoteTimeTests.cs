using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// GroundMovementLogic の Coyote Time 機構を検証する。
    /// UpdateGroundedState で追跡する _timeSinceLeftGround と
    /// TryStartJump の許容判定 (isGrounded || IsInCoyoteWindow) を単体で検証。
    /// </summary>
    [TestFixture]
    public class CoyoteTimeTests
    {
        private GroundMovementLogic _logic;
        private MoveParams _moveParams;

        [SetUp]
        public void SetUp()
        {
            _logic = new GroundMovementLogic();
            _moveParams = new MoveParams
            {
                moveSpeed = 6f,
                jumpForce = 10f,
                jumpStaminaCost = 0f,
            };
        }

        [Test]
        public void CoyoteTime_WhenGrounded_JumpSucceeds()
        {
            _logic.UpdateGroundedState(isGrounded: true, deltaTime: 0.02f);
            float jumpForce = _logic.TryStartJump(true, isGrounded: true, _moveParams);
            Assert.Greater(jumpForce, 0f, "接地中はジャンプが成立する");
        }

        [Test]
        public void CoyoteTime_WhenJustLeftGround_JumpAllowedWithinWindow()
        {
            // 接地中 → 離地直後 (5ms 経過) で Coyote 窓内
            _logic.UpdateGroundedState(true, 0.02f);
            _logic.UpdateGroundedState(false, 0.005f);
            Assert.IsTrue(_logic.IsInCoyoteWindow, "離地直後は Coyote 窓内");

            float jumpForce = _logic.TryStartJump(true, isGrounded: false, _moveParams);
            Assert.Greater(jumpForce, 0f, "Coyote 窓内なら空中でもジャンプ成立");
        }

        [Test]
        public void CoyoteTime_WhenExceededWindow_JumpRejected()
        {
            _logic.UpdateGroundedState(true, 0.02f);
            // 100ms 経過 (k_CoyoteTimeSeconds = 0.1f) を確実に超える
            _logic.UpdateGroundedState(false, 0.11f);
            Assert.IsFalse(_logic.IsInCoyoteWindow, "窓を越えたら Coyote 不成立");

            float jumpForce = _logic.TryStartJump(true, isGrounded: false, _moveParams);
            Assert.AreEqual(0f, jumpForce, "窓を越えた空中ジャンプは不可");
        }

        [Test]
        public void CoyoteTime_WhenJumpTriggeredInWindow_CoyoteConsumedPreventsSecondJump()
        {
            _logic.UpdateGroundedState(true, 0.02f);
            _logic.UpdateGroundedState(false, 0.01f);

            float firstJump = _logic.TryStartJump(true, isGrounded: false, _moveParams);
            Assert.Greater(firstJump, 0f, "1 度目の Coyote ジャンプは成立");

            // 直後にもう 1 度ジャンプ入力しても窓は消費済み
            _logic.UpdateGroundedState(false, 0.01f);
            float secondJump = _logic.TryStartJump(true, isGrounded: false, _moveParams);
            Assert.AreEqual(0f, secondJump, "Coyote 窓は 1 ジャンプで消費、2 度目は不可");
        }

        [Test]
        public void CoyoteTime_NeverGroundedSinceCreation_AirJumpRejected()
        {
            // 空中スポーン等で一度も接地していない場合、
            // _timeSinceLeftGround = MaxValue で Coyote が誤発動しない
            Assert.IsFalse(_logic.IsInCoyoteWindow, "未接地状態は Coyote 窓外");
            float jumpForce = _logic.TryStartJump(true, isGrounded: false, _moveParams);
            Assert.AreEqual(0f, jumpForce, "接地未経験の空中ジャンプは不可");
        }

        [Test]
        public void CoyoteTime_AfterGrounded_WindowResetsForNextLeave()
        {
            // 1 度空中に出て窓を消化 → 再接地 → 再離地で窓が復活する
            _logic.UpdateGroundedState(true, 0.02f);
            _logic.UpdateGroundedState(false, 0.01f);
            _logic.TryStartJump(true, false, _moveParams); // 窓消費

            _logic.UpdateGroundedState(true, 0.02f); // 再接地 → 窓リセット
            _logic.UpdateGroundedState(false, 0.01f);

            float jumpForce = _logic.TryStartJump(true, isGrounded: false, _moveParams);
            Assert.Greater(jumpForce, 0f, "再接地後は Coyote 窓が復活する");
        }
    }
}
