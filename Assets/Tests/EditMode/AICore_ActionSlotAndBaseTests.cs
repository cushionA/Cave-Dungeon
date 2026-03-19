using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class AICore_ActionSlotAndBaseTests
    {
        [Test]
        public void ActionSlot_Create_SetsExecTypeCorrectly()
        {
            ActionSlot slot = new ActionSlot
            {
                execType = ActionExecType.Cast,
                paramId = 5,
                paramValue = 2.0f,
                displayName = "Fireball"
            };

            Assert.AreEqual(ActionExecType.Cast, slot.execType);
            Assert.AreEqual(5, slot.paramId);
            Assert.AreEqual(2.0f, slot.paramValue, 0.001f);
            Assert.AreEqual("Fireball", slot.displayName);
        }

        [Test]
        public void AttackAction_Execute_SetsStateAndParams()
        {
            AttackActionHandler action = new AttackActionHandler();
            ActionSlot slot = new ActionSlot { execType = ActionExecType.Attack, paramId = 3 };

            action.Execute(100, 200, slot);

            Assert.IsTrue(action.IsExecuting);
            Assert.AreEqual(100, action.LastOwnerHash);
            Assert.AreEqual(200, action.LastTargetHash);
            Assert.AreEqual(3, action.LastParamId);
        }

        [Test]
        public void InstantAction_Execute_CompletesImmediately()
        {
            InstantActionHandler action = new InstantActionHandler();
            ActionSlot slot = new ActionSlot { execType = ActionExecType.Instant, paramId = 1 };
            bool completed = false;
            action.OnCompleted += () => completed = true;

            action.Execute(100, 200, slot);

            Assert.IsFalse(action.IsExecuting);
            Assert.IsTrue(completed);
        }

        [Test]
        public void SustainedAction_Tick_CompletesAfterDuration()
        {
            SustainedActionHandler action = new SustainedActionHandler();
            ActionSlot slot = new ActionSlot
            {
                execType = ActionExecType.Sustained,
                paramId = 2,
                paramValue = 1.0f
            };

            action.Execute(100, 200, slot);
            Assert.IsTrue(action.IsExecuting);

            action.Tick(0.5f);
            Assert.IsTrue(action.IsExecuting);

            action.Tick(0.6f);
            Assert.IsFalse(action.IsExecuting);
        }

        [Test]
        public void ActionBase_Cancel_StopsExecution()
        {
            AttackActionHandler action = new AttackActionHandler();
            ActionSlot slot = new ActionSlot { execType = ActionExecType.Attack, paramId = 0 };

            action.Execute(100, 200, slot);
            Assert.IsTrue(action.IsExecuting);

            action.Cancel();
            Assert.IsFalse(action.IsExecuting);
        }
    }
}
