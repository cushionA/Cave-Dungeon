using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class EnemySystem_LootAndRewardTests
    {
        [Test]
        public void LootDistributor_ExpReward_FiresEvent()
        {
            LootRewardDistributor dist = new LootRewardDistributor();
            int received = 0;
            dist.OnExpRewarded += (exp) => received = exp;

            DropTableEvaluator.DropResult result = new DropTableEvaluator.DropResult { exp = 100 };
            dist.Distribute(result);

            Assert.AreEqual(100, received);
        }

        [Test]
        public void LootDistributor_CurrencyReward_FiresEvent()
        {
            LootRewardDistributor dist = new LootRewardDistributor();
            int received = 0;
            dist.OnCurrencyRewarded += (c) => received = c;

            DropTableEvaluator.DropResult result = new DropTableEvaluator.DropResult { currency = 50 };
            dist.Distribute(result);

            Assert.AreEqual(50, received);
        }

        [Test]
        public void LootDistributor_ItemDrop_FiresEvent()
        {
            LootRewardDistributor dist = new LootRewardDistributor();
            int receivedId = 0;
            int receivedCount = 0;
            dist.OnItemDropped += (id, count) => { receivedId = id; receivedCount = count; };

            DropTableEvaluator.DropResult result = new DropTableEvaluator.DropResult
            {
                droppedItemIds = new int[] { 42 },
                droppedItemCounts = new int[] { 3 },
                droppedItemCount = 1
            };
            dist.Distribute(result);

            Assert.AreEqual(42, receivedId);
            Assert.AreEqual(3, receivedCount);
        }

        [Test]
        public void LootDistributor_ZeroValues_NoEvents()
        {
            LootRewardDistributor dist = new LootRewardDistributor();
            bool expFired = false;
            bool currencyFired = false;
            dist.OnExpRewarded += (_) => expFired = true;
            dist.OnCurrencyRewarded += (_) => currencyFired = true;

            DropTableEvaluator.DropResult result = new DropTableEvaluator.DropResult();
            dist.Distribute(result);

            Assert.IsFalse(expFired);
            Assert.IsFalse(currencyFired);
        }
    }
}
