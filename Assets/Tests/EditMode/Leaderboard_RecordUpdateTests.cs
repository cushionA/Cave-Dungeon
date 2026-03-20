using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    [TestFixture]
    public class Leaderboard_RecordUpdateTests
    {
        private LeaderboardManager _manager;

        [SetUp]
        public void SetUp()
        {
            _manager = new LeaderboardManager();
        }

        [Test]
        public void UpdateRecord_WhenFirstAttempt_ShouldCreateNewEntry()
        {
            // Arrange
            ChallengeResult result = new ChallengeResult
            {
                challengeId = "challenge_01",
                rank = ChallengeRank.Silver,
                state = ChallengeState.Completed,
                clearTime = 120.5f,
                score = 5000,
                deathCount = 2,
                totalDamageDealt = 1000,
                totalDamageTaken = 300,
                isNewRecord = false,
            };

            // Act
            bool isNewRecord = _manager.UpdateRecord(result);

            // Assert
            Assert.IsTrue(isNewRecord);

            LeaderboardEntry entry = _manager.GetBestRecord("challenge_01");
            Assert.AreEqual("challenge_01", entry.challengeId);
            Assert.AreEqual(ChallengeRank.Silver, entry.rank);
            Assert.AreEqual(120.5f, entry.bestTime, 0.001f);
            Assert.AreEqual(5000, entry.bestScore);
            Assert.AreEqual(1, entry.attemptCount);
            Assert.AreEqual(1, entry.clearCount);
        }

        [Test]
        public void UpdateRecord_WhenBetterScore_ShouldUpdateAndReturnTrue()
        {
            // Arrange: first attempt
            ChallengeResult firstResult = new ChallengeResult
            {
                challengeId = "challenge_02",
                rank = ChallengeRank.Bronze,
                state = ChallengeState.Completed,
                clearTime = 200.0f,
                score = 3000,
                deathCount = 5,
                totalDamageDealt = 800,
                totalDamageTaken = 500,
                isNewRecord = false,
            };
            _manager.UpdateRecord(firstResult);

            // Better score, better time, better rank
            ChallengeResult betterResult = new ChallengeResult
            {
                challengeId = "challenge_02",
                rank = ChallengeRank.Gold,
                state = ChallengeState.Completed,
                clearTime = 90.0f,
                score = 8000,
                deathCount = 0,
                totalDamageDealt = 1200,
                totalDamageTaken = 100,
                isNewRecord = false,
            };

            // Act
            bool isNewRecord = _manager.UpdateRecord(betterResult);

            // Assert
            Assert.IsTrue(isNewRecord);

            LeaderboardEntry entry = _manager.GetBestRecord("challenge_02");
            Assert.AreEqual(ChallengeRank.Gold, entry.rank);
            Assert.AreEqual(90.0f, entry.bestTime, 0.001f);
            Assert.AreEqual(8000, entry.bestScore);
            Assert.AreEqual(2, entry.attemptCount);
            Assert.AreEqual(2, entry.clearCount);
        }

        [Test]
        public void UpdateRecord_WhenWorseScore_ShouldOnlyIncrementAttemptCount()
        {
            // Arrange: first attempt with good score
            ChallengeResult firstResult = new ChallengeResult
            {
                challengeId = "challenge_03",
                rank = ChallengeRank.Gold,
                state = ChallengeState.Completed,
                clearTime = 60.0f,
                score = 10000,
                deathCount = 0,
                totalDamageDealt = 2000,
                totalDamageTaken = 50,
                isNewRecord = false,
            };
            _manager.UpdateRecord(firstResult);

            // Worse score, worse time, worse rank
            ChallengeResult worseResult = new ChallengeResult
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

            // Act
            bool isNewRecord = _manager.UpdateRecord(worseResult);

            // Assert
            Assert.IsFalse(isNewRecord);

            LeaderboardEntry entry = _manager.GetBestRecord("challenge_03");
            Assert.AreEqual(ChallengeRank.Gold, entry.rank);
            Assert.AreEqual(60.0f, entry.bestTime, 0.001f);
            Assert.AreEqual(10000, entry.bestScore);
            Assert.AreEqual(2, entry.attemptCount);
            // Failed attempt should not increment clearCount
            Assert.AreEqual(1, entry.clearCount);
        }

        [Test]
        public void SerializeDeserialize_ShouldPreserveRecords()
        {
            // Arrange: add two records
            ChallengeResult result1 = new ChallengeResult
            {
                challengeId = "challenge_a",
                rank = ChallengeRank.Gold,
                state = ChallengeState.Completed,
                clearTime = 45.0f,
                score = 12000,
                deathCount = 0,
                totalDamageDealt = 3000,
                totalDamageTaken = 100,
                isNewRecord = false,
            };
            ChallengeResult result2 = new ChallengeResult
            {
                challengeId = "challenge_b",
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
            _manager.UpdateRecord(result2);

            // Act: serialize and deserialize into a new instance
            object serialized = _manager.Serialize();
            LeaderboardManager restored = new LeaderboardManager();
            restored.Deserialize(serialized);

            // Assert
            LeaderboardEntry[] allRecords = restored.GetAllRecords();
            Assert.AreEqual(2, allRecords.Length);

            LeaderboardEntry entryA = restored.GetBestRecord("challenge_a");
            Assert.AreEqual("challenge_a", entryA.challengeId);
            Assert.AreEqual(ChallengeRank.Gold, entryA.rank);
            Assert.AreEqual(45.0f, entryA.bestTime, 0.001f);
            Assert.AreEqual(12000, entryA.bestScore);

            LeaderboardEntry entryB = restored.GetBestRecord("challenge_b");
            Assert.AreEqual("challenge_b", entryB.challengeId);
            Assert.AreEqual(ChallengeRank.Platinum, entryB.rank);
            Assert.AreEqual(30.0f, entryB.bestTime, 0.001f);
            Assert.AreEqual(20000, entryB.bestScore);
        }
    }
}
