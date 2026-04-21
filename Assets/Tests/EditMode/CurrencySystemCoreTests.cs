using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class CurrencySystemCoreTests
    {
        [Test]
        public void CurrencyManager_Add_IncreasesBalance()
        {
            CurrencyManager manager = new CurrencyManager();

            manager.Add(100);

            Assert.AreEqual(100, manager.Balance);
        }

        [Test]
        public void CurrencyManager_TrySpend_WhenInsufficient_ReturnsFalse()
        {
            CurrencyManager manager = new CurrencyManager(50);

            bool result = manager.TrySpend(100);

            Assert.IsFalse(result);
            Assert.AreEqual(50, manager.Balance);
        }

        [Test]
        public void CurrencyManager_ApplyDeathPenalty_Loses20Percent()
        {
            CurrencyManager manager = new CurrencyManager(100);

            int lost = manager.ApplyDeathPenalty();

            Assert.AreEqual(80, manager.Balance);
            Assert.AreEqual(20, lost);
        }

        [Test]
        public void CurrencyManager_SerializeDeserialize_RestoresBalance()
        {
            CurrencyManager manager = new CurrencyManager(500);
            int serialized = manager.SerializeBalance();

            CurrencyManager restored = new CurrencyManager();
            restored.DeserializeBalance(serialized);

            Assert.AreEqual(500, restored.Balance);
        }

        [Test]
        public void CurrencyManager_Add_WhenOverflowWouldOccur_ClampsToIntMax()
        {
            CurrencyManager manager = new CurrencyManager(int.MaxValue - 10);

            manager.Add(100);

            Assert.AreEqual(int.MaxValue, manager.Balance,
                "オーバーフローは int.MaxValue で飽和する");
        }

        [Test]
        public void CurrencyManager_Add_WhenNearLimitWithSafeAmount_AddsNormally()
        {
            CurrencyManager manager = new CurrencyManager(int.MaxValue - 100);

            manager.Add(50);

            Assert.AreEqual(int.MaxValue - 50, manager.Balance,
                "オーバーフロー域に入らない追加は通常通り加算");
        }
    }
}
