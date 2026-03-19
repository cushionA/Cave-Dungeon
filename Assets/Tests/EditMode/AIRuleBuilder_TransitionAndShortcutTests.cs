using NUnit.Framework;
using UnityEngine;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class AIRuleBuilder_TransitionAndShortcutTests
    {
        private ModeController CreateModeController()
        {
            SoACharaDataDic data = new SoACharaDataDic();
            ActionExecutor executor = new ActionExecutor();
            JudgmentLoop loop = new JudgmentLoop(executor, data, 1);
            ModeController controller = new ModeController(loop);
            controller.SetModes(new AIMode[]
            {
                new AIMode { modeName = "Normal", judgeInterval = Vector2.one },
                new AIMode { modeName = "Combat", judgeInterval = Vector2.one },
                new AIMode { modeName = "Support", judgeInterval = Vector2.one }
            }, null);
            return controller;
        }

        [Test]
        public void TransitionEditor_ShortcutActivate_SwitchesMode()
        {
            ModeController mc = CreateModeController();
            ModeTransitionEditor editor = new ModeTransitionEditor(mc);
            editor.SetShortcutBinding(0, 1);

            editor.ActivateShortcut(0);

            Assert.AreEqual(1, mc.CurrentModeIndex);
            Assert.IsTrue(editor.IsOverriding);
        }

        [Test]
        public void TransitionEditor_OverrideTimeout_Reverts()
        {
            ModeController mc = CreateModeController();
            ModeTransitionEditor editor = new ModeTransitionEditor(mc, 5f);
            editor.SetShortcutBinding(0, 2);

            editor.ActivateShortcut(0);
            Assert.AreEqual(2, mc.CurrentModeIndex);

            editor.Tick(6f);
            Assert.IsFalse(editor.IsOverriding);
            Assert.AreEqual(0, mc.CurrentModeIndex);
        }

        [Test]
        public void TransitionEditor_NoOverride_NoRevert()
        {
            ModeController mc = CreateModeController();
            ModeTransitionEditor editor = new ModeTransitionEditor(mc);

            editor.Tick(100f);

            Assert.IsFalse(editor.IsOverriding);
            Assert.AreEqual(0, mc.CurrentModeIndex);
        }

        [Test]
        public void TransitionEditor_MultipleShortcuts_LastWins()
        {
            ModeController mc = CreateModeController();
            ModeTransitionEditor editor = new ModeTransitionEditor(mc, 10f);
            editor.SetShortcutBinding(0, 1);
            editor.SetShortcutBinding(1, 2);

            editor.ActivateShortcut(0);
            editor.ActivateShortcut(1);

            Assert.AreEqual(2, mc.CurrentModeIndex);
        }
    }
}
