using System;
using System.Collections.Generic;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    public class AttackTargetAuthoringTests
    {
        private sealed class World : IExecutionWorld
        {
            public bool TryGetActor(int hash, out ActionActorState actor) { actor = default; return false; }
            public void CommitActor(int hash, in ActionActorState actor) { }
        }

        private readonly List<UnityEngine.Object> _assets = new List<UnityEngine.Object>();
        private ActionExecutionSystem _execution;
        private ActionCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            _execution = new ActionExecutionSystem(new World());
            _catalog = new ActionCatalog(_execution);
        }

        [TearDown]
        public void TearDown()
        {
            _execution.Dispose();
            for (int i = _assets.Count - 1; i >= 0; i--)
            {
                UnityEngine.Object.DestroyImmediate(_assets[i]);
            }
            _assets.Clear();
        }

        [TestCase(MagicType.Attack, AttackTargetRelation.Hostile)]
        [TestCase(MagicType.Support, AttackTargetRelation.Hostile)]
        [TestCase(MagicType.Recover, AttackTargetRelation.Friendly | AttackTargetRelation.Self)]
        public void AttackTargetAuthoring_DefaultPolicy_CapturesEffectiveTargetsInBothPackets(
            MagicType type, AttackTargetRelation expected)
        {
            AttackInfo asset = CreateAttack();
            asset.magicType = type;
            int id = _catalog.Register(asset);
            Assert.IsTrue(_catalog.TryGet(id, out ActionCatalog.Entry entry));

            AssertPolicy(entry.CreateAttack().targetPolicy, expected);
            AssertPolicy(entry.CreateMagic().targetPolicy, expected);
            AssertPolicy(CombatDataHelper.BuildMotionData(asset).targetPolicy, expected);
        }

        [TestCase(AttackTargetRelation.None)]
        [TestCase(AttackTargetRelation.Friendly | AttackTargetRelation.Self)]
        [TestCase(AttackTargetRelation.Neutral)]
        [TestCase(AttackTargetRelation.All)]
        public void AttackTargetAuthoring_ExplicitPolicy_CopiesTargetsToAttackMagicAndDamage(
            AttackTargetRelation targets)
        {
            AttackInfo asset = CreateAttack();
            asset.magicType = MagicType.Support;
            asset.targetPolicy = Policy(targets);
            int id = _catalog.Register(asset);
            Assert.IsTrue(_catalog.TryGet(id, out ActionCatalog.Entry entry));
            AttackMotionData motion = entry.CreateAttack();
            DamageData damage = CombatDataHelper.BuildDamageData(10, 20, motion, default);

            AssertPolicy(motion.targetPolicy, targets);
            AssertPolicy(entry.CreateMagic().targetPolicy, targets);
            AssertPolicy(damage.targetPolicy, targets);
        }

        [Test]
        public void AttackTargetAuthoring_AuthorAndPacketsChange_PreservesRegisteredTargets()
        {
            AttackInfo asset = CreateAttack();
            asset.targetPolicy = Policy(AttackTargetRelation.Friendly | AttackTargetRelation.Self);
            int id = _catalog.Register(asset);
            Assert.IsTrue(_catalog.TryGet(id, out ActionCatalog.Entry entry));
            asset.targetPolicy = Policy(AttackTargetRelation.Hostile);
            asset.magicType = MagicType.Attack;
            AttackMotionData attackPacket = entry.CreateAttack();
            MagicDefinition magicPacket = entry.CreateMagic();
            attackPacket.targetPolicy = Policy(AttackTargetRelation.None);
            magicPacket.targetPolicy = Policy(AttackTargetRelation.All);

            AssertPolicy(entry.CreateAttack().targetPolicy, AttackTargetRelation.Friendly | AttackTargetRelation.Self);
            AssertPolicy(entry.CreateMagic().targetPolicy, AttackTargetRelation.Friendly | AttackTargetRelation.Self);
        }

        [TestCase(ActionVariant.Aerial)]
        [TestCase(ActionVariant.Diving)]
        public void AttackTargetAuthoring_VariantCreatedAfterAuthorChanges_InheritsRegisteredTargets(ActionVariant variant)
        {
            AttackInfo asset = CreateAttack();
            asset.targetPolicy = Policy(AttackTargetRelation.Neutral);
            _catalog.Register(asset);
            asset.targetPolicy = Policy(AttackTargetRelation.All);
            int id = _catalog.Register(asset, variant);
            Assert.IsTrue(_catalog.TryGet(id, out ActionCatalog.Entry entry));

            AssertPolicy(entry.CreateAttack().targetPolicy, AttackTargetRelation.Neutral);
            AssertPolicy(entry.CreateMagic().targetPolicy, AttackTargetRelation.Neutral);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AttackTargetAuthoring_InvalidTargetBits_RejectsGraphWithoutPartialRegistration(bool overrideTargets)
        {
            AttackInfo asset = CreateAttack();
            asset.targetPolicy = new AttackTargetPolicy
            {
                overrideTargets = overrideTargets, targets = (AttackTargetRelation)128,
            };

            Assert.Catch<ArgumentException>(() => _catalog.Register(asset));
            Assert.AreEqual(0, _execution.DefinitionCount);
            asset.targetPolicy = Policy(AttackTargetRelation.Hostile);
            int id = _catalog.Register(asset);
            Assert.IsTrue(_catalog.TryGet(id, out ActionCatalog.Entry entry));
            AssertPolicy(entry.CreateMagic().targetPolicy, AttackTargetRelation.Hostile);
        }

        [Test]
        public void AttackTargetAuthoring_ChildMagic_InheritsParentTargetsWithoutSharingMutableSettings()
        {
            MagicDefinition parent = new MagicDefinition
            {
                magicType = MagicType.Support,
                targetPolicy = Policy(AttackTargetRelation.Friendly | AttackTargetRelation.Self),
                childBullet = new ChildBulletConfig { trigger = ChildBulletTrigger.OnHit, count = 2 },
            };
            MagicDefinition child = ChildBulletHelper.CreateChildMagic(parent, parent.childBullet);
            parent.targetPolicy = Policy(AttackTargetRelation.Hostile);

            AssertPolicy(child.targetPolicy, AttackTargetRelation.Friendly | AttackTargetRelation.Self);
            Assert.IsNull(child.childBullet);
        }

        private AttackInfo CreateAttack()
        {
            MotionDefinition motion = ScriptableObject.CreateInstance<MotionDefinition>();
            _assets.Add(motion);
            motion.info = new MotionInfo { activeMotionDuration = 0.25f };
            AttackInfo attack = ScriptableObject.CreateInstance<AttackInfo>();
            _assets.Add(attack);
            attack.motion = motion;
            attack.damageMultiplier = 1f;
            attack.projectileInfo = new ProjectileInfo
            {
                hasProjectile = true, speed = 2f, lifetime = 1f, projectileCount = 1,
            };
            return attack;
        }

        private static AttackTargetPolicy Policy(AttackTargetRelation targets)
            => new AttackTargetPolicy { overrideTargets = true, targets = targets };

        private static void AssertPolicy(AttackTargetPolicy policy, AttackTargetRelation expected)
        {
            Assert.IsTrue(policy.overrideTargets, "The packet must retain resolved targets independently of effect type.");
            Assert.AreEqual(expected, policy.targets);
        }
    }
}
