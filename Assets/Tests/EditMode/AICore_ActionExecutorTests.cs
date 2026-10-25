using System;
using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class AICore_ActionExecutorTests
    {
        private sealed class ExecutionBackend : IActionExecutionBackend
        {
            public bool IsExecuting { get; private set; }
            public bool accepted = true;
            public int owner;
            public int target;
            public ActionSlot slot;
            public int cancelCount;
            public int tickCount;
            public event Action OnActionCompleted;

            public bool Execute(int ownerHash, int targetHash, ActionSlot action)
            {
                owner = ownerHash;
                target = targetHash;
                slot = action;
                if (accepted) { IsExecuting = true; }
                return accepted;
            }

            public void CancelCurrent()
            {
                cancelCount++;
                IsExecuting = false;
            }

            public void Tick(float deltaTime) { tickCount++; }

            public void Complete()
            {
                IsExecuting = false;
                OnActionCompleted?.Invoke();
            }
        }

        [Test]
        public void ActionExecutor_SharedBackend_DispatchesInstantBeforeLocalCompletion()
        {
            ActionExecutor executor = new ActionExecutor();
            executor.Register(new InstantActionHandler());
            ExecutionBackend backend = new ExecutionBackend();
            executor.SetExecutionBackend(backend);
            int completed = 0;
            executor.OnActionCompleted += () => completed++;
            ActionSlot slot = new ActionSlot { execType = ActionExecType.Instant, paramId = (int)InstantAction.Dodge, paramValue = 0.75f };

            Assert.IsTrue(executor.Execute(12, 34, slot));

            Assert.AreEqual(12, backend.owner);
            Assert.AreEqual(34, backend.target);
            Assert.AreEqual(slot.paramId, backend.slot.paramId);
            Assert.AreEqual(0.75f, backend.slot.paramValue);
            Assert.IsTrue(executor.IsExecuting);
            Assert.IsNull(executor.CurrentAction);
            Assert.AreEqual(0, completed);
            backend.Complete();
            Assert.IsFalse(executor.IsExecuting);
            Assert.AreEqual(1, completed);
        }

        [Test]
        public void ActionExecutor_SharedBackend_RejectedRequestPreservesCurrentExecution()
        {
            ActionExecutor executor = new ActionExecutor();
            ExecutionBackend backend = new ExecutionBackend();
            executor.SetExecutionBackend(backend);
            executor.Execute(1, 2, new ActionSlot { execType = ActionExecType.Attack });
            backend.accepted = false;

            Assert.IsFalse(executor.Execute(1, 3, new ActionSlot { execType = ActionExecType.Cast }));

            Assert.IsTrue(executor.IsExecuting);
            Assert.AreEqual(0, backend.cancelCount);
        }

        [Test]
        public void ActionExecutor_SharedBackend_TickUsesSharedLifetimeAndCancelDelegates()
        {
            ActionExecutor executor = new ActionExecutor();
            executor.Register(new SustainedActionHandler());
            ExecutionBackend backend = new ExecutionBackend();
            executor.SetExecutionBackend(backend);
            executor.Execute(1, 2, new ActionSlot { execType = ActionExecType.Sustained, paramValue = 0.1f });

            executor.Tick(10f);
            Assert.IsTrue(executor.IsExecuting);
            Assert.AreEqual(1, backend.tickCount);
            executor.CancelCurrent();

            Assert.IsFalse(executor.IsExecuting);
            Assert.AreEqual(1, backend.cancelCount);
        }

        [Test]
        public void ActionExecutor_SharedBackend_RebindingRemovesOldCompletionSubscription()
        {
            ActionExecutor executor = new ActionExecutor();
            ExecutionBackend oldBackend = new ExecutionBackend();
            ExecutionBackend newBackend = new ExecutionBackend();
            executor.SetExecutionBackend(oldBackend);
            int completed = 0;
            executor.OnActionCompleted += () => completed++;
            executor.SetExecutionBackend(newBackend);
            executor.SetExecutionBackend(newBackend);

            oldBackend.Complete();
            Assert.AreEqual(0, completed);
            newBackend.Complete();
            Assert.AreEqual(1, completed);
        }

        [Test]
        public void ActionExecutor_Register_HandlerAvailable()
        {
            ActionExecutor executor = new ActionExecutor();
            executor.Register(new AttackActionHandler());

            Assert.IsTrue(executor.HasHandler(ActionExecType.Attack));
            Assert.IsFalse(executor.HasHandler(ActionExecType.Cast));
        }

        [Test]
        public void ActionExecutor_Execute_DispatchesToCorrectHandler()
        {
            ActionExecutor executor = new ActionExecutor();
            AttackActionHandler attack = new AttackActionHandler();
            executor.Register(attack);

            ActionSlot slot = new ActionSlot { execType = ActionExecType.Attack, paramId = 7 };
            bool result = executor.Execute(1, 2, slot);

            Assert.IsTrue(result);
            Assert.IsTrue(executor.IsExecuting);
            Assert.AreEqual(7, attack.LastParamId);
        }

        [Test]
        public void ActionExecutor_Execute_UnregisteredType_ReturnsFalse()
        {
            ActionExecutor executor = new ActionExecutor();

            ActionSlot slot = new ActionSlot { execType = ActionExecType.Cast };
            bool result = executor.Execute(1, 2, slot);

            Assert.IsFalse(result);
        }

        [Test]
        public void ActionExecutor_Execute_CancelsPreviousAction()
        {
            ActionExecutor executor = new ActionExecutor();
            AttackActionHandler attack = new AttackActionHandler();
            CastActionHandler cast = new CastActionHandler();
            executor.Register(attack);
            executor.Register(cast);

            executor.Execute(1, 2, new ActionSlot { execType = ActionExecType.Attack });
            Assert.IsTrue(attack.IsExecuting);

            executor.Execute(1, 2, new ActionSlot { execType = ActionExecType.Cast });
            Assert.IsFalse(attack.IsExecuting);
            Assert.IsTrue(cast.IsExecuting);
        }

        [Test]
        public void ActionExecutor_OnCompleted_FiresOnInstantComplete()
        {
            ActionExecutor executor = new ActionExecutor();
            executor.Register(new InstantActionHandler());
            bool completed = false;
            executor.OnActionCompleted += () => completed = true;

            executor.Execute(1, 2, new ActionSlot { execType = ActionExecType.Instant });

            Assert.IsTrue(completed);
            Assert.IsFalse(executor.IsExecuting);
        }
    }
}
