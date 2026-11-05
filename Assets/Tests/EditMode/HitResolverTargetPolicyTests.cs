using Game.Core;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    public class HitResolverTargetPolicyTests
    {
        private static readonly ActorRef s_Source = new ActorRef(101, 1);
        private static readonly ActorRef s_Target = new ActorRef(202, 1);

        private static DamageData Captured(AttackTargetRelation targets, ActorRef target)
        {
            AttackContext context = AttackContext.Capture(s_Source,
                new ElementalStatus { slash = 20 }, 1f, CharacterBelong.Ally, null, targetRelations: targets);
            DamageData attack = new DamageData
            {
                motionValue = 1f,
                guardDamage = 30f,
                armorBreakValue = 10f,
            };
            context.ApplyTo(ref attack, target);
            return attack;
        }

        private static HitResolution Resolve(DamageData attack, CharacterBelong belong,
            ref CharacterVitals vitals, ref CharacterDefenseState defense)
        {
            CharacterFlags flags = CharacterFlags.Pack(belong, CharacterFeature.NPC,
                ActState.Neutral, AbilityFlag.None);
            HitContext context = new HitContext
            {
                attackerAttackMultiplier = 1f,
                isAttackFromFront = true,
                currentTime = 10f,
                bonusConfig = SituationalBonusConfig.Default,
            };
            return HitResolver.Resolve(attack, ref vitals, default, ref flags, ref defense, context);
        }

        [TestCase(AttackTargetRelation.Hostile, CharacterBelong.Ally)]
        [TestCase(AttackTargetRelation.Friendly, CharacterBelong.Enemy)]
        [TestCase(AttackTargetRelation.None, CharacterBelong.Enemy)]
        public void HitResolver_TargetRejected_PreservesHpArmorStaminaAndDefense(
            AttackTargetRelation targets, CharacterBelong belong)
        {
            CharacterVitals vitals = new CharacterVitals
            {
                currentHp = 100, maxHp = 100,
                currentArmor = 50f, maxArmor = 50f,
                currentStamina = 40f, maxStamina = 40f,
            };
            CharacterDefenseState defense = new CharacterDefenseState
            {
                isGuarding = true, guardElapsedTime = 1f, actionArmorConsumed = 7f,
            };

            HitResolution result = Resolve(Captured(targets, s_Target), belong, ref vitals, ref defense);

            Assert.IsTrue(result.ignored);
            Assert.AreEqual(100, vitals.currentHp);
            Assert.AreEqual(50f, vitals.currentArmor);
            Assert.AreEqual(40f, vitals.currentStamina);
            Assert.IsTrue(defense.isGuarding);
            Assert.AreEqual(1f, defense.guardElapsedTime);
            Assert.AreEqual(7f, defense.actionArmorConsumed);
            Assert.AreEqual(0, result.result.totalDamage);
        }

        [TestCase(AttackTargetRelation.Hostile, CharacterBelong.Enemy)]
        [TestCase(AttackTargetRelation.Friendly, CharacterBelong.Ally)]
        [TestCase(AttackTargetRelation.Neutral, CharacterBelong.Neutral)]
        public void HitResolver_TargetAccepted_UsesNormalDamagePipeline(
            AttackTargetRelation targets, CharacterBelong belong)
        {
            CharacterVitals vitals = new CharacterVitals { currentHp = 100, maxHp = 100 };
            CharacterDefenseState defense = default;

            HitResolution result = Resolve(Captured(targets, s_Target), belong, ref vitals, ref defense);

            Assert.IsFalse(result.ignored);
            Assert.Greater(result.result.totalDamage, 0);
            Assert.AreEqual(100 - result.result.totalDamage, vitals.currentHp);
            Assert.GreaterOrEqual(vitals.currentHp, 0);
        }

        [Test]
        public void HitResolver_SelfOnlyPolicy_DoesNotMatchReusedSourceHash()
        {
            CharacterVitals vitals = new CharacterVitals { currentHp = 100, maxHp = 100 };
            CharacterDefenseState defense = default;
            ActorRef replacement = new ActorRef(s_Source.Hash, s_Source.Generation + 1);

            HitResolution result = Resolve(Captured(AttackTargetRelation.Self, replacement),
                CharacterBelong.Ally, ref vitals, ref defense);

            Assert.IsTrue(result.ignored);
            Assert.AreEqual(100, vitals.currentHp);
        }
    }
}
