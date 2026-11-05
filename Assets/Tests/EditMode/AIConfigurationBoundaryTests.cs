using Game.Core;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    public class AIConfigurationBoundaryTests
    {
        private static AIMode Mode(int value = 10)
        {
            return new AIMode
            {
                modeName = "Mode",
                actions = new[] { new ActionSlot { actionDefinitionId = "original", paramId = value } },
                actionRules = new[] { new AIRule { conditions = new[] { new AICondition { operandA = value } } } },
                targetRules = new[] { new AIRule { conditions = new[] { new AICondition { operandA = value } } } },
                targetSelects = new[] { new AITargetSelect { sortKey = TargetSortKey.Distance } },
            };
        }

        private static CompanionAIConfig Config(AIMode mode)
        {
            return new CompanionAIConfig
            {
                modes = new[] { mode },
                modeTransitionRules = new[]
                {
                    new ModeTransitionRule { conditions = new[] { new AICondition { operandA = 10 } } },
                },
                shortcutModeBindings = new[] { 0, -1, -1, -1 },
            };
        }

        private static void Mutate(AIMode mode)
        {
            mode.actions[0].paramId = 99;
            mode.actions[0].actionDefinitionId = "mutated";
            mode.actionRules[0].conditions[0].operandA = 99;
            mode.targetRules[0].conditions[0].operandA = 99;
            mode.targetSelects[0].sortKey = TargetSortKey.HpValue;
        }

        private static void AssertOriginal(AIMode mode)
        {
            Assert.AreEqual(10, mode.actions[0].paramId);
            Assert.AreEqual("original", mode.actions[0].actionDefinitionId);
            Assert.AreEqual(10, mode.actionRules[0].conditions[0].operandA);
            Assert.AreEqual(10, mode.targetRules[0].conditions[0].operandA);
            Assert.AreEqual(TargetSortKey.Distance, mode.targetSelects[0].sortKey);
        }

        [Test]
        public void ModePreset_Save_CapturesNestedInputArrays()
        {
            ModePresetRegistry registry = new ModePresetRegistry();
            AIMode source = Mode();
            string id = registry.Save(source);
            Mutate(source);
            AssertOriginal(registry.GetById(id).Value);
        }

        [Test]
        public void ModePreset_GetById_ReturnsIndependentNestedArrays()
        {
            ModePresetRegistry registry = new ModePresetRegistry();
            string id = registry.Save(Mode());
            Mutate(registry.GetById(id).Value);
            AssertOriginal(registry.GetById(id).Value);
        }

        [Test]
        public void ModePreset_GetAll_ReturnsIndependentNestedArrays()
        {
            ModePresetRegistry registry = new ModePresetRegistry();
            string id = registry.Save(Mode());
            Mutate(registry.GetAll()[0]);
            AssertOriginal(registry.GetById(id).Value);
        }

        [Test]
        public void ModePreset_Update_DoesNotExposeStoredArraysToSubscriber()
        {
            ModePresetRegistry registry = new ModePresetRegistry();
            string id = registry.Save(Mode());
            registry.OnModeUpdated += (key, value) => Mutate(value);
            registry.UpdateById(id, Mode());
            AssertOriginal(registry.GetById(id).Value);
        }

        [Test]
        public void ModePreset_NotificationSubscriber_CannotChangeFollowingSubscribersPayload()
        {
            ModePresetRegistry registry = new ModePresetRegistry();
            string id = registry.Save(Mode());
            registry.OnModeUpdated += (key, value) => Mutate(value);
            AIMode observed = default;
            registry.OnModeUpdated += (key, value) => observed = value;
            registry.UpdateById(id, Mode());
            AssertOriginal(observed);
        }

        [Test]
        public void TacticalPreset_Save_CapturesModesTransitionsAndShortcuts()
        {
            using (TacticalPresetRegistry registry = new TacticalPresetRegistry(null))
            {
                CompanionAIConfig source = Config(Mode());
                string id = registry.Save("Tactic", source);
                Mutate(source.modes[0]);
                source.modeTransitionRules[0].conditions[0].operandA = 99;
                source.shortcutModeBindings[0] = 99;
                CompanionAIConfig stored = registry.GetById(id).Value;
                AssertOriginal(stored.modes[0]);
                Assert.AreEqual(10, stored.modeTransitionRules[0].conditions[0].operandA);
                Assert.AreEqual(0, stored.shortcutModeBindings[0]);
            }
        }

        [Test]
        public void TacticalPreset_GetById_DoesNotExposeStoredConfiguration()
        {
            using (TacticalPresetRegistry registry = new TacticalPresetRegistry(null))
            {
                string id = registry.Save("Tactic", Config(Mode()));
                CompanionAIConfig output = registry.GetById(id).Value;
                Mutate(output.modes[0]);
                output.modeTransitionRules[0].conditions[0].operandA = 99;
                output.shortcutModeBindings[0] = 99;
                CompanionAIConfig stored = registry.GetById(id).Value;
                AssertOriginal(stored.modes[0]);
                Assert.AreEqual(10, stored.modeTransitionRules[0].conditions[0].operandA);
                Assert.AreEqual(0, stored.shortcutModeBindings[0]);
            }
        }

        [Test]
        public void TacticalPreset_Cascade_CapturesUpdatedModeForEachTactic()
        {
            ModePresetRegistry modes = new ModePresetRegistry();
            string modeId = modes.Save(Mode());
            using (TacticalPresetRegistry registry = new TacticalPresetRegistry(modes))
            {
                AIMode mode = modes.GetById(modeId).Value;
                string first = registry.Save("First", Config(mode));
                string second = registry.Save("Second", Config(mode));
                modes.UpdateById(modeId, Mode());
                Mutate(registry.GetById(first).Value.modes[0]);
                AssertOriginal(registry.GetById(first).Value.modes[0]);
                AssertOriginal(registry.GetById(second).Value.modes[0]);
                AssertOriginal(modes.GetById(modeId).Value);
            }
        }

        [Test]
        public void ModePreset_ReentrantUpdate_DoesNotRollTacticBackWithOlderNotification()
        {
            ModePresetRegistry modes = new ModePresetRegistry();
            string modeId = modes.Save(Mode());
            bool updating = false;
            modes.OnModeUpdated += (key, value) =>
            {
                if (updating) { return; }
                updating = true;
                modes.UpdateById(key, Mode(20));
            };
            using (TacticalPresetRegistry registry = new TacticalPresetRegistry(modes))
            {
                string tacticId = registry.Save("Tactic", Config(modes.GetById(modeId).Value));
                modes.UpdateById(modeId, Mode());
                Assert.AreEqual(20, modes.GetById(modeId).Value.actions[0].paramId);
                Assert.AreEqual(20, registry.GetById(tacticId).Value.modes[0].actions[0].paramId);
            }
        }

        [Test]
        public void AIEditor_UnsavedNestedEdit_DoesNotChangeCurrentTacticOrSavedPreset()
        {
            ModePresetRegistry modes = new ModePresetRegistry();
            using (TacticalPresetRegistry registry = new TacticalPresetRegistry(modes))
            {
                string id = registry.Save("Saved", Config(Mode()));
                CompanionAISettingsLogic editor = new CompanionAISettingsLogic(modes, registry);
                editor.SwitchEditingTarget(id, true);
                editor.ApplyBufferToCurrentTactic();
                Mutate(editor.EditingBuffer.modes[0]);
                AssertOriginal(editor.CurrentTactic.modes[0]);
                AssertOriginal(registry.GetById(id).Value.modes[0]);
                editor.SwitchEditingTarget(null, true);
                AssertOriginal(editor.EditingBuffer.modes[0]);
            }
        }

        [Test]
        public void AIEditor_UnsavedTransitionEdit_DoesNotChangeCurrentTactic()
        {
            ModePresetRegistry modes = new ModePresetRegistry();
            using (TacticalPresetRegistry registry = new TacticalPresetRegistry(modes))
            {
                CompanionAISettingsLogic editor = new CompanionAISettingsLogic(modes, registry);
                editor.AddModeToBuffer(Mode());
                editor.SetTransitionRulesInBuffer(Config(Mode()).modeTransitionRules);
                editor.ApplyBufferToCurrentTactic();
                editor.EditingBuffer.modeTransitionRules[0].conditions[0].operandA = 99;
                Assert.AreEqual(10, editor.CurrentTactic.modeTransitionRules[0].conditions[0].operandA);
            }
        }
    }
}
