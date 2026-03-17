using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class DamageCorCalcTests
    {
        // --- 基本ダメージ計算 ---

        [Test]
        public void DamageCalculator_CalculateBaseDamage_AppliesFormula()
        {
            // atk=100, motionValue=1.2, def=30 => 100*1.2-30 = 90
            int result = DamageCalculator.CalculateBaseDamage(100, 1.2f, 30);

            Assert.AreEqual(90, result);
        }

        [Test]
        public void DamageCalculator_CalculateBaseDamage_MinDamageGuarantee()
        {
            // def > atk*motionValue => 最小ダメージ保証
            int result = DamageCalculator.CalculateBaseDamage(10, 0.5f, 100);

            Assert.AreEqual(DamageCalculator.k_MinDamage, result);
        }

        // --- 属性ダメージ ---

        [Test]
        public void DamageCalculator_CalculateElementalDamage_AppliesWeakness()
        {
            // 弱点属性 => 1.5x
            int weakResult = DamageCalculator.CalculateElementalDamage(
                100, Element.Fire, Element.Fire, Element.Thunder);
            Assert.AreEqual(150, weakResult);

            // 耐性属性 => 0.5x
            int resistResult = DamageCalculator.CalculateElementalDamage(
                100, Element.Thunder, Element.Fire, Element.Thunder);
            Assert.AreEqual(50, resistResult);

            // 通常属性 => 1.0x
            int normalResult = DamageCalculator.CalculateElementalDamage(
                100, Element.Light, Element.Fire, Element.Thunder);
            Assert.AreEqual(100, normalResult);
        }

        // --- クリティカル ---

        [Test]
        public void DamageCalculator_ApplyCritical_MultipliesDamage()
        {
            // isCritical=true, mult=1.5 => damage*1.5
            int critResult = DamageCalculator.ApplyCritical(100, 1.5f, true);
            Assert.AreEqual(150, critResult);

            // isCritical=false => ダメージそのまま
            int noCritResult = DamageCalculator.ApplyCritical(100, 1.5f, false);
            Assert.AreEqual(100, noCritResult);
        }

        // --- クリティカル判定（決定論的） ---

        [Test]
        public void DamageCalculator_IsCritical_DeterministicCheck()
        {
            // randomValue < critRate => true
            Assert.IsTrue(DamageCalculator.IsCritical(0.5f, 0.3f));

            // randomValue >= critRate => false
            Assert.IsFalse(DamageCalculator.IsCritical(0.5f, 0.5f));
            Assert.IsFalse(DamageCalculator.IsCritical(0.5f, 0.8f));

            // critRate=0 => always false
            Assert.IsFalse(DamageCalculator.IsCritical(0.0f, 0.0f));

            // critRate=1 => always true (randomValue < 1.0)
            Assert.IsTrue(DamageCalculator.IsCritical(1.0f, 0.99f));
        }
    }
}
