using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    [TestFixture]
    public class AITemplates_ApplyRevertTests
    {
        private AITemplateManager _manager;

        [SetUp]
        public void SetUp()
        {
            _manager = new AITemplateManager();

            // テスト用テンプレートを1つ登録しておく
            AITemplateData template = new AITemplateData
            {
                templateId = "aggressive",
                templateName = "Aggressive",
                description = "攻撃的AI",
                authorName = "System",
                category = AITemplateCategory.General,
                config = new CompanionAIConfig
                {
                    modes = new AIMode[] { new AIMode { modeName = "Attack" } },
                    modeTransitionRules = new ModeTransitionRule[0],
                    shortcutModeBindings = new int[4]
                },
                tags = new string[] { "aggressive" }
            };
            _manager.SaveTemplate(template);
        }

        [Test]
        public void ApplyTemplate_WhenValidTemplate_ShouldSavePreviousConfig()
        {
            int companionHash = 42;
            CompanionAIConfig previousConfig = new CompanionAIConfig
            {
                modes = new AIMode[] { new AIMode { modeName = "Idle" } },
                modeTransitionRules = new ModeTransitionRule[0],
                shortcutModeBindings = new int[4]
            };

            bool result = _manager.ApplyTemplate("aggressive", companionHash, previousConfig);

            Assert.IsTrue(result);
            Assert.IsTrue(_manager.HasAppliedTemplate(companionHash));

            // Revert して保存された config が previousConfig と一致するか確認
            bool reverted = _manager.RevertTemplate(companionHash, out CompanionAIConfig restored);
            Assert.IsTrue(reverted);
            Assert.AreEqual(1, restored.modes.Length);
            Assert.AreEqual("Idle", restored.modes[0].modeName);
        }

        [Test]
        public void ApplyTemplate_WhenTemplateNotFound_ShouldReturnFalse()
        {
            int companionHash = 42;
            CompanionAIConfig currentConfig = new CompanionAIConfig
            {
                modes = new AIMode[0],
                modeTransitionRules = new ModeTransitionRule[0],
                shortcutModeBindings = new int[4]
            };

            bool result = _manager.ApplyTemplate("nonexistent_template", companionHash, currentConfig);

            Assert.IsFalse(result);
            Assert.IsFalse(_manager.HasAppliedTemplate(companionHash));
        }

        [Test]
        public void RevertTemplate_WhenApplied_ShouldRestorePreviousConfig()
        {
            int companionHash = 99;
            CompanionAIConfig previousConfig = new CompanionAIConfig
            {
                modes = new AIMode[]
                {
                    new AIMode { modeName = "Defense" },
                    new AIMode { modeName = "Support" }
                },
                modeTransitionRules = new ModeTransitionRule[0],
                shortcutModeBindings = new int[] { 0, 1, -1, -1 }
            };

            _manager.ApplyTemplate("aggressive", companionHash, previousConfig);

            bool result = _manager.RevertTemplate(companionHash, out CompanionAIConfig restored);

            Assert.IsTrue(result);
            Assert.AreEqual(2, restored.modes.Length);
            Assert.AreEqual("Defense", restored.modes[0].modeName);
            Assert.AreEqual("Support", restored.modes[1].modeName);
            Assert.AreEqual(0, restored.shortcutModeBindings[0]);
            Assert.AreEqual(1, restored.shortcutModeBindings[1]);

            // Revert 後は適用状態が解除されている
            Assert.IsFalse(_manager.HasAppliedTemplate(companionHash));
        }

        [Test]
        public void HasAppliedTemplate_WhenNotApplied_ShouldReturnFalse()
        {
            int companionHash = 777;

            bool result = _manager.HasAppliedTemplate(companionHash);

            Assert.IsFalse(result);
        }
    }
}
