using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// ガード判定ロジックのテスト。新仕様:
    /// - ダメージ軽減はGuardStats属性別カットで決まる。GetDamageReductionは廃止。
    /// - JustGuardはダメージ完全0。通常窓(0.1s) or 連続JG窓中で成立。
    /// - 即ブレイク判定(attackPower > guardStrength)は廃止。
    ///   ブレイクは「スタミナ削り(armorBreakValue - guardStrength)がcurrentStaminaを超えた時」のみ。
    /// - GuardAttack効果中はスタミナ不足でもブレイクしない。
    /// </summary>
    public class ParryGuardJudgmentTests
    {
        // --- ヘルパー: 十分なスタミナで呼ぶショートカット ---
        private static GuardResult Judge(
            bool isGuarding,
            float guardTimeSinceStart,
            AttackFeature feature = AttackFeature.None,
            bool inContinuousJustGuardWindow = false,
            GuardDirection guardDirection = GuardDirection.Front,
            bool isAttackFromFront = true,
            bool hasGuardAttackEffect = false,
            float currentStamina = 100f,
            float guardStrength = 100f,
            float armorBreakValue = 0f)
        {
            return GuardJudgmentLogic.Judge(
                isGuarding, guardTimeSinceStart, inContinuousJustGuardWindow,
                feature, guardDirection, isAttackFromFront, hasGuardAttackEffect,
                currentStamina, guardStrength, armorBreakValue);
        }

        // --- 通常ガード判定 ---

        [Test]
        public void GuardJudgmentLogic_Judge_WhenNotGuarding_ReturnsNoGuard()
        {
            GuardResult result = Judge(isGuarding: false, guardTimeSinceStart: 0f);
            Assert.AreEqual(GuardResult.NoGuard, result);
        }

        [Test]
        public void GuardJudgmentLogic_Judge_Unparriable_ReturnsNoGuard()
        {
            GuardResult result = Judge(
                isGuarding: true, guardTimeSinceStart: 0.05f,
                feature: AttackFeature.Unparriable);
            Assert.AreEqual(GuardResult.NoGuard, result);
        }

        [Test]
        public void GuardJudgmentLogic_Judge_WithinJustGuardWindow_ReturnsJustGuard()
        {
            GuardResult result = Judge(isGuarding: true, guardTimeSinceStart: 0.05f);
            Assert.AreEqual(GuardResult.JustGuard, result);
        }

        [Test]
        public void GuardJudgmentLogic_Judge_OutsideJustGuardWindow_ReturnsGuarded()
        {
            GuardResult result = Judge(isGuarding: true, guardTimeSinceStart: 0.5f);
            Assert.AreEqual(GuardResult.Guarded, result);
        }

        [Test]
        public void GuardJudgmentLogic_Judge_JustGuardImmune_OutsideWindow_ReturnsGuarded()
        {
            // JustGuardImmune: JustGuard窓内でも JustGuard 不成立
            GuardResult result = Judge(
                isGuarding: true, guardTimeSinceStart: 0.05f,
                feature: AttackFeature.JustGuardImmune);
            Assert.AreEqual(GuardResult.Guarded, result);
        }

        // --- 連続ジャストガード窓 ---

        [Test]
        public void GuardJudgmentLogic_Judge_InContinuousJustGuardWindow_ReturnsJustGuard()
        {
            // 通常JG窓外(guardTimeSinceStart=0.5)でも、連続JG窓中なら即JustGuard
            GuardResult result = Judge(
                isGuarding: true, guardTimeSinceStart: 0.5f,
                inContinuousJustGuardWindow: true);
            Assert.AreEqual(GuardResult.JustGuard, result);
        }

        [Test]
        public void GuardJudgmentLogic_Judge_ContinuousWindow_DoesNotBypassJustGuardImmune()
        {
            // JustGuardImmune攻撃は連続窓中でもJustGuard成立しない
            GuardResult result = Judge(
                isGuarding: true, guardTimeSinceStart: 0.5f,
                inContinuousJustGuardWindow: true,
                feature: AttackFeature.JustGuardImmune);
            Assert.AreEqual(GuardResult.Guarded, result);
        }

        // --- スタミナ削りによるガードブレイク ---

        [Test]
        public void GuardJudgmentLogic_Judge_StaminaSufficient_ReturnsGuarded()
        {
            // 削り量 = max(0, 50 - 30) = 20、スタミナ100 >= 20 → Guarded
            GuardResult result = Judge(
                isGuarding: true, guardTimeSinceStart: 0.5f,
                currentStamina: 100f, guardStrength: 30f, armorBreakValue: 50f);
            Assert.AreEqual(GuardResult.Guarded, result);
        }

        [Test]
        public void GuardJudgmentLogic_Judge_StaminaInsufficient_ReturnsGuardBreak()
        {
            // 削り量 = max(0, 100 - 10) = 90、スタミナ5 < 90 → GuardBreak
            GuardResult result = Judge(
                isGuarding: true, guardTimeSinceStart: 0.5f,
                currentStamina: 5f, guardStrength: 10f, armorBreakValue: 100f);
            Assert.AreEqual(GuardResult.GuardBreak, result);
        }

        [Test]
        public void GuardJudgmentLogic_Judge_StaminaInsufficient_WithGuardAttack_ReturnsGuarded()
        {
            // スタミナ不足でもGuardAttack効果中はGuardBreakしない
            GuardResult result = Judge(
                isGuarding: true, guardTimeSinceStart: 0.5f,
                hasGuardAttackEffect: true,
                currentStamina: 5f, guardStrength: 10f, armorBreakValue: 100f);
            Assert.AreEqual(GuardResult.Guarded, result);
        }

        [Test]
        public void GuardJudgmentLogic_Judge_ArmorBreakValueNotExceedingGuardStrength_NoDrain()
        {
            // 削り量 0 でスタミナ不足でもガード成立(完全受け流し)
            GuardResult result = Judge(
                isGuarding: true, guardTimeSinceStart: 0.5f,
                currentStamina: 0f, guardStrength: 50f, armorBreakValue: 30f);
            Assert.AreEqual(GuardResult.Guarded, result);
        }

        [Test]
        public void GuardJudgmentLogic_CalculateStaminaDrain_PositiveDifference()
        {
            Assert.AreEqual(20f, GuardJudgmentLogic.CalculateStaminaDrain(50f, 30f), 0.001f);
        }

        [Test]
        public void GuardJudgmentLogic_CalculateStaminaDrain_ArmorBreakLessThanGuard_ReturnsZero()
        {
            Assert.AreEqual(0f, GuardJudgmentLogic.CalculateStaminaDrain(20f, 50f), 0.001f);
        }

        // --- ガード方向 ---

        [Test]
        public void GuardJudgmentLogic_IsGuardDirectionValid_FrontAndFromFront_ReturnsTrue()
        {
            Assert.IsTrue(GuardJudgmentLogic.IsGuardDirectionValid(GuardDirection.Front, true));
        }

        [Test]
        public void GuardJudgmentLogic_IsGuardDirectionValid_FrontAndFromBehind_ReturnsFalse()
        {
            Assert.IsFalse(GuardJudgmentLogic.IsGuardDirectionValid(GuardDirection.Front, false));
        }

        [Test]
        public void GuardJudgmentLogic_IsGuardDirectionValid_Both_AlwaysTrue()
        {
            Assert.IsTrue(GuardJudgmentLogic.IsGuardDirectionValid(GuardDirection.Both, true));
            Assert.IsTrue(GuardJudgmentLogic.IsGuardDirectionValid(GuardDirection.Both, false));
        }

        [Test]
        public void GuardJudgmentLogic_Judge_WrongDirection_ReturnsNoGuard()
        {
            GuardResult result = Judge(
                isGuarding: true, guardTimeSinceStart: 0.05f,
                guardDirection: GuardDirection.Front,
                isAttackFromFront: false);
            Assert.AreEqual(GuardResult.NoGuard, result);
        }

        // --- IsGuardSucceeded ---

        [Test]
        public void GuardJudgmentLogic_IsGuardSucceeded_GuardedAndJustGuard_True()
        {
            Assert.IsTrue(GuardJudgmentLogic.IsGuardSucceeded(GuardResult.Guarded));
            Assert.IsTrue(GuardJudgmentLogic.IsGuardSucceeded(GuardResult.JustGuard));
        }

        [Test]
        public void GuardJudgmentLogic_IsGuardSucceeded_NoGuardAndBreak_False()
        {
            Assert.IsFalse(GuardJudgmentLogic.IsGuardSucceeded(GuardResult.NoGuard));
            Assert.IsFalse(GuardJudgmentLogic.IsGuardSucceeded(GuardResult.GuardBreak));
        }

        // --- JustGuard 回復 ---

        [Test]
        public void GuardJudgmentLogic_JustGuardRecovery_RestoresStaminaAndArmor()
        {
            float stamina = 50f;
            float armor = 20f;

            GuardJudgmentLogic.ApplyJustGuardRecovery(
                ref stamina, 100f, ref armor, 50f);

            Assert.AreEqual(65f, stamina, 0.01f, "スタミナ+15回復");
            Assert.AreEqual(30f, armor, 0.01f, "アーマー+10回復");
        }

        [Test]
        public void GuardJudgmentLogic_JustGuardRecovery_ClampsToMax()
        {
            float stamina = 95f;
            float armor = 45f;

            GuardJudgmentLogic.ApplyJustGuardRecovery(
                ref stamina, 100f, ref armor, 50f);

            Assert.AreEqual(100f, stamina, 0.01f, "スタミナはmaxにクランプ");
            Assert.AreEqual(50f, armor, 0.01f, "アーマーはmaxにクランプ");
        }
    }
}
