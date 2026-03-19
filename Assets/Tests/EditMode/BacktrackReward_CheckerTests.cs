using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class BacktrackReward_CheckerTests
    {
        [Test]
        public void BacktrackRewardChecker_CanCollect_WithAbility_ReturnsTrue()
        {
            BacktrackRewardData reward = new BacktrackRewardData
            {
                rewardId = "reward_01",
                requiredAbility = AbilityFlag.WallKick
            };

            bool result = BacktrackRewardChecker.CanCollect(reward, AbilityFlag.WallKick | AbilityFlag.DoubleJump);
            Assert.IsTrue(result);
        }

        [Test]
        public void BacktrackRewardChecker_CanCollect_WithoutAbility_ReturnsFalse()
        {
            BacktrackRewardData reward = new BacktrackRewardData
            {
                rewardId = "reward_01",
                requiredAbility = AbilityFlag.WallKick
            };

            bool result = BacktrackRewardChecker.CanCollect(reward, AbilityFlag.DoubleJump);
            Assert.IsFalse(result);
        }

        [Test]
        public void BacktrackRewardChecker_CanCollect_MultiAbility_RequiresAll()
        {
            BacktrackRewardData reward = new BacktrackRewardData
            {
                rewardId = "reward_combo",
                requiredAbility = AbilityFlag.WallKick | AbilityFlag.AirDash
            };

            // 片方だけ → false
            Assert.IsFalse(BacktrackRewardChecker.CanCollect(reward, AbilityFlag.WallKick));
            Assert.IsFalse(BacktrackRewardChecker.CanCollect(reward, AbilityFlag.AirDash));

            // 両方あり → true
            Assert.IsTrue(BacktrackRewardChecker.CanCollect(reward,
                AbilityFlag.WallKick | AbilityFlag.AirDash));
        }

        [Test]
        public void BacktrackRewardChecker_CanCollect_NoneRequired_AlwaysTrue()
        {
            BacktrackRewardData reward = new BacktrackRewardData
            {
                rewardId = "reward_free",
                requiredAbility = AbilityFlag.None
            };

            Assert.IsTrue(BacktrackRewardChecker.CanCollect(reward, AbilityFlag.None));
            Assert.IsTrue(BacktrackRewardChecker.CanCollect(reward, AbilityFlag.WallKick));
        }
    }
}
