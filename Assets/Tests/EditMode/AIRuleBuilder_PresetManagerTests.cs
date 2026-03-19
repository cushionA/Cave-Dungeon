using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class AIRuleBuilder_PresetManagerTests
    {
        [Test]
        public void PresetManager_SaveCustom_Succeeds()
        {
            PresetManager manager = new PresetManager();
            CompanionAIConfig config = new CompanionAIConfig();

            bool result = manager.SaveCustomPreset("MyPreset", config);

            Assert.IsTrue(result);
            Assert.AreEqual(1, manager.CustomPresetCount);
        }

        [Test]
        public void PresetManager_LoadPreset_Roundtrips()
        {
            PresetManager manager = new PresetManager();
            CompanionAIConfig config = new CompanionAIConfig
            {
                modes = new AIMode[] { new AIMode { modeName = "TestMode" } }
            };
            manager.SaveCustomPreset("Test", config);

            CompanionAIConfig? loaded = manager.LoadPreset(0, false);

            Assert.IsNotNull(loaded);
            Assert.AreEqual("TestMode", loaded.Value.modes[0].modeName);
        }

        [Test]
        public void PresetManager_MaxSlots_RejectsExcess()
        {
            PresetManager manager = new PresetManager();
            for (int i = 0; i < 20; i++)
            {
                manager.SaveCustomPreset($"Preset{i}", new CompanionAIConfig());
            }

            bool result = manager.SaveCustomPreset("Extra", new CompanionAIConfig());

            Assert.IsFalse(result);
            Assert.AreEqual(20, manager.CustomPresetCount);
        }

        [Test]
        public void PresetManager_Delete_RemovesPreset()
        {
            PresetManager manager = new PresetManager();
            manager.SaveCustomPreset("ToDelete", new CompanionAIConfig());

            bool result = manager.DeleteCustomPreset(0);

            Assert.IsTrue(result);
            Assert.AreEqual(0, manager.CustomPresetCount);
        }
    }
}
