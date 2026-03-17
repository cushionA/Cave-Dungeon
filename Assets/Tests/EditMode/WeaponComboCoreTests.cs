using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class WeaponComboCoreTests
    {
        [Test]
        public void ComboManager_Advance_IncrementsStep()
        {
            // Arrange
            ComboManager combo = new ComboManager(3);
            Assert.AreEqual(0, combo.CurrentStep);

            // Act
            bool advanced = combo.Advance();

            // Assert
            Assert.IsTrue(advanced);
            Assert.AreEqual(1, combo.CurrentStep);
        }

        [Test]
        public void ComboManager_Advance_AtMaxSteps_ReturnsFalse()
        {
            // Arrange
            ComboManager combo = new ComboManager(3);
            combo.Advance(); // step 1
            combo.Advance(); // step 2
            combo.Advance(); // step 3

            // Act
            bool advanced = combo.Advance();

            // Assert
            Assert.IsFalse(advanced);
            Assert.AreEqual(3, combo.CurrentStep);
        }

        [Test]
        public void ComboManager_Tick_WhenWindowExpires_Resets()
        {
            // Arrange
            ComboManager combo = new ComboManager(3);
            combo.Advance(); // step 1
            combo.OpenInputWindow(0.5f);
            Assert.IsTrue(combo.InInputWindow);

            // Act — tick past the window duration
            combo.Tick(1.0f);

            // Assert — combo should reset
            Assert.AreEqual(0, combo.CurrentStep);
            Assert.IsFalse(combo.InInputWindow);
        }

        [Test]
        public void ComboManager_QueueChain_TryConsume_Advances()
        {
            // Arrange
            ComboManager combo = new ComboManager(3);
            combo.Advance(); // step 1
            combo.OpenInputWindow(1.0f);

            // Act — queue input during window, then consume
            combo.QueueChain();
            bool consumed = combo.TryConsumeChain();

            // Assert
            Assert.IsTrue(consumed);
            Assert.AreEqual(2, combo.CurrentStep);
        }
    }
}
