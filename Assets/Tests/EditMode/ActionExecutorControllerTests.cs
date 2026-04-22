using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// ActionExecutorController の純ロジック部分のテスト。
    /// ActionBase.ForceComplete と ActionPhaseCoordinator を検証する。
    /// </summary>
    [TestFixture]
    public class ActionExecutorControllerTests
    {
        // ─── ActionBase.ForceComplete ───

        [Test]
        public void ActionBase_ForceComplete_行動が完了しOnCompletedが発火する()
        {
            AttackActionHandler handler = new AttackActionHandler();
            bool completed = false;
            handler.OnCompleted += () => completed = true;

            handler.Execute(1, 2, new ActionSlot { execType = ActionExecType.Attack, paramId = 0 });
            Assert.IsTrue(handler.IsExecuting);

            handler.ForceComplete();
            Assert.IsFalse(handler.IsExecuting);
            Assert.IsTrue(completed);
        }

        [Test]
        public void ActionBase_ForceComplete_CastHandler_行動が完了する()
        {
            CastActionHandler handler = new CastActionHandler();
            bool completed = false;
            handler.OnCompleted += () => completed = true;

            handler.Execute(1, 2, new ActionSlot { execType = ActionExecType.Cast, paramId = 0 });
            handler.ForceComplete();

            Assert.IsFalse(handler.IsExecuting);
            Assert.IsTrue(completed);
        }

        [Test]
        public void ActionBase_ForceComplete_SustainedHandler_行動が完了する()
        {
            SustainedActionHandler handler = new SustainedActionHandler();
            bool completed = false;
            handler.OnCompleted += () => completed = true;

            handler.Execute(1, 2, new ActionSlot
            {
                execType = ActionExecType.Sustained,
                paramId = 0,
                paramValue = 0f
            });
            handler.ForceComplete();

            Assert.IsFalse(handler.IsExecuting);
            Assert.IsTrue(completed);
        }

        [Test]
        public void ActionBase_ForceComplete_実行中でない場合_何もしない()
        {
            AttackActionHandler handler = new AttackActionHandler();
            bool completed = false;
            handler.OnCompleted += () => completed = true;

            handler.ForceComplete();

            Assert.IsFalse(completed);
        }

        // ─── ActionExecutor + ForceComplete 統合 ───

        [Test]
        public void ActionExecutor_ForceCompleteでOnActionCompletedが発火()
        {
            ActionExecutor executor = new ActionExecutor();
            AttackActionHandler handler = new AttackActionHandler();
            executor.Register(handler);

            bool completed = false;
            executor.OnActionCompleted += () => completed = true;

            executor.Execute(1, 2, new ActionSlot { execType = ActionExecType.Attack, paramId = 0 });
            Assert.IsTrue(executor.IsExecuting);

            handler.ForceComplete();
            Assert.IsFalse(executor.IsExecuting);
            Assert.IsTrue(completed);
        }

        // ─── ActionPhaseCoordinator ───

        [Test]
        public void ActionPhaseCoordinator_Activeフェーズ_ActivateHitboxを返す()
        {
            ActionPhaseCoordinator coordinator = new ActionPhaseCoordinator();
            coordinator.BeginAction();

            ActionPhaseCoordinator.HitboxCommand cmd = coordinator.OnPhaseChanged(AnimationPhase.Active);
            Assert.AreEqual(ActionPhaseCoordinator.HitboxCommand.Activate, cmd);
        }

        [Test]
        public void ActionPhaseCoordinator_Recoveryフェーズ_DeactivateHitboxを返す()
        {
            ActionPhaseCoordinator coordinator = new ActionPhaseCoordinator();
            coordinator.BeginAction();

            ActionPhaseCoordinator.HitboxCommand cmd = coordinator.OnPhaseChanged(AnimationPhase.Recovery);
            Assert.AreEqual(ActionPhaseCoordinator.HitboxCommand.Deactivate, cmd);
        }

        [Test]
        public void ActionPhaseCoordinator_Neutralフェーズ_DeactivateHitboxを返す()
        {
            ActionPhaseCoordinator coordinator = new ActionPhaseCoordinator();
            coordinator.BeginAction();

            ActionPhaseCoordinator.HitboxCommand cmd = coordinator.OnPhaseChanged(AnimationPhase.Neutral);
            Assert.AreEqual(ActionPhaseCoordinator.HitboxCommand.Deactivate, cmd);
        }

        [Test]
        public void ActionPhaseCoordinator_行動中でない場合_Noneを返す()
        {
            ActionPhaseCoordinator coordinator = new ActionPhaseCoordinator();

            ActionPhaseCoordinator.HitboxCommand cmd = coordinator.OnPhaseChanged(AnimationPhase.Active);
            Assert.AreEqual(ActionPhaseCoordinator.HitboxCommand.None, cmd);
        }

        [Test]
        public void ActionPhaseCoordinator_NeutralでShouldCompleteActionがtrueになる()
        {
            ActionPhaseCoordinator coordinator = new ActionPhaseCoordinator();
            coordinator.BeginAction();

            ActionPhaseCoordinator.HitboxCommand cmd = coordinator.OnPhaseChanged(AnimationPhase.Neutral);
            Assert.IsTrue(coordinator.ShouldCompleteAction);
        }

        [Test]
        public void ActionPhaseCoordinator_EndAction後_行動中でなくなる()
        {
            ActionPhaseCoordinator coordinator = new ActionPhaseCoordinator();
            coordinator.BeginAction();
            coordinator.EndAction();

            Assert.IsFalse(coordinator.IsActionInProgress);
        }

        [Test]
        public void ActionPhaseCoordinator_Anticipationフェーズ_Noneを返す()
        {
            ActionPhaseCoordinator coordinator = new ActionPhaseCoordinator();
            coordinator.BeginAction();

            ActionPhaseCoordinator.HitboxCommand cmd = coordinator.OnPhaseChanged(AnimationPhase.Anticipation);
            Assert.AreEqual(ActionPhaseCoordinator.HitboxCommand.None, cmd);
        }

        // ─── コスト検証 ───

        [Test]
        public void ActionCostValidator_スタミナ不足_実行不可()
        {
            bool canExecute = ActionCostValidator.CanAfford(
                currentStamina: 5f, currentMp: 100,
                staminaCost: 10f, mpCost: 0);
            Assert.IsFalse(canExecute);
        }

        [Test]
        public void ActionCostValidator_MP不足_実行不可()
        {
            bool canExecute = ActionCostValidator.CanAfford(
                currentStamina: 100f, currentMp: 5,
                staminaCost: 0f, mpCost: 10);
            Assert.IsFalse(canExecute);
        }

        [Test]
        public void ActionCostValidator_コスト十分_実行可能()
        {
            bool canExecute = ActionCostValidator.CanAfford(
                currentStamina: 50f, currentMp: 50,
                staminaCost: 10f, mpCost: 20);
            Assert.IsTrue(canExecute);
        }

        [Test]
        public void ActionCostValidator_コストゼロ_常に実行可能()
        {
            bool canExecute = ActionCostValidator.CanAfford(
                currentStamina: 0f, currentMp: 0,
                staminaCost: 0f, mpCost: 0);
            Assert.IsTrue(canExecute);
        }

        [Test]
        public void ActionCostValidator_DeductCost_スタミナとMPを消費する()
        {
            float stamina = 50f;
            int mp = 30;
            ActionCostValidator.DeductCost(ref stamina, ref mp, 10f, 5);

            Assert.AreEqual(40f, stamina, 0.001f);
            Assert.AreEqual(25, mp);
        }

        // ─── int 型一本化後の境界値 ───

        [Test]
        public void ActionCostValidator_CanAfford_MP等しい_実行可能()
        {
            bool canExecute = ActionCostValidator.CanAfford(
                currentStamina: 100f, currentMp: 10,
                staminaCost: 0f, mpCost: 10);
            Assert.IsTrue(canExecute, "currentMp == mpCost で実行可能 (>= 判定)");
        }

        [Test]
        public void ActionCostValidator_DeductCost_MPジャスト消費_残高ゼロ()
        {
            float stamina = 20f;
            int mp = 10;
            ActionCostValidator.DeductCost(ref stamina, ref mp, 0f, 10);
            Assert.AreEqual(0, mp, "MP ぴったり消費で残高 0");
        }

        [Test]
        public void ActionCostValidator_DeductCost_MP不足時はゼロにクランプ()
        {
            float stamina = 20f;
            int mp = 5;
            ActionCostValidator.DeductCost(ref stamina, ref mp, 0f, 100);
            Assert.AreEqual(0, mp, "超過消費時は 0 にクランプ (負値にならない)");
        }
    }
}
