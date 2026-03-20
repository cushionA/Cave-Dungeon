using NUnit.Framework;
using UnityEngine;
using Game.Core;

namespace Game.Tests.EditMode
{
    [TestFixture]
    public class ChallengeMode_ScoreTests
    {
        private ChallengeDefinition _definition;

        [SetUp]
        public void SetUp()
        {
            _definition = ScriptableObject.CreateInstance<ChallengeDefinition>();
            _definition.SetForTest(
                challengeId: "test_challenge",
                challengeName: "Test Challenge",
                challengeType: ChallengeType.TimeAttack,
                silverTimeThreshold: 120f,
                goldTimeThreshold: 60f,
                goldScoreThreshold: 8000,
                platinumScoreThreshold: 15000
            );
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_definition);
        }

        // ----- CalculateScore Tests -----

        [Test]
        public void CalculateScore_WithFastClearTime_ShouldReturnHighScore()
        {
            // Arrange: 高ダメージ出力、被ダメ少、死亡0、クリアタイム短い
            ChallengeResult result = new ChallengeResult
            {
                challengeId = "test_challenge",
                state = ChallengeState.Completed,
                clearTime = 10f,
                deathCount = 0,
                totalDamageDealt = 500,
                totalDamageTaken = 50,
            };

            // Act
            int score = ChallengeScoreCalculator.CalculateScore(result, _definition);

            // Assert
            // baseScore=10000, clearTime=10
            // hpBonusRatio = (500/50) * 0.1 = 1.0 (capped at 2.0, so 1.0)
            // deathPenalty = max(0.1, 1.0 - 0*0.1) = 1.0
            // score = (10000/10) * (1+1.0) * 1.0 = 1000 * 2.0 * 1.0 = 2000
            Assert.AreEqual(2000, score);
        }

        [Test]
        public void CalculateScore_WithMultipleDeaths_ShouldApplyPenalty()
        {
            // Arrange: 死亡5回で0.5ペナルティ
            ChallengeResult result = new ChallengeResult
            {
                challengeId = "test_challenge",
                state = ChallengeState.Completed,
                clearTime = 100f,
                deathCount = 5,
                totalDamageDealt = 100,
                totalDamageTaken = 100,
            };

            // Act
            int score = ChallengeScoreCalculator.CalculateScore(result, _definition);

            // Assert
            // baseScore=10000, clearTime=100
            // hpBonusRatio = (100/100) * 0.1 = 0.1
            // deathPenalty = max(0.1, 1.0 - 5*0.1) = 0.5
            // score = (10000/100) * (1+0.1) * 0.5 = 100 * 1.1 * 0.5 = 55
            Assert.AreEqual(55, score);
        }

        [Test]
        public void CalculateScore_WithZeroDamageTaken_ShouldCapBonusRatio()
        {
            // Arrange: 被ダメ0の場合、max(1, 0) = 1 で割る
            ChallengeResult result = new ChallengeResult
            {
                challengeId = "test_challenge",
                state = ChallengeState.Completed,
                clearTime = 50f,
                deathCount = 0,
                totalDamageDealt = 1000,
                totalDamageTaken = 0,
            };

            // Act
            int score = ChallengeScoreCalculator.CalculateScore(result, _definition);

            // Assert
            // hpBonusRatio = (1000 / max(1,0)) * 0.1 = 1000 * 0.1 = 100.0 → capped at 2.0
            // deathPenalty = 1.0
            // score = (10000/50) * (1+2.0) * 1.0 = 200 * 3.0 = 600
            Assert.AreEqual(600, score);
        }

        [Test]
        public void CalculateScore_WithExcessiveDeaths_ShouldClampPenaltyToMinimum()
        {
            // Arrange: 死亡15回で1.0 - 1.5 = -0.5 → max(0.1, -0.5) = 0.1
            ChallengeResult result = new ChallengeResult
            {
                challengeId = "test_challenge",
                state = ChallengeState.Completed,
                clearTime = 100f,
                deathCount = 15,
                totalDamageDealt = 0,
                totalDamageTaken = 100,
            };

            // Act
            int score = ChallengeScoreCalculator.CalculateScore(result, _definition);

            // Assert
            // hpBonusRatio = (0/100) * 0.1 = 0.0
            // deathPenalty = max(0.1, 1.0 - 15*0.1) = max(0.1, -0.5) = 0.1
            // score = (10000/100) * (1+0.0) * 0.1 = 100 * 1.0 * 0.1 = 10
            Assert.AreEqual(10, score);
        }

        // ----- EvaluateRank Tests -----

        [Test]
        public void EvaluateRank_WhenScoreExceedsPlatinum_ShouldReturnPlatinum()
        {
            // Arrange: スコアがプラチナ閾値以上 AND タイムがゴールド閾値以下 AND 死亡0
            ChallengeResult result = new ChallengeResult
            {
                challengeId = "test_challenge",
                state = ChallengeState.Completed,
                score = 16000,
                clearTime = 50f,
                deathCount = 0,
            };

            // Act
            ChallengeRank rank = ChallengeScoreCalculator.EvaluateRank(result, _definition);

            // Assert
            Assert.AreEqual(ChallengeRank.Platinum, rank);
        }

        [Test]
        public void EvaluateRank_WhenOnlyCleared_ShouldReturnBronze()
        {
            // Arrange: クリア済みだがスコア・タイムともに閾値未満
            ChallengeResult result = new ChallengeResult
            {
                challengeId = "test_challenge",
                state = ChallengeState.Completed,
                score = 1000,
                clearTime = 200f,
                deathCount = 10,
            };

            // Act
            ChallengeRank rank = ChallengeScoreCalculator.EvaluateRank(result, _definition);

            // Assert
            Assert.AreEqual(ChallengeRank.Bronze, rank);
        }

        [Test]
        public void EvaluateRank_WhenNotCompleted_ShouldReturnNone()
        {
            // Arrange: 未クリア
            ChallengeResult result = new ChallengeResult
            {
                challengeId = "test_challenge",
                state = ChallengeState.Failed,
                score = 20000,
                clearTime = 30f,
                deathCount = 0,
            };

            // Act
            ChallengeRank rank = ChallengeScoreCalculator.EvaluateRank(result, _definition);

            // Assert
            Assert.AreEqual(ChallengeRank.None, rank);
        }

        [Test]
        public void EvaluateRank_WhenGoldScoreButNotPlatinum_ShouldReturnGold()
        {
            // Arrange: ゴールドスコア達成、プラチナ未達（死亡あり）
            ChallengeResult result = new ChallengeResult
            {
                challengeId = "test_challenge",
                state = ChallengeState.Completed,
                score = 9000,
                clearTime = 200f,
                deathCount = 3,
            };

            // Act
            ChallengeRank rank = ChallengeScoreCalculator.EvaluateRank(result, _definition);

            // Assert
            Assert.AreEqual(ChallengeRank.Gold, rank);
        }

        [Test]
        public void EvaluateRank_WhenSilverTime_ShouldReturnSilver()
        {
            // Arrange: シルバータイム閾値以下だがゴールド条件未達
            ChallengeResult result = new ChallengeResult
            {
                challengeId = "test_challenge",
                state = ChallengeState.Completed,
                score = 3000,
                clearTime = 100f,
                deathCount = 5,
            };

            // Act
            ChallengeRank rank = ChallengeScoreCalculator.EvaluateRank(result, _definition);

            // Assert
            Assert.AreEqual(ChallengeRank.Silver, rank);
        }
    }
}
