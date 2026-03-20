using System;
using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    [TestFixture]
    public class Common_Section4TypesTests
    {
        // ===== ChallengeType =====

        [Test]
        public void ChallengeType_EnumValues_ShouldHaveFiveMembers()
        {
            string[] names = Enum.GetNames(typeof(ChallengeType));
            Assert.AreEqual(5, names.Length);
        }

        [Test]
        public void ChallengeType_AllValues_ShouldBeDefined()
        {
            Assert.IsTrue(Enum.IsDefined(typeof(ChallengeType), (byte)0)); // BossRush
            Assert.IsTrue(Enum.IsDefined(typeof(ChallengeType), (byte)1)); // TimeAttack
            Assert.IsTrue(Enum.IsDefined(typeof(ChallengeType), (byte)2)); // Survival
            Assert.IsTrue(Enum.IsDefined(typeof(ChallengeType), (byte)3)); // Restriction
            Assert.IsTrue(Enum.IsDefined(typeof(ChallengeType), (byte)4)); // ScoreAttack
        }

        // ===== ChallengeRank =====

        [Test]
        public void ChallengeRank_Order_ShouldBeNoneBronzeSilverGoldPlatinum()
        {
            Assert.Less((int)ChallengeRank.None, (int)ChallengeRank.Bronze);
            Assert.Less((int)ChallengeRank.Bronze, (int)ChallengeRank.Silver);
            Assert.Less((int)ChallengeRank.Silver, (int)ChallengeRank.Gold);
            Assert.Less((int)ChallengeRank.Gold, (int)ChallengeRank.Platinum);
        }

        [Test]
        public void ChallengeRank_EnumValues_ShouldHaveFiveMembers()
        {
            string[] names = Enum.GetNames(typeof(ChallengeRank));
            Assert.AreEqual(5, names.Length);
        }

        // ===== ChallengeState =====

        [Test]
        public void ChallengeState_EnumValues_ShouldHaveFourMembers()
        {
            string[] names = Enum.GetNames(typeof(ChallengeState));
            Assert.AreEqual(4, names.Length);
        }

        // ===== AITemplateCategory =====

        [Test]
        public void AITemplateCategory_EnumValues_ShouldHaveSevenMembers()
        {
            string[] names = Enum.GetNames(typeof(AITemplateCategory));
            Assert.AreEqual(7, names.Length);
        }

        // ===== ChallengeResult =====

        [Test]
        public void ChallengeResult_WhenFieldsSet_ShouldRetainValues()
        {
            ChallengeResult result = new ChallengeResult
            {
                challengeId = "boss_rush_01",
                rank = ChallengeRank.Gold,
                clearTime = 120.5f,
                score = 9500,
                deathCount = 2,
                totalDamageDealt = 50000,
                totalDamageTaken = 3000,
                isNewRecord = true
            };

            Assert.AreEqual("boss_rush_01", result.challengeId);
            Assert.AreEqual(ChallengeRank.Gold, result.rank);
            Assert.AreEqual(120.5f, result.clearTime, 0.001f);
            Assert.AreEqual(9500, result.score);
            Assert.AreEqual(2, result.deathCount);
            Assert.AreEqual(50000, result.totalDamageDealt);
            Assert.AreEqual(3000, result.totalDamageTaken);
            Assert.IsTrue(result.isNewRecord);
        }

        [Test]
        public void ChallengeResult_DefaultValues_ShouldBeZeroOrNull()
        {
            ChallengeResult result = default;

            Assert.IsNull(result.challengeId);
            Assert.AreEqual(ChallengeRank.None, result.rank);
            Assert.AreEqual(0f, result.clearTime);
            Assert.AreEqual(0, result.score);
            Assert.AreEqual(0, result.deathCount);
            Assert.AreEqual(0, result.totalDamageDealt);
            Assert.AreEqual(0, result.totalDamageTaken);
            Assert.IsFalse(result.isNewRecord);
        }

        // ===== LeaderboardEntry =====

        [Test]
        public void LeaderboardEntry_WhenFieldsSet_ShouldRetainValues()
        {
            LeaderboardEntry entry = new LeaderboardEntry
            {
                challengeId = "time_attack_03",
                rank = ChallengeRank.Platinum,
                bestTime = 45.2f,
                bestScore = 12000,
                attemptCount = 15,
                clearCount = 10,
                dateAchieved = "2026-03-20"
            };

            Assert.AreEqual("time_attack_03", entry.challengeId);
            Assert.AreEqual(ChallengeRank.Platinum, entry.rank);
            Assert.AreEqual(45.2f, entry.bestTime, 0.001f);
            Assert.AreEqual(12000, entry.bestScore);
            Assert.AreEqual(15, entry.attemptCount);
            Assert.AreEqual(10, entry.clearCount);
            Assert.AreEqual("2026-03-20", entry.dateAchieved);
        }

        // ===== AITemplateData =====

        [Test]
        public void AITemplateData_WhenFieldsSet_ShouldRetainValues()
        {
            AITemplateData template = new AITemplateData
            {
                templateId = "tmpl_aggressive_01",
                templateName = "Aggressive Striker",
                description = "Full offense companion build",
                authorName = "TestUser",
                category = AITemplateCategory.Aggressive,
                tags = new string[] { "melee", "dps", "boss" }
            };

            Assert.AreEqual("tmpl_aggressive_01", template.templateId);
            Assert.AreEqual("Aggressive Striker", template.templateName);
            Assert.AreEqual("Full offense companion build", template.description);
            Assert.AreEqual("TestUser", template.authorName);
            Assert.AreEqual(AITemplateCategory.Aggressive, template.category);
            Assert.AreEqual(3, template.tags.Length);
            Assert.AreEqual("melee", template.tags[0]);
        }

        [Test]
        public void AITemplateData_WithCompanionAIConfig_ShouldEmbedConfig()
        {
            AIMode testMode = new AIMode
            {
                modeName = "TestMode",
                targetRules = new AIRule[0],
                actionRules = new AIRule[0],
                targetSelects = new AITargetSelect[0],
                actions = new ActionSlot[0],
                defaultActionIndex = 0
            };

            CompanionAIConfig config = new CompanionAIConfig
            {
                modes = new AIMode[] { testMode },
                modeTransitionRules = new ModeTransitionRule[0],
                shortcutModeBindings = new int[] { 0, -1, -1, -1 }
            };

            AITemplateData template = new AITemplateData
            {
                templateId = "tmpl_custom_01",
                templateName = "Custom Build",
                description = "Custom companion AI",
                authorName = "Player",
                category = AITemplateCategory.Custom,
                config = config,
                tags = new string[] { "custom" }
            };

            Assert.AreEqual(1, template.config.modes.Length);
            Assert.AreEqual("TestMode", template.config.modes[0].modeName);
            Assert.AreEqual(4, template.config.shortcutModeBindings.Length);
        }
    }
}
