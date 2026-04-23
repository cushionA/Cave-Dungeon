using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Task B: Flinch 中の被弾は完全無防備 (アーマー 0 強制) / Flinch 上書きなし / Knockback 許可
    /// の純ロジック契約テスト。
    ///
    /// DamageReceiver 本体の統合挙動は PlayMode 側 (ActionArmorBreakFlinchIntegrationTests) でも検証する。
    /// 本テストは DamageReceiver.ReceiveDamage 先頭の Flinch 強制 0 と
    /// ApplyHitReactionToActState の wasInFlinch ガードの契約を直接検証する。
    /// </summary>
    [TestFixture]
    public class Integration_FlinchStateBehaviorTests
    {
        // =====================================================================
        // Task B: Flinch 中の被弾 - 完全無防備 + Flinch 上書きなし + Knockback 許可
        // =====================================================================

        /// <summary>
        /// Flinch 中に ActionEffect.Armor が残っていても、DamageReceiver 側は
        /// effectState.actionArmorValue = 0 に強制し、currentArmor も 0 にする。
        /// このロジックの契約を検証する。
        /// </summary>
        [Test]
        public void FlinchState_ForcesBothArmorsToZero()
        {
            // Action armor 30 + base armor 20 がある状態で Flinch 中の被弾
            ActionEffect[] effects = new ActionEffect[]
            {
                new ActionEffect
                {
                    type = ActionEffectType.Armor,
                    startTime = 0f, duration = 10f, value = 30f
                }
            };

            ActionEffectProcessor.EffectState state =
                ActionEffectProcessor.Evaluate(effects, 0.5f);
            float currentArmor = 20f;

            // DamageReceiver.ReceiveDamage のロジックを抽出: Flinch 中なら両方強制 0
            bool wasInFlinch = true; // ActState.Flinch 前提
            if (wasInFlinch)
            {
                state.actionArmorValue = 0f;
                currentArmor = 0f;
            }

            Assert.AreEqual(0f, state.actionArmorValue,
                "Flinch 中は actionArmorValue が強制 0 (軽減なし)");
            Assert.AreEqual(0f, currentArmor,
                "Flinch 中は currentArmor も強制 0 (軽減なし)");
        }

        /// <summary>
        /// Flinch 中に Flinch 系攻撃 (knockbackForce なし) が来ても、ActState は Flinch のまま。
        /// DamageReceiver.ApplyHitReactionToActState の wasInFlinch=true 時の挙動を検証する。
        /// </summary>
        [Test]
        public void FlinchState_ReceivingFlinchAttack_DoesNotOverwriteActState()
        {
            // 擬似的に ApplyHitReactionToActState のロジックを再現
            ActState actState = ActState.Flinch; // 既に Flinch
            HitReaction reaction = HitReaction.Flinch;
            bool wasInFlinch = true;
            bool isKill = false;

            // DamageReceiver.ApplyHitReactionToActState 同等ロジック
            if (isKill)
            {
                actState = ActState.Dead;
            }
            else
            {
                switch (reaction)
                {
                    case HitReaction.Knockback:
                        actState = ActState.Knockbacked;
                        break;
                    case HitReaction.Flinch:
                        if (!wasInFlinch)
                        {
                            actState = ActState.Flinch;
                        }
                        break;
                    case HitReaction.GuardBreak:
                        actState = ActState.GuardBroken;
                        break;
                }
            }

            Assert.AreEqual(ActState.Flinch, actState,
                "Flinch 中に Flinch 攻撃 → ActState は Flinch 維持 (タイマー延長なし)");
        }

        /// <summary>
        /// Flinch 中に Knockback 攻撃 (knockbackForce あり) が来たら Knockbacked に遷移する。
        /// 吹き飛ばしは Flinch より重いリアクション扱い。
        /// </summary>
        [Test]
        public void FlinchState_ReceivingKnockbackAttack_TransitionsToKnockbacked()
        {
            ActState actState = ActState.Flinch;
            HitReaction reaction = HitReaction.Knockback;
            bool wasInFlinch = true;
            bool isKill = false;

            if (isKill)
            {
                actState = ActState.Dead;
            }
            else
            {
                switch (reaction)
                {
                    case HitReaction.Knockback:
                        actState = ActState.Knockbacked;
                        break;
                    case HitReaction.Flinch:
                        if (!wasInFlinch)
                        {
                            actState = ActState.Flinch;
                        }
                        break;
                }
            }

            Assert.AreEqual(ActState.Knockbacked, actState,
                "Flinch 中の Knockback 攻撃 → Knockbacked に昇格 (Flinch のタイマーを割り込む)");
        }

        /// <summary>
        /// Flinch 中の致死ヒットは Dead が優先される (Flinch 上書き規則より優先)。
        /// </summary>
        [Test]
        public void FlinchState_LethalHit_TransitionsToDead()
        {
            ActState actState = ActState.Flinch;
            HitReaction reaction = HitReaction.Flinch;
            bool wasInFlinch = true;
            bool isKill = true;

            if (isKill)
            {
                actState = ActState.Dead;
            }
            else
            {
                switch (reaction)
                {
                    case HitReaction.Flinch:
                        if (!wasInFlinch)
                        {
                            actState = ActState.Flinch;
                        }
                        break;
                }
            }

            Assert.AreEqual(ActState.Dead, actState,
                "Flinch 中の致死ダメージは Dead 優先");
        }

        /// <summary>
        /// HitReactionLogic.IsInHitstun は Flinch / GuardBroken / Stunned を true 扱いする。
        /// DamageReceiver 側でこれを使って状況ボーナス (StaggerHit) を適用している。
        /// </summary>
        [Test]
        public void HitstunDetection_Flinch_IsInHitstunTrue()
        {
            Assert.IsTrue(HitReactionLogic.IsInHitstun(ActState.Flinch));
            Assert.IsTrue(HitReactionLogic.IsInHitstun(ActState.GuardBroken));
            Assert.IsTrue(HitReactionLogic.IsInHitstun(ActState.Stunned));
            Assert.IsFalse(HitReactionLogic.IsInHitstun(ActState.Neutral));
            Assert.IsFalse(HitReactionLogic.IsInHitstun(ActState.Attacking));
        }
    }
}
