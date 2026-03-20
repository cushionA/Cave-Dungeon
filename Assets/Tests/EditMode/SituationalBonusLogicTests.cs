using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class SituationalBonusLogicTests
    {
        // ===== 個別ボーナス =====

        [Test]
        public void SituationalBonus_Counter_WhenTargetAttacking_ReturnsCounterBonus()
        {
            (float multiplier, SituationalBonus bonus) = SituationalBonusLogic.Evaluate(
                isTargetAttacking: true,
                isAttackFromBehind: false,
                isTargetInHitstun: false);

            Assert.AreEqual(SituationalBonusLogic.k_CounterBonusMult, multiplier, 0.001f);
            Assert.AreEqual(SituationalBonus.Counter, bonus);
        }

        [Test]
        public void SituationalBonus_Backstab_WhenFromBehind_ReturnsBackstabBonus()
        {
            (float multiplier, SituationalBonus bonus) = SituationalBonusLogic.Evaluate(
                isTargetAttacking: false,
                isAttackFromBehind: true,
                isTargetInHitstun: false);

            Assert.AreEqual(SituationalBonusLogic.k_BackstabBonusMult, multiplier, 0.001f);
            Assert.AreEqual(SituationalBonus.Backstab, bonus);
        }

        [Test]
        public void SituationalBonus_StaggerHit_WhenTargetInHitstun_ReturnsStaggerBonus()
        {
            (float multiplier, SituationalBonus bonus) = SituationalBonusLogic.Evaluate(
                isTargetAttacking: false,
                isAttackFromBehind: false,
                isTargetInHitstun: true);

            Assert.AreEqual(SituationalBonusLogic.k_StaggerHitBonusMult, multiplier, 0.001f);
            Assert.AreEqual(SituationalBonus.StaggerHit, bonus);
        }

        // ===== ボーナスなし =====

        [Test]
        public void SituationalBonus_NoneApplicable_Returns1x()
        {
            (float multiplier, SituationalBonus bonus) = SituationalBonusLogic.Evaluate(
                isTargetAttacking: false,
                isAttackFromBehind: false,
                isTargetInHitstun: false);

            Assert.AreEqual(1.0f, multiplier, 0.001f);
            Assert.AreEqual(SituationalBonus.None, bonus);
        }

        // ===== 重複なし: 最大値のみ適用 =====

        [Test]
        public void SituationalBonus_CounterAndBackstab_TakesHighest()
        {
            // Counter(1.3) > Backstab(1.25) → Counter wins
            (float multiplier, SituationalBonus bonus) = SituationalBonusLogic.Evaluate(
                isTargetAttacking: true,
                isAttackFromBehind: true,
                isTargetInHitstun: false);

            Assert.AreEqual(SituationalBonusLogic.k_CounterBonusMult, multiplier, 0.001f,
                "カウンターが最大なのでカウンターが適用");
            Assert.AreEqual(SituationalBonus.Counter, bonus);
        }

        [Test]
        public void SituationalBonus_BackstabAndStagger_TakesHighest()
        {
            // Backstab(1.25) > Stagger(1.2) → Backstab wins
            (float multiplier, SituationalBonus bonus) = SituationalBonusLogic.Evaluate(
                isTargetAttacking: false,
                isAttackFromBehind: true,
                isTargetInHitstun: true);

            Assert.AreEqual(SituationalBonusLogic.k_BackstabBonusMult, multiplier, 0.001f,
                "背面攻撃が最大なので背面攻撃が適用");
            Assert.AreEqual(SituationalBonus.Backstab, bonus);
        }

        [Test]
        public void SituationalBonus_AllThree_TakesCounter()
        {
            // Counter(1.3) > Backstab(1.25) > Stagger(1.2)
            (float multiplier, SituationalBonus bonus) = SituationalBonusLogic.Evaluate(
                isTargetAttacking: true,
                isAttackFromBehind: true,
                isTargetInHitstun: true);

            Assert.AreEqual(SituationalBonusLogic.k_CounterBonusMult, multiplier, 0.001f,
                "全条件満たす場合もカウンターのみ適用（重複なし）");
            Assert.AreEqual(SituationalBonus.Counter, bonus);
        }

        // ===== ActState からの判定ヘルパー =====

        [Test]
        public void IsTargetAttacking_AttackPrep_ReturnsTrue()
        {
            Assert.IsTrue(SituationalBonusLogic.IsTargetAttacking(ActState.AttackPrep));
        }

        [Test]
        public void IsTargetAttacking_Attacking_ReturnsTrue()
        {
            Assert.IsTrue(SituationalBonusLogic.IsTargetAttacking(ActState.Attacking));
        }

        [Test]
        public void IsTargetAttacking_AttackRecovery_ReturnsTrue()
        {
            Assert.IsTrue(SituationalBonusLogic.IsTargetAttacking(ActState.AttackRecovery));
        }

        [Test]
        public void IsTargetAttacking_Neutral_ReturnsFalse()
        {
            Assert.IsFalse(SituationalBonusLogic.IsTargetAttacking(ActState.Neutral));
        }

        [Test]
        public void IsTargetAttacking_Guarding_ReturnsFalse()
        {
            Assert.IsFalse(SituationalBonusLogic.IsTargetAttacking(ActState.Guarding));
        }
    }
}
