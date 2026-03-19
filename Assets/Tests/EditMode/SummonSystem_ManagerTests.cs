using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class SummonSystem_ManagerTests
    {
        [Test]
        public void SummonManager_Constructor_StartsEmpty()
        {
            SummonManager manager = new SummonManager();

            Assert.AreEqual(0, manager.ActiveCount);
            Assert.IsTrue(manager.HasEmptySlot());
        }

        [Test]
        public void SummonManager_AddSummon_IncreasesCount()
        {
            SummonManager manager = new SummonManager();

            bool result = manager.AddSummon(100, 30f, SummonType.Combat);

            Assert.IsTrue(result);
            Assert.AreEqual(1, manager.ActiveCount);
        }

        [Test]
        public void SummonManager_AddSummon_MaxSlots_RejectsFull()
        {
            SummonManager manager = new SummonManager();

            manager.AddSummon(100, 30f, SummonType.Combat);
            manager.AddSummon(200, 30f, SummonType.Combat);

            // 3体目 → 枠なし → false
            bool result = manager.AddSummon(300, 30f, SummonType.Combat);
            Assert.IsFalse(result);
            Assert.AreEqual(2, manager.ActiveCount);
        }

        [Test]
        public void SummonManager_Dismiss_FreesSlot()
        {
            SummonManager manager = new SummonManager();
            manager.AddSummon(100, 30f, SummonType.Combat);

            int dismissedHash = -1;
            manager.OnSummonDismissed += (hash) => dismissedHash = hash;

            manager.Dismiss(100);

            Assert.AreEqual(0, manager.ActiveCount);
            Assert.AreEqual(100, dismissedHash);
            Assert.IsTrue(manager.HasEmptySlot());
        }

        [Test]
        public void SummonManager_DismissAll_ClearsAllSlots()
        {
            SummonManager manager = new SummonManager();
            manager.AddSummon(100, 30f, SummonType.Combat);
            manager.AddSummon(200, 30f, SummonType.Utility);

            manager.DismissAll();

            Assert.AreEqual(0, manager.ActiveCount);
            Assert.IsTrue(manager.HasEmptySlot());
        }
    }
}
