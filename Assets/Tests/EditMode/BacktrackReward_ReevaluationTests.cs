using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class BacktrackReward_ReevaluationTests
    {
        [Test]
        public void BacktrackRewardManager_ReevaluateAll_NotifiesNewlyAvailable()
        {
            BacktrackRewardManager manager = new BacktrackRewardManager();
            manager.RegisterRewards("area_01", new BacktrackRewardData[]
            {
                new BacktrackRewardData { rewardId = "r1", requiredAbility = AbilityFlag.WallKick },
                new BacktrackRewardData { rewardId = "r2", requiredAbility = AbilityFlag.DoubleJump }
            });

            int notifiedCount = 0;
            manager.OnRewardAvailable += (id, ability) => notifiedCount++;

            int count = manager.ReevaluateAll(AbilityFlag.WallKick);

            Assert.AreEqual(1, count);
            Assert.AreEqual(1, notifiedCount);
        }

        [Test]
        public void BacktrackRewardManager_ReevaluateAll_SkipsCollected()
        {
            BacktrackRewardManager manager = new BacktrackRewardManager();
            manager.RegisterRewards("area_01", new BacktrackRewardData[]
            {
                new BacktrackRewardData { rewardId = "r1", requiredAbility = AbilityFlag.WallKick },
                new BacktrackRewardData { rewardId = "r2", requiredAbility = AbilityFlag.WallKick }
            });

            manager.MarkCollected("r1");

            int count = manager.ReevaluateAll(AbilityFlag.WallKick);
            Assert.AreEqual(1, count); // r2のみ
        }

        [Test]
        public void BacktrackRewardManager_ReevaluateAll_MultipleAreas()
        {
            BacktrackRewardManager manager = new BacktrackRewardManager();
            manager.RegisterRewards("area_01", new BacktrackRewardData[]
            {
                new BacktrackRewardData { rewardId = "r1", requiredAbility = AbilityFlag.WallKick }
            });
            manager.RegisterRewards("area_02", new BacktrackRewardData[]
            {
                new BacktrackRewardData { rewardId = "r2", requiredAbility = AbilityFlag.WallKick }
            });

            int count = manager.ReevaluateAll(AbilityFlag.WallKick);
            Assert.AreEqual(2, count);
        }

        [Test]
        public void BacktrackRewardManager_GetCollectionRate_CalculatesCorrectly()
        {
            BacktrackRewardManager manager = new BacktrackRewardManager();
            manager.RegisterRewards("area_01", new BacktrackRewardData[]
            {
                new BacktrackRewardData { rewardId = "r1" },
                new BacktrackRewardData { rewardId = "r2" },
                new BacktrackRewardData { rewardId = "r3" },
                new BacktrackRewardData { rewardId = "r4" }
            });

            manager.MarkCollected("r1");
            manager.MarkCollected("r2");

            Assert.AreEqual(0.5f, manager.GetCollectionRate(), 0.001f);
        }
    }
}
