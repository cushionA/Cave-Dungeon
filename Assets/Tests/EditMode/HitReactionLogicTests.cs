using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class HitReactionLogicTests
    {
        // ===== SuperArmor =====

        [Test]
        public void HitReaction_SuperArmor_ReturnsNone()
        {
            HitReaction result = HitReactionLogic.Determine(
                hasSuperArmor: true,
                hasKnockbackImmunity: false,
                totalArmorBefore: 0f,
                hasKnockbackForce: true,
                guardResult: GuardResult.NoGuard,
                currentState: ActState.Neutral);

            Assert.AreEqual(HitReaction.None, result);
        }

        // ===== アーマー =====

        [Test]
        public void HitReaction_ArmorPositive_ReturnsNone()
        {
            HitReaction result = HitReactionLogic.Determine(
                hasSuperArmor: false,
                hasKnockbackImmunity: false,
                totalArmorBefore: 10f,
                hasKnockbackForce: true,
                guardResult: GuardResult.NoGuard,
                currentState: ActState.Neutral);

            Assert.AreEqual(HitReaction.None, result);
        }

        [Test]
        public void HitReaction_ArmorZero_NoKnockback_ReturnsFlinch()
        {
            HitReaction result = HitReactionLogic.Determine(
                hasSuperArmor: false,
                hasKnockbackImmunity: false,
                totalArmorBefore: 0f,
                hasKnockbackForce: false,
                guardResult: GuardResult.NoGuard,
                currentState: ActState.Neutral);

            Assert.AreEqual(HitReaction.Flinch, result);
        }

        [Test]
        public void HitReaction_ArmorZero_WithKnockback_ReturnsKnockback()
        {
            HitReaction result = HitReactionLogic.Determine(
                hasSuperArmor: false,
                hasKnockbackImmunity: false,
                totalArmorBefore: 0f,
                hasKnockbackForce: true,
                guardResult: GuardResult.NoGuard,
                currentState: ActState.Neutral);

            Assert.AreEqual(HitReaction.Knockback, result);
        }

        // ===== KnockbackImmunity =====

        [Test]
        public void HitReaction_KnockbackImmunity_WithKnockback_ReturnsFlinch()
        {
            HitReaction result = HitReactionLogic.Determine(
                hasSuperArmor: false,
                hasKnockbackImmunity: true,
                totalArmorBefore: 0f,
                hasKnockbackForce: true,
                guardResult: GuardResult.NoGuard,
                currentState: ActState.Neutral);

            Assert.AreEqual(HitReaction.Flinch, result);
        }

        [Test]
        public void HitReaction_KnockbackImmunity_NoKnockback_ReturnsFlinch()
        {
            HitReaction result = HitReactionLogic.Determine(
                hasSuperArmor: false,
                hasKnockbackImmunity: true,
                totalArmorBefore: 0f,
                hasKnockbackForce: false,
                guardResult: GuardResult.NoGuard,
                currentState: ActState.Neutral);

            Assert.AreEqual(HitReaction.Flinch, result);
        }

        // ===== ガード =====

        [Test]
        public void HitReaction_GuardBreak_ReturnsGuardBreak()
        {
            HitReaction result = HitReactionLogic.Determine(
                hasSuperArmor: false,
                hasKnockbackImmunity: false,
                totalArmorBefore: 0f,
                hasKnockbackForce: true,
                guardResult: GuardResult.GuardBreak,
                currentState: ActState.Guarding);

            Assert.AreEqual(HitReaction.GuardBreak, result);
        }

        [Test]
        public void HitReaction_Guarded_ReturnsNone()
        {
            HitReaction result = HitReactionLogic.Determine(
                hasSuperArmor: false,
                hasKnockbackImmunity: false,
                totalArmorBefore: 0f,
                hasKnockbackForce: true,
                guardResult: GuardResult.Guarded,
                currentState: ActState.Guarding);

            Assert.AreEqual(HitReaction.None, result);
        }

        [Test]
        public void HitReaction_JustGuard_ReturnsNone()
        {
            HitReaction result = HitReactionLogic.Determine(
                hasSuperArmor: false,
                hasKnockbackImmunity: false,
                totalArmorBefore: 0f,
                hasKnockbackForce: true,
                guardResult: GuardResult.JustGuard,
                currentState: ActState.Guarding);

            Assert.AreEqual(HitReaction.None, result);
        }

        // ===== ヒットスタン中の吹き飛ばし =====

        [Test]
        public void HitReaction_InFlinch_WithKnockback_ReturnsKnockback()
        {
            HitReaction result = HitReactionLogic.Determine(
                hasSuperArmor: false,
                hasKnockbackImmunity: false,
                totalArmorBefore: 5f,
                hasKnockbackForce: true,
                guardResult: GuardResult.NoGuard,
                currentState: ActState.Flinch);

            Assert.AreEqual(HitReaction.Knockback, result,
                "ヒットスタン中はアーマーを無視して吹き飛ばし");
        }

        [Test]
        public void HitReaction_InGuardBroken_WithKnockback_ReturnsKnockback()
        {
            HitReaction result = HitReactionLogic.Determine(
                hasSuperArmor: false,
                hasKnockbackImmunity: false,
                totalArmorBefore: 5f,
                hasKnockbackForce: true,
                guardResult: GuardResult.NoGuard,
                currentState: ActState.GuardBroken);

            Assert.AreEqual(HitReaction.Knockback, result,
                "ガードブレイク中に吹き飛ばし攻撃 → Knockback");
        }

        [Test]
        public void HitReaction_InStunned_WithKnockback_ReturnsKnockback()
        {
            HitReaction result = HitReactionLogic.Determine(
                hasSuperArmor: false,
                hasKnockbackImmunity: false,
                totalArmorBefore: 5f,
                hasKnockbackForce: true,
                guardResult: GuardResult.NoGuard,
                currentState: ActState.Stunned);

            Assert.AreEqual(HitReaction.Knockback, result,
                "スタン中に吹き飛ばし攻撃 → Knockback");
        }

        [Test]
        public void HitReaction_InFlinch_WithKnockback_KnockbackImmunity_ReturnsFlinch()
        {
            HitReaction result = HitReactionLogic.Determine(
                hasSuperArmor: false,
                hasKnockbackImmunity: true,
                totalArmorBefore: 5f,
                hasKnockbackForce: true,
                guardResult: GuardResult.NoGuard,
                currentState: ActState.Flinch);

            Assert.AreEqual(HitReaction.Flinch, result,
                "ヒットスタン中でもKnockbackImmunityならFlinch");
        }

        [Test]
        public void HitReaction_InFlinch_NoKnockback_WithArmor_ReturnsNone()
        {
            HitReaction result = HitReactionLogic.Determine(
                hasSuperArmor: false,
                hasKnockbackImmunity: false,
                totalArmorBefore: 5f,
                hasKnockbackForce: false,
                guardResult: GuardResult.NoGuard,
                currentState: ActState.Flinch);

            Assert.AreEqual(HitReaction.None, result,
                "ヒットスタン中でも吹き飛ばし力なし＋アーマーありならNone");
        }

        // ===== SuperArmor はヒットスタン中の吹き飛ばしも無効 =====

        [Test]
        public void HitReaction_SuperArmor_InFlinch_WithKnockback_ReturnsNone()
        {
            HitReaction result = HitReactionLogic.Determine(
                hasSuperArmor: true,
                hasKnockbackImmunity: false,
                totalArmorBefore: 0f,
                hasKnockbackForce: true,
                guardResult: GuardResult.NoGuard,
                currentState: ActState.Flinch);

            Assert.AreEqual(HitReaction.None, result,
                "SuperArmorはヒットスタン中の吹き飛ばしも無効");
        }

        // ===== IsInHitstun =====

        [Test]
        public void IsInHitstun_Flinch_ReturnsTrue()
        {
            Assert.IsTrue(HitReactionLogic.IsInHitstun(ActState.Flinch));
        }

        [Test]
        public void IsInHitstun_GuardBroken_ReturnsTrue()
        {
            Assert.IsTrue(HitReactionLogic.IsInHitstun(ActState.GuardBroken));
        }

        [Test]
        public void IsInHitstun_Stunned_ReturnsTrue()
        {
            Assert.IsTrue(HitReactionLogic.IsInHitstun(ActState.Stunned));
        }

        [Test]
        public void IsInHitstun_Neutral_ReturnsFalse()
        {
            Assert.IsFalse(HitReactionLogic.IsInHitstun(ActState.Neutral));
        }

        [Test]
        public void IsInHitstun_Attacking_ReturnsFalse()
        {
            Assert.IsFalse(HitReactionLogic.IsInHitstun(ActState.Attacking));
        }

        [Test]
        public void IsInHitstun_Knockbacked_ReturnsFalse()
        {
            Assert.IsFalse(HitReactionLogic.IsInHitstun(ActState.Knockbacked));
        }
    }
}
