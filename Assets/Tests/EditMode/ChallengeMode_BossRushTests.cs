using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    [TestFixture]
    public class ChallengeMode_BossRushTests
    {
        private static readonly string[] k_TestBossIds = new string[]
        {
            "boss_dragon",
            "boss_golem",
            "boss_lich"
        };

        [Test]
        public void GetNextBossId_WhenStarted_ShouldReturnFirstBoss()
        {
            // Arrange
            BossRushLogic logic = new BossRushLogic(k_TestBossIds);

            // Act
            string nextBoss = logic.GetNextBossId();

            // Assert
            Assert.AreEqual("boss_dragon", nextBoss);
            Assert.AreEqual(0, logic.CurrentBossIndex);
            Assert.IsFalse(logic.IsAllDefeated);
        }

        [Test]
        public void MarkDefeated_WhenCorrectBoss_ShouldAdvanceToNext()
        {
            // Arrange
            BossRushLogic logic = new BossRushLogic(k_TestBossIds);

            // Act
            bool result = logic.MarkDefeated("boss_dragon");

            // Assert
            Assert.IsTrue(result);
            Assert.AreEqual(1, logic.CurrentBossIndex);
            Assert.AreEqual("boss_golem", logic.GetNextBossId());

            // Act - wrong boss id should fail
            bool wrongResult = logic.MarkDefeated("boss_lich");

            // Assert
            Assert.IsFalse(wrongResult);
            Assert.AreEqual(1, logic.CurrentBossIndex);
        }

        [Test]
        public void IsAllDefeated_WhenAllBossesDefeated_ShouldReturnTrue()
        {
            // Arrange
            BossRushLogic logic = new BossRushLogic(k_TestBossIds);

            // Act
            logic.MarkDefeated("boss_dragon");
            logic.MarkDefeated("boss_golem");
            logic.MarkDefeated("boss_lich");

            // Assert
            Assert.IsTrue(logic.IsAllDefeated);
            Assert.AreEqual(3, logic.CurrentBossIndex);
            Assert.IsNull(logic.GetNextBossId());
        }
    }
}
