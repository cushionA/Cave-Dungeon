using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    [TestFixture]
    public class Leaderboard_StatisticsTests
    {
        private LeaderboardManager _manager;

        [SetUp]
        public void SetUp()
        {
            _manager = new LeaderboardManager();
        }

        [Test]
        public void GetStatistics_WithMultipleRecords_ShouldAggregateCorrectly()
        {
            // Arrange: 3 challenges with varying results
            ChallengeResult result1 = new ChallengeResult
            {
                challengeId = "challenge_01",
                rank = ChallengeRank.Platinum,
                state = ChallengeState.Completed,
                clearTime = 30.0f,
                score = 20000,
                deathCount = 0,
                totalDamageDealt = 5000,
                totalDamageTaken = 0,
                isNewRecord = false,
            };
            _manager.UpdateRecord(result1);

            // Second attempt on challenge_01 (attemptCount=2, clearCount=2)
            ChallengeResult result1b = new ChallengeResult
            {
                challengeId = "challenge_01",
                rank = ChallengeRank.Gold,
                state = ChallengeState.Completed,
                clearTime = 50.0f,
                score = 15000,
                deathCount = 1,
                totalDamageDealt = 4000,
                totalDamageTaken = 200,
                isNewRecord = false,
            };
            _manager.UpdateRecord(result1b);

            ChallengeResult result2 = new ChallengeResult
            {
                challengeId = "challenge_02",
                rank = ChallengeRank.Silver,
                state = ChallengeState.Completed,
                clearTime = 120.0f,
                score = 8000,
                deathCount = 3,
                totalDamageDealt = 2000,
                totalDamageTaken = 500,
                isNewRecord = false,
            };
            _manager.UpdateRecord(result2);

            ChallengeResult result3 = new ChallengeResult
            {
                challengeId = "challenge_03",
                rank = ChallengeRank.Bronze,
                state = ChallengeState.Failed,
                clearTime = 300.0f,
                score = 1000,
                deathCount = 10,
                totalDamageDealt = 500,
                totalDamageTaken = 900,
                isNewRecord = false,
            };
            _manager.UpdateRecord(result3);

            // Act
            (int attempts, int clears, int platinums) stats = _manager.GetStatistics();

            // Assert
            // challenge_01: attemptCount=2, clearCount=2, rank=Platinum
            // challenge_02: attemptCount=1, clearCount=1, rank=Silver
            // challenge_03: attemptCount=1, clearCount=0, rank=Bronze
            Assert.AreEqual(4, stats.attempts);
            Assert.AreEqual(3, stats.clears);
            Assert.AreEqual(1, stats.platinums);
        }

        [Test]
        public void GetAttemptCount_WhenNoRecord_ShouldReturnZero()
        {
            // Arrange: no records added for "nonexistent_challenge"

            // Act
            int attemptCount = _manager.GetAttemptCount("nonexistent_challenge");

            // Assert
            Assert.AreEqual(0, attemptCount);
        }

        [Test]
        public void HasCleared_WhenCleared_ShouldReturnTrue()
        {
            // Arrange
            ChallengeResult result = new ChallengeResult
            {
                challengeId = "challenge_cleared",
                rank = ChallengeRank.Gold,
                state = ChallengeState.Completed,
                clearTime = 60.0f,
                score = 10000,
                deathCount = 0,
                totalDamageDealt = 3000,
                totalDamageTaken = 100,
                isNewRecord = false,
            };
            _manager.UpdateRecord(result);

            // Act
            bool hasCleared = _manager.HasCleared("challenge_cleared");
            bool hasNotCleared = _manager.HasCleared("never_played");

            // Assert
            Assert.IsTrue(hasCleared);
            Assert.IsFalse(hasNotCleared);
        }
    }
}
