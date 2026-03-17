using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class ParryGuardJudgmentTests
    {
        // --- 通常ガード判定 ---

        [Test]
        public void GuardJudgmentLogic_Judge_WhenNotGuarding_ReturnsNoGuard()
        {
            // isGuarding=false => NoGuard（ガードしていない）
            GuardResult result = GuardJudgmentLogic.Judge(
                isGuarding: false,
                guardTimeSinceStart: 0f,
                guardStrength: 100f,
                attackPower: 50f,
                attackFeature: AttackFeature.None);

            Assert.AreEqual(GuardResult.NoGuard, result);
        }

        // --- ジャストガードウィンドウ ---

        [Test]
        public void GuardJudgmentLogic_Judge_WithinJustGuardWindow_ReturnsJustGuard()
        {
            // guardTimeSinceStart=0.05 <= JustGuardWindow(0.1) => JustGuard
            GuardResult result = GuardJudgmentLogic.Judge(
                isGuarding: true,
                guardTimeSinceStart: 0.05f,
                guardStrength: 100f,
                attackPower: 50f,
                attackFeature: AttackFeature.None);

            Assert.AreEqual(GuardResult.JustGuard, result);
        }

        // --- ガードブレイク条件 ---

        [Test]
        public void GuardJudgmentLogic_Judge_WhenGuardBreak_ReturnsGuardBreak()
        {
            // attackPower > guardStrength => GuardBreak
            // guardTimeSinceStart=0.5 (JustGuardウィンドウ外)
            GuardResult result = GuardJudgmentLogic.Judge(
                isGuarding: true,
                guardTimeSinceStart: 0.5f,
                guardStrength: 40f,
                attackPower: 50f,
                attackFeature: AttackFeature.None);

            Assert.AreEqual(GuardResult.GuardBreak, result);
        }

        // --- ガード無効攻撃 ---

        [Test]
        public void GuardJudgmentLogic_Judge_Unparriable_ReturnsNoGuard()
        {
            // Unparriable flag => ガード中でもNoGuard
            GuardResult result = GuardJudgmentLogic.Judge(
                isGuarding: true,
                guardTimeSinceStart: 0.05f,
                guardStrength: 100f,
                attackPower: 50f,
                attackFeature: AttackFeature.Unparriable);

            Assert.AreEqual(GuardResult.NoGuard, result);
        }

        // --- 強化ガード判定 ---

        [Test]
        public void GuardJudgmentLogic_Judge_WithinEnhancedGuardWindow_ReturnsEnhancedGuard()
        {
            // guardTimeSinceStart=0.03 <= EnhancedGuardWindow(0.05)
            // JustGuardImmune が付いている場合、JustGuardは不成立だが
            // EnhancedGuardWindow内なので EnhancedGuard になる
            GuardResult result = GuardJudgmentLogic.Judge(
                isGuarding: true,
                guardTimeSinceStart: 0.03f,
                guardStrength: 100f,
                attackPower: 50f,
                attackFeature: AttackFeature.JustGuardImmune);

            Assert.AreEqual(GuardResult.EnhancedGuard, result);
        }

        // --- ダメージ軽減率 ---

        [Test]
        public void GuardJudgmentLogic_GetDamageReduction_ReturnsCorrectValues()
        {
            Assert.AreEqual(0f, GuardJudgmentLogic.GetDamageReduction(GuardResult.NoGuard), 0.001f);
            Assert.AreEqual(0.7f, GuardJudgmentLogic.GetDamageReduction(GuardResult.Guarded), 0.001f);
            Assert.AreEqual(1.0f, GuardJudgmentLogic.GetDamageReduction(GuardResult.JustGuard), 0.001f);
            Assert.AreEqual(0f, GuardJudgmentLogic.GetDamageReduction(GuardResult.GuardBreak), 0.001f);
            Assert.AreEqual(0.9f, GuardJudgmentLogic.GetDamageReduction(GuardResult.EnhancedGuard), 0.001f);
        }

        // --- JustGuardImmune ---

        [Test]
        public void GuardJudgmentLogic_Judge_JustGuardImmune_ReturnsGuardedInsteadOfJustGuard()
        {
            // JustGuardImmune flag + JustGuardウィンドウ内 => Guarded（JustGuardにならない）
            // ただしEnhancedGuardウィンドウ外
            GuardResult result = GuardJudgmentLogic.Judge(
                isGuarding: true,
                guardTimeSinceStart: 0.08f,
                guardStrength: 100f,
                attackPower: 50f,
                attackFeature: AttackFeature.JustGuardImmune);

            Assert.AreEqual(GuardResult.Guarded, result);
        }
    }
}
