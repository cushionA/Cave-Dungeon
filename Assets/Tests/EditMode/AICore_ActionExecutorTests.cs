using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class AICore_ActionExecutorTests
    {
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
