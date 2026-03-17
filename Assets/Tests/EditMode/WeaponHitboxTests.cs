using NUnit.Framework;
using UnityEngine;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class WeaponHitboxTests
    {
        [Test]
        public void HitboxLogic_RectContainsPoint_DetectsCorrectly()
        {
            // Arrange
            Rect hitbox = new Rect(0f, 0f, 10f, 10f);
            Vector2 insidePoint = new Vector2(5f, 5f);
            Vector2 outsidePoint = new Vector2(15f, 15f);
            Vector2 edgePoint = new Vector2(0f, 0f);
            Vector2 cornerPoint = new Vector2(10f, 10f);

            // Act & Assert — inside
            Assert.IsTrue(HitboxLogic.RectContainsPoint(hitbox, insidePoint),
                "Point inside hitbox should return true");

            // Act & Assert — outside
            Assert.IsFalse(HitboxLogic.RectContainsPoint(hitbox, outsidePoint),
                "Point outside hitbox should return false");

            // Act & Assert — edge (min boundary inclusive)
            Assert.IsTrue(HitboxLogic.RectContainsPoint(hitbox, edgePoint),
                "Point on min edge should return true");

            // Act & Assert — max boundary (Rect.Contains treats max edge as exclusive)
            Assert.IsFalse(HitboxLogic.RectContainsPoint(hitbox, cornerPoint),
                "Point on max edge should return false (Rect exclusive upper bound)");
        }

        [Test]
        public void HitboxLogic_TryRegisterHit_RespectsMaxAndDuplicates()
        {
            // Arrange — maxHitCount = 2
            HitboxLogic hitbox = new HitboxLogic(2);
            int targetA = 100;
            int targetB = 200;
            int targetC = 300;

            // Act & Assert — first hit registers
            Assert.IsTrue(hitbox.TryRegisterHit(targetA),
                "First hit on targetA should register");
            Assert.AreEqual(1, hitbox.HitCount);

            // Act & Assert — duplicate hit rejected
            Assert.IsFalse(hitbox.TryRegisterHit(targetA),
                "Duplicate hit on same target should be rejected");
            Assert.AreEqual(1, hitbox.HitCount);

            // Act & Assert — second unique hit registers
            Assert.IsTrue(hitbox.TryRegisterHit(targetB),
                "Second unique target should register");
            Assert.AreEqual(2, hitbox.HitCount);
            Assert.IsTrue(hitbox.IsExhausted, "Should be exhausted at maxHitCount");

            // Act & Assert — third hit rejected (max reached)
            Assert.IsFalse(hitbox.TryRegisterHit(targetC),
                "Hit beyond max should be rejected");
            Assert.AreEqual(2, hitbox.HitCount);
        }

        [Test]
        public void HitboxLogic_Reset_ClearsState()
        {
            // Arrange
            HitboxLogic hitbox = new HitboxLogic(1);
            hitbox.TryRegisterHit(100);
            Assert.IsTrue(hitbox.IsExhausted);

            // Act — reset with new max
            hitbox.Reset(3);

            // Assert
            Assert.AreEqual(0, hitbox.HitCount);
            Assert.IsFalse(hitbox.IsExhausted);
            Assert.IsTrue(hitbox.TryRegisterHit(100),
                "After reset, previously hit target should be registerable again");
        }

        [Test]
        public void AttackMovementLogic_CalculateAttackMoveOffset_LinearInterpolation()
        {
            // Arrange
            float totalDistance = 4.0f;
            float duration = 2.0f;

            // Act & Assert — elapsed = 0 → offset = 0
            float offsetAtStart = AttackMovementLogic.CalculateAttackMoveOffset(0f, duration, totalDistance);
            Assert.AreEqual(0f, offsetAtStart, 0.001f,
                "At elapsed=0, offset should be 0");

            // Act & Assert — elapsed = duration/2 → offset = distance/2
            float offsetAtHalf = AttackMovementLogic.CalculateAttackMoveOffset(1.0f, duration, totalDistance);
            Assert.AreEqual(2.0f, offsetAtHalf, 0.001f,
                "At half duration, offset should be half distance");

            // Act & Assert — elapsed = duration → offset = distance
            float offsetAtEnd = AttackMovementLogic.CalculateAttackMoveOffset(2.0f, duration, totalDistance);
            Assert.AreEqual(4.0f, offsetAtEnd, 0.001f,
                "At full duration, offset should be full distance");

            // Act & Assert — elapsed > duration → clamped to distance
            float offsetBeyond = AttackMovementLogic.CalculateAttackMoveOffset(3.0f, duration, totalDistance);
            Assert.AreEqual(4.0f, offsetBeyond, 0.001f,
                "Beyond duration, offset should clamp to total distance");

            // Act & Assert — elapsed < 0 → clamped to 0
            float offsetNegative = AttackMovementLogic.CalculateAttackMoveOffset(-1.0f, duration, totalDistance);
            Assert.AreEqual(0f, offsetNegative, 0.001f,
                "Negative elapsed should clamp to 0");
        }

        [Test]
        public void AttackMovementLogic_CalculateAttackMoveOffset_ZeroDuration_ReturnsFullDistance()
        {
            // Arrange — duration=0 is edge case (instant move)
            float totalDistance = 5.0f;
            float duration = 0f;

            // Act
            float offset = AttackMovementLogic.CalculateAttackMoveOffset(0f, duration, totalDistance);

            // Assert — instant move should return full distance
            Assert.AreEqual(totalDistance, offset, 0.001f,
                "Zero duration should return full distance immediately");
        }
    }
}
