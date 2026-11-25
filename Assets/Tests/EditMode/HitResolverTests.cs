using Game.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    public class HitResolverTests
    {
        private static CharacterVitals Vitals(float armor = 100f, float stamina = 100f)
        {
            return new CharacterVitals
            {
                currentHp = 1000, maxHp = 1000,
                currentStamina = stamina, maxStamina = 100f,
                currentArmor = armor, maxArmor = 100f,
            };
        }

        private static DamageData Attack(float guardDamage = 0f, float armorDamage = 0f)
        {
            return new DamageData
            {
                damage = new ElementalStatus { slash = 20 }, motionValue = 1f,
                guardDamage = guardDamage, armorBreakValue = armorDamage,
            };
        }

        private static HitContext Context()
        {
            return new HitContext
            {
                attackerAttackMultiplier = 1f, isAttackFromFront = true,
                currentTime = 10f, bonusConfig = SituationalBonusConfig.Default,
            };
        }

        private static CombatStats Combat(float guardStrength = 0f)
        {
            return new CombatStats
            {
                guardStats = new GuardStats { guardDirection = GuardDirection.Both, guardStrength = guardStrength },
            };
        }

        [Test]
        public void HitResolver_VanishedTarget_IgnoresDamageWithoutChangingResourcesOrGuard()
        {
            CharacterVitals vitals = Vitals();
            CharacterFlags flags = default;
            flags.IsVanished = true;
            CharacterDefenseState defense = new CharacterDefenseState { isGuarding = true, guardElapsedTime = 1f };

            HitResolution result = HitResolver.Resolve(Attack(30f, 10f), ref vitals,
                Combat(), ref flags, ref defense, Context());

            Assert.IsTrue(result.ignored);
            Assert.AreEqual(1000, vitals.currentHp);
            Assert.AreEqual(100f, vitals.currentStamina);
            Assert.AreEqual(100f, vitals.currentArmor);
            Assert.IsTrue(defense.isGuarding);
            Assert.AreEqual(1f, defense.guardElapsedTime);
        }

        [Test]
        public void HitResolver_GuardAndArmorDamage_AreIndependentChannels()
        {
            CharacterVitals vitals = Vitals(stamina: 50f);
            CharacterFlags flags = default;
            CharacterDefenseState defense = new CharacterDefenseState { isGuarding = true, guardElapsedTime = 1f };

            HitResolution resolution = HitResolver.Resolve(Attack(30f, 5f), ref vitals,
                Combat(10f), ref flags, ref defense, Context());

            Assert.AreEqual(GuardResult.Guarded, resolution.result.guardResult);
            Assert.AreEqual(30f, vitals.currentStamina);
            Assert.AreEqual(95f, vitals.currentArmor);
            Assert.AreEqual(20f, resolution.result.guardDamage);
            Assert.AreEqual(5f, resolution.result.armorDamage);
            Assert.AreEqual(0f, resolution.result.actionArmorDamage);
        }

        [Test]
        public void HitResolver_ExactStaminaDrain_RemainsGuardedAtZeroStamina()
        {
            CharacterVitals vitals = Vitals(stamina: 20f);
            CharacterFlags flags = default;
            CharacterDefenseState defense = new CharacterDefenseState { isGuarding = true, guardElapsedTime = 1f };

            HitResolution resolution = HitResolver.Resolve(Attack(30f), ref vitals,
                Combat(10f), ref flags, ref defense, Context());

            Assert.AreEqual(GuardResult.Guarded, resolution.result.guardResult);
            Assert.AreEqual(0f, vitals.currentStamina);
            Assert.AreEqual(20f, resolution.result.guardDamage);
        }

        [Test]
        public void HitResolver_InsufficientStamina_ReportsActualDrainAndGuardBreak()
        {
            CharacterVitals vitals = Vitals(stamina: 5f);
            CharacterFlags flags = default;
            CharacterDefenseState defense = new CharacterDefenseState { isGuarding = true, guardElapsedTime = 1f };

            HitResolution resolution = HitResolver.Resolve(Attack(30f), ref vitals,
                Combat(10f), ref flags, ref defense, Context());

            Assert.AreEqual(GuardResult.GuardBreak, resolution.result.guardResult);
            Assert.AreEqual(5f, resolution.result.guardDamage);
            Assert.AreEqual(0f, vitals.currentStamina);
            Assert.AreEqual(ActState.GuardBroken, flags.ActState);
        }

        [TestCase(false, 15f)]
        [TestCase(true, 30f)]
        public void HitResolver_JustGuard_RecoversBeforeApplyingAllowedArmorDamage(bool projectile, float expectedArmor)
        {
            CharacterVitals vitals = Vitals(20f, 40f);
            CharacterFlags flags = default;
            CharacterDefenseState defense = new CharacterDefenseState { isGuarding = true };
            DamageData attack = Attack(99f, 30f);
            attack.isProjectile = projectile;
            attack.justGuardResistance = 50f;

            HitResolution resolution = HitResolver.Resolve(attack, ref vitals,
                Combat(), ref flags, ref defense, Context());

            Assert.AreEqual(GuardResult.JustGuard, resolution.result.guardResult);
            Assert.AreEqual(0, resolution.result.totalDamage);
            Assert.AreEqual(0f, resolution.result.guardDamage);
            Assert.AreEqual(55f, vitals.currentStamina);
            Assert.AreEqual(expectedArmor, vitals.currentArmor);
            Assert.AreEqual(16f, defense.continuousJustGuardUntil);
            Assert.IsFalse(resolution.interruptAction);
        }

        [Test]
        public void HitResolver_GuardAttack_PreventsBreakWhileConsumingAvailableStamina()
        {
            CharacterVitals vitals = Vitals(stamina: 5f);
            CharacterFlags flags = default;
            CharacterDefenseState defense = new CharacterDefenseState { isGuarding = true, guardElapsedTime = 1f };
            HitContext context = Context();
            context.actionEffects.hasGuardAttackEffect = true;

            HitResolution resolution = HitResolver.Resolve(Attack(30f), ref vitals,
                Combat(10f), ref flags, ref defense, context);

            Assert.AreEqual(GuardResult.Guarded, resolution.result.guardResult);
            Assert.AreEqual(5f, resolution.result.guardDamage);
            Assert.AreEqual(0f, vitals.currentStamina);
            Assert.AreEqual(ActState.Neutral, flags.ActState);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void HitResolver_ActionBlockingStatus_DisablesGuardWithoutConsumingStamina(bool guardPoint)
        {
            StatusEffectStore store = new StatusEffectStore();
            store.Apply(1, new StatusEffectInfo { effect = StatusEffectId.Stun, duration = 5f });
            CharacterVitals vitals = Vitals();
            CharacterFlags flags = default;
            CharacterDefenseState defense = new CharacterDefenseState { isGuarding = !guardPoint };
            HitContext context = Context();
            context.actionEffects.hasGuardPoint = guardPoint;
            context.targetEffects = store.GetSummary(1);

            HitResolution resolution = HitResolver.Resolve(Attack(30f), ref vitals,
                Combat(), ref flags, ref defense, context);

            Assert.AreEqual(GuardResult.NoGuard, resolution.result.guardResult);
            Assert.AreEqual(100f, vitals.currentStamina);
            Assert.AreEqual(0f, resolution.result.guardDamage);
        }

        [TestCase(AttackFeature.None, true, 50f, GuardResult.Guarded, 20f)]
        [TestCase(AttackFeature.JustGuardImmune, true, 50f, GuardResult.Guarded, 20f)]
        [TestCase(AttackFeature.Unguardable, true, 50f, GuardResult.NoGuard, 0f)]
        [TestCase(AttackFeature.None, false, 50f, GuardResult.NoGuard, 0f)]
        [TestCase(AttackFeature.None, true, 5f, GuardResult.GuardBreak, 5f)]
        public void HitResolver_GuardPoint_UsesOrdinaryGuardRules(AttackFeature feature, bool front,
            float stamina, GuardResult expected, float drain)
        {
            CharacterVitals vitals = Vitals(stamina: stamina);
            CharacterFlags flags = default;
            CharacterDefenseState defense = new CharacterDefenseState { continuousJustGuardUntil = 12f };
            HitContext context = Context();
            context.actionEffects.hasGuardPoint = true;
            context.isAttackFromFront = front;
            DamageData attack = Attack(30f);
            attack.feature = feature;
            CombatStats combat = Combat(10f);
            combat.guardStats.guardDirection = GuardDirection.Front;
            combat.guardStats.slashCut = 0.5f;

            HitResolution resolution = HitResolver.Resolve(attack, ref vitals,
                combat, ref flags, ref defense, context);

            Assert.AreEqual(expected, resolution.result.guardResult);
            Assert.AreEqual(drain, resolution.result.guardDamage);
            Assert.AreEqual(stamina - drain, vitals.currentStamina);
            Assert.IsFalse(defense.isGuarding);
            Assert.AreEqual(12f, defense.continuousJustGuardUntil);
            if (expected == GuardResult.Guarded) { Assert.AreEqual(10, resolution.result.totalDamage); }
        }

        [TestCase(0f, -1f, GuardResult.JustGuard)]
        [TestCase(1f, 12f, GuardResult.JustGuard)]
        [TestCase(1f, -1f, GuardResult.Guarded)]
        public void HitResolver_GuardPoint_PreservesManualJustGuardTiming(float elapsed,
            float continuousUntil, GuardResult expected)
        {
            CharacterVitals vitals = Vitals(stamina: 50f);
            CharacterFlags flags = default;
            CharacterDefenseState defense = new CharacterDefenseState
            {
                isGuarding = true, guardElapsedTime = elapsed, continuousJustGuardUntil = continuousUntil,
            };
            HitContext context = Context();
            context.actionEffects.hasGuardPoint = true;

            HitResolution resolution = HitResolver.Resolve(Attack(30f), ref vitals,
                Combat(), ref flags, ref defense, context);

            Assert.AreEqual(expected, resolution.result.guardResult);
            Assert.AreEqual(expected == GuardResult.JustGuard ? 65f : 20f, vitals.currentStamina);
            Assert.AreEqual(elapsed, defense.guardElapsedTime);
            Assert.IsTrue(defense.isGuarding);
        }

        [TestCase(ActState.Dead, false, 0)]
        [TestCase(ActState.WakeUp, false, 1000)]
        [TestCase(ActState.Neutral, true, 1000)]
        public void HitResolver_IgnoredHit_DoesNotMutateDefenseOrVitals(ActState state, bool invincible, int hp)
        {
            CharacterVitals vitals = Vitals(20f, 40f);
            vitals.currentHp = hp;
            CharacterFlags flags = default;
            flags.ActState = state;
            CharacterDefenseState defense = new CharacterDefenseState { actionArmorConsumed = 5f, armorRecoveryTimer = 2f };
            HitContext context = Context();
            context.actionEffects.isInvincible = invincible;

            HitResolution resolution = HitResolver.Resolve(Attack(90f, 90f), ref vitals,
                Combat(), ref flags, ref defense, context);

            Assert.IsTrue(resolution.ignored);
            Assert.AreEqual(hp, vitals.currentHp);
            Assert.AreEqual(20f, vitals.currentArmor);
            Assert.AreEqual(40f, vitals.currentStamina);
            Assert.AreEqual(5f, defense.actionArmorConsumed);
            Assert.AreEqual(2f, defense.armorRecoveryTimer);
            Assert.AreEqual(state, flags.ActState);
        }

        [Test]
        public void HitResolver_ActionArmorBreak_InterruptsOnceAndFlinchRemovesArmorOnNextHit()
        {
            CharacterVitals vitals = Vitals();
            CharacterFlags flags = default;
            CharacterDefenseState defense = default;
            HitContext context = Context();
            context.actionEffects.actionArmorValue = 10f;

            HitResolution first = HitResolver.Resolve(Attack(armorDamage: 15f), ref vitals,
                Combat(), ref flags, ref defense, context);
            HitResolution second = HitResolver.Resolve(Attack(armorDamage: 15f), ref vitals,
                Combat(), ref flags, ref defense, context);

            Assert.IsTrue(first.interruptAction);
            Assert.AreEqual(10f, first.result.actionArmorDamage);
            Assert.AreEqual(5f, first.result.armorDamage);
            Assert.AreEqual(ActState.Flinch, flags.ActState);
            Assert.IsFalse(second.interruptAction);
            Assert.LessOrEqual(vitals.currentArmor, 0f);
        }

        [Test]
        public void HitResolver_SuperArmor_PreventsReactionAndInterruptionButStillTakesDamage()
        {
            CharacterVitals vitals = Vitals(0f);
            CharacterFlags flags = default;
            CharacterDefenseState defense = default;
            HitContext context = Context();
            context.actionEffects.actionArmorValue = 10f;
            context.actionEffects.hasSuperArmor = true;

            HitResolution resolution = HitResolver.Resolve(Attack(armorDamage: 15f), ref vitals,
                Combat(), ref flags, ref defense, context);

            Assert.Less(vitals.currentHp, vitals.maxHp);
            Assert.AreEqual(HitReaction.None, resolution.result.hitReaction);
            Assert.IsFalse(resolution.interruptAction);
        }

        [Test]
        public void HitResolver_ArmorBreakBonus_AppliesOnlyOnTheBreakingHit()
        {
            CharacterVitals vitals = Vitals(10f);
            CharacterFlags flags = default;
            CharacterDefenseState defense = default;

            HitResolution first = HitResolver.Resolve(Attack(armorDamage: 15f), ref vitals,
                Combat(), ref flags, ref defense, Context());
            HitResolution second = HitResolver.Resolve(Attack(armorDamage: 15f), ref vitals,
                Combat(), ref flags, ref defense, Context());

            Assert.AreEqual(26, first.result.totalDamage);
            Assert.AreEqual(20, second.result.totalDamage);
        }

        [Test]
        public void HitResolver_CounterBonus_PreservesExistingFloatRounding()
        {
            CharacterVitals vitals = Vitals();
            CharacterFlags flags = default;
            flags.ActState = ActState.AttackPrep;
            CharacterDefenseState defense = default;
            DamageData attack = Attack();
            attack.damage = new ElementalStatus { slash = 10 };

            HitResolution resolution = HitResolver.Resolve(attack, ref vitals,
                Combat(), ref flags, ref defense, Context());

            Assert.AreEqual(13, resolution.result.totalDamage);
            Assert.AreEqual(SituationalBonus.Counter, resolution.result.situationalBonus);
        }

        [Test]
        public void HitResolver_Knockback_ComputesResistanceAdjustedImpulse()
        {
            CharacterVitals vitals = Vitals(0f);
            CharacterFlags flags = default;
            CharacterDefenseState defense = default;
            CombatStats combat = Combat();
            combat.knockbackResistance = 0.25f;
            DamageData attack = Attack();
            attack.knockbackForce = new Vector2(8f, 4f);

            HitResolution resolution = HitResolver.Resolve(attack, ref vitals,
                combat, ref flags, ref defense, Context());

            Assert.AreEqual(HitReaction.Knockback, resolution.result.hitReaction);
            Assert.AreEqual(new Vector2(6f, 3f), resolution.knockback);
            Assert.AreEqual(ActState.Knockbacked, flags.ActState);
        }

        [Test]
        public void HitResolver_DefenseTick_PreservesRecoveryDelayAndRestoresArmorOnStunExit()
        {
            CharacterVitals vitals = Vitals(20f);
            CharacterDefenseState defense = new CharacterDefenseState
            {
                isGuarding = true, armorRecoveryRate = 10f, armorRecoveryTimer = 0.5f,
            };

            HitResolver.TickDefense(ref vitals, ActState.Neutral, ref defense, 1f, true);
            Assert.AreEqual(20f, vitals.currentArmor);
            Assert.AreEqual(1f, defense.guardElapsedTime);
            Assert.AreEqual(1f, defense.actionElapsedTime);
            HitResolver.TickDefense(ref vitals, ActState.Neutral, ref defense, 0.5f, false);
            Assert.AreEqual(25f, vitals.currentArmor);
            defense.previousActState = ActState.Flinch;
            HitResolver.TickDefense(ref vitals, ActState.Neutral, ref defense, 0.1f, false);
            Assert.AreEqual(100f, vitals.currentArmor);
            Assert.AreEqual(ActState.Neutral, defense.previousActState);
        }
    }
}
