using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class SummonSystem_LifetimeTests
    {
        [Test]
        public void SummonManager_Tick_ReducesRemainingTime()
        {
            SummonManager manager = new SummonManager();
            manager.AddSummon(100, 30f, SummonType.Combat);

            manager.Tick(10f);

            SummonSlot[] slots = manager.GetActiveSlots();
            Assert.AreEqual(1, slots.Length);
            Assert.AreEqual(20f, slots[0].remainingTime, 0.001f);
        }

        [Test]
        public void SummonManager_Tick_Expiry_AutoDismisses()
        {
            SummonManager manager = new SummonManager();
            manager.AddSummon(100, 5f, SummonType.Combat);

            int dismissedHash = -1;
            manager.OnSummonDismissed += (hash) => dismissedHash = hash;

            manager.Tick(6f);

            Assert.AreEqual(0, manager.ActiveCount);
            Assert.AreEqual(100, dismissedHash);
        }

        [Test]
        public void SummonManager_Tick_ZeroDuration_NeverExpires()
        {
            SummonManager manager = new SummonManager();
            manager.AddSummon(100, 0f, SummonType.Utility); // 無制限

            manager.Tick(9999f);

            Assert.AreEqual(1, manager.ActiveCount);
        }

        [Test]
        public void SummonManager_OnSummonDeath_FreesSlot()
        {
            SummonManager manager = new SummonManager();
            manager.AddSummon(100, 30f, SummonType.Combat);
            manager.AddSummon(200, 30f, SummonType.Combat);

            manager.OnSummonDeath(100);

            Assert.AreEqual(1, manager.ActiveCount);
            Assert.IsTrue(manager.HasEmptySlot());
        }
    }
}
