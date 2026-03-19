using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class ElementalGate_MultiHitTests
    {
        [Test]
        public void ElementalGateLogic_MultiHit_RequiresMultipleHits()
        {
            ElementalGateLogic gate = new ElementalGateLogic(
                ElementalRequirement.Strike, 0f, true, 3);

            Assert.IsFalse(gate.OnElementalHit(Element.Strike, 10f)); // 1/3
            Assert.IsFalse(gate.OnElementalHit(Element.Strike, 10f)); // 2/3
            Assert.IsTrue(gate.OnElementalHit(Element.Strike, 10f));  // 3/3 → 開放
        }

        [Test]
        public void ElementalGateLogic_MultiHit_WrongElement_DoesNotCount()
        {
            ElementalGateLogic gate = new ElementalGateLogic(
                ElementalRequirement.Fire, 0f, true, 2);

            gate.OnElementalHit(Element.Thunder, 10f); // 不一致
            Assert.AreEqual(0, gate.CurrentHitCount);

            gate.OnElementalHit(Element.Fire, 10f); // 一致 1/2
            Assert.AreEqual(1, gate.CurrentHitCount);
        }

        [Test]
        public void ElementalGateLogic_ResetHitCount_ClearsProgress()
        {
            ElementalGateLogic gate = new ElementalGateLogic(
                ElementalRequirement.Fire, 0f, true, 3);

            gate.OnElementalHit(Element.Fire, 10f); // 1/3
            gate.OnElementalHit(Element.Fire, 10f); // 2/3

            gate.ResetHitCount();

            Assert.AreEqual(0, gate.CurrentHitCount);
            Assert.IsFalse(gate.IsOpened);
        }
    }
}
