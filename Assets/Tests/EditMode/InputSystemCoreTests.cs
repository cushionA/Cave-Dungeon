using NUnit.Framework;
using UnityEngine;
using Game.Core;

namespace Game.Tests.EditMode
{
    [TestFixture]
    public class InputSystemCoreTests
    {
        // ===== InputConverter Tests =====

        [Test]
        public void InputConverter_NormalizeDirection_WhenBelowDeadZone_ReturnsZero()
        {
            // Arrange: magnitude が DeadZone (0.15) 以下の入力
            Vector2 smallInput = new Vector2(0.1f, 0.05f); // magnitude ~= 0.112

            // Act
            Vector2 result = InputConverter.NormalizeDirection(smallInput);

            // Assert
            Assert.AreEqual(Vector2.zero, result);
        }

        [Test]
        public void InputConverter_NormalizeDirection_WhenAboveOne_Normalizes()
        {
            // Arrange: magnitude > 1.0 の入力
            Vector2 largeInput = new Vector2(1.5f, 1.5f); // magnitude ~= 2.12

            // Act
            Vector2 result = InputConverter.NormalizeDirection(largeInput);

            // Assert: 正規化されてmagnitudeが1.0になる
            Assert.AreEqual(1f, result.magnitude, 0.001f);
            // 方向が保持されていることを確認
            Assert.Greater(result.x, 0f);
            Assert.Greater(result.y, 0f);
        }

        [Test]
        public void InputConverter_ConvertAttackInput_WhenAirborne_ReturnsAerialType()
        {
            // Act: 空中でLightAttack (buttonId=0)
            AttackInputType? aerialLight = InputConverter.ConvertAttackInput(0, isAirborne: true, isCharging: false);
            // Act: 空中でHeavyAttack (buttonId=1)
            AttackInputType? aerialHeavy = InputConverter.ConvertAttackInput(1, isAirborne: true, isCharging: false);

            // Assert
            Assert.AreEqual(AttackInputType.AerialLight, aerialLight);
            Assert.AreEqual(AttackInputType.AerialHeavy, aerialHeavy);
        }

        // ===== InputBuffer Tests =====

        [Test]
        public void InputBuffer_Buffer_WhenWithinTime_HasInputTrue()
        {
            // Arrange
            InputBuffer buffer = new InputBuffer(0.2f);

            // Act: 入力をバッファに追加
            buffer.Buffer(AttackInputType.LightAttack);

            // Assert: 有効時間内なのでHasInput == true
            Assert.IsTrue(buffer.HasInput);
        }

        [Test]
        public void InputBuffer_Tick_WhenTimeExpires_HasInputFalse()
        {
            // Arrange
            InputBuffer buffer = new InputBuffer(0.2f);
            buffer.Buffer(AttackInputType.LightAttack);

            // Act: 有効時間を超過させる
            buffer.Tick(0.3f);

            // Assert: 期限切れでHasInput == false
            Assert.IsFalse(buffer.HasInput);
        }
    }
}
