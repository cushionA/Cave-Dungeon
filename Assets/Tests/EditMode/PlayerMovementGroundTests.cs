using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class PlayerMovementGroundTests
    {
        private GroundMovementLogic _logic;
        private MoveParams _defaultParams;

        [SetUp]
        public void SetUp()
        {
            _logic = new GroundMovementLogic();
            _defaultParams = new MoveParams
            {
                moveSpeed = 5f,
                jumpForce = 10f,
                dashSpeed = 12f,
                dashDuration = 0.3f,
                gravityScale = 1f,
                weightRatio = 1f
            };
        }

        // --- CalculateHorizontalSpeed ---

        [Test]
        public void GroundMovementLogic_CalculateHorizontalSpeed_AppliesMoveSpeed()
        {
            float speed = _logic.CalculateHorizontalSpeed(1f, _defaultParams);

            Assert.AreEqual(5f, speed, 0.001f);
        }

        [Test]
        public void GroundMovementLogic_CalculateHorizontalSpeed_NegativeInput_ReturnsNegativeSpeed()
        {
            float speed = _logic.CalculateHorizontalSpeed(-1f, _defaultParams);

            Assert.AreEqual(-5f, speed, 0.001f);
        }

        [Test]
        public void GroundMovementLogic_CalculateHorizontalSpeed_ZeroInput_ReturnsZero()
        {
            float speed = _logic.CalculateHorizontalSpeed(0f, _defaultParams);

            Assert.AreEqual(0f, speed, 0.001f);
        }

        [Test]
        public void GroundMovementLogic_CalculateHorizontalSpeed_WhileDashing_UsesDashSpeed()
        {
            float stamina = 100f;
            _logic.TryStartDash(true, ref stamina, _defaultParams);

            float speed = _logic.CalculateHorizontalSpeed(1f, _defaultParams);

            Assert.AreEqual(12f, speed, 0.001f);
        }

        // --- TryStartJump ---

        [Test]
        public void GroundMovementLogic_TryStartJump_WhenGrounded_ReturnsJumpForce()
        {
            float jumpVelocity = _logic.TryStartJump(true, true, _defaultParams);

            Assert.AreEqual(10f, jumpVelocity, 0.001f);
            Assert.IsTrue(_logic.IsJumping);
        }

        [Test]
        public void GroundMovementLogic_TryStartJump_WhenNotGrounded_ReturnsZero()
        {
            float jumpVelocity = _logic.TryStartJump(true, false, _defaultParams);

            Assert.AreEqual(0f, jumpVelocity, 0.001f);
            Assert.IsFalse(_logic.IsJumping);
        }

        [Test]
        public void GroundMovementLogic_TryStartJump_WhenNotPressed_ReturnsZero()
        {
            float jumpVelocity = _logic.TryStartJump(false, true, _defaultParams);

            Assert.AreEqual(0f, jumpVelocity, 0.001f);
            Assert.IsFalse(_logic.IsJumping);
        }

        // --- CheckGrounded ---

        [Test]
        public void GroundMovementLogic_CheckGrounded_WhenBelowThreshold_ReturnsTrue()
        {
            bool grounded = GroundMovementLogic.CheckGrounded(0f, 0f, 0.1f);

            Assert.IsTrue(grounded);
        }

        [Test]
        public void GroundMovementLogic_CheckGrounded_WhenAtThreshold_ReturnsTrue()
        {
            bool grounded = GroundMovementLogic.CheckGrounded(0.1f, 0f, 0.1f);

            Assert.IsTrue(grounded);
        }

        [Test]
        public void GroundMovementLogic_CheckGrounded_WhenAboveThreshold_ReturnsFalse()
        {
            bool grounded = GroundMovementLogic.CheckGrounded(0.5f, 0f, 0.1f);

            Assert.IsFalse(grounded);
        }

        // --- TryStartDash ---

        [Test]
        public void GroundMovementLogic_TryStartDash_ConsumesStamina()
        {
            float stamina = 100f;

            bool started = _logic.TryStartDash(true, ref stamina, _defaultParams);

            Assert.IsTrue(started);
            Assert.IsTrue(_logic.IsDashing);
            Assert.AreEqual(100f - GroundMovementLogic.k_DodgeStaminaCost, stamina, 0.001f);
        }

        [Test]
        public void GroundMovementLogic_TryStartDash_InsufficientStamina_DoesNotStart()
        {
            float stamina = 5f;

            bool started = _logic.TryStartDash(true, ref stamina, _defaultParams);

            Assert.IsFalse(started);
            Assert.IsFalse(_logic.IsDashing);
            Assert.AreEqual(5f, stamina, 0.001f);
        }

        [Test]
        public void GroundMovementLogic_TryStartDash_NotPressed_DoesNotStart()
        {
            float stamina = 100f;

            bool started = _logic.TryStartDash(false, ref stamina, _defaultParams);

            Assert.IsFalse(started);
            Assert.AreEqual(100f, stamina, 0.001f);
        }

        [Test]
        public void GroundMovementLogic_TryStartDash_AlreadyDashing_DoesNotStartAgain()
        {
            float stamina = 100f;
            _logic.TryStartDash(true, ref stamina, _defaultParams);
            float staminaAfterFirst = stamina;

            bool startedAgain = _logic.TryStartDash(true, ref stamina, _defaultParams);

            Assert.IsFalse(startedAgain);
            Assert.AreEqual(staminaAfterFirst, stamina, 0.001f);
        }

        // --- UpdateJumpHold ---

        [Test]
        public void GroundMovementLogic_UpdateJumpHold_WhileHeld_ReturnsOne()
        {
            _logic.TryStartJump(true, true, _defaultParams);

            float factor = _logic.UpdateJumpHold(true, 0.01f);

            Assert.AreEqual(1f, factor, 0.001f);
        }

        [Test]
        public void GroundMovementLogic_UpdateJumpHold_WhenReleased_ReturnsZero()
        {
            _logic.TryStartJump(true, true, _defaultParams);
            // Hold past minimum time
            _logic.UpdateJumpHold(true, GroundMovementLogic.k_MinJumpHoldTime + 0.01f);

            float factor = _logic.UpdateJumpHold(false, 0.016f);

            Assert.AreEqual(0f, factor, 0.001f);
            Assert.IsFalse(_logic.IsJumping);
        }

        [Test]
        public void GroundMovementLogic_UpdateJumpHold_ExceedsMaxTime_ReturnsZero()
        {
            _logic.TryStartJump(true, true, _defaultParams);

            // Advance past max hold time
            _logic.UpdateJumpHold(true, GroundMovementLogic.k_MaxJumpHoldTime + 0.1f);

            Assert.IsFalse(_logic.IsJumping);
        }

        // --- UpdateDash ---

        [Test]
        public void GroundMovementLogic_UpdateDash_BeforeDurationEnds_ReturnsTrue()
        {
            float stamina = 100f;
            _logic.TryStartDash(true, ref stamina, _defaultParams);

            bool stillDashing = _logic.UpdateDash(0.1f, _defaultParams);

            Assert.IsTrue(stillDashing);
            Assert.IsTrue(_logic.IsDashing);
        }

        [Test]
        public void GroundMovementLogic_UpdateDash_AfterDurationEnds_ReturnsFalse()
        {
            float stamina = 100f;
            _logic.TryStartDash(true, ref stamina, _defaultParams);

            bool stillDashing = _logic.UpdateDash(_defaultParams.dashDuration + 0.1f, _defaultParams);

            Assert.IsFalse(stillDashing);
            Assert.IsFalse(_logic.IsDashing);
        }
    }
}
