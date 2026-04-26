using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Flinch 解除時のアーマー満タンリセット仕様 (FUTURE_TASKS L528) のテスト。
    /// Flinch 中は currentArmor = 0 が強制されるが、解除瞬間に maxArmor へ満タン復帰する。
    /// 純ロジック判定 <see cref="HitReactionLogic.ShouldResetArmorOnFlinchExit"/> の契約を検証する。
    /// </summary>
    [TestFixture]
    public class FlinchArmorResetTests
    {
        // ---- ShouldResetArmorOnFlinchExit 単体テスト ----

        [Test]
        public void ShouldResetArmorOnFlinchExit_FlinchToNeutral_ReturnsTrue()
        {
            bool result = HitReactionLogic.ShouldResetArmorOnFlinchExit(
                ActState.Flinch, ActState.Neutral);
            Assert.IsTrue(result, "Flinch → Neutral 遷移は armor リセットの対象");
        }

        [Test]
        public void ShouldResetArmorOnFlinchExit_FlinchToFlinch_ReturnsFalse()
        {
            bool result = HitReactionLogic.ShouldResetArmorOnFlinchExit(
                ActState.Flinch, ActState.Flinch);
            Assert.IsFalse(result, "Flinch 継続中はリセット対象外 (上書きなし仕様と整合)");
        }

        [Test]
        public void ShouldResetArmorOnFlinchExit_NeutralToNeutral_ReturnsFalse()
        {
            bool result = HitReactionLogic.ShouldResetArmorOnFlinchExit(
                ActState.Neutral, ActState.Neutral);
            Assert.IsFalse(result, "Flinch を経由しない遷移はリセット対象外");
        }

        [Test]
        public void ShouldResetArmorOnFlinchExit_NeutralToFlinch_ReturnsFalse()
        {
            bool result = HitReactionLogic.ShouldResetArmorOnFlinchExit(
                ActState.Neutral, ActState.Flinch);
            Assert.IsFalse(result, "Flinch 突入はリセット対象外 (Flinch 中は currentArmor=0 強制)");
        }

        [Test]
        public void ShouldResetArmorOnFlinchExit_FlinchToKnockback_ReturnsTrue()
        {
            bool result = HitReactionLogic.ShouldResetArmorOnFlinchExit(
                ActState.Flinch, ActState.Knockbacked);
            Assert.IsTrue(result, "Flinch → Knockbacked への遷移もリセット対象 (Flinch 解除条件は state 不問)");
        }

        [Test]
        public void ShouldResetArmorOnFlinchExit_FlinchToDead_ReturnsTrue()
        {
            bool result = HitReactionLogic.ShouldResetArmorOnFlinchExit(
                ActState.Flinch, ActState.Dead);
            Assert.IsTrue(result, "Flinch → Dead もリセット対象 (死亡時は無害な副作用)");
        }

        // ---- 行動連続テスト: 複数フレームの遷移をシミュレート ----

        [Test]
        public void ShouldResetArmorOnFlinchExit_FlinchSequence_ResetsOnlyOnce()
        {
            // Neutral → Flinch → Flinch → Neutral → Neutral のシーケンスで
            // リセット発火は Flinch → Neutral の 1 フレームのみ
            ActState[] sequence =
            {
                ActState.Neutral, ActState.Flinch, ActState.Flinch,
                ActState.Neutral, ActState.Neutral
            };
            int resetCount = 0;
            for (int i = 1; i < sequence.Length; i++)
            {
                if (HitReactionLogic.ShouldResetArmorOnFlinchExit(sequence[i - 1], sequence[i]))
                {
                    resetCount++;
                }
            }
            Assert.AreEqual(1, resetCount, "Flinch → Neutral の 1 遷移のみでリセット発火");
        }

        // ---- アーマー満タンリセット結合シミュレート ----

        [Test]
        public void FlinchArmorReset_FlinchExitWithArmorZero_RestoresToMaxArmor()
        {
            // Flinch 中に currentArmor=0 強制された後、解除瞬間に maxArmor 復帰する契約
            float maxArmor = 50f;
            float currentArmor = 0f;  // Flinch 中に強制 0 された状態
            ActState previousState = ActState.Flinch;
            ActState currentState = ActState.Neutral;

            if (HitReactionLogic.ShouldResetArmorOnFlinchExit(previousState, currentState))
            {
                currentArmor = maxArmor;
            }

            Assert.AreEqual(maxArmor, currentArmor,
                "Flinch 解除時に currentArmor が maxArmor へ満タン復帰");
        }

        [Test]
        public void FlinchArmorReset_DuringFlinch_ArmorRemainsZero()
        {
            // Flinch 継続中はリセットされない
            float maxArmor = 50f;
            float currentArmor = 0f;

            if (HitReactionLogic.ShouldResetArmorOnFlinchExit(ActState.Flinch, ActState.Flinch))
            {
                currentArmor = maxArmor;
            }

            Assert.AreEqual(0f, currentArmor, "Flinch 継続中は currentArmor=0 のまま");
        }
    }
}
