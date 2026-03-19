using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class AICore_DeliberationBufferTests
    {
        [Test]
        public void DeliberationBuffer_EasyDelay_InRange()
        {
            DeliberationBuffer buffer = new DeliberationBuffer(AIDifficulty.Easy);

            float delayMin = buffer.GetDelay(0f);
            float delayMax = buffer.GetDelay(1f);

            Assert.AreEqual(0.3f, delayMin, 0.01f);
            Assert.AreEqual(0.6f, delayMax, 0.01f);
        }

        [Test]
        public void DeliberationBuffer_HardDelay_InRange()
        {
            DeliberationBuffer buffer = new DeliberationBuffer(AIDifficulty.Hard);

            float delayMin = buffer.GetDelay(0f);
            float delayMax = buffer.GetDelay(1f);

            Assert.AreEqual(0.03f, delayMin, 0.01f);
            Assert.AreEqual(0.13f, delayMax, 0.01f);
        }

        [Test]
        public void DeliberationBuffer_BufferAndTick_ExecutesAfterDelay()
        {
            DeliberationBuffer buffer = new DeliberationBuffer(AIDifficulty.Normal);
            bool executed = false;

            buffer.Buffer(() => executed = true, 0f);
            Assert.IsTrue(buffer.IsBuffering);

            buffer.Tick(0.05f);
            Assert.IsTrue(buffer.IsBuffering);

            buffer.Tick(0.1f);
            Assert.IsFalse(buffer.IsBuffering);
            Assert.IsTrue(executed);
        }
    }
}
