using Game.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    public class ActorReferenceSnapshotTests
    {
        private sealed class Writer : IStatusEffectWriter
        {
            public StatusEffectId LastEffect;
            public bool Apply(int hash, StatusEffectInfo info, int sourceHash = 0)
            { LastEffect = info.effect; return true; }
            public bool Accumulate(int hash, StatusEffectInfo info, float cut, int sourceHash = 0)
            { LastEffect = info.effect; return true; }
        }
        [Test]
        public void ActorRegistry_ReusedHash_InvalidatesOldIndividualWithoutChangingCapturedIdentity()
        {
            ActorRegistry actors = new ActorRegistry();
            ActorRef first = actors.Register(17);
            Assert.IsTrue(actors.IsCurrent(first));
            actors.Unregister(17);
            ActorRef second = actors.Register(17);
            Assert.AreEqual(first.Hash, second.Hash);
            Assert.AreNotEqual(first, second);
            Assert.IsFalse(actors.IsCurrent(first));
            Assert.IsTrue(actors.IsCurrent(second));
            Assert.IsFalse(actors.IsCurrent(default));
        }

        [Test]
        public void AttackContext_SourceChangesOrDisappears_PreservesAttackBuffEquipmentAndOriginalOwner()
        {
            ActorRegistry actors = new ActorRegistry();
            ActorRef owner = actors.Register(17);
            ActorRef target = actors.Register(18);
            ElementalStatus stats = new ElementalStatus { fire = 50 };
            StatusEffectApplicationData[] effects = new[]
            {
                new StatusEffectApplicationData { info = new StatusEffectInfo { effect = StatusEffectId.Poison, duration = 5f } },
            };
            AttackContext context = AttackContext.Capture(owner, stats, 1.5f, CharacterBelong.Ally, effects);
            stats.fire = 999;
            effects[0] = default;
            actors.Unregister(owner.Hash);
            ActorRef replacement = actors.Register(owner.Hash);
            DamageData damage = default;
            context.ApplyTo(ref damage, target);
            Assert.AreEqual(50, damage.damage.fire);
            Assert.AreEqual(1.5f, damage.attackContext.AttackMultiplier);
            Assert.AreEqual(owner, damage.attackerRef);
            Assert.AreNotEqual(replacement, damage.attackerRef);
            Assert.AreEqual(target, damage.defenderRef);
            Assert.IsNull(damage.equipmentStatusEffects);
            damage.equipmentStatusEffects = new[] { new StatusEffectApplicationData() };
            Writer writer = new Writer();
            context.ApplyStatusEffects(0, target.Hash, GuardResult.NoGuard, 0f, writer);
            Assert.AreEqual(StatusEffectId.Poison, writer.LastEffect);
            Assert.AreEqual(0, context.GetLiveSourceHash(actors));
        }

        [Test]
        public void Projectile_StaleExplicitTarget_ReleasesTrackingAndRejectsOldReference()
        {
            ActorRegistry actors = new ActorRegistry();
            ActorRef owner = actors.Register(17);
            ActorRef target = actors.Register(18);
            Projectile projectile = new Projectile();
            projectile.Initialize(owner, new BulletProfile { speed = 10f, lifeTime = 5f }, Vector2.zero, Vector2.right);
            Assert.IsTrue(projectile.TrySetTarget(target, actors));
            actors.Unregister(target.Hash);
            ActorRef replacement = actors.Register(target.Hash);
            Assert.IsFalse(projectile.CanHit(target, actors));
            Assert.IsFalse(projectile.CanHit(replacement, actors));
            Assert.IsFalse(projectile.RefreshTarget(actors));
            Assert.AreEqual(0, projectile.TargetHash);
            Assert.IsFalse(projectile.TargetRef.IsValid);
            Assert.IsFalse(projectile.TrySetTarget(target, actors));
            Assert.IsTrue(projectile.TrySetTarget(replacement, actors));
            Assert.AreEqual(replacement, projectile.TargetRef);
            projectile.Reset();
            Assert.IsFalse(projectile.CasterRef.IsValid);
            Assert.IsFalse(projectile.TargetRef.IsValid);
        }

        [Test]
        public void ChildProjectile_SourceGone_InheritsCapturedAttackRatherThanReplacementEquipment()
        {
            ActorRegistry actors = new ActorRegistry();
            ActorRef source = actors.Register(17);
            ActorRef target = actors.Register(18);
            StatusEffectApplicationData[] effects = new[]
            { new StatusEffectApplicationData { info = new StatusEffectInfo { effect = StatusEffectId.Poison, duration = 5f } } };
            MagicDefinition parent = new MagicDefinition
            {
                attackContext = AttackContext.Capture(source, new ElementalStatus { fire = 35 }, 1.5f,
                    CharacterBelong.Ally, effects),
                childBullet = new ChildBulletConfig { count = 1, trigger = ChildBulletTrigger.OnHit },
            };
            actors.Unregister(source.Hash);
            actors.Register(source.Hash);
            effects[0] = default;
            MagicDefinition child = ChildBulletHelper.CreateChildMagic(parent,
                new ChildBulletConfig { profile = new BulletProfile { speed = 3f } });
            DamageData packet = default;
            child.attackContext.ApplyTo(ref packet, target);
            Writer writer = new Writer();
            child.attackContext.ApplyStatusEffects(0, target.Hash, GuardResult.NoGuard, 0f, writer);
            Assert.AreEqual(source, packet.attackerRef);
            Assert.AreEqual(35, packet.damage.fire);
            Assert.AreEqual(1.5f, child.attackContext.AttackMultiplier);
            Assert.AreEqual(StatusEffectId.Poison, writer.LastEffect);
            Assert.IsNull(child.childBullet);
        }

        [Test]
        public void AttackContext_AcceptedCastMayFinish_RequiresSameOwnerGenerationAndActiveCast()
        {
            ActorRegistry actors = new ActorRegistry();
            ActorRef owner = actors.Register(17);
            ActionExecution cast = new ActionExecution
            {
                instanceId = 3, groupId = 7, ownerHash = owner.Hash, ownerRef = owner,
                phase = AnimationPhase.Active,
                definition = new ActionDefinitionData { gameplay = ActionGameplayFlags.Cast },
            };
            Assert.IsTrue(AttackContext.CanFinishAcceptedCast(owner, cast));
            cast.phase = AnimationPhase.Anticipation;
            Assert.IsFalse(AttackContext.CanFinishAcceptedCast(owner, cast));
            cast.phase = AnimationPhase.Active;
            actors.Unregister(owner.Hash);
            ActorRef replacement = actors.Register(owner.Hash);
            Assert.IsFalse(AttackContext.CanFinishAcceptedCast(replacement, cast));
            cast.ownerRef = replacement;
            cast.definition = new ActionDefinitionData { gameplay = ActionGameplayFlags.Attack };
            Assert.IsFalse(AttackContext.CanFinishAcceptedCast(replacement, cast));
        }
    }
}
