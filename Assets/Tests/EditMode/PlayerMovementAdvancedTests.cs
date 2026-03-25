using NUnit.Framework;
using UnityEngine;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class PlayerMovementAdvancedTests
    {
        // --- TryWallKick ---

        [Test]
        public void AdvancedMovementLogic_TryWallKick_WithoutFlag_ReturnsZero()
        {
            Vector2 result = AdvancedMovementLogic.TryWallKick(
                AbilityFlag.None, true, true, true);

            Assert.AreEqual(Vector2.zero, result);
        }

        [Test]
        public void AdvancedMovementLogic_TryWallKick_WithFlag_WallTouch_JumpPressed_ReturnsKickForce()
        {
            Vector2 result = AdvancedMovementLogic.TryWallKick(
                AbilityFlag.WallKick, true, true, true);

            Assert.AreEqual(AdvancedMovementLogic.k_WallKickForceX, result.x, 0.001f);
            Assert.AreEqual(AdvancedMovementLogic.k_WallKickForceY, result.y, 0.001f);
        }

        [Test]
        public void AdvancedMovementLogic_TryWallKick_FacingLeft_ReturnsNegativeX()
        {
            Vector2 result = AdvancedMovementLogic.TryWallKick(
                AbilityFlag.WallKick, true, true, false);

            Assert.AreEqual(-AdvancedMovementLogic.k_WallKickForceX, result.x, 0.001f);
            Assert.AreEqual(AdvancedMovementLogic.k_WallKickForceY, result.y, 0.001f);
        }

        [Test]
        public void AdvancedMovementLogic_TryWallKick_NotTouchingWall_ReturnsZero()
        {
            Vector2 result = AdvancedMovementLogic.TryWallKick(
                AbilityFlag.WallKick, false, true, true);

            Assert.AreEqual(Vector2.zero, result);
        }

        [Test]
        public void AdvancedMovementLogic_TryWallKick_JumpNotPressed_ReturnsZero()
        {
            Vector2 result = AdvancedMovementLogic.TryWallKick(
                AbilityFlag.WallKick, true, false, true);

            Assert.AreEqual(Vector2.zero, result);
        }

        // --- GetWallClingSlideSpeed ---

        [Test]
        public void AdvancedMovementLogic_GetWallClingSlideSpeed_WithFlag_ReturnsSlideSpeed()
        {
            float speed = AdvancedMovementLogic.GetWallClingSlideSpeed(
                AbilityFlag.WallCling, true);

            Assert.AreEqual(AdvancedMovementLogic.k_WallClingSlideSpeed, speed, 0.001f);
        }

        [Test]
        public void AdvancedMovementLogic_GetWallClingSlideSpeed_WithoutFlag_ReturnsZero()
        {
            float speed = AdvancedMovementLogic.GetWallClingSlideSpeed(
                AbilityFlag.None, true);

            Assert.AreEqual(0f, speed, 0.001f);
        }

        [Test]
        public void AdvancedMovementLogic_GetWallClingSlideSpeed_NotTouchingWall_ReturnsZero()
        {
            float speed = AdvancedMovementLogic.GetWallClingSlideSpeed(
                AbilityFlag.WallCling, false);

            Assert.AreEqual(0f, speed, 0.001f);
        }

        // --- CanStartDropAttack ---

        [Test]
        public void AdvancedMovementLogic_CanStartDropAttack_WhenConditionsMet_ReturnsTrue()
        {
            bool canDrop = AdvancedMovementLogic.CanStartDropAttack(
                false, AdvancedMovementLogic.k_DropAttackMinHeight + 1f, true);

            Assert.IsTrue(canDrop);
        }

        [Test]
        public void AdvancedMovementLogic_CanStartDropAttack_WhenGrounded_ReturnsFalse()
        {
            bool canDrop = AdvancedMovementLogic.CanStartDropAttack(
                true, AdvancedMovementLogic.k_DropAttackMinHeight + 1f, true);

            Assert.IsFalse(canDrop);
        }

        [Test]
        public void AdvancedMovementLogic_CanStartDropAttack_BelowMinHeight_ReturnsFalse()
        {
            bool canDrop = AdvancedMovementLogic.CanStartDropAttack(
                false, AdvancedMovementLogic.k_DropAttackMinHeight - 0.5f, true);

            Assert.IsFalse(canDrop);
        }

        [Test]
        public void AdvancedMovementLogic_CanStartDropAttack_NoDownInput_ReturnsFalse()
        {
            bool canDrop = AdvancedMovementLogic.CanStartDropAttack(
                false, AdvancedMovementLogic.k_DropAttackMinHeight + 1f, false);

            Assert.IsFalse(canDrop);
        }

        // --- CalculateWeightPenalty ---

        [Test]
        public void AdvancedMovementLogic_CalculateWeightPenalty_AboveThreshold_Penalizes()
        {
            // weightRatio 0.9 → t = (0.9-0.7)/0.3 = 2/3, penalty = 1.0 - 0.5*(2/3) ≈ 0.6667
            float penalty = AdvancedMovementLogic.CalculateWeightPenalty(0.9f);

            float expected = 1.0f + (0.5f - 1.0f) * ((0.9f - 0.7f) / 0.3f);
            Assert.AreEqual(expected, penalty, 0.001f);
        }

        [Test]
        public void AdvancedMovementLogic_CalculateWeightPenalty_BelowThreshold_ReturnsOne()
        {
            float penalty = AdvancedMovementLogic.CalculateWeightPenalty(0.5f);

            Assert.AreEqual(1.0f, penalty, 0.001f);
        }

        [Test]
        public void AdvancedMovementLogic_CalculateWeightPenalty_AtThreshold_ReturnsOne()
        {
            float penalty = AdvancedMovementLogic.CalculateWeightPenalty(
                AdvancedMovementLogic.k_WeightPenaltyThreshold);

            Assert.AreEqual(1.0f, penalty, 0.001f);
        }

        [Test]
        public void AdvancedMovementLogic_CalculateWeightPenalty_ExtremeWeight_ClampsToMinimum()
        {
            // weightRatio 1.5 → 1 - (1.5 - 0.7) = 0.2, but above 0.5 minimum
            // weightRatio 2.0 → 1 - (2.0 - 0.7) = -0.3 → clamped to 0.5
            float penalty = AdvancedMovementLogic.CalculateWeightPenalty(2.0f);

            Assert.AreEqual(0.5f, penalty, 0.001f);
        }
    }
}
