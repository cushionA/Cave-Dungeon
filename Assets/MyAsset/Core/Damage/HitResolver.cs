using System;
using UnityEngine;

namespace Game.Core
{
    public struct HitContext
    {
        public ActionEffectProcessor.EffectState actionEffects;
        public CharacterStatusEffects targetEffects;
        public float attackerAttackMultiplier;
        public bool isAttackFromFront;
        public float currentTime;
        public SituationalBonusConfig bonusConfig;
    }

    public struct HitResolution
    {
        public DamageResult result;
        public bool ignored;
        public bool interruptAction;
        public Vector2 knockback;
    }

    public static class HitResolver
    {
        public static HitResolution Resolve(in DamageData attack, ref CharacterVitals vitals,
            in CombatStats combat, ref CharacterFlags flags, ref CharacterDefenseState defense,
            in HitContext context)
        {
            ActState currentState = flags.ActState;
            ActionEffectProcessor.EffectState effects = context.actionEffects;
            if (vitals.currentHp <= 0 || currentState == ActState.Dead || flags.IsVanished
                || effects.isInvincible || WakeUpLogic.IsWakeUpState(currentState))
            {
                return new HitResolution { ignored = true };
            }

            bool wasInFlinch = currentState == ActState.Flinch;
            if (wasInFlinch)
            {
                effects.actionArmorValue = 0f;
                defense.actionArmorConsumed = 0f;
                vitals.currentArmor = 0f;
            }

            GuardEvaluation guard = GuardJudgmentLogic.Evaluate(
                defense.isGuarding && !context.targetEffects.BlocksActions,
                defense.guardElapsedTime,
                context.currentTime < defense.continuousJustGuardUntil,
                attack.feature, combat.guardStats.guardDirection, context.isAttackFromFront,
                effects.hasGuardAttackEffect, vitals.currentStamina,
                combat.guardStats.guardStrength, attack.guardDamage);

            if (guard.result == GuardResult.JustGuard)
            {
                GuardJudgmentLogic.ApplyJustGuardRecovery(ref vitals.currentStamina, vitals.maxStamina,
                    ref vitals.currentArmor, vitals.maxArmor);
                defense.continuousJustGuardUntil = context.currentTime + GuardJudgmentLogic.k_ContinuousJustGuardWindow;
            }
            float staminaBefore = vitals.currentStamina;
            vitals.currentStamina = Math.Max(0f, vitals.currentStamina - guard.staminaDrain);
            float staminaDamage = staminaBefore - vitals.currentStamina;

            int damage = CalculateDamage(attack, combat, currentState, guard.result, effects, context,
                out SituationalBonus bonus);
            float actionArmor = Math.Max(0f, effects.actionArmorValue - defense.actionArmorConsumed);
            float actionArmorBefore = actionArmor;
            float armorBefore = vitals.currentArmor;
            float armorBreak = attack.armorBreakValue;
            if (guard.result == GuardResult.JustGuard)
            {
                armorBreak = attack.isProjectile ? 0f : armorBreak * (1f - attack.justGuardResistance / 100f);
            }

            (int actualDamage, bool isKill, bool _) = HpArmorLogic.ApplyDamage(
                ref vitals.currentHp, ref vitals.currentArmor, damage, armorBreak, ref actionArmor);
            float actionArmorDamage = actionArmorBefore - actionArmor;
            defense.actionArmorConsumed += actionArmorDamage;
            bool interrupt = actionArmorBefore > 0f && actionArmorDamage > 0f && actionArmor <= 0f
                && !effects.hasSuperArmor && !GuardJudgmentLogic.IsGuardSucceeded(guard.result);
            bool hasKnockback = attack.knockbackForce.sqrMagnitude > 0.01f;
            HitReaction reaction = HitReactionLogic.Determine(effects.hasSuperArmor,
                effects.hasKnockbackImmunity, armorBefore + actionArmorBefore,
                hasKnockback, guard.result, currentState);
            if (interrupt && reaction == HitReaction.None)
            {
                reaction = hasKnockback && !effects.hasKnockbackImmunity ? HitReaction.Knockback : HitReaction.Flinch;
            }
            ApplyReaction(ref flags, reaction, isKill, wasInFlinch);
            if (damage > 0)
            {
                defense.armorRecoveryTimer = defense.armorRecoveryDelay;
            }

            return new HitResolution
            {
                result = new DamageResult
                {
                    actionInstanceId = attack.actionInstanceId,
                    totalDamage = actualDamage, guardResult = guard.result,
                    hitReaction = reaction, situationalBonus = bonus, isKill = isKill,
                    armorDamage = armorBefore - vitals.currentArmor,
                    actionArmorDamage = actionArmorDamage, guardDamage = staminaDamage,
                },
                interruptAction = interrupt && !isKill,
                knockback = hasKnockback && reaction == HitReaction.Knockback
                    ? HpArmorLogic.CalculateKnockback(attack.knockbackForce, combat.knockbackResistance) : default,
            };
        }

        public static void TickDefense(ref CharacterVitals vitals, ActState currentState,
            ref CharacterDefenseState defense, float deltaTime, bool hasActionEffects)
        {
            if (defense.isGuarding)
            {
                defense.guardElapsedTime += deltaTime;
            }
            if (hasActionEffects)
            {
                defense.actionElapsedTime += deltaTime;
            }
            TickRecovery(ref vitals, currentState, ref defense, deltaTime);
        }

        public static void TickRecovery(ref CharacterVitals vitals, ActState currentState,
            ref CharacterDefenseState defense, float deltaTime)
        {
            if (vitals.currentHp > 0 && defense.armorRecoveryRate > 0f)
            {
                if (defense.armorRecoveryTimer > 0f)
                {
                    defense.armorRecoveryTimer -= deltaTime;
                }
                else
                {
                    HpArmorLogic.RecoverArmor(ref vitals.currentArmor, defense.maxArmor,
                        defense.armorRecoveryRate, deltaTime);
                }
            }
            if (HitReactionLogic.ShouldResetArmorOnStunExit(defense.previousActState, currentState))
            {
                vitals.currentArmor = vitals.maxArmor;
            }
            defense.previousActState = currentState;
        }

        private static int CalculateDamage(in DamageData attack, in CombatStats combat, ActState currentState,
            GuardResult guard, in ActionEffectProcessor.EffectState effects, in HitContext context,
            out SituationalBonus bonus)
        {
            bonus = SituationalBonus.None;
            if (guard == GuardResult.JustGuard)
            {
                return 0;
            }
            bool guardSucceeded = GuardJudgmentLogic.IsGuardSucceeded(guard);
            ElementalStatus attackStats = DamageCalculator.ScaleStats(attack.damage, context.attackerAttackMultiplier);
            ElementalStatus defenseStats = DamageCalculator.ScaleStats(combat.defense, context.targetEffects.DefenseMultiplier);
            int damage = DamageCalculator.CalculateTotalDamageWithElementalCut(attackStats, attack.motionValue,
                defenseStats, combat.guardStats, guardSucceeded);
            if (!guardSucceeded)
            {
                (float multiplier, SituationalBonus selected) = SituationalBonusLogic.Evaluate(
                    SituationalBonusLogic.IsTargetAttacking(currentState), !context.isAttackFromFront,
                    HitReactionLogic.IsInHitstun(currentState), context.bonusConfig);
                if (multiplier > 1f)
                {
                    damage = ScaleDamage(damage, multiplier);
                    bonus = selected;
                }
            }
            damage = ScaleDamage(damage, context.targetEffects.DamageTakenMultiplier);
            return effects.damageReduction > 0f ? ScaleDamage(damage, 1f - effects.damageReduction) : damage;
        }

        private static int ScaleDamage(int damage, float multiplier)
        {
            return (int)Math.Max(0d, Math.Min(int.MaxValue, Math.Floor(damage * multiplier)));
        }

        private static void ApplyReaction(ref CharacterFlags flags, HitReaction reaction, bool isKill, bool wasInFlinch)
        {
            if (isKill)
            {
                flags.ActState = ActState.Dead;
                return;
            }
            switch (reaction)
            {
                case HitReaction.Knockback:
                    flags.ActState = ActState.Knockbacked;
                    break;
                case HitReaction.Flinch:
                    if (!wasInFlinch)
                    {
                        flags.ActState = ActState.Flinch;
                    }
                    break;
                case HitReaction.GuardBreak:
                    flags.ActState = ActState.GuardBroken;
                    break;
            }
        }
    }
}
