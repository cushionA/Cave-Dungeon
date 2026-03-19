using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class ConfusionMagic_DurationTests
    {
        [Test]
        public void ConfusionEffectProcessor_Tick_ReducesDuration()
        {
            ConfusionEffectProcessor processor = new ConfusionEffectProcessor();
            processor.ApplyConfusion(100, 10f, 200);

            processor.Tick(5f);

            Assert.IsTrue(processor.IsConfused(100));
            processor.TryGetConfusionState(100, out ConfusionState state);
            Assert.AreEqual(5f, state.remainingDuration, 0.001f);
        }

        [Test]
        public void ConfusionEffectProcessor_Tick_ExpiresConfusion()
        {
            ConfusionEffectProcessor processor = new ConfusionEffectProcessor();
            processor.ApplyConfusion(100, 5f, 200);

            int clearedHash = -1;
            processor.OnConfusionCleared += (hash) => clearedHash = hash;

            processor.Tick(6f);

            Assert.IsFalse(processor.IsConfused(100));
            Assert.AreEqual(100, clearedHash);
        }

        [Test]
        public void ConfusionEffectProcessor_AccumulateDamageBreak_ClearsOnThreshold()
        {
            ConfusionEffectProcessor processor = new ConfusionEffectProcessor();
            processor.ApplyConfusion(100, 30f, 200);

            // ダメージ50/100 → まだ解除されない
            bool broke = processor.AccumulateDamageBreak(100, 50f, 100f);
            Assert.IsFalse(broke);
            Assert.IsTrue(processor.IsConfused(100));

            // ダメージ追加60 → 合計110/100 → 解除
            broke = processor.AccumulateDamageBreak(100, 60f, 100f);
            Assert.IsTrue(broke);
            Assert.IsFalse(processor.IsConfused(100));
        }

        [Test]
        public void ConfusionEffectProcessor_AccumulateDamageBreak_ZeroThreshold_NeverBreaks()
        {
            ConfusionEffectProcessor processor = new ConfusionEffectProcessor();
            processor.ApplyConfusion(100, 30f, 200);

            // breakThreshold=0 → ダメージによる解除なし
            bool broke = processor.AccumulateDamageBreak(100, 9999f, 0f);
            Assert.IsFalse(broke);
            Assert.IsTrue(processor.IsConfused(100));
        }
    }
}
