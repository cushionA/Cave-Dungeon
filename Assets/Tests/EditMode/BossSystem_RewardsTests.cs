using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class BossSystem_RewardsTests
    {
        [Test]
        public void BossRewardLogic_CalculateReward_ReturnsCorrectValues()
        {
            BossRewardLogic logic = new BossRewardLogic(500, 1000);
            BossRewardResult result = logic.CalculateReward();

            Assert.AreEqual(500, result.expReward);
            Assert.AreEqual(1000, result.currencyReward);
        }

        [Test]
        public void BossRewardLogic_CalculateReward_ZeroValues()
        {
            BossRewardLogic logic = new BossRewardLogic(0, 0);
            BossRewardResult result = logic.CalculateReward();

            Assert.AreEqual(0, result.expReward);
            Assert.AreEqual(0, result.currencyReward);
        }

        [Test]
        public void BossRewardLogic_IsDistributed_DefaultFalse()
        {
            BossRewardLogic logic = new BossRewardLogic(100, 200);
            Assert.IsFalse(logic.IsDistributed);
        }

        [Test]
        public void BossRewardLogic_MarkDistributed_PreventsDoubleReward()
        {
            BossRewardLogic logic = new BossRewardLogic(100, 200);
            logic.MarkDistributed();

            Assert.IsTrue(logic.IsDistributed);
        }
    }
}
