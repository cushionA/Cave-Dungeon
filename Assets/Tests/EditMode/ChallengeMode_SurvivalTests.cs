using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class ChallengeMode_SurvivalTests
    {
        [Test]
        public void OnEnemyDefeated_WhenAllEnemiesInWaveKilled_ShouldReturnTrue()
        {
            // Arrange
            int totalWaves = 3;
            int enemiesPerWave = 2;
            SurvivalLogic logic = new SurvivalLogic(totalWaves, enemiesPerWave);

            // Act - 1体目を倒す (まだ残り1体)
            bool firstResult = logic.OnEnemyDefeated();

            // Assert - まだ全滅していない
            Assert.IsFalse(firstResult);
            Assert.AreEqual(1, logic.EnemiesRemainingInWave);

            // Act - 2体目を倒す (Wave全滅)
            bool secondResult = logic.OnEnemyDefeated();

            // Assert - Wave内の敵が全滅したので true
            Assert.IsTrue(secondResult);
            Assert.AreEqual(0, logic.EnemiesRemainingInWave);
        }

        [Test]
        public void AdvanceWave_WhenCalled_ShouldIncrementWaveAndResetEnemyCount()
        {
            // Arrange
            int totalWaves = 3;
            int enemiesPerWave = 5;
            SurvivalLogic logic = new SurvivalLogic(totalWaves, enemiesPerWave);
            Assert.AreEqual(1, logic.CurrentWave);

            // Act
            logic.AdvanceWave();

            // Assert
            Assert.AreEqual(2, logic.CurrentWave);
            Assert.AreEqual(enemiesPerWave, logic.EnemiesRemainingInWave);
        }

        [Test]
        public void IsAllWavesCleared_WhenLastWaveCleared_ShouldReturnTrue()
        {
            // Arrange
            int totalWaves = 2;
            int enemiesPerWave = 1;
            SurvivalLogic logic = new SurvivalLogic(totalWaves, enemiesPerWave);

            // Wave 1: 敵を倒す
            logic.OnEnemyDefeated();

            // Wave 1 -> Wave 2 へ進む
            logic.AdvanceWave();

            // Wave 2: 敵を倒す
            logic.OnEnemyDefeated();

            // Assert - 全Wave完了
            Assert.IsTrue(logic.IsAllWavesCleared);
        }

        [Test]
        public void IsAllWavesCleared_WhenNotAllWavesCleared_ShouldReturnFalse()
        {
            // Arrange
            int totalWaves = 3;
            int enemiesPerWave = 1;
            SurvivalLogic logic = new SurvivalLogic(totalWaves, enemiesPerWave);

            // Wave 1 の敵を倒す
            logic.OnEnemyDefeated();

            // Assert - まだ全Waveクリアしていない
            Assert.IsFalse(logic.IsAllWavesCleared);
        }
    }
}
