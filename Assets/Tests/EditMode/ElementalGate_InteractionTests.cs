using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class ElementalGate_InteractionTests
    {
        [Test]
        public void ElementalGateLogic_MatchingElement_ReturnsTrue()
        {
            ElementalGateLogic gate = new ElementalGateLogic(ElementalRequirement.Fire, 0f, false, 0);

            bool result = gate.OnElementalHit(Element.Fire, 10f);

            Assert.IsTrue(result);
            Assert.IsTrue(gate.IsOpened);
        }

        [Test]
        public void ElementalGateLogic_NonMatchingElement_ReturnsFalse()
        {
            ElementalGateLogic gate = new ElementalGateLogic(ElementalRequirement.Fire, 0f, false, 0);

            bool result = gate.OnElementalHit(Element.Thunder, 10f);

            Assert.IsFalse(result);
            Assert.IsFalse(gate.IsOpened);
        }

        [Test]
        public void ElementalGateLogic_MultiElement_MatchesIfContains()
        {
            ElementalGateLogic gate = new ElementalGateLogic(ElementalRequirement.Fire, 0f, false, 0);

            // 複合属性（Fire | Slash）の攻撃 → Fireを含むのでマッチ
            bool result = gate.OnElementalHit(Element.Fire | Element.Slash, 10f);

            Assert.IsTrue(result);
        }

        [Test]
        public void ElementalGateLogic_MinDamage_RejectsWeak()
        {
            ElementalGateLogic gate = new ElementalGateLogic(ElementalRequirement.Thunder, 50f, false, 0);

            // ダメージ不足
            bool result = gate.OnElementalHit(Element.Thunder, 30f);
            Assert.IsFalse(result);

            // ダメージ十分
            result = gate.OnElementalHit(Element.Thunder, 50f);
            Assert.IsTrue(result);
        }

        [Test]
        public void ElementalGateLogic_AlreadyOpened_IgnoresSubsequentHits()
        {
            ElementalGateLogic gate = new ElementalGateLogic(ElementalRequirement.Fire, 0f, false, 0);

            gate.OnElementalHit(Element.Fire, 10f);
            Assert.IsTrue(gate.IsOpened);

            // 2回目のヒット → falseを返す（既に開いている）
            bool result = gate.OnElementalHit(Element.Fire, 10f);
            Assert.IsFalse(result);
        }
    }
}
