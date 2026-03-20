using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// ダメージシステム結合テスト。
    /// ActionEffectProcessor → GuardJudgmentLogic → HpArmorLogic → HitReactionLogic
    /// の連携を検証する。
    /// </summary>
    public class Integration_DamageHitReactionTests
    {
        // ===== ActionEffect → HitReaction 結合 =====

        [Test]
        public void ActionEffectSuperArmor_ThenHitReaction_ReturnsNone()
        {
            // Arrange: SuperArmor が 0.0〜1.0 にアクティブ
            ActionEffect[] effects = new ActionEffect[]
            {
                new ActionEffect
                {
                    type = ActionEffectType.SuperArmor,
                    startTime = 0f,
                    duration = 1f,
                    value = 0f
                }
            };

            // Act: ActionEffectProcessor で評価
            ActionEffectProcessor.EffectState state =
                ActionEffectProcessor.Evaluate(effects, 0.5f);

            // HitReactionLogic に渡す
            HitReaction reaction = HitReactionLogic.Determine(
                state.hasSuperArmor,
                state.hasKnockbackImmunity,
                totalArmorBefore: 0f,
                hasKnockbackForce: true,
                GuardResult.NoGuard,
                ActState.Neutral);

            // Assert
            Assert.IsTrue(state.hasSuperArmor);
            Assert.AreEqual(HitReaction.None, reaction,
                "ActionEffectのSuperArmorがHitReactionLogicに正しく伝播");
        }

        [Test]
        public void ActionEffectKnockbackImmunity_ThenHitReaction_ConvertsFlinch()
        {
            ActionEffect[] effects = new ActionEffect[]
            {
                new ActionEffect
                {
                    type = ActionEffectType.KnockbackImmunity,
                    startTime = 0f,
                    duration = 5f,
                    value = 0f
                }
            };

            ActionEffectProcessor.EffectState state =
                ActionEffectProcessor.Evaluate(effects, 1f);

            HitReaction reaction = HitReactionLogic.Determine(
                state.hasSuperArmor,
                state.hasKnockbackImmunity,
                totalArmorBefore: 0f,
                hasKnockbackForce: true,
                GuardResult.NoGuard,
                ActState.Neutral);

            Assert.IsTrue(state.hasKnockbackImmunity);
            Assert.AreEqual(HitReaction.Flinch, reaction,
                "KnockbackImmunityで吹き飛ばし→怯みに変換");
        }

        [Test]
        public void ActionEffectArmor_PreventsHitReaction()
        {
            // アクションアーマー30を付与
            ActionEffect[] effects = new ActionEffect[]
            {
                new ActionEffect
                {
                    type = ActionEffectType.Armor,
                    startTime = 0f,
                    duration = 2f,
                    value = 30f
                }
            };

            ActionEffectProcessor.EffectState state =
                ActionEffectProcessor.Evaluate(effects, 0.5f);

            // ベースアーマー0 + アクションアーマー30 = 合計30
            float totalArmor = 0f + state.actionArmorValue;

            HitReaction reaction = HitReactionLogic.Determine(
                state.hasSuperArmor,
                state.hasKnockbackImmunity,
                totalArmor,
                hasKnockbackForce: true,
                GuardResult.NoGuard,
                ActState.Neutral);

            Assert.AreEqual(30f, state.actionArmorValue);
            Assert.AreEqual(HitReaction.None, reaction,
                "アクションアーマーがあれば怯まない");
        }

        // ===== Guard → HitReaction 結合 =====

        [Test]
        public void GuardSuccess_ThenHitReaction_ReturnsNone()
        {
            GuardResult guardResult = GuardJudgmentLogic.Judge(
                isGuarding: true,
                guardTimeSinceStart: 0.5f,
                guardStrength: 100f,
                attackPower: 50f,
                attackFeature: AttackFeature.None,
                guardDirection: GuardDirection.Front,
                isAttackFromFront: true,
                hasGuardAttackEffect: false);

            HitReaction reaction = HitReactionLogic.Determine(
                hasSuperArmor: false,
                hasKnockbackImmunity: false,
                totalArmorBefore: 0f,
                hasKnockbackForce: true,
                guardResult,
                ActState.Guarding);

            Assert.AreEqual(GuardResult.Guarded, guardResult);
            Assert.AreEqual(HitReaction.None, reaction,
                "ガード成功 → リアクションなし");
        }

        [Test]
        public void GuardBreak_ThenHitReaction_ReturnsGuardBreak()
        {
            GuardResult guardResult = GuardJudgmentLogic.Judge(
                isGuarding: true,
                guardTimeSinceStart: 0.5f,
                guardStrength: 30f,
                attackPower: 50f,
                attackFeature: AttackFeature.None,
                guardDirection: GuardDirection.Front,
                isAttackFromFront: true,
                hasGuardAttackEffect: false);

            HitReaction reaction = HitReactionLogic.Determine(
                hasSuperArmor: false,
                hasKnockbackImmunity: false,
                totalArmorBefore: 0f,
                hasKnockbackForce: true,
                guardResult,
                ActState.Guarding);

            Assert.AreEqual(GuardResult.GuardBreak, guardResult);
            Assert.AreEqual(HitReaction.GuardBreak, reaction);
        }

        [Test]
        public void JustGuard_ThenHitReaction_ReturnsNone_AndRecovery()
        {
            // ジャストガードタイミング
            GuardResult guardResult = GuardJudgmentLogic.Judge(
                isGuarding: true,
                guardTimeSinceStart: 0.05f,
                guardStrength: 30f,
                attackPower: 50f,
                attackFeature: AttackFeature.None,
                guardDirection: GuardDirection.Front,
                isAttackFromFront: true,
                hasGuardAttackEffect: false);

            // ジャストガード回復
            float stamina = 50f;
            float armor = 20f;
            GuardJudgmentLogic.ApplyJustGuardRecovery(
                ref stamina, 100f, ref armor, 100f);

            HitReaction reaction = HitReactionLogic.Determine(
                hasSuperArmor: false,
                hasKnockbackImmunity: false,
                totalArmorBefore: 0f,
                hasKnockbackForce: true,
                guardResult,
                ActState.Guarding);

            Assert.AreEqual(GuardResult.JustGuard, guardResult);
            Assert.AreEqual(HitReaction.None, reaction);
            Assert.AreEqual(65f, stamina, "スタミナ+15回復");
            Assert.AreEqual(30f, armor, "アーマー+10回復");
        }

        // ===== ガード方向不一致 → ノーガード → Flinch/Knockback =====

        [Test]
        public void GuardDirectionMismatch_ThenHitReaction_Flinch()
        {
            // 前方ガードに後方攻撃
            GuardResult guardResult = GuardJudgmentLogic.Judge(
                isGuarding: true,
                guardTimeSinceStart: 0.5f,
                guardStrength: 100f,
                attackPower: 50f,
                attackFeature: AttackFeature.None,
                guardDirection: GuardDirection.Front,
                isAttackFromFront: false,
                hasGuardAttackEffect: false);

            HitReaction reaction = HitReactionLogic.Determine(
                hasSuperArmor: false,
                hasKnockbackImmunity: false,
                totalArmorBefore: 0f,
                hasKnockbackForce: false,
                guardResult,
                ActState.Guarding);

            Assert.AreEqual(GuardResult.NoGuard, guardResult,
                "ガード方向不一致でNoGuard");
            Assert.AreEqual(HitReaction.Flinch, reaction);
        }

        [Test]
        public void GuardBothDirection_BackAttack_StillGuards()
        {
            GuardResult guardResult = GuardJudgmentLogic.Judge(
                isGuarding: true,
                guardTimeSinceStart: 0.5f,
                guardStrength: 100f,
                attackPower: 50f,
                attackFeature: AttackFeature.None,
                guardDirection: GuardDirection.Both,
                isAttackFromFront: false,
                hasGuardAttackEffect: false);

            HitReaction reaction = HitReactionLogic.Determine(
                hasSuperArmor: false,
                hasKnockbackImmunity: false,
                totalArmorBefore: 0f,
                hasKnockbackForce: true,
                guardResult,
                ActState.Guarding);

            Assert.AreEqual(GuardResult.Guarded, guardResult,
                "前後両方ガードなら後方攻撃もガード成功");
            Assert.AreEqual(HitReaction.None, reaction);
        }

        // ===== GuardAttack効果 → ブレイク防止 =====

        [Test]
        public void GuardAttackEffect_PreventsGuardBreak_ReturnsNone()
        {
            // ガード攻撃中: attackPower > guardStrength でもブレイクしない
            GuardResult guardResult = GuardJudgmentLogic.Judge(
                isGuarding: true,
                guardTimeSinceStart: 0.5f,
                guardStrength: 30f,
                attackPower: 50f,
                attackFeature: AttackFeature.None,
                guardDirection: GuardDirection.Front,
                isAttackFromFront: true,
                hasGuardAttackEffect: true);

            HitReaction reaction = HitReactionLogic.Determine(
                hasSuperArmor: false,
                hasKnockbackImmunity: false,
                totalArmorBefore: 0f,
                hasKnockbackForce: true,
                guardResult,
                ActState.Guarding);

            Assert.AreEqual(GuardResult.Guarded, guardResult,
                "GuardAttack中はブレイクせずGuardedに昇格");
            Assert.AreEqual(HitReaction.None, reaction);
        }

        // ===== ActionEffect → HpArmor → HitReaction フルフロー =====

        [Test]
        public void FullFlow_ActionArmor_AbsorbsHit_NoFlinch()
        {
            // ActionEffectでアーマー20付与
            ActionEffect[] effects = new ActionEffect[]
            {
                new ActionEffect
                {
                    type = ActionEffectType.Armor,
                    startTime = 0f,
                    duration = 2f,
                    value = 20f
                }
            };

            ActionEffectProcessor.EffectState state =
                ActionEffectProcessor.Evaluate(effects, 0.5f);

            float actionArmor = state.actionArmorValue;
            float baseArmor = 10f;
            float totalArmorBefore = baseArmor + actionArmor;
            int hp = 100;

            // ダメージ適用（アーマーブレイク15: アクション優先消費）
            HpArmorLogic.ApplyDamage(
                ref hp, ref baseArmor, 20, 15f, ref actionArmor);

            // HitReaction: アーマーが事前に > 0 だったので None
            HitReaction reaction = HitReactionLogic.Determine(
                state.hasSuperArmor,
                state.hasKnockbackImmunity,
                totalArmorBefore,
                hasKnockbackForce: true,
                GuardResult.NoGuard,
                ActState.Attacking);

            Assert.AreEqual(HitReaction.None, reaction,
                "アーマーがある状態で被弾 → 怯まない");
            Assert.AreEqual(5f, actionArmor,
                "アクションアーマー: 20 - 15 = 5");
            Assert.AreEqual(10f, baseArmor,
                "ベースアーマーは消費されない");
        }

        [Test]
        public void FullFlow_ActionArmor_Overflows_ToBaseArmor()
        {
            ActionEffect[] effects = new ActionEffect[]
            {
                new ActionEffect
                {
                    type = ActionEffectType.Armor,
                    startTime = 0f,
                    duration = 2f,
                    value = 5f
                }
            };

            ActionEffectProcessor.EffectState state =
                ActionEffectProcessor.Evaluate(effects, 0.5f);

            float actionArmor = state.actionArmorValue;
            float baseArmor = 10f;
            float totalArmorBefore = baseArmor + actionArmor;
            int hp = 100;

            // アーマーブレイク20: アクション5消費 → 残り15がベースへ
            HpArmorLogic.ApplyDamage(
                ref hp, ref baseArmor, 20, 20f, ref actionArmor);

            HitReaction reaction = HitReactionLogic.Determine(
                state.hasSuperArmor,
                state.hasKnockbackImmunity,
                totalArmorBefore,
                hasKnockbackForce: true,
                GuardResult.NoGuard,
                ActState.Attacking);

            Assert.AreEqual(HitReaction.None, reaction,
                "事前アーマー > 0 だったので怯まない");
            Assert.AreEqual(0f, actionArmor, "アクションアーマー全消費");
            Assert.AreEqual(-5f, baseArmor, "ベースアーマー: 10 - 15 = -5");
        }

        [Test]
        public void FullFlow_NoArmor_WithKnockback_ReturnsKnockback()
        {
            int hp = 100;
            float baseArmor = 0f;
            float actionArmor = 0f;
            float totalArmorBefore = 0f;

            HpArmorLogic.ApplyDamage(
                ref hp, ref baseArmor, 30, 0f, ref actionArmor);

            HitReaction reaction = HitReactionLogic.Determine(
                hasSuperArmor: false,
                hasKnockbackImmunity: false,
                totalArmorBefore,
                hasKnockbackForce: true,
                GuardResult.NoGuard,
                ActState.Neutral);

            Assert.AreEqual(HitReaction.Knockback, reaction);
            Assert.AreEqual(70, hp);
        }

        // ===== DamageReduction + Guard Reduction 併用 =====

        [Test]
        public void FullFlow_GuardReduction_PlusActionEffectReduction()
        {
            // ガード成功（70%軽減） + ActionEffect DamageReduction（50%軽減）
            ActionEffect[] effects = new ActionEffect[]
            {
                new ActionEffect
                {
                    type = ActionEffectType.DamageReduction,
                    startTime = 0f,
                    duration = 2f,
                    value = 0.5f
                }
            };

            ActionEffectProcessor.EffectState state =
                ActionEffectProcessor.Evaluate(effects, 0.5f);

            // ガード判定
            GuardResult guardResult = GuardJudgmentLogic.Judge(
                isGuarding: true,
                guardTimeSinceStart: 0.5f,
                guardStrength: 100f,
                attackPower: 50f,
                attackFeature: AttackFeature.None,
                guardDirection: GuardDirection.Front,
                isAttackFromFront: true,
                hasGuardAttackEffect: false);

            // ダメージ計算: 100 * (1-0.7) * (1-0.5) = 100 * 0.3 * 0.5 = 15
            int rawDamage = 100;
            float guardReduction = GuardJudgmentLogic.GetDamageReduction(guardResult);
            int afterGuard = UnityEngine.Mathf.FloorToInt(rawDamage * (1f - guardReduction));
            int finalDamage = UnityEngine.Mathf.FloorToInt(afterGuard * (1f - state.damageReduction));

            Assert.AreEqual(GuardResult.Guarded, guardResult);
            Assert.AreEqual(0.7f, guardReduction, 0.001f);
            Assert.AreEqual(30, afterGuard, "ガード後: 100 * 0.3 = 30");
            Assert.AreEqual(15, finalDamage, "DamageReduction後: 30 * 0.5 = 15");
        }

        // ===== ヒットスタン連鎖テスト =====

        [Test]
        public void HitstunChain_Flinch_ThenKnockback_Escalates()
        {
            // 1回目: アーマーなし、吹き飛ばしなし → Flinch
            HitReaction first = HitReactionLogic.Determine(
                hasSuperArmor: false,
                hasKnockbackImmunity: false,
                totalArmorBefore: 0f,
                hasKnockbackForce: false,
                GuardResult.NoGuard,
                ActState.Neutral);

            Assert.AreEqual(HitReaction.Flinch, first);

            // 2回目: Flinch中に吹き飛ばし攻撃（アーマー回復して5ある）→ アーマー無視でKnockback
            HitReaction second = HitReactionLogic.Determine(
                hasSuperArmor: false,
                hasKnockbackImmunity: false,
                totalArmorBefore: 5f,
                hasKnockbackForce: true,
                GuardResult.NoGuard,
                ActState.Flinch);

            Assert.AreEqual(HitReaction.Knockback, second,
                "Flinch中の吹き飛ばしはアーマーを無視してKnockback");
        }

        // ===== 無敵 → HitReaction は呼ばれない確認 =====

        [Test]
        public void Invincible_ActionEffect_SkipsHitReaction()
        {
            ActionEffect[] effects = new ActionEffect[]
            {
                new ActionEffect
                {
                    type = ActionEffectType.Invincible,
                    startTime = 0f,
                    duration = 1f,
                    value = 0f
                }
            };

            ActionEffectProcessor.EffectState state =
                ActionEffectProcessor.Evaluate(effects, 0.5f);

            // DamageReceiver と同じフロー: 無敵なら即 return
            Assert.IsTrue(state.isInvincible,
                "無敵フラグが立っている場合、ダメージ処理自体がスキップされる");
        }
    }
}
