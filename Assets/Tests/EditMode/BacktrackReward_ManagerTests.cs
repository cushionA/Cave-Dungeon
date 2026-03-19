using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class BacktrackReward_ManagerTests
    {
        private BacktrackRewardData[] CreateTestRewards()
        {
            return new BacktrackRewardData[]
            {
                new BacktrackRewardData
                {
                    rewardId = "reward_01",
                    rewardType = BacktrackRewardType.Item,
                    requiredAbility = AbilityFlag.WallKick,
                    locationHint = "壁蹴りで到達できる高台"
                },
                new BacktrackRewardData
                {
                    rewardId = "reward_02",
                    rewardType = BacktrackRewardType.Currency,
                    requiredAbility = AbilityFlag.DoubleJump,
                    locationHint = "二段ジャンプで届く棚"
                },
                new BacktrackRewardData
                {
                    rewardId = "reward_03",
                    rewardType = BacktrackRewardType.AbilityOrb,
                    requiredAbility = AbilityFlag.WallKick | AbilityFlag.AirDash,
                    locationHint = "壁蹴り+エアダッシュで到達"
                }
            };
        }

        [Test]
        public void BacktrackRewardManager_Constructor_StartsEmpty()
        {
            BacktrackRewardManager manager = new BacktrackRewardManager();
            Assert.AreEqual(0, manager.TotalRewardCount);
        }

        [Test]
        public void BacktrackRewardManager_RegisterRewards_IncrementsCount()
        {
            BacktrackRewardManager manager = new BacktrackRewardManager();
            manager.RegisterRewards("area_01", CreateTestRewards());

            Assert.AreEqual(3, manager.TotalRewardCount);
        }

        [Test]
        public void BacktrackRewardManager_IsCollected_DefaultFalse()
        {
            BacktrackRewardManager manager = new BacktrackRewardManager();
            manager.RegisterRewards("area_01", CreateTestRewards());

            Assert.IsFalse(manager.IsCollected("reward_01"));
        }

        [Test]
        public void BacktrackRewardManager_MarkCollected_SetsTrue()
        {
            BacktrackRewardManager manager = new BacktrackRewardManager();
            manager.RegisterRewards("area_01", CreateTestRewards());

            manager.MarkCollected("reward_01");

            Assert.IsTrue(manager.IsCollected("reward_01"));
            Assert.IsFalse(manager.IsCollected("reward_02"));
        }

        [Test]
        public void BacktrackRewardManager_GetAvailableRewards_FiltersCorrectly()
        {
            BacktrackRewardManager manager = new BacktrackRewardManager();
            manager.RegisterRewards("area_01", CreateTestRewards());

            // WallKickのみ所持 → reward_01だけアクセス可能
            BacktrackRewardData[] available = manager.GetAvailableRewards("area_01", AbilityFlag.WallKick);
            Assert.AreEqual(1, available.Length);
            Assert.AreEqual("reward_01", available[0].rewardId);

            // WallKick + DoubleJump → reward_01, reward_02がアクセス可能
            available = manager.GetAvailableRewards("area_01", AbilityFlag.WallKick | AbilityFlag.DoubleJump);
            Assert.AreEqual(2, available.Length);

            // 全能力 → 全3つアクセス可能
            available = manager.GetAvailableRewards("area_01",
                AbilityFlag.WallKick | AbilityFlag.DoubleJump | AbilityFlag.AirDash);
            Assert.AreEqual(3, available.Length);
        }
    }
}
