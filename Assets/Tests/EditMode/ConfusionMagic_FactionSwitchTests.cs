using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class ConfusionMagic_FactionSwitchTests
    {
        [Test]
        public void ConfusionEffectProcessor_ApplyConfusion_StoresOriginalBelong()
        {
            ConfusionEffectProcessor processor = new ConfusionEffectProcessor();
            processor.ApplyConfusion(100, 10f, 200);

            Assert.IsTrue(processor.TryGetConfusionState(100, out ConfusionState state));
            Assert.AreEqual(CharacterBelong.Enemy, state.originalBelong);
        }

        [Test]
        public void ConfusionEffectProcessor_ClearConfusion_RemovesState()
        {
            ConfusionEffectProcessor processor = new ConfusionEffectProcessor();
            processor.ApplyConfusion(100, 10f, 200);
            Assert.IsTrue(processor.IsConfused(100));

            processor.ClearConfusion(100);
            Assert.IsFalse(processor.IsConfused(100));
            Assert.AreEqual(0, processor.ConfusedCount);
        }

        [Test]
        public void ConfusionEffectProcessor_ClearConfusion_FiresEvent()
        {
            ConfusionEffectProcessor processor = new ConfusionEffectProcessor();
            processor.ApplyConfusion(100, 10f, 200);

            int clearedHash = -1;
            processor.OnConfusionCleared += (hash) => clearedHash = hash;

            processor.ClearConfusion(100);
            Assert.AreEqual(100, clearedHash);
        }

        [Test]
        public void ConfusionEffectProcessor_ApplyConfusion_FiresEvent()
        {
            ConfusionEffectProcessor processor = new ConfusionEffectProcessor();

            int appliedTarget = -1;
            int appliedController = -1;
            processor.OnConfusionApplied += (target, controller) =>
            {
                appliedTarget = target;
                appliedController = controller;
            };

            processor.ApplyConfusion(100, 10f, 200);

            Assert.AreEqual(100, appliedTarget);
            Assert.AreEqual(200, appliedController);
        }

        [Test]
        public void ConfusionEffectProcessor_ApplyConfusion_DuplicateIgnored()
        {
            ConfusionEffectProcessor processor = new ConfusionEffectProcessor();
            processor.ApplyConfusion(100, 10f, 200);

            int eventCount = 0;
            processor.OnConfusionApplied += (_, __) => eventCount++;

            processor.ApplyConfusion(100, 20f, 300); // 重複 → 無視

            Assert.AreEqual(0, eventCount);
            Assert.AreEqual(1, processor.ConfusedCount);
        }
    }
}
