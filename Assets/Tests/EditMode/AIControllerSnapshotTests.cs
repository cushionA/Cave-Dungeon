using System;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>実Controllerを使用する。Unity依存のSoA型は未使用(null)だがUnityでの型解決が必要。</summary>
    public class AIControllerSnapshotTests
    {
        private sealed class Probe : ActionBase
        {
            public override ActionExecType ExecType => ActionExecType.Attack;
            public int LastParameter { get; private set; }
            protected override void OnExecute(int ownerHash, int targetHash, ActionSlot slot)
            {
                LastParameter = slot.paramId;
            }
        }

        private static AIMode Mode(int parameter)
        {
            return new AIMode
            {
                actions = new[] { new ActionSlot { execType = ActionExecType.Attack, paramId = parameter } },
                actionRules = new[] { new AIRule { conditions = Array.Empty<AICondition>(), actionIndex = 0 } },
                defaultActionIndex = 0,
            };
        }

        [Test]
        public void ModeController_SetModes_DetachesNestedInputForLaterSwitch()
        {
            ActionExecutor executor = new ActionExecutor();
            JudgmentLoop loop = new JudgmentLoop(executor, null, 1);
            ModeController controller = new ModeController(loop);
            AIMode[] input = { Mode(10), Mode(20) };
            controller.SetModes(input, null);
            input[1].actions[0].paramId = 99;

            controller.SwitchMode(1);

            Assert.AreEqual(20, controller.CurrentMode.actions[0].paramId);
        }

        [Test]
        public void ModeController_CurrentMode_DoesNotExposeOwnedArrays()
        {
            JudgmentLoop loop = new JudgmentLoop(new ActionExecutor(), null, 1);
            ModeController controller = new ModeController(loop);
            controller.SetModes(new[] { Mode(10) }, null);
            AIMode output = controller.CurrentMode;
            output.actions[0].paramId = 99;
            output.actionRules[0].actionIndex = 99;

            Assert.AreEqual(10, controller.CurrentMode.actions[0].paramId);
            Assert.AreEqual(0, controller.CurrentMode.actionRules[0].actionIndex);
        }

        [Test]
        public void ModeController_SetModes_CapturesTransitionConditions()
        {
            JudgmentLoop loop = new JudgmentLoop(new ActionExecutor(), null, 1);
            ModeController controller = new ModeController(loop);
            ModeTransitionRule[] input =
            {
                new ModeTransitionRule { sourceModeIndex = 0, targetModeIndex = 1, conditions = Array.Empty<AICondition>() },
            };
            controller.SetModes(new[] { Mode(10), Mode(20) }, input);
            input[0].targetModeIndex = 0;
            AIObservationContext observation = new AIObservationContext();
            observation.BeginSnapshot(1, 0f);

            controller.EvaluateTransitions(1, 0, observation);

            Assert.AreEqual(1, controller.CurrentModeIndex);
        }

        [Test]
        public void JudgmentLoop_PublicSetMode_CapturesInputBeforeEvaluation()
        {
            ActionExecutor executor = new ActionExecutor();
            Probe probe = new Probe();
            executor.Register(probe);
            JudgmentLoop loop = new JudgmentLoop(executor, null, 1);
            AIMode source = Mode(10);
            loop.SetMode(source);
            source.actions[0].paramId = 99;

            loop.EvaluateAction(0f);

            Assert.AreEqual(10, probe.LastParameter);
        }

        [Test]
        public void ModeController_ClearModes_CancelsOldActionAndClearsJudgmentConfiguration()
        {
            ActionExecutor executor = new ActionExecutor();
            Probe probe = new Probe();
            executor.Register(probe);
            JudgmentLoop loop = new JudgmentLoop(executor, null, 1);
            ModeController controller = new ModeController(loop);
            controller.SetModes(new[] { Mode(10) }, null);
            loop.EvaluateAction(0f);
            Assert.IsTrue(executor.IsExecuting);

            controller.SetModes(Array.Empty<AIMode>(), null);
            loop.EvaluateAction(0f);

            Assert.IsFalse(executor.IsExecuting);
            Assert.IsNull(controller.CurrentMode.actions);
        }
    }
}
