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
            // 新式: (atk² × motionValue) / (atk + def)
            // atk=100, motionValue=1.2, def=30 => (10000*1.2)/(130) = 92
            int result = DamageCalculator.CalculateBaseDamage(100, 1.2f, 30);

            Assert.AreEqual(92, result);
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
        public void DamageCalculator_GetWeaknessMultiplier_AppliesWeakness()
        {
            // 弱点属性 => 1.5x
            float weakMult = DamageCalculator.GetWeaknessMultiplier(Element.Fire, Element.Fire);
            Assert.AreEqual(DamageCalculator.k_WeaknessMult, weakMult, 0.001f);

            // 非弱点属性 => 1.0x
            float normalMult = DamageCalculator.GetWeaknessMultiplier(Element.Light, Element.Fire);
            Assert.AreEqual(1.0f, normalMult, 0.001f);

            // None属性 => 1.0x
            float noneMult = DamageCalculator.GetWeaknessMultiplier(Element.None, Element.Fire);
            Assert.AreEqual(1.0f, noneMult, 0.001f);
        }

        [Test]
        public void DamageCalculator_CalculateChannelDamage_AppliesWeaknessMultiplier()
        {
            // atk=100, motionValue=1.0, def=0, 弱点ヒット => baseDmg * 1.5
            int weakResult = DamageCalculator.CalculateChannelDamage(100, 1.0f, 0, Element.Fire, Element.Fire);
            int baseResult = DamageCalculator.CalculateBaseDamage(100, 1.0f, 0);
            Assert.AreEqual((int)(baseResult * DamageCalculator.k_WeaknessMult), weakResult);

            // 非弱点 => baseDmg * 1.0
            int normalResult = DamageCalculator.CalculateChannelDamage(100, 1.0f, 0, Element.Light, Element.Fire);
            Assert.AreEqual(baseResult, normalResult);
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
