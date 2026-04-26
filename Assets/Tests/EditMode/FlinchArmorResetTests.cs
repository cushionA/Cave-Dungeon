using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// スタン解除時のアーマー満タンリセット仕様 (FUTURE_TASKS L528) のテスト。
    /// 対象スタン: Flinch / GuardBroken / Stunned / Knockbacked。
    /// いずれも armor=0 が前提または強制された無防備状態のため、抜けた瞬間に maxArmor へ満タン復帰する。
    /// 純ロジック判定 <see cref="HitReactionLogic.ShouldResetArmorOnStunExit"/> および
    /// <see cref="HitReactionLogic.IsArmorResetStun"/> の契約を検証する。
    /// </summary>
    [TestFixture]
    public class FlinchArmorResetTests
    {
        // ---- IsArmorResetStun 単体テスト ----

        [Test]
        public void IsArmorResetStun_Flinch_ReturnsTrue()
        {
            Assert.IsTrue(HitReactionLogic.IsArmorResetStun(ActState.Flinch),
                "Flinch は armor リセット対象スタン");
        }

        [Test]
        public void IsArmorResetStun_GuardBroken_ReturnsTrue()
        {
            Assert.IsTrue(HitReactionLogic.IsArmorResetStun(ActState.GuardBroken),
                "GuardBroken は armor リセット対象 (スタミナ削り切り後の無防備状態)");
        }

        [Test]
        public void IsArmorResetStun_Stunned_ReturnsTrue()
        {
            Assert.IsTrue(HitReactionLogic.IsArmorResetStun(ActState.Stunned),
                "Stunned は armor リセット対象 (状態異常蓄積による気絶)");
        }

        [Test]
        public void IsArmorResetStun_Knockbacked_ReturnsTrue()
        {
            Assert.IsTrue(HitReactionLogic.IsArmorResetStun(ActState.Knockbacked),
                "Knockbacked は armor リセット対象 (吹き飛ばしは armor 削り切りが前提)");
        }

        [Test]
        public void IsArmorResetStun_Neutral_ReturnsFalse()
        {
            Assert.IsFalse(HitReactionLogic.IsArmorResetStun(ActState.Neutral),
                "Neutral はスタンではない");
        }

        [Test]
        public void IsArmorResetStun_WakeUp_ReturnsFalse()
        {
            Assert.IsFalse(HitReactionLogic.IsArmorResetStun(ActState.WakeUp),
                "WakeUp は無敵状態だがスタンとして扱わない (Knockbacked → WakeUp でリセット発火)");
        }

        [Test]
        public void IsArmorResetStun_Dead_ReturnsFalse()
        {
            Assert.IsFalse(HitReactionLogic.IsArmorResetStun(ActState.Dead),
                "Dead はスタンではない");
        }

        // ---- ShouldResetArmorOnStunExit 単体テスト ----

        [Test]
        public void ShouldResetArmorOnStunExit_FlinchToNeutral_ReturnsTrue()
        {
            bool result = HitReactionLogic.ShouldResetArmorOnStunExit(
                ActState.Flinch, ActState.Neutral);
            Assert.IsTrue(result, "Flinch → Neutral 遷移は armor リセットの対象");
        }

        [Test]
        public void ShouldResetArmorOnStunExit_GuardBrokenToNeutral_ReturnsTrue()
        {
            bool result = HitReactionLogic.ShouldResetArmorOnStunExit(
                ActState.GuardBroken, ActState.Neutral);
            Assert.IsTrue(result, "GuardBroken → Neutral 遷移は armor リセットの対象");
        }

        [Test]
        public void ShouldResetArmorOnStunExit_StunnedToNeutral_ReturnsTrue()
        {
            bool result = HitReactionLogic.ShouldResetArmorOnStunExit(
                ActState.Stunned, ActState.Neutral);
            Assert.IsTrue(result, "Stunned → Neutral 遷移は armor リセットの対象");
        }

        [Test]
        public void ShouldResetArmorOnStunExit_KnockbackedToWakeUp_ReturnsTrue()
        {
            bool result = HitReactionLogic.ShouldResetArmorOnStunExit(
                ActState.Knockbacked, ActState.WakeUp);
            Assert.IsTrue(result,
                "Knockbacked → WakeUp 遷移は armor リセットの対象 (起き上がり開始時に復帰)");
        }

        [Test]
        public void ShouldResetArmorOnStunExit_FlinchToFlinch_ReturnsFalse()
        {
            bool result = HitReactionLogic.ShouldResetArmorOnStunExit(
                ActState.Flinch, ActState.Flinch);
            Assert.IsFalse(result, "Flinch 継続中はリセット対象外");
        }

        [Test]
        public void ShouldResetArmorOnStunExit_FlinchToKnockbacked_ReturnsFalse()
        {
            bool result = HitReactionLogic.ShouldResetArmorOnStunExit(
                ActState.Flinch, ActState.Knockbacked);
            Assert.IsFalse(result,
                "Flinch → Knockbacked はスタン同士の遷移なのでリセット対象外 (連続無防備状態として扱う)");
        }

        [Test]
        public void ShouldResetArmorOnStunExit_GuardBrokenToStunned_ReturnsFalse()
        {
            bool result = HitReactionLogic.ShouldResetArmorOnStunExit(
                ActState.GuardBroken, ActState.Stunned);
            Assert.IsFalse(result, "スタン同士の遷移はリセット対象外");
        }

        [Test]
        public void ShouldResetArmorOnStunExit_NeutralToNeutral_ReturnsFalse()
        {
            bool result = HitReactionLogic.ShouldResetArmorOnStunExit(
                ActState.Neutral, ActState.Neutral);
            Assert.IsFalse(result, "スタンを経由しない遷移はリセット対象外");
        }

        [Test]
        public void ShouldResetArmorOnStunExit_NeutralToFlinch_ReturnsFalse()
        {
            bool result = HitReactionLogic.ShouldResetArmorOnStunExit(
                ActState.Neutral, ActState.Flinch);
            Assert.IsFalse(result, "スタン突入はリセット対象外 (スタン中は armor=0 が前提)");
        }

        [Test]
        public void ShouldResetArmorOnStunExit_FlinchToDead_ReturnsTrue()
        {
            bool result = HitReactionLogic.ShouldResetArmorOnStunExit(
                ActState.Flinch, ActState.Dead);
            Assert.IsTrue(result, "Flinch → Dead もリセット対象 (死亡時は無害な副作用)");
        }

        // ---- 行動連続テスト: 複数フレームの遷移をシミュレート ----

        [Test]
        public void ShouldResetArmorOnStunExit_FlinchSequence_ResetsOnlyOnce()
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
                if (HitReactionLogic.ShouldResetArmorOnStunExit(sequence[i - 1], sequence[i]))
                {
                    resetCount++;
                }
            }
            Assert.AreEqual(1, resetCount, "Flinch → Neutral の 1 遷移のみでリセット発火");
        }

        [Test]
        public void ShouldResetArmorOnStunExit_FlinchToKnockbackedToWakeUp_ResetsOnceAtWakeUp()
        {
            // Neutral → Flinch → Knockbacked → WakeUp のシーケンスで
            // Flinch → Knockbacked はスタン同士なのでリセットなし、Knockbacked → WakeUp で 1 回のみ発火
            ActState[] sequence =
            {
                ActState.Neutral, ActState.Flinch, ActState.Knockbacked, ActState.WakeUp
            };
            int resetCount = 0;
            for (int i = 1; i < sequence.Length; i++)
            {
                if (HitReactionLogic.ShouldResetArmorOnStunExit(sequence[i - 1], sequence[i]))
                {
                    resetCount++;
                }
            }
            Assert.AreEqual(1, resetCount,
                "Flinch → Knockbacked → WakeUp で WakeUp 突入時のみリセット発火 (連続スタン中はリセットなし)");
        }

        // ---- アーマー満タンリセット結合シミュレート ----

        [Test]
        public void StunArmorReset_FlinchExitWithArmorZero_RestoresToMaxArmor()
        {
            // Flinch 中に currentArmor=0 強制された後、解除瞬間に maxArmor 復帰する契約
            float maxArmor = 50f;
            float currentArmor = 0f;  // Flinch 中に強制 0 された状態
            ActState previousState = ActState.Flinch;
            ActState currentState = ActState.Neutral;

            if (HitReactionLogic.ShouldResetArmorOnStunExit(previousState, currentState))
            {
                currentArmor = maxArmor;
            }

            Assert.AreEqual(maxArmor, currentArmor,
                "Flinch 解除時に currentArmor が maxArmor へ満タン復帰");
        }

        [Test]
        public void StunArmorReset_KnockbackedExitWithArmorZero_RestoresToMaxArmor()
        {
            // Knockbacked → WakeUp 解除時に currentArmor=0 から maxArmor 復帰
            float maxArmor = 50f;
            float currentArmor = 0f;
            ActState previousState = ActState.Knockbacked;
            ActState currentState = ActState.WakeUp;

            if (HitReactionLogic.ShouldResetArmorOnStunExit(previousState, currentState))
            {
                currentArmor = maxArmor;
            }

            Assert.AreEqual(maxArmor, currentArmor,
                "Knockbacked → WakeUp 解除時に currentArmor が maxArmor へ満タン復帰");
        }

        [Test]
        public void StunArmorReset_DuringFlinch_ArmorRemainsZero()
        {
            // Flinch 継続中はリセットされない
            float maxArmor = 50f;
            float currentArmor = 0f;

            if (HitReactionLogic.ShouldResetArmorOnStunExit(ActState.Flinch, ActState.Flinch))
            {
                currentArmor = maxArmor;
            }

            Assert.AreEqual(0f, currentArmor, "Flinch 継続中は currentArmor=0 のまま");
        }
    }
}
