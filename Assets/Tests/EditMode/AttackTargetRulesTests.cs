using System;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    public class AttackTargetRulesTests
    {
        [TestCase(CharacterBelong.Ally, CharacterBelong.Enemy, false, AttackTargetRelation.Hostile)]
        [TestCase(CharacterBelong.Enemy, CharacterBelong.Ally, false, AttackTargetRelation.Hostile)]
        [TestCase(CharacterBelong.Ally, CharacterBelong.Ally, false, AttackTargetRelation.Friendly)]
        [TestCase(CharacterBelong.Enemy, CharacterBelong.Enemy, false, AttackTargetRelation.Friendly)]
        [TestCase(CharacterBelong.Ally, CharacterBelong.Enemy, true, AttackTargetRelation.Self)]
        [TestCase(CharacterBelong.Ally, CharacterBelong.Neutral, false, AttackTargetRelation.Neutral)]
        [TestCase(CharacterBelong.Neutral, CharacterBelong.Enemy, false, AttackTargetRelation.Neutral)]
        [TestCase(CharacterBelong.Neutral, CharacterBelong.Neutral, false, AttackTargetRelation.Neutral)]
        [TestCase((CharacterBelong)0, CharacterBelong.Enemy, false, AttackTargetRelation.Neutral)]
        [TestCase(CharacterBelong.Ally | CharacterBelong.Neutral,
            CharacterBelong.Enemy | CharacterBelong.Neutral, false, AttackTargetRelation.Hostile)]
        [TestCase(CharacterBelong.Ally | CharacterBelong.Enemy,
            CharacterBelong.Enemy, false, AttackTargetRelation.Friendly)]
        public void TargetRelation_FactionFlagsAndSelf_UsesOneRelation(CharacterBelong source,
            CharacterBelong target, bool self, AttackTargetRelation expected)
        {
            Assert.AreEqual(expected, AttackTargetRules.GetRelation(source, target, self));
        }

        [TestCase(AttackTargetRelation.Hostile, false, CharacterBelong.Enemy, true)]
        [TestCase(AttackTargetRelation.Hostile, false, CharacterBelong.Ally, false)]
        [TestCase(AttackTargetRelation.Hostile, false, CharacterBelong.Neutral, false)]
        [TestCase(AttackTargetRelation.Hostile, true, CharacterBelong.Ally, false)]
        [TestCase(AttackTargetRelation.Friendly, false, CharacterBelong.Ally, true)]
        [TestCase(AttackTargetRelation.Friendly, true, CharacterBelong.Ally, false)]
        [TestCase(AttackTargetRelation.Friendly | AttackTargetRelation.Self, true, CharacterBelong.Ally, true)]
        [TestCase(AttackTargetRelation.Neutral, false, CharacterBelong.Neutral, true)]
        [TestCase(AttackTargetRelation.None, false, CharacterBelong.Enemy, false)]
        [TestCase(AttackTargetRelation.All, false, CharacterBelong.Enemy, true)]
        [TestCase(AttackTargetRelation.All, true, CharacterBelong.Ally, true)]
        public void TargetMask_RelativeConditions_AdmitsOnlyConfiguredRelation(AttackTargetRelation targets,
            bool self, CharacterBelong targetBelong, bool expected)
        {
            Assert.AreEqual(expected,
                AttackTargetRules.CanAffect(CharacterBelong.Ally, targetBelong, self, targets));
        }

        [TestCase(MagicType.Attack, AttackTargetRelation.Hostile)]
        [TestCase(MagicType.Support, AttackTargetRelation.Hostile)]
        [TestCase(MagicType.Recover, AttackTargetRelation.Friendly | AttackTargetRelation.Self)]
        public void TargetPolicy_Unspecified_ResolvesDocumentedDefault(MagicType type,
            AttackTargetRelation expected)
        {
            Assert.AreEqual(expected, default(AttackTargetPolicy).Resolve(type));
        }

        [Test]
        public void TargetPolicy_ExplicitNoneAndFriendlySupport_StayIndependentOfEffectKind()
        {
            AttackTargetPolicy disabled = new AttackTargetPolicy { overrideTargets = true };
            Assert.AreEqual(AttackTargetRelation.None, disabled.Resolve(MagicType.Recover));
            AttackTargetPolicy buff = new AttackTargetPolicy
            {
                overrideTargets = true,
                targets = AttackTargetRelation.Friendly | AttackTargetRelation.Self,
            };
            Assert.AreEqual(buff.targets, buff.Resolve(MagicType.Support));
        }

        [Test]
        public void TargetPolicy_UnknownTargetBits_RejectsInvalidInput()
        {
            AttackTargetPolicy invalid = new AttackTargetPolicy
            {
                overrideTargets = true,
                targets = (AttackTargetRelation)128,
            };
            Assert.Throws<ArgumentOutOfRangeException>(() => AttackTargetRules.Validate(invalid));
            Assert.Throws<ArgumentOutOfRangeException>(() => invalid.Resolve());
            Assert.IsFalse(AttackTargetRules.CanAffect(CharacterBelong.Ally, CharacterBelong.Enemy,
                false, (AttackTargetRelation)128));
        }

        [Test]
        public void TargetIdentity_ReusedHash_IsNotOriginalSelf()
        {
            ActorRef source = new ActorRef(17, 1);
            ActorRef replacement = new ActorRef(17, 2);
            Assert.IsTrue(AttackTargetRules.CanAffect(source, CharacterBelong.Ally,
                source, CharacterBelong.Ally, AttackTargetRelation.Self));
            Assert.IsFalse(AttackTargetRules.CanAffect(source, CharacterBelong.Ally,
                replacement, CharacterBelong.Ally, AttackTargetRelation.Self));
            Assert.IsTrue(AttackTargetRules.CanAffect(source, CharacterBelong.Ally,
                replacement, CharacterBelong.Ally, AttackTargetRelation.Friendly));
            Assert.IsFalse(AttackTargetRules.CanAffect(default, CharacterBelong.Ally,
                replacement, CharacterBelong.Enemy, AttackTargetRelation.All));
            Assert.IsFalse(AttackTargetRules.CanAffect(source, CharacterBelong.Ally,
                default, CharacterBelong.Enemy, AttackTargetRelation.All));
        }

        [Test]
        public void TargetContext_SourceRemovedAndPolicyEdited_PreservesCapturedRelationsAndFaction()
        {
            ActorRegistry actors = new ActorRegistry();
            ActorRef source = actors.Register(17);
            ActorRef target = actors.Register(18);
            AttackTargetPolicy policy = new AttackTargetPolicy
            {
                overrideTargets = true,
                targets = AttackTargetRelation.Friendly | AttackTargetRelation.Self,
            };
            AttackContext context = AttackContext.Capture(source, default, 1f, CharacterBelong.Ally,
                null, targetRelations: policy.Resolve());
            policy.targets = AttackTargetRelation.Hostile;
            actors.Unregister(source.Hash);
            actors.Register(source.Hash);
            Assert.IsFalse(actors.IsCurrent(context.Owner));
            Assert.IsTrue(AttackTargetRules.CanAffect(context, target, CharacterBelong.Ally));
            Assert.IsFalse(AttackTargetRules.CanAffect(context, target, CharacterBelong.Enemy));
            DamageData packet = new DamageData { targetPolicy = policy };
            context.ApplyTo(ref packet, target);
            Assert.AreEqual(AttackTargetRelation.Friendly | AttackTargetRelation.Self,
                packet.targetPolicy.Resolve());
            Assert.IsFalse(AttackTargetRules.CanAffect(default(AttackContext), target, CharacterBelong.Ally));
        }
    }
}
