using NUnit.Framework;
using UnityEngine;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class AICore_ModeControllerTests
    {
        [Test]
        public void ModeController_SetModes_InitializesToFirstMode()
        {
            SoACharaDataDic data = new SoACharaDataDic();
            ActionExecutor executor = new ActionExecutor();
            JudgmentLoop loop = new JudgmentLoop(executor, data, 1);
            ModeController controller = new ModeController(loop);

            AIMode[] modes = new AIMode[]
            {
                new AIMode { modeName = "Normal", judgeInterval = new Vector2(1f, 1f) },
                new AIMode { modeName = "Aggressive", judgeInterval = new Vector2(0.5f, 0.5f) }
            };
            controller.SetModes(modes, null);

            Assert.AreEqual(0, controller.CurrentModeIndex);
            Assert.AreEqual("Normal", controller.CurrentMode.modeName);

            data.Dispose();
        }

        [Test]
        public void ModeController_EvaluateTransitions_SwitchesOnCondition()
        {
            SoACharaDataDic data = new SoACharaDataDic();
            data.Add(1, new CharacterVitals { currentHp = 20, maxHp = 100 }, default, default, default);
            data.Add(2, default, default, default, default);

            ActionExecutor executor = new ActionExecutor();
            JudgmentLoop loop = new JudgmentLoop(executor, data, 1);
            ModeController controller = new ModeController(loop);

            AIMode[] modes = new AIMode[]
            {
                new AIMode { modeName = "Normal", judgeInterval = Vector2.one },
                new AIMode { modeName = "Flee", judgeInterval = Vector2.one }
            };
            ModeTransitionRule[] rules = new ModeTransitionRule[]
            {
                new ModeTransitionRule
                {
                    conditions = new AICondition[]
                    {
                        new AICondition { conditionType = AIConditionType.HpRatio, compareOp = CompareOp.Less, operandA = 30, filter = new TargetFilter { includeSelf = true } }
                    },
                    targetModeIndex = 1
                }
            };

            controller.SetModes(modes, rules);
            controller.EvaluateTransitions(1, 2, data, 0f);

            Assert.AreEqual(1, controller.CurrentModeIndex);
            Assert.AreEqual("Flee", controller.CurrentMode.modeName);

            data.Dispose();
        }

        [Test]
        public void ModeController_NoMatchingTransition_StaysCurrentMode()
        {
            SoACharaDataDic data = new SoACharaDataDic();
            data.Add(1, new CharacterVitals { currentHp = 80, maxHp = 100 }, default, default, default);
            data.Add(2, default, default, default, default);

            ActionExecutor executor = new ActionExecutor();
            JudgmentLoop loop = new JudgmentLoop(executor, data, 1);
            ModeController controller = new ModeController(loop);

            AIMode[] modes = new AIMode[]
            {
                new AIMode { modeName = "Normal", judgeInterval = Vector2.one },
                new AIMode { modeName = "Flee", judgeInterval = Vector2.one }
            };
            ModeTransitionRule[] rules = new ModeTransitionRule[]
            {
                new ModeTransitionRule
                {
                    conditions = new AICondition[]
                    {
                        new AICondition { conditionType = AIConditionType.HpRatio, compareOp = CompareOp.Less, operandA = 30, filter = new TargetFilter { includeSelf = true } }
                    },
                    targetModeIndex = 1
                }
            };

            controller.SetModes(modes, rules);
            controller.EvaluateTransitions(1, 2, data, 0f);

            Assert.AreEqual(0, controller.CurrentModeIndex);

            data.Dispose();
        }

        [Test]
        public void ModeController_SwitchMode_FiresEvent()
        {
            SoACharaDataDic data = new SoACharaDataDic();
            ActionExecutor executor = new ActionExecutor();
            JudgmentLoop loop = new JudgmentLoop(executor, data, 1);
            ModeController controller = new ModeController(loop);

            AIMode[] modes = new AIMode[]
            {
                new AIMode { modeName = "A", judgeInterval = Vector2.one },
                new AIMode { modeName = "B", judgeInterval = Vector2.one }
            };
            controller.SetModes(modes, null);

            int firedIndex = -1;
            controller.OnModeChanged += (idx) => firedIndex = idx;
            controller.SwitchMode(1);

            Assert.AreEqual(1, firedIndex);

            data.Dispose();
        }
    }
}
