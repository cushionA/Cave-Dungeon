using Game.Core;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    public class EquipmentCombatFlowTests
    {
        private static CharacterBaseStats Basis(int attack = 0, int defense = 0)
        {
            return new CharacterBaseStats
            {
                maxHp = 1000,
                maxStamina = 100f,
                maxArmor = 100f,
                combat = new CombatStats
                {
                    attack = new ElementalStatus { slash = attack },
                    defense = new ElementalStatus { slash = defense },
                },
            };
        }

        private static HitContext Context()
        {
            return new HitContext
            {
                attackerAttackMultiplier = 1f,
                isAttackFromFront = true,
                currentTime = 10f,
                bonusConfig = SituationalBonusConfig.Default,
            };
        }

        private static HitResolution Resolve(ElementalStatus attack, in CombatStats targetCombat,
            in HitContext context, ref CharacterVitals targetVitals, bool guarding = false)
        {
            CharacterFlags flags = default;
            CharacterDefenseState defense = new CharacterDefenseState
            {
                isGuarding = guarding,
                guardElapsedTime = 1f,
            };
            DamageData damage = new DamageData
            {
                damage = attack,
                motionValue = 1f,
                guardDamage = 30f,
            };
            return HitResolver.Resolve(damage, ref targetVitals, targetCombat, ref flags, ref defense, context);
        }

        [Test]
        public void EquipmentCombat_AttackBonusAndRemoval_ChangeActualDamageAndPreserveHpClamp()
        {
            CharacterBaseStats attacker = Basis(attack: 20);
            CharacterGrowth growth = new CharacterGrowth { level = 1 };
            CharacterVitals current = CharacterStatCalculator.CreateVitals(attacker, growth);
            EquipmentStatModifiers weapon = new EquipmentStatModifiers
            {
                attackBonus = new ElementalStatus { slash = 20 },
            };
            EquipmentDerivedStats equipped = EquipmentStatCalculator.Calculate(attacker, growth, current,
                weapon, default, default);
            EquipmentDerivedStats removed = EquipmentStatCalculator.Calculate(attacker, growth, equipped.vitals,
                default, default, default);
            CharacterBaseStats target = Basis();
            CharacterVitals full = CharacterStatCalculator.CreateVitals(target, growth);
            CharacterVitals unarmedVictim = full;
            CharacterVitals armedVictim = full;
            CharacterVitals removedVictim = full;

            Assert.AreEqual(20, Resolve(attacker.combat.attack, target.combat, Context(), ref unarmedVictim).result.totalDamage);
            Assert.AreEqual(40, Resolve(equipped.combat.attack, target.combat, Context(), ref armedVictim).result.totalDamage);
            Assert.AreEqual(20, Resolve(removed.combat.attack, target.combat, Context(), ref removedVictim).result.totalDamage);
            Assert.AreEqual(980, unarmedVictim.currentHp);
            Assert.AreEqual(960, armedVictim.currentHp);
            Assert.AreEqual(980, removedVictim.currentHp);
            Assert.AreEqual(20, attacker.combat.attack.slash);

            CharacterVitals lethalVictim = full;
            lethalVictim.currentHp = 10;
            HitResolution lethal = Resolve(equipped.combat.attack, target.combat, Context(), ref lethalVictim);
            Assert.IsTrue(lethal.result.isKill);
            Assert.AreEqual(0, lethalVictim.currentHp);
            Assert.AreEqual(0, lethalVictim.hpRatio);
        }

        [Test]
        public void EquipmentCombat_DefenseAndShieldGuard_ApplyToDamageAndActualStaminaDrain()
        {
            CharacterBaseStats target = Basis();
            CharacterGrowth growth = new CharacterGrowth { level = 1 };
            CharacterVitals current = CharacterStatCalculator.CreateVitals(target, growth);
            EquipmentStatModifiers shield = new EquipmentStatModifiers
            {
                overridesGuard = true,
                guardStats = new GuardStats
                {
                    slashCut = 0.5f,
                    guardStrength = 10f,
                    guardDirection = GuardDirection.Both,
                },
            };
            EquipmentStatModifiers core = new EquipmentStatModifiers
            {
                defenseBonus = new ElementalStatus { slash = 100 },
            };
            EquipmentDerivedStats equipped = EquipmentStatCalculator.Calculate(target, growth, current,
                default, shield, core);
            CharacterVitals unguarded = equipped.vitals;
            CharacterVitals guarded = equipped.vitals;
            ElementalStatus attack = new ElementalStatus { slash = 100 };

            HitResolution openHit = Resolve(attack, equipped.combat, Context(), ref unguarded);
            HitResolution guardedHit = Resolve(attack, equipped.combat, Context(), ref guarded, guarding: true);

            Assert.AreEqual(50, openHit.result.totalDamage);
            Assert.AreEqual(950, unguarded.currentHp);
            Assert.AreEqual(GuardResult.Guarded, guardedHit.result.guardResult);
            Assert.AreEqual(25, guardedHit.result.totalDamage);
            Assert.AreEqual(975, guarded.currentHp);
            Assert.AreEqual(20f, guardedHit.result.guardDamage);
            Assert.AreEqual(80f, guarded.currentStamina);
            Assert.AreEqual(100f, guarded.currentArmor);
        }

        [Test]
        public void EquipmentCombat_StatusMultipliers_ApplyAtHitWithoutChangingPermanentEquipmentValues()
        {
            const int attackerHash = 11;
            const int targetHash = 22;
            CharacterGrowth growth = new CharacterGrowth { level = 1 };
            CharacterBaseStats attacker = Basis(attack: 20);
            CharacterBaseStats target = Basis(defense: 20);
            EquipmentDerivedStats attackStats = EquipmentStatCalculator.Calculate(attacker, growth,
                CharacterStatCalculator.CreateVitals(attacker, growth),
                new EquipmentStatModifiers { attackBonus = new ElementalStatus { slash = 20 } }, default, default);
            EquipmentDerivedStats defenseStats = EquipmentStatCalculator.Calculate(target, growth,
                CharacterStatCalculator.CreateVitals(target, growth), default, default,
                new EquipmentStatModifiers { defenseBonus = new ElementalStatus { slash = 20 } });
            StatusEffectStore effects = new StatusEffectStore();
            effects.Apply(attackerHash, new StatusEffectInfo { effect = StatusEffectId.AttackUp, duration = 5f, modifier = 1f });
            effects.Apply(targetHash, new StatusEffectInfo { effect = StatusEffectId.DefenseUp, duration = 5f, modifier = 1f });
            HitContext buffedContext = Context();
            buffedContext.attackerAttackMultiplier = effects.GetSummary(attackerHash).AttackMultiplier;
            buffedContext.targetEffects = effects.GetSummary(targetHash);
            CharacterVitals normalVictim = defenseStats.vitals;
            CharacterVitals buffedVictim = defenseStats.vitals;
            CharacterVitals expiredVictim = defenseStats.vitals;

            Assert.AreEqual(20, Resolve(attackStats.combat.attack, defenseStats.combat, Context(), ref normalVictim).result.totalDamage);
            Assert.AreEqual(40, Resolve(attackStats.combat.attack, defenseStats.combat, buffedContext, ref buffedVictim).result.totalDamage);
            effects.Remove(attackerHash, StatusEffectId.AttackUp);
            effects.Remove(targetHash, StatusEffectId.DefenseUp);
            HitContext expiredContext = Context();
            expiredContext.attackerAttackMultiplier = effects.GetSummary(attackerHash).AttackMultiplier;
            expiredContext.targetEffects = effects.GetSummary(targetHash);
            Assert.AreEqual(20, Resolve(attackStats.combat.attack, defenseStats.combat, expiredContext, ref expiredVictim).result.totalDamage);
            Assert.AreEqual(980, normalVictim.currentHp);
            Assert.AreEqual(960, buffedVictim.currentHp);
            Assert.AreEqual(980, expiredVictim.currentHp);
            Assert.AreEqual(40, attackStats.combat.attack.slash);
            Assert.AreEqual(40, defenseStats.combat.defense.slash);
        }
    }
}
