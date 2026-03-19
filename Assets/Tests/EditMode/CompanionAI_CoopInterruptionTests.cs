using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class CompanionAI_CoopInterruptionTests
    {
        [Test]
        public void CoopInterruption_Interrupt_SavesState()
        {
            CoopInterruptionHandler handler = new CoopInterruptionHandler();
            ActionSlot slot = new ActionSlot { execType = ActionExecType.Attack, paramId = 5 };

            bool result = handler.InterruptForCoop(slot, 100);

            Assert.IsTrue(result);
            Assert.IsTrue(handler.IsInterrupted);
        }

        [Test]
        public void CoopInterruption_Resume_RestoresState()
        {
            CoopInterruptionHandler handler = new CoopInterruptionHandler();
            ActionSlot slot = new ActionSlot { execType = ActionExecType.Cast, paramId = 3 };
            handler.InterruptForCoop(slot, 200);

            (ActionSlot s, int t)? result = handler.ResumeFromCoop();

            Assert.IsNotNull(result);
            Assert.AreEqual(ActionExecType.Cast, result.Value.s.execType);
            Assert.AreEqual(200, result.Value.t);
            Assert.IsFalse(handler.IsInterrupted);
        }

        [Test]
        public void CoopInterruption_DoubleInterrupt_Rejected()
        {
            CoopInterruptionHandler handler = new CoopInterruptionHandler();
            handler.InterruptForCoop(new ActionSlot(), 100);

            bool result = handler.InterruptForCoop(new ActionSlot(), 200);

            Assert.IsFalse(result);
        }

        [Test]
        public void CoopInterruption_ForceResume_ClearsState()
        {
            CoopInterruptionHandler handler = new CoopInterruptionHandler();
            handler.InterruptForCoop(new ActionSlot(), 100);

            handler.ForceResume();

            Assert.IsFalse(handler.IsInterrupted);
        }
    }
}
