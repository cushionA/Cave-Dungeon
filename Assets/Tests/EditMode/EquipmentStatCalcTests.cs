using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class EquipmentStatCalcTests
    {
        [Test]
        public void EquipmentStatCalculator_CalculateScaledAttack_AppliesScaling()
        {
            // Arrange
            int baseAttack = 100;
            float scalingFactor = 0.8f;
            int statValue = 50;

            // Act
            int result = EquipmentStatCalculator.CalculateScaledAttack(baseAttack, scalingFactor, statValue);

            // Assert
            // 100 * (1 + 0.8 * 50 / 100) = 100 * 1.4 ≈ 139 (float精度によるint切り捨て)
            Assert.AreEqual(139, result);
        }

        [Test]
        public void EquipmentStatCalculator_CalculateWeightRatio_ClampsToZeroOne()
        {
            // Normal case
            float normal = EquipmentStatCalculator.CalculateWeightRatio(50, 100);
            Assert.AreEqual(0.5f, normal, 0.001f);

            // Zero weight
            float zero = EquipmentStatCalculator.CalculateWeightRatio(0, 100);
            Assert.AreEqual(0.0f, zero, 0.001f);

            // Over max (should clamp to 1.0)
            float over = EquipmentStatCalculator.CalculateWeightRatio(150, 100);
            Assert.AreEqual(1.0f, over, 0.001f);

            // Negative weight (should clamp to 0.0)
            float negative = EquipmentStatCalculator.CalculateWeightRatio(-10, 100);
            Assert.AreEqual(0.0f, negative, 0.001f);

            // Zero maxCarryWeight (edge case, should return 1.0 to indicate overloaded)
            float zeroMax = EquipmentStatCalculator.CalculateWeightRatio(10, 0);
            Assert.AreEqual(1.0f, zeroMax, 0.001f);
        }

        [Test]
        public void EquipmentStatCalculator_CalculatePerformanceMultiplier_PenalizesAboveThreshold()
        {
            // Below threshold (0.5) -> 1.0
            float belowThreshold = EquipmentStatCalculator.CalculatePerformanceMultiplier(0.5f);
            Assert.AreEqual(1.0f, belowThreshold, 0.001f);

            // At threshold (0.7) -> 1.0
            float atThreshold = EquipmentStatCalculator.CalculatePerformanceMultiplier(0.7f);
            Assert.AreEqual(1.0f, atThreshold, 0.001f);

            // Midpoint above threshold (0.85) -> Lerp(1.0, 0.5, (0.85-0.7)/0.3) = Lerp(1.0, 0.5, 0.5) = 0.75
            float midpoint = EquipmentStatCalculator.CalculatePerformanceMultiplier(0.85f);
            Assert.AreEqual(0.75f, midpoint, 0.001f);

            // Max weight (1.0) -> 0.5
            float maxWeight = EquipmentStatCalculator.CalculatePerformanceMultiplier(1.0f);
            Assert.AreEqual(0.5f, maxWeight, 0.001f);
        }
    }
}
