using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class ConfusionMagic_AccumulationTests
    {
        [Test]
        public void ConfusionEffectProcessor_Constructor_StartsEmpty()
        {
            ConfusionEffectProcessor processor = new ConfusionEffectProcessor();

            Assert.AreEqual(0, processor.ConfusedCount);
            Assert.IsFalse(processor.IsConfused(100));
        }

        [Test]
        public void ConfusionEffectProcessor_Accumulate_BelowThreshold_DoesNotConfuse()
        {
            ConfusionEffectProcessor processor = new ConfusionEffectProcessor();

            bool result = processor.Accumulate(100, 50f, 0f, 200, 10f);

            Assert.IsFalse(result);
            Assert.IsFalse(processor.IsConfused(100));
        }

        [Test]
        public void ConfusionEffectProcessor_Accumulate_AtThreshold_Confuses()
        {
            ConfusionEffectProcessor processor = new ConfusionEffectProcessor();

            processor.Accumulate(100, 60f, 0f, 200, 10f);
            bool result = processor.Accumulate(100, 50f, 0f, 200, 10f);

            Assert.IsTrue(result);
            Assert.IsTrue(processor.IsConfused(100));
            Assert.AreEqual(1, processor.ConfusedCount);
        }

        [Test]
        public void ConfusionEffectProcessor_Accumulate_WithResistance_ReducesValue()
        {
            ConfusionEffectProcessor processor = new ConfusionEffectProcessor();

            // resistance=0.5 → 100 * 0.5 = 50、閾値100に到達しない
            bool result = processor.Accumulate(100, 100f, 0.5f, 200, 10f);
            Assert.IsFalse(result);
            Assert.IsFalse(processor.IsConfused(100));

            // もう50追加（50*0.5=25 → 合計75）まだ
            result = processor.Accumulate(100, 50f, 0.5f, 200, 10f);
            Assert.IsFalse(result);

            // もう60追加（60*0.5=30 → 合計105）発症
            result = processor.Accumulate(100, 60f, 0.5f, 200, 10f);
            Assert.IsTrue(result);
        }

        [Test]
        public void ConfusionEffectProcessor_Accumulate_FullResistance_NeverConfuses()
        {
            ConfusionEffectProcessor processor = new ConfusionEffectProcessor();

            // resistance=1.0 → 完全耐性（ボス用）
            bool result = processor.Accumulate(100, 1000f, 1.0f, 200, 10f);
            Assert.IsFalse(result);
            Assert.IsFalse(processor.IsConfused(100));
        }
    }
}
