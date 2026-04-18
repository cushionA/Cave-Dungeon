using NUnit.Framework;
using System.Collections.Generic;
using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// TacticalPresetRegistry の単体テスト。
    /// 戦術プリセットのCRUD + ModePresetRegistry との連動カスケード更新を検証する。
    /// </summary>
    [TestFixture]
    public class AIRuleBuilder_TacticalPresetRegistryTests
    {
        private ModePresetRegistry _modeRegistry;
        private TacticalPresetRegistry _tacticalRegistry;

        [SetUp]
        public void SetUp()
        {
            _modeRegistry = new ModePresetRegistry();
            _tacticalRegistry = new TacticalPresetRegistry(_modeRegistry);
        }

        [TearDown]
        public void TearDown()
        {
            _tacticalRegistry?.Dispose();
        }

        [Test]
        public void TacticalPresetRegistry_Save_AssignsGuidAndStoresEntry()
        {
            CompanionAIConfig config = new CompanionAIConfig
            {
                modes = new AIMode[0],
                modeTransitionRules = new ModeTransitionRule[0],
                shortcutModeBindings = new int[4]
            };

            string configId = _tacticalRegistry.Save("Balanced", config);

            Assert.IsNotNull(configId);
            Assert.IsNotEmpty(configId);
            Assert.AreEqual(1, _tacticalRegistry.Count);
        }

        [Test]
        public void TacticalPresetRegistry_Save_WritesIdAndNameBackOnStoredEntry()
        {
            CompanionAIConfig config = new CompanionAIConfig
            {
                modes = new AIMode[0],
                modeTransitionRules = new ModeTransitionRule[0],
                shortcutModeBindings = new int[4]
            };

            string configId = _tacticalRegistry.Save("Balanced", config);
            CompanionAIConfig? stored = _tacticalRegistry.GetById(configId);

            Assert.IsNotNull(stored);
            Assert.AreEqual(configId, stored.Value.configId);
            Assert.AreEqual("Balanced", stored.Value.configName);
        }

        [Test]
        public void TacticalPresetRegistry_UpdateById_ExistingId_ReplacesEntry()
        {
            CompanionAIConfig config = new CompanionAIConfig
            {
                modes = new AIMode[] { new AIMode { modeName = "Old" } },
                modeTransitionRules = new ModeTransitionRule[0],
                shortcutModeBindings = new int[4]
            };
            string configId = _tacticalRegistry.Save("Preset", config);

            CompanionAIConfig updated = new CompanionAIConfig
            {
                modes = new AIMode[] { new AIMode { modeName = "New" } },
                modeTransitionRules = new ModeTransitionRule[0],
                shortcutModeBindings = new int[4]
            };
            bool result = _tacticalRegistry.UpdateById(configId, updated);

            CompanionAIConfig? stored = _tacticalRegistry.GetById(configId);
            Assert.IsTrue(result);
            Assert.IsNotNull(stored);
            Assert.AreEqual("New", stored.Value.modes[0].modeName);
            Assert.AreEqual(configId, stored.Value.configId);
        }

        [Test]
        public void TacticalPresetRegistry_UpdateById_UnknownId_ReturnsFalse()
        {
            bool result = _tacticalRegistry.UpdateById("nonexistent", new CompanionAIConfig());

            Assert.IsFalse(result);
            Assert.AreEqual(0, _tacticalRegistry.Count);
        }

        [Test]
        public void TacticalPresetRegistry_Delete_ExistingId_RemovesEntry()
        {
            string configId = _tacticalRegistry.Save("ToDelete", new CompanionAIConfig());

            bool result = _tacticalRegistry.Delete(configId);

            Assert.IsTrue(result);
            Assert.AreEqual(0, _tacticalRegistry.Count);
        }

        [Test]
        public void TacticalPresetRegistry_GetReferencingConfigs_MultipleMatches_ReturnsAll()
        {
            string modeId = _modeRegistry.Save(new AIMode { modeName = "Shared" });
            AIMode referencedMode = _modeRegistry.GetById(modeId).Value;

            CompanionAIConfig configA = new CompanionAIConfig
            {
                modes = new AIMode[] { referencedMode },
                modeTransitionRules = new ModeTransitionRule[0],
                shortcutModeBindings = new int[4]
            };
            CompanionAIConfig configB = new CompanionAIConfig
            {
                modes = new AIMode[] { referencedMode },
                modeTransitionRules = new ModeTransitionRule[0],
                shortcutModeBindings = new int[4]
            };
            CompanionAIConfig configC = new CompanionAIConfig
            {
                modes = new AIMode[] { new AIMode { modeName = "Other", modeId = "unrelated" } },
                modeTransitionRules = new ModeTransitionRule[0],
                shortcutModeBindings = new int[4]
            };

            string idA = _tacticalRegistry.Save("A", configA);
            string idB = _tacticalRegistry.Save("B", configB);
            _tacticalRegistry.Save("C", configC);

            List<string> referencing = _tacticalRegistry.GetReferencingConfigs(modeId);

            Assert.AreEqual(2, referencing.Count);
            Assert.Contains(idA, referencing);
            Assert.Contains(idB, referencing);
        }

        [Test]
        public void TacticalPresetRegistry_GetReferencingConfigs_EmptyModeId_ReturnsEmpty()
        {
            _tacticalRegistry.Save("A", new CompanionAIConfig());

            List<string> result = _tacticalRegistry.GetReferencingConfigs("");

            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void TacticalPresetRegistry_WhenModePresetUpdated_CascadesToReferencingConfigs()
        {
            // モード保存→戦術に埋め込む→モード上書き→戦術のmodesも更新される
            string modeId = _modeRegistry.Save(new AIMode { modeName = "Original", defaultActionIndex = 0 });
            AIMode referencedMode = _modeRegistry.GetById(modeId).Value;

            CompanionAIConfig config = new CompanionAIConfig
            {
                modes = new AIMode[] { referencedMode },
                modeTransitionRules = new ModeTransitionRule[0],
                shortcutModeBindings = new int[4]
            };
            string configId = _tacticalRegistry.Save("Test", config);

            AIMode updated = new AIMode { modeName = "Updated", defaultActionIndex = 5 };
            _modeRegistry.UpdateById(modeId, updated);

            CompanionAIConfig? stored = _tacticalRegistry.GetById(configId);
            Assert.IsNotNull(stored);
            Assert.AreEqual("Updated", stored.Value.modes[0].modeName);
            Assert.AreEqual(5, stored.Value.modes[0].defaultActionIndex);
            Assert.AreEqual(modeId, stored.Value.modes[0].modeId);
        }

        [Test]
        public void TacticalPresetRegistry_WhenModePresetUpdated_UnrelatedConfigsUnchanged()
        {
            string sharedModeId = _modeRegistry.Save(new AIMode { modeName = "Shared" });
            AIMode shared = _modeRegistry.GetById(sharedModeId).Value;

            CompanionAIConfig referencingConfig = new CompanionAIConfig
            {
                modes = new AIMode[] { shared },
                modeTransitionRules = new ModeTransitionRule[0],
                shortcutModeBindings = new int[4]
            };
            CompanionAIConfig independentConfig = new CompanionAIConfig
            {
                modes = new AIMode[] { new AIMode { modeName = "Independent", modeId = "independent-id" } },
                modeTransitionRules = new ModeTransitionRule[0],
                shortcutModeBindings = new int[4]
            };

            _tacticalRegistry.Save("Referencing", referencingConfig);
            string independentId = _tacticalRegistry.Save("Independent", independentConfig);

            _modeRegistry.UpdateById(sharedModeId, new AIMode { modeName = "Changed" });

            CompanionAIConfig? stored = _tacticalRegistry.GetById(independentId);
            Assert.IsNotNull(stored);
            Assert.AreEqual("Independent", stored.Value.modes[0].modeName);
        }

        [Test]
        public void TacticalPresetRegistry_WhenModePresetUpdated_MultipleEntriesInSameConfigAllUpdate()
        {
            string modeId = _modeRegistry.Save(new AIMode { modeName = "Shared" });
            AIMode shared = _modeRegistry.GetById(modeId).Value;

            CompanionAIConfig config = new CompanionAIConfig
            {
                // 同じモードを2つの位置に埋め込む
                modes = new AIMode[] { shared, new AIMode { modeName = "Unrelated", modeId = "u" }, shared },
                modeTransitionRules = new ModeTransitionRule[0],
                shortcutModeBindings = new int[4]
            };
            string configId = _tacticalRegistry.Save("Test", config);

            _modeRegistry.UpdateById(modeId, new AIMode { modeName = "Changed" });

            CompanionAIConfig? stored = _tacticalRegistry.GetById(configId);
            Assert.IsNotNull(stored);
            Assert.AreEqual("Changed", stored.Value.modes[0].modeName);
            Assert.AreEqual("Unrelated", stored.Value.modes[1].modeName);
            Assert.AreEqual("Changed", stored.Value.modes[2].modeName);
        }

        [Test]
        public void TacticalPresetRegistry_Dispose_UnsubscribesFromModeRegistry()
        {
            string modeId = _modeRegistry.Save(new AIMode { modeName = "Shared" });
            AIMode shared = _modeRegistry.GetById(modeId).Value;

            CompanionAIConfig config = new CompanionAIConfig
            {
                modes = new AIMode[] { shared },
                modeTransitionRules = new ModeTransitionRule[0],
                shortcutModeBindings = new int[4]
            };
            string configId = _tacticalRegistry.Save("Test", config);

            _tacticalRegistry.Dispose();

            // Dispose 後の UpdateById は戦術側にカスケードしないはず
            _modeRegistry.UpdateById(modeId, new AIMode { modeName = "PostDispose" });

            // Dispose 済みレジストリから Get はまだ動く（内部Dictionaryは生きている）
            CompanionAIConfig? stored = _tacticalRegistry.GetById(configId);
            Assert.IsNotNull(stored);
            Assert.AreEqual("Shared", stored.Value.modes[0].modeName);
        }

        [Test]
        public void TacticalPresetRegistry_Save_ExceedsMaxPresets_ReturnsNull()
        {
            for (int i = 0; i < 20; i++)
            {
                _tacticalRegistry.Save("Preset" + i, new CompanionAIConfig());
            }

            string overflow = _tacticalRegistry.Save("Overflow", new CompanionAIConfig());

            Assert.IsNull(overflow);
            Assert.AreEqual(20, _tacticalRegistry.Count);
        }

        [Test]
        public void TacticalPresetRegistry_GetAll_ReturnsAllStoredEntries()
        {
            _tacticalRegistry.Save("A", new CompanionAIConfig());
            _tacticalRegistry.Save("B", new CompanionAIConfig());

            CompanionAIConfig[] all = _tacticalRegistry.GetAll();

            Assert.AreEqual(2, all.Length);
        }
    }
}
