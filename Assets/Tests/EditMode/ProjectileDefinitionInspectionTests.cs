using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    public class ProjectileDefinitionInspectionTests
    {
        [Test]
        public void ProjectileInspection_ChildConfigurationMutation_DoesNotChangeStoredDefinition()
        {
            MagicDefinition stored = new MagicDefinition
            {
                childBullet = new ChildBulletConfig
                {
                    trigger = ChildBulletTrigger.OnTimer, count = 2, spreadAngle = 15f,
                    profile = new BulletProfile { speed = 3f, lifeTime = 5f },
                },
            };

            MagicDefinition inspection = ChildBulletHelper.DeepCopyForInspection(stored);

            Assert.AreNotSame(stored.childBullet, inspection.childBullet);
            inspection.childBullet.count = 99;
            inspection.childBullet.trigger = ChildBulletTrigger.OnDestroy;
            inspection.childBullet.profile = new BulletProfile { speed = 999f };
            Assert.AreEqual(2, stored.childBullet.count);
            Assert.AreEqual(ChildBulletTrigger.OnTimer, stored.childBullet.trigger);
            Assert.AreEqual(3f, stored.childBullet.profile.speed);
        }

        [Test]
        public void ProjectileInspection_EffectCollectionMutation_DoesNotChangeStoredDefinition()
        {
            StatusEffectApplicationData poison = new StatusEffectApplicationData
            {
                info = new StatusEffectInfo { effect = StatusEffectId.Poison, duration = 5f },
            };
            StatusEffectApplicationData burn = new StatusEffectApplicationData
            {
                info = new StatusEffectInfo { effect = StatusEffectId.Burn, duration = 3f },
            };
            MagicDefinition stored = new MagicDefinition
            {
                statusEffects = new[] { poison }, equipmentStatusEffects = new[] { burn },
            };

            MagicDefinition inspection = ChildBulletHelper.DeepCopyForInspection(stored);

            Assert.AreNotSame(stored.statusEffects, inspection.statusEffects);
            Assert.AreNotSame(stored.equipmentStatusEffects, inspection.equipmentStatusEffects);
            inspection.statusEffects[0] = default;
            ((IList<StatusEffectApplicationData>)inspection.equipmentStatusEffects)[0] = default;
            Assert.AreEqual(StatusEffectId.Poison, stored.statusEffects[0].info.effect);
            Assert.AreEqual(StatusEffectId.Burn, stored.equipmentStatusEffects[0].info.effect);
        }

        [Test]
        public void ProjectileInspection_EmptyDefinition_PreservesNullAndCapturedIdentity()
        {
            ActorRef source = new ActorRef(123, 7);
            AttackContext context = AttackContext.Capture(source,
                new ElementalStatus { slash = 30 }, 1.5f, CharacterBelong.Ally, null);
            MagicDefinition stored = new MagicDefinition
            {
                actionInstanceId = 42, motionValue = 1.5f,
                attackContext = context, targetPolicy = new AttackTargetPolicy
                {
                    overrideTargets = true, targets = AttackTargetRelation.Friendly,
                },
            };

            MagicDefinition inspection = ChildBulletHelper.DeepCopyForInspection(stored);

            Assert.IsNull(inspection.childBullet);
            Assert.IsNull(inspection.statusEffects);
            Assert.IsNull(inspection.equipmentStatusEffects);
            Assert.AreEqual(source, inspection.attackContext.Owner);
            Assert.AreEqual(42, inspection.actionInstanceId);
            Assert.AreEqual(1.5f, inspection.motionValue);
            Assert.AreEqual(AttackTargetRelation.Friendly, inspection.targetPolicy.targets);
        }
    }
}
