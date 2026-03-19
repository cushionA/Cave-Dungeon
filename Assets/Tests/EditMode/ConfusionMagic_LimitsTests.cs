using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class ConfusionMagic_LimitsTests
    {
        [Test]
        public void ConfusionEffectProcessor_CanConfuseMore_UnderLimit_ReturnsTrue()
        {
            ConfusionEffectProcessor processor = new ConfusionEffectProcessor();
            Assert.IsTrue(processor.CanConfuseMore());

            processor.ApplyConfusion(100, 10f, 200);
            Assert.IsTrue(processor.CanConfuseMore()); // 1/3
        }

        [Test]
        public void ConfusionEffectProcessor_CanConfuseMore_AtLimit_ReturnsFalse()
        {
            ConfusionEffectProcessor processor = new ConfusionEffectProcessor();
            processor.ApplyConfusion(100, 10f, 200);
            processor.ApplyConfusion(200, 10f, 200);
            processor.ApplyConfusion(300, 10f, 200);

            Assert.IsFalse(processor.CanConfuseMore()); // 3/3
        }

        [Test]
        public void ConfusionEffectProcessor_ClearAll_ClearsAllConfused()
        {
            ConfusionEffectProcessor processor = new ConfusionEffectProcessor();
            processor.ApplyConfusion(100, 10f, 200);
            processor.ApplyConfusion(200, 10f, 200);

            processor.ClearAll();

            Assert.AreEqual(0, processor.ConfusedCount);
            Assert.IsFalse(processor.IsConfused(100));
            Assert.IsFalse(processor.IsConfused(200));
        }
    }
}
