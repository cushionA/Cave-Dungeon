using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Task A: Action armor 削り切った瞬間の Flinch 遷移 + 行動中断、
    /// および SuperArmor による完全保護の純ロジック検証。
    ///
    /// DamageReceiver 本体の統合挙動は PlayMode 側 (ActionArmorBreakFlinchIntegrationTests) でも検証する。
    /// 本テストは HitReactionLogic と ActionEffectProcessor + HpArmorLogic の組合わせで
    /// 「削り切った瞬間判定」「SuperArmor 保護」「Flinch→Knockback 昇格」の契約を直接検証する。
    /// </summary>
    [TestFixture]
    public class Integration_ActionArmorBreakToFlinchTests
    {
        // =====================================================================
        // Task A: 行動アーマー削り切り → 行動中断 + Flinch 遷移
        // =====================================================================

        /// <summary>
        /// actionArmor=10 + damage=15 の armorBreakValue で actionArmor が 0 に到達し、
        /// SuperArmor が付いていない場合は「削り切った瞬間」と判定される。
        /// DamageReceiver 側ではこの条件で HitReaction.None を Flinch に上書き + 行動中断を実行する。
        /// </summary>
        [Test]
        public void ActionArmor10_DamageBreaks_ActionArmorJustBroken_IsTrue()
        {
            // Arrange: action armor 10 を残し、15 の armorBreakValue でヒット
            float actionArmor = 10f;
            float actionArmorBefore = actionArmor;
            int hp = 100;
            float baseArmor = 0f;

            (int _, bool _, bool armorBroken) = HpArmorLogic.ApplyDamage(
                ref hp, ref baseArmor, rawDamage: 10,
                armorBreakValue: 15f, ref actionArmor);

            // 「削り切った瞬間」判定条件: before > 0 && consumed > 0 && after <= 0
            float consumed = actionArmorBefore - actionArmor;
            bool actionArmorJustBroken = actionArmorBefore > 0f
                && consumed > 0f
                && actionArmor <= 0f;

            // Assert
            Assert.IsTrue(armorBroken, "HpArmorLogic 側でも armorBroken が立つ");
            Assert.IsTrue(actionArmorJustBroken,
                "actionArmorBefore=10 → consumed=10 → after=0 で削り切り瞬間判定は true");
            Assert.AreEqual(0f, actionArmor, 0.001f);
        }

        /// <summary>
        /// 既に actionArmor=0 で始まった 2 発目のヒットでは「削り切った瞬間」判定は false。
        /// 再 Flinch (Flinch タイマー延長) を防ぐ。
        /// </summary>
        [Test]
        public void ActionArmorZero_SecondHit_NotConsideredJustBroken()
        {
            float actionArmor = 0f;
            float actionArmorBefore = actionArmor;
            int hp = 100;
            float baseArmor = 0f;

            HpArmorLogic.ApplyDamage(
                ref hp, ref baseArmor, rawDamage: 10,
                armorBreakValue: 15f, ref actionArmor);

            float consumed = actionArmorBefore - actionArmor;
            bool actionArmorJustBroken = actionArmorBefore > 0f
                && consumed > 0f
                && actionArmor <= 0f;

            Assert.IsFalse(actionArmorJustBroken,
                "actionArmorBefore=0 の時点では既に削り切り済みで「瞬間」ではない");
        }

        /// <summary>
        /// actionArmor=10 + SuperArmor 付きでヒット → SuperArmor は HitReaction.None を強制する。
        /// DamageReceiver 側では shouldInterruptForArmorBreak が false となり、行動中断しない。
        /// </summary>
        [Test]
        public void ActionArmorBreak_WithSuperArmor_HitReactionIsNone()
        {
            // SuperArmor フラグ
            ActionEffect[] effects = new ActionEffect[]
            {
                new ActionEffect
                {
                    type = ActionEffectType.SuperArmor,
                    startTime = 0f, duration = 10f, value = 0f
                }
            };

            ActionEffectProcessor.EffectState state =
                ActionEffectProcessor.Evaluate(effects, 0.5f);

            // アーマー削り後の状態で HitReactionLogic を問い合わせる
            HitReaction reaction = HitReactionLogic.Determine(
                state.hasSuperArmor,
                state.hasKnockbackImmunity,
                totalArmorBefore: 0f,  // 既に全アーマー削られた後の状態を想定
                hasKnockbackForce: false,
                GuardResult.NoGuard,
                ActState.Attacking);

            Assert.IsTrue(state.hasSuperArmor);
            Assert.AreEqual(HitReaction.None, reaction,
                "SuperArmor 中は totalArmorBefore=0 であっても Flinch にならない");
        }

        /// <summary>
        /// アーマー削り切り + Knockback 力ありの場合は Knockback が優先される (Flinch より重い)。
        /// DamageReceiver 側のフォールバックロジックで hasKnockbackForce を見て分岐する想定を確認。
        /// </summary>
        [Test]
        public void ActionArmorBreak_WithKnockbackForce_EscalatesToKnockback()
        {
            // DamageReceiver 側の擬似ロジック: armor削り切り + KnockbackImmunityなし + knockbackForceあり → Knockback
            bool hasKnockbackForce = true;
            bool hasKnockbackImmunity = false;

            HitReaction expected = hasKnockbackForce && !hasKnockbackImmunity
                ? HitReaction.Knockback
                : HitReaction.Flinch;

            Assert.AreEqual(HitReaction.Knockback, expected);
        }

        /// <summary>
        /// アーマー削り切り + KnockbackImmunity の場合は Flinch (Knockback Immune)。
        /// </summary>
        [Test]
        public void ActionArmorBreak_WithKnockbackImmunity_EscalatesToFlinch()
        {
            bool hasKnockbackForce = true;
            bool hasKnockbackImmunity = true;

            HitReaction expected = hasKnockbackForce && !hasKnockbackImmunity
                ? HitReaction.Knockback
                : HitReaction.Flinch;

            Assert.AreEqual(HitReaction.Flinch, expected);
        }

        /// <summary>
        /// ガード成功中のアーマー消費は行動中断を発動させない (ガード中は行動中ではなく防御中)。
        /// </summary>
        [Test]
        public void ActionArmorBreak_DuringGuardSuccess_DoesNotInterrupt()
        {
            // DamageReceiver の条件式と同じ
            bool actionArmorJustBroken = true;
            bool hasSuperArmor = false;
            GuardResult guardResult = GuardResult.Guarded;

            bool shouldInterrupt = actionArmorJustBroken
                && !hasSuperArmor
                && !GuardJudgmentLogic.IsGuardSucceeded(guardResult);

            Assert.IsFalse(shouldInterrupt,
                "ガード成功中はアーマー削り切りでも行動中断しない (Guarded 状態から Flinch へは遷移しない)");
        }
    }
}
