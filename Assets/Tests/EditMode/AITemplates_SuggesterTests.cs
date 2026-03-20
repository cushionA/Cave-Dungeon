using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    [TestFixture]
    public class AITemplates_SuggesterTests
    {
        private AITemplateData MakeTemplate(string id, AITemplateCategory category)
        {
            return new AITemplateData
            {
                templateId = id,
                templateName = id,
                description = "",
                authorName = "Test",
                category = category,
                config = default,
                tags = System.Array.Empty<string>(),
            };
        }

        [Test]
        public void SuggestTemplates_WhenBossFight_ShouldPrioritizeBossFightCategory()
        {
            // Arrange
            AITemplateData[] templates = new AITemplateData[]
            {
                MakeTemplate("general-1", AITemplateCategory.General),
                MakeTemplate("boss-1", AITemplateCategory.BossFight),
                MakeTemplate("boss-2", AITemplateCategory.BossFight),
                MakeTemplate("mob-1", AITemplateCategory.MobClear),
                MakeTemplate("general-2", AITemplateCategory.General),
            };

            // Act
            string[] result = AITemplateSuggester.SuggestTemplates(
                isBossFight: true,
                playerHpRatio: 1.0f,
                enemyCount: 1,
                availableTemplates: templates);

            // Assert
            Assert.That(result.Length, Is.LessThanOrEqualTo(3));
            Assert.That(result.Length, Is.GreaterThan(0));
            // BossFight カテゴリが先頭に来ること
            Assert.AreEqual("boss-1", result[0]);
            Assert.AreEqual("boss-2", result[1]);
            // 残りは General で埋められること
            Assert.AreEqual("general-1", result[2]);
        }

        [Test]
        public void SuggestTemplates_WhenLowHp_ShouldPrioritizeSupportAndDefensive()
        {
            // Arrange
            AITemplateData[] templates = new AITemplateData[]
            {
                MakeTemplate("aggressive-1", AITemplateCategory.Aggressive),
                MakeTemplate("support-1", AITemplateCategory.SupportFocus),
                MakeTemplate("defensive-1", AITemplateCategory.Defensive),
                MakeTemplate("general-1", AITemplateCategory.General),
                MakeTemplate("support-2", AITemplateCategory.SupportFocus),
            };

            // Act
            string[] result = AITemplateSuggester.SuggestTemplates(
                isBossFight: false,
                playerHpRatio: 0.2f,
                enemyCount: 2,
                availableTemplates: templates);

            // Assert
            Assert.That(result.Length, Is.LessThanOrEqualTo(3));
            Assert.That(result.Length, Is.GreaterThan(0));
            // SupportFocus カテゴリが先に収集され、次に Defensive が続く
            Assert.AreEqual("support-1", result[0]);
            Assert.AreEqual("support-2", result[1]);
            Assert.AreEqual("defensive-1", result[2]);
        }

        [Test]
        public void SuggestTemplates_WhenNoMatchingCategory_ShouldReturnGeneral()
        {
            // Arrange: BossFight/MobClear/SupportFocus/Defensive がない場合
            AITemplateData[] templates = new AITemplateData[]
            {
                MakeTemplate("general-1", AITemplateCategory.General),
                MakeTemplate("aggressive-1", AITemplateCategory.Aggressive),
                MakeTemplate("general-2", AITemplateCategory.General),
                MakeTemplate("custom-1", AITemplateCategory.Custom),
            };

            // Act: 条件に合わない（ボスでもない、HP高い、敵少ない）
            string[] result = AITemplateSuggester.SuggestTemplates(
                isBossFight: false,
                playerHpRatio: 0.8f,
                enemyCount: 2,
                availableTemplates: templates);

            // Assert: General / Aggressive がフォールバックで返る
            Assert.That(result.Length, Is.LessThanOrEqualTo(3));
            Assert.That(result.Length, Is.GreaterThan(0));
            Assert.AreEqual("general-1", result[0]);
            Assert.AreEqual("general-2", result[1]);
            Assert.AreEqual("aggressive-1", result[2]);
        }
    }
}
