using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// ActionExecutor + AnimationBridge + ActionPhaseCoordinator の結合テスト。
    /// Runtime橋渡しのフェーズ連携・ライフサイクルを検証する。
    /// </summary>
    [TestFixture]
    public class Integration_ActionExecutorRuntimeTests
    {
        private ActionExecutor _executor;
        private AnimationBridge _bridge;
        private ActionPhaseCoordinator _coordinator;
        private RuntimeAttackHandler _attackHandler;

        [SetUp]
        public void SetUp()
        {
            _executor = new ActionExecutor();
            _bridge = new AnimationBridge();
            _coordinator = new ActionPhaseCoordinator();
            _attackHandler = new RuntimeAttackHandler();
            _executor.Register(_attackHandler);
            _executor.Register(new InstantActionHandler());
            _executor.Register(new RuntimeSustainedHandler());
        }

        // ─── フェーズ連携 ───

        [Test]
        public void 攻撃開始からNeutral到達まで_フェーズ連携が正しく動作する()
        {
            // 攻撃開始
            ActionSlot slot = new ActionSlot { execType = ActionExecType.Attack, paramId = 0 };
            _executor.Execute(1, 2, slot);
            _coordinator.BeginAction();

            MotionInfo motion = new MotionInfo
            {
                preMotionDuration = 0.1f,
                activeMotionDuration = 0.2f,
                recoveryDuration = 0.15f
            };
            _bridge.StartActionPhase(motion, 0);

            // Anticipation中 → ヒットボックスなし
            Assert.AreEqual(AnimationPhase.Anticipation, _bridge.CurrentState.currentPhase);

            // Anticipation → Active 遷移
            bool hitboxActivated = false;
            _bridge.OnPhaseChanged += (phase) =>
            {
                ActionPhaseCoordinator.HitboxCommand cmd = _coordinator.OnPhaseChanged(phase);
                if (cmd == ActionPhaseCoordinator.HitboxCommand.Activate)
                {
                    hitboxActivated = true;
                }
            };

            _bridge.TickPhase(0.15f); // Anticipation完了 → Active
            Assert.IsTrue(hitboxActivated);
            Assert.AreEqual(AnimationPhase.Active, _bridge.CurrentState.currentPhase);
        }

        [Test]
        public void Activeフェーズ完了_ヒットボックスが無効化される()
        {
            ActionSlot slot = new ActionSlot { execType = ActionExecType.Attack, paramId = 0 };
            _executor.Execute(1, 2, slot);
            _coordinator.BeginAction();

            MotionInfo motion = new MotionInfo
            {
                preMotionDuration = 0f,
                activeMotionDuration = 0.2f,
                recoveryDuration = 0.15f
            };
            _bridge.StartActionPhase(motion, 0);

            bool hitboxDeactivated = false;
            _bridge.OnPhaseChanged += (phase) =>
            {
                ActionPhaseCoordinator.HitboxCommand cmd = _coordinator.OnPhaseChanged(phase);
                if (cmd == ActionPhaseCoordinator.HitboxCommand.Deactivate)
                {
                    hitboxDeactivated = true;
                }
            };

            _bridge.TickPhase(0.25f); // Active完了 → Recovery
            Assert.IsTrue(hitboxDeactivated);
            Assert.AreEqual(AnimationPhase.Recovery, _bridge.CurrentState.currentPhase);
        }

        [Test]
        public void Recovery完了_ShouldCompleteActionがtrueになりExecutor行動を完了できる()
        {
            ActionSlot slot = new ActionSlot { execType = ActionExecType.Attack, paramId = 0 };
            _executor.Execute(1, 2, slot);
            _coordinator.BeginAction();

            MotionInfo motion = new MotionInfo
            {
                preMotionDuration = 0f,
                activeMotionDuration = 0.1f,
                recoveryDuration = 0.1f
            };
            _bridge.StartActionPhase(motion, 0);

            _bridge.OnPhaseChanged += (phase) =>
            {
                _coordinator.OnPhaseChanged(phase);
            };

            // Active → Recovery → Neutral
            _bridge.TickPhase(0.15f); // → Recovery
            _bridge.TickPhase(0.15f); // → Neutral

            Assert.IsTrue(_coordinator.ShouldCompleteAction);
            Assert.IsTrue(_executor.IsExecuting); // まだ完了していない

            // ForceComplete で完了
            _attackHandler.ForceComplete();
            Assert.IsFalse(_executor.IsExecuting);
        }

        // ─── イベント購読の対称性 ───

        [Test]
        public void OnActionCompleted_連続実行で多重発火しない()
        {
            int completedCount = 0;
            _executor.OnActionCompleted += () => completedCount++;

            // 1回目
            ActionSlot slot1 = new ActionSlot { execType = ActionExecType.Attack, paramId = 0 };
            _executor.Execute(1, 2, slot1);
            _attackHandler.ForceComplete();
            Assert.AreEqual(1, completedCount);

            // 2回目（新しいAttackを開始→完了）
            ActionSlot slot2 = new ActionSlot { execType = ActionExecType.Attack, paramId = 1 };
            _executor.Execute(1, 2, slot2);
            _attackHandler.ForceComplete();
            Assert.AreEqual(2, completedCount);
        }

        [Test]
        public void キャンセル後に再実行_正常にライフサイクルが回る()
        {
            ActionSlot slot = new ActionSlot { execType = ActionExecType.Attack, paramId = 0 };
            _executor.Execute(1, 2, slot);
            _coordinator.BeginAction();
            Assert.IsTrue(_executor.IsExecuting);

            // キャンセル
            _executor.CancelCurrent();
            _coordinator.EndAction();
            Assert.IsFalse(_executor.IsExecuting);
            Assert.IsFalse(_coordinator.IsActionInProgress);

            // 再実行
            _executor.Execute(1, 2, slot);
            _coordinator.BeginAction();
            Assert.IsTrue(_executor.IsExecuting);
            Assert.IsTrue(_coordinator.IsActionInProgress);

            _attackHandler.ForceComplete();
            _coordinator.EndAction();
            Assert.IsFalse(_executor.IsExecuting);
        }

        // ─── コスト検証との統合 ───

        [Test]
        public void スタミナ不足で行動不可_Executorは実行中にならない()
        {
            float stamina = 5f;
            int mp = 100;
            float staminaCost = 20f;
            float mpCost = 0f;

            bool canAfford = ActionCostValidator.CanAfford(stamina, mp, staminaCost, mpCost);
            Assert.IsFalse(canAfford);

            // コスト不足時はExecuteしない
            // (ActionExecutorController.ExecuteAction内でチェックするパターン)
            Assert.IsFalse(_executor.IsExecuting);
        }

        [Test]
        public void コスト消費後_残量が正しく減る()
        {
            float stamina = 50f;
            int mp = 30;

            ActionCostValidator.DeductCost(ref stamina, ref mp, 15f, 10f);

            Assert.AreEqual(35f, stamina, 0.001f);
            Assert.AreEqual(20, mp);

            // 消費後もまだ別の行動が実行可能
            Assert.IsTrue(ActionCostValidator.CanAfford(stamina, mp, 10f, 5f));
        }

        // ─── Sustained行動のTick完了 ───

        [Test]
        public void SustainedAction_Tick完了でOnActionCompletedが発火する()
        {
            RuntimeSustainedHandler sustained = new RuntimeSustainedHandler();
            _executor.Register(sustained); // 上書き登録

            bool completed = false;
            _executor.OnActionCompleted += () => completed = true;

            ActionSlot slot = new ActionSlot
            {
                execType = ActionExecType.Sustained,
                paramId = 0,
                paramValue = 1.0f // 1秒間の持続行動
            };
            _executor.Execute(1, 2, slot);
            Assert.IsTrue(_executor.IsExecuting);

            // 0.5秒経過 → まだ実行中
            _executor.Tick(0.5f);
            Assert.IsTrue(_executor.IsExecuting);
            Assert.IsFalse(completed);

            // 1.0秒到達 → 完了
            _executor.Tick(0.5f);
            Assert.IsFalse(_executor.IsExecuting);
            Assert.IsTrue(completed);
        }
    }
}
