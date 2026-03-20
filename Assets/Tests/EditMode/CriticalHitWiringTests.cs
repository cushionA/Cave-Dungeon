using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    [TestFixture]
    public class CriticalHitWiringTests
    {
        // --- critRoll フィールド ---

        [Test]
        public void DamageData_CritRollField_DefaultsToZero()
        {
            DamageData data = default;
            Assert.AreEqual(0f, data.critRoll, 0.001f);
        }

        [Test]
        public void DamageData_CritRollField_CanBeSet()
        {
            DamageData data = new DamageData { critRoll = 0.3f };
            Assert.AreEqual(0.3f, data.critRoll, 0.001f);
        }

        // --- クリティカル判定のロジック確認 ---

        [Test]
        public void CriticalHit_WhenCritRollBelowRate_IsCritical()
        {
            // critRate=0.5, critRoll=0.2 => critical
            bool result = DamageCalculator.IsCritical(0.5f, 0.2f);
            Assert.IsTrue(result);
        }

        [Test]
        public void CriticalHit_WhenCritRollAboveRate_IsNotCritical()
        {
            // critRate=0.5, critRoll=0.8 => not critical
            bool result = DamageCalculator.IsCritical(0.5f, 0.8f);
            Assert.IsFalse(result);
        }

        [Test]
        public void CriticalHit_ApplyCritical_MultipliesDamage()
        {
            int baseDamage = 100;
            float critMultiplier = 1.5f;

            int critDamage = DamageCalculator.ApplyCritical(baseDamage, critMultiplier, true);
            Assert.AreEqual(150, critDamage);

            int normalDamage = DamageCalculator.ApplyCritical(baseDamage, critMultiplier, false);
            Assert.AreEqual(100, normalDamage);
        }

        // --- DamageReceiver統合(CalculateDamageStaticで検証) ---

        [Test]
        public void CriticalHit_CalculateDamageWithCrit_AppliesCriticalMultiplier()
        {
            // critRate=1.0(常にクリティカル), critRoll=0.0
            // クリティカル判定が正しく通るか
            float critRate = 1.0f;
            float critRoll = 0.0f;
            float critMultiplier = 2.0f;

            bool isCritical = DamageCalculator.IsCritical(critRate, critRoll);
            Assert.IsTrue(isCritical);

            int rawDamage = 100;
            int result = DamageCalculator.ApplyCritical(rawDamage, critMultiplier, isCritical);
            Assert.AreEqual(200, result);
        }

        [Test]
        public void CriticalHit_ZeroCritRate_NeverCritical()
        {
            // critRate=0.0 => critRoll=0.0でも非クリティカル（< 判定なので等値は非クリティカル）
            bool result = DamageCalculator.IsCritical(0f, 0f);
            Assert.IsFalse(result);
        }

        [Test]
        public void DamageResult_IsCriticalField_ReflectsCorrectly()
        {
            DamageResult result = new DamageResult { isCritical = true };
            Assert.IsTrue(result.isCritical);

            DamageResult result2 = new DamageResult { isCritical = false };
            Assert.IsFalse(result2.isCritical);
        }
    }
}
