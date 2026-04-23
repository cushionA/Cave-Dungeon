using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// DropThroughLogic のピュアロジックテスト。
    /// 下入力 + ジャンプ入力 + 接地 の 3 条件ANDで発動するかを検証する。
    /// </summary>
    public class DropThroughLogicTests
    {
        // --- TryDropThrough: 発動条件の組み合わせ検証 ---

        [Test]
        public void DropThroughLogic_WhenDownAndJumpAndGrounded_ShouldReturnTrue()
        {
            float duration;
            bool result = DropThroughLogic.TryDropThrough(
                downPressed: true, jumpPressed: true, isGrounded: true, out duration);

            Assert.IsTrue(result);
            Assert.AreEqual(DropThroughLogic.k_DropThroughDurationSeconds, duration, 0.0001f);
        }

        [Test]
        public void DropThroughLogic_WhenDownNotPressed_ShouldReturnFalse()
        {
            float duration;
            bool result = DropThroughLogic.TryDropThrough(
                downPressed: false, jumpPressed: true, isGrounded: true, out duration);

            Assert.IsFalse(result);
            Assert.AreEqual(0f, duration, 0.0001f);
        }

        [Test]
        public void DropThroughLogic_WhenJumpNotPressed_ShouldReturnFalse()
        {
            float duration;
            bool result = DropThroughLogic.TryDropThrough(
                downPressed: true, jumpPressed: false, isGrounded: true, out duration);

            Assert.IsFalse(result);
            Assert.AreEqual(0f, duration, 0.0001f);
        }

        [Test]
        public void DropThroughLogic_WhenNotGrounded_ShouldReturnFalse()
        {
            float duration;
            bool result = DropThroughLogic.TryDropThrough(
                downPressed: true, jumpPressed: true, isGrounded: false, out duration);

            Assert.IsFalse(result);
            Assert.AreEqual(0f, duration, 0.0001f);
        }

        [Test]
        public void DropThroughLogic_AllConditionsFalse_ShouldReturnFalse()
        {
            float duration;
            bool result = DropThroughLogic.TryDropThrough(
                downPressed: false, jumpPressed: false, isGrounded: false, out duration);

            Assert.IsFalse(result);
            Assert.AreEqual(0f, duration, 0.0001f);
        }

        // --- IsDownInput: Y軸入力値から下入力判定 ---

        [Test]
        public void DropThroughLogic_IsDownInput_WithNegativeY_ReturnsTrue()
        {
            bool result = DropThroughLogic.IsDownInput(-1f);

            Assert.IsTrue(result);
        }

        [Test]
        public void DropThroughLogic_IsDownInput_WithZero_ReturnsFalse()
        {
            bool result = DropThroughLogic.IsDownInput(0f);

            Assert.IsFalse(result);
        }

        [Test]
        public void DropThroughLogic_IsDownInput_WithPositiveY_ReturnsFalse()
        {
            bool result = DropThroughLogic.IsDownInput(1f);

            Assert.IsFalse(result);
        }

        [Test]
        public void DropThroughLogic_IsDownInput_AtThreshold_ReturnsTrue()
        {
            // しきい値 -0.5 ちょうどで下入力成立
            bool result = DropThroughLogic.IsDownInput(-DropThroughLogic.k_DownInputThreshold);

            Assert.IsTrue(result);
        }

        [Test]
        public void DropThroughLogic_IsDownInput_JustAboveNegativeThreshold_ReturnsFalse()
        {
            // -0.4 はしきい値(-0.5)より大きいので未成立
            bool result = DropThroughLogic.IsDownInput(-DropThroughLogic.k_DownInputThreshold + 0.1f);

            Assert.IsFalse(result);
        }

        // --- 持続時間の妥当性 ---

        [Test]
        public void DropThroughLogic_DurationConstant_IsPositive()
        {
            Assert.Greater(DropThroughLogic.k_DropThroughDurationSeconds, 0f);
        }
    }
}
