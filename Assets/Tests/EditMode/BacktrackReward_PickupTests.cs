using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class BacktrackReward_PickupTests
    {
        [Test]
        public void BacktrackPickupLogic_TryCollect_WithAbility_Succeeds()
        {
            BacktrackRewardManager manager = new BacktrackRewardManager();
            manager.RegisterRewards("area_01", new BacktrackRewardData[]
            {
                new BacktrackRewardData
                {
                    rewardId = "r1",
                    rewardType = BacktrackRewardType.Currency,
                    requiredAbility = AbilityFlag.WallKick,
                    currencyAmount = 500
                }
            });

            BacktrackPickupLogic pickup = new BacktrackPickupLogic(manager);
            bool result = pickup.TryCollect("r1", AbilityFlag.WallKick);

            Assert.IsTrue(result);
            Assert.IsTrue(manager.IsCollected("r1"));
        }

        [Test]
        public void BacktrackPickupLogic_TryCollect_WithoutAbility_Fails()
        {
            BacktrackRewardManager manager = new BacktrackRewardManager();
            manager.RegisterRewards("area_01", new BacktrackRewardData[]
            {
                new BacktrackRewardData
                {
                    rewardId = "r1",
                    requiredAbility = AbilityFlag.WallKick
                }
            });

            BacktrackPickupLogic pickup = new BacktrackPickupLogic(manager);
            bool result = pickup.TryCollect("r1", AbilityFlag.None);

            Assert.IsFalse(result);
            Assert.IsFalse(manager.IsCollected("r1"));
        }

        [Test]
        public void BacktrackPickupLogic_TryCollect_AlreadyCollected_Fails()
        {
            BacktrackRewardManager manager = new BacktrackRewardManager();
            manager.RegisterRewards("area_01", new BacktrackRewardData[]
            {
                new BacktrackRewardData
                {
                    rewardId = "r1",
                    requiredAbility = AbilityFlag.None
                }
            });

            BacktrackPickupLogic pickup = new BacktrackPickupLogic(manager);
            pickup.TryCollect("r1", AbilityFlag.None);

            bool result = pickup.TryCollect("r1", AbilityFlag.None);
            Assert.IsFalse(result);
        }
    }
}
