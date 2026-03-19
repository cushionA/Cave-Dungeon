using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class SummonSystem_MagicIntegrationTests
    {
        [Test]
        public void SummonMagicBridge_TrySummon_Success_ReturnsTrue()
        {
            SummonManager manager = new SummonManager();
            SummonMagicBridge bridge = new SummonMagicBridge(manager);

            bool result = bridge.TrySummon(100, 30f, SummonType.Combat, 200);

            Assert.IsTrue(result);
            Assert.AreEqual(1, manager.ActiveCount);
        }

        [Test]
        public void SummonMagicBridge_TrySummon_FullSlots_ReturnsFalse()
        {
            SummonManager manager = new SummonManager();
            SummonMagicBridge bridge = new SummonMagicBridge(manager);

            bridge.TrySummon(100, 30f, SummonType.Combat, 200);
            bridge.TrySummon(200, 30f, SummonType.Combat, 200);

            bool result = bridge.TrySummon(300, 30f, SummonType.Combat, 200);
            Assert.IsFalse(result);
        }

        [Test]
        public void SummonMagicBridge_IsSummonMagic_ReturnsCorrectly()
        {
            Assert.IsTrue(SummonMagicBridge.IsSummonMagic(MagicType.Summon));
            Assert.IsFalse(SummonMagicBridge.IsSummonMagic(MagicType.Attack));
            Assert.IsFalse(SummonMagicBridge.IsSummonMagic(MagicType.Recover));
            Assert.IsFalse(SummonMagicBridge.IsSummonMagic(MagicType.Support));
        }

        [Test]
        public void SummonMagicBridge_TrySummon_FiresCreatedEvent()
        {
            SummonManager manager = new SummonManager();
            SummonMagicBridge bridge = new SummonMagicBridge(manager);

            int createdHash = -1;
            manager.OnSummonCreated += (hash) => createdHash = hash;

            bridge.TrySummon(100, 30f, SummonType.Combat, 200);

            Assert.AreEqual(100, createdHash);
        }
    }
}
