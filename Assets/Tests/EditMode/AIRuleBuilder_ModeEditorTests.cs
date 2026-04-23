using NUnit.Framework;
using UnityEngine;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class AIRuleBuilder_ModeEditorTests
    {
        [Test]
        public void ModeEditor_AddMode_Succeeds()
        {
            ActionTypeRegistry registry = new ActionTypeRegistry();
            RuleEditorLogic editor = new RuleEditorLogic(registry);

            bool result = editor.AddMode(new AIMode { modeName = "Combat", judgeInterval = Vector2.one });

            Assert.IsTrue(result);
            Assert.AreEqual(1, editor.ModeCount);
        }

        [Test]
        public void ModeEditor_MaxModes_RejectsExcess()
        {
            ActionTypeRegistry registry = new ActionTypeRegistry();
            RuleEditorLogic editor = new RuleEditorLogic(registry);

            editor.AddMode(new AIMode { modeName = "A" });
            editor.AddMode(new AIMode { modeName = "B" });
            editor.AddMode(new AIMode { modeName = "C" });
            editor.AddMode(new AIMode { modeName = "D" });
            bool result = editor.AddMode(new AIMode { modeName = "E" });

            Assert.IsFalse(result);
            Assert.AreEqual(4, editor.ModeCount);
        }

        [Test]
        public void ModeEditor_RemoveMode_Succeeds()
        {
            ActionTypeRegistry registry = new ActionTypeRegistry();
            RuleEditorLogic editor = new RuleEditorLogic(registry);

            editor.AddMode(new AIMode { modeName = "A" });
            editor.AddMode(new AIMode { modeName = "B" });
            bool result = editor.RemoveMode(1);

            Assert.IsTrue(result);
            Assert.AreEqual(1, editor.ModeCount);
        }

        [Test]
        public void ModeEditor_BuildConfig_ReturnsValidConfig()
        {
            ActionTypeRegistry registry = new ActionTypeRegistry();
            RuleEditorLogic editor = new RuleEditorLogic(registry);
            editor.AddMode(new AIMode { modeName = "Combat", judgeInterval = Vector2.one });

            CompanionAIConfig config = editor.BuildConfig();

            Assert.AreEqual(1, config.modes.Length);
            Assert.AreEqual("Combat", config.modes[0].modeName);
        }

        [Test]
        public void ModeEditor_IsActionAvailable_ChecksRegistry()
        {
            ActionTypeRegistry registry = new ActionTypeRegistry();
            RuleEditorLogic editor = new RuleEditorLogic(registry);

            ActionSlot available = new ActionSlot { execType = ActionExecType.Attack, paramId = 0 };
            ActionSlot unavailable = new ActionSlot { execType = ActionExecType.Broadcast, paramId = 99 };

            Assert.IsTrue(editor.IsActionAvailable(available));
            Assert.IsFalse(editor.IsActionAvailable(unavailable));
        }

        /// <summary>
        /// BuildConfig は manualOverrideTimeoutSeconds を 0 初期化して返し、
        /// 呼び出し元が上書きしない場合は CompanionController 側のデフォルトフォールバックに委ねる契約を検証する。
        /// </summary>
        [Test]
        public void ModeEditor_BuildConfig_InitializesManualOverrideTimeoutToZero()
        {
            ActionTypeRegistry registry = new ActionTypeRegistry();
            RuleEditorLogic editor = new RuleEditorLogic(registry);
            editor.AddMode(new AIMode { modeName = "Combat", judgeInterval = Vector2.one });

            CompanionAIConfig config = editor.BuildConfig();

            Assert.AreEqual(0f, config.manualOverrideTimeoutSeconds,
                "BuildConfig は manualOverrideTimeoutSeconds を 0（未指定）で返し、Controller 側のフォールバックに委ねる");
        }
    }
}
