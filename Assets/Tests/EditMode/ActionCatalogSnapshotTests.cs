using System;
using System.Collections.Generic;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    public class ActionCatalogSnapshotTests
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

        [Test]
        public void ActionCatalog_AuthoringValuesAndArraysChange_PreservesRegisteredDefinitionAndPackets()
        {
            AttackInfo attack = CreateAttack();
            StatusEffectDefinition effect = attack.statusEffects[0].definition;
            int id = _catalog.Register(attack);
            Assert.IsTrue(_catalog.TryGet(id, out ActionCatalog.Entry entry));

            attack.staminaCost = 50f;
            attack.damageMultiplier = 20f;
            attack.inputWindow = 9f;
            attack.actionEffects[0] = new ActionEffect { type = ActionEffectType.Invincible, duration = 99f };
            attack.statusEffects[0] = default;
            effect.duration = 99f;
            attack.motion.info = new MotionInfo { activeMotionDuration = 9f };
            attack.projectileInfo = new ProjectileInfo { hasProjectile = false, speed = 90f, projectileCount = 9 };

            Assert.AreEqual(5f, entry.definition.staminaCost);
            Assert.AreEqual(0.25f, entry.definition.activeDuration);
            Assert.AreEqual(ActionEffectType.Armor, entry.definition.effects[0].type);
            Assert.AreEqual(7f, entry.definition.effects[0].value);
            Assert.AreEqual(0.12f, entry.attackSettings.InputWindow);
            Assert.AreEqual(2f, entry.CreateAttack().motionValue);
            Assert.AreEqual(3f, entry.CreateAttack().statusEffects[0].info.duration);
            Assert.IsTrue(entry.projectile);
            Assert.AreEqual(2, entry.CreateMagic().bulletCount);
            Assert.AreEqual(4f, entry.CreateMagic().bulletProfile.speed);
            Assert.AreEqual(id, _catalog.Register(attack));
        }

        [Test]
        public void ActionCatalog_GroundImpactIsAuthored_PreservesItsRangeAndRadius()
        {
            AttackInfo attack = CreateAttack();
            attack.projectileInfo = new ProjectileInfo
            {
                hasProjectile = true, speed = 4f, aoeRadius = 1.5f, groundImpactRange = 7f,
            };
            int id = _catalog.Register(attack);
            Assert.IsTrue(_catalog.TryGet(id, out ActionCatalog.Entry entry));
            attack.projectileInfo = default;

            MagicDefinition magic = entry.CreateMagic();
            Assert.AreEqual(7f, magic.groundImpactRange);
            Assert.AreEqual(1.5f, magic.bulletProfile.explodeRadius);
            Assert.AreEqual(1, magic.bulletCount);
        }

        [Test]
        public void ActionCatalog_AttackRepeatUnset_PreservesLegacySingleHitPacket()
        {
            AttackInfo attack = CreateAttack();
            attack.projectileInfo = default;
            int id = _catalog.Register(attack);

            Assert.IsTrue(_catalog.TryGet(id, out ActionCatalog.Entry entry));
            AttackMotionData packet = entry.CreateAttack();
            Assert.AreEqual(1, packet.maxHitCount);
            Assert.AreEqual(0, packet.repeatHits.maxHitsPerTarget);
            Assert.AreEqual(0f, packet.repeatHits.interval);
        }

        [TestCase(AttackShape3D.Melee)]
        [TestCase(AttackShape3D.GroundArea)]
        public void ActionCatalog_AttackRepeatAuthoringChanges_PreservesRegisteredCountAndInterval(AttackShape3D shape)
        {
            AttackInfo attack = CreateAttack();
            attack.projectileInfo = default;
            attack.attack3D = new AttackProfile3D { shape = shape, halfExtents = Vector3.one, radius = 1.5f };
            attack.repeatHits = new HitRepeatSettings { maxHitsPerTarget = 3, interval = 0.15f };
            int id = _catalog.Register(attack);

            attack.repeatHits = new HitRepeatSettings { maxHitsPerTarget = 5, interval = 0.4f };
            Assert.AreEqual(id, _catalog.Register(attack));
            Assert.IsTrue(_catalog.TryGet(id, out ActionCatalog.Entry entry));
            AttackMotionData packet = entry.CreateAttack();
            Assert.AreEqual(3, packet.repeatHits.maxHitsPerTarget);
            Assert.AreEqual(0.15f, packet.repeatHits.interval);
            packet.repeatHits = default;

            AttackInfo other = CreateAttack();
            other.projectileInfo = default;
            other.attack3D = attack.attack3D;
            other.repeatHits = attack.repeatHits;
            int otherId = _catalog.Register(other);
            Assert.IsTrue(_catalog.TryGet(otherId, out ActionCatalog.Entry otherEntry));
            Assert.AreEqual(5, otherEntry.CreateAttack().repeatHits.maxHitsPerTarget);
            Assert.AreEqual(0.4f, otherEntry.CreateAttack().repeatHits.interval);
            Assert.AreEqual(3, entry.CreateAttack().repeatHits.maxHitsPerTarget);
            Assert.AreEqual(0.15f, entry.CreateAttack().repeatHits.interval);
        }

        [TestCase(-1, 0f)]
        [TestCase(1, -0.1f)]
        [TestCase(2, 0f)]
        [TestCase(1, float.NaN)]
        [TestCase(2, float.PositiveInfinity)]
        public void ActionCatalog_InvalidAttackRepeat_RejectsBeforeRegistrationAndCanRetry(int count, float interval)
        {
            AttackInfo attack = CreateAttack();
            attack.projectileInfo = default;
            attack.attack3D = new AttackProfile3D { shape = AttackShape3D.Melee, halfExtents = Vector3.one };
            attack.repeatHits = new HitRepeatSettings { maxHitsPerTarget = count, interval = interval };

            Assert.Throws<ArgumentException>(() => _catalog.Register(attack));
            Assert.AreEqual(0, _execution.DefinitionCount);

            attack.repeatHits = new HitRepeatSettings { maxHitsPerTarget = 2, interval = 0.1f };
            int id = _catalog.Register(attack);
            Assert.IsTrue(_catalog.TryGet(id, out _));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ActionCatalog_AttackRepeatOptInForProjectile_RejectsWithoutChangingProjectileLimits(bool native3D)
        {
            AttackInfo attack = CreateAttack();
            if (native3D)
            {
                attack.attack3D = new AttackProfile3D { shape = AttackShape3D.Projectile, projectileRadius = 0.16f };
            }
            else
            {
                attack.projectileInfo.pierceCount = 2;
            }
            attack.repeatHits = new HitRepeatSettings { maxHitsPerTarget = 2, interval = 0.1f };

            Assert.Throws<ArgumentException>(() => _catalog.Register(attack));
            Assert.AreEqual(0, _execution.DefinitionCount);

            attack.repeatHits = default;
            int id = _catalog.Register(attack);
            Assert.IsTrue(_catalog.TryGet(id, out ActionCatalog.Entry entry));
            Assert.AreEqual(native3D ? 1 : 3, entry.CreateMagic().bulletProfile.hitLimit);
        }

        [Test]
        public void ActionCatalog_Registered3DAttack_PreservesTheAuthoredShape()
        {
            AttackInfo attack = CreateAttack();
            attack.projectileInfo = default;
            attack.attack3D = new AttackProfile3D
            {
                shape = AttackShape3D.Melee,
                localCenter = new Vector3(0f, 0.9f, 1.2f),
                halfExtents = new Vector3(0.8f, 0.6f, 1f),
                targetLayers = 1 << 8,
            };
            int id = _catalog.Register(attack);
            attack.attack3D = default;

            Assert.IsTrue(_catalog.TryGet(id, out ActionCatalog.Entry entry));
            Assert.AreEqual(AttackShape3D.Melee, entry.attack3D.shape);
            Assert.AreEqual(new Vector3(0f, 0.9f, 1.2f), entry.attack3D.localCenter);
            Assert.AreEqual(new Vector3(0.8f, 0.6f, 1f), entry.attack3D.halfExtents);
            Assert.AreEqual(1 << 8, entry.attack3D.targetLayers.value);
        }

        [Test]
        public void ActionCatalog_Invalid3DAreaRadius_CanRegisterAfterCorrection()
        {
            AttackInfo attack = CreateAttack();
            attack.projectileInfo = default;
            attack.attack3D = new AttackProfile3D { shape = AttackShape3D.GroundArea, radius = float.NaN };
            Assert.Throws<ArgumentException>(() => _catalog.Register(attack));

            attack.attack3D.radius = 1.5f;
            int id = _catalog.Register(attack);
            Assert.IsTrue(_catalog.TryGet(id, out ActionCatalog.Entry entry));
            Assert.AreEqual(1.5f, entry.attack3D.radius);
        }

        [Test]
        public void ActionCatalog_3DProjectileRadiusRequired_CapturesWorldShapeAndFlightProfile()
        {
            AttackInfo attack = CreateAttack();
            attack.attack3D = new AttackProfile3D { shape = AttackShape3D.Projectile, localCenter = Vector3.up };
            attack.projectileInfo = new ProjectileInfo
            {
                hasProjectile = true,
                trajectory = BulletMoveType.Homing,
                speed = 12f,
                lifetime = 2f,
                homingStrength = 8f,
            };
            Assert.Throws<ArgumentException>(() => _catalog.Register(attack));

            attack.attack3D.projectileRadius = 0.16f;
            int id = _catalog.Register(attack);
            Assert.IsTrue(_catalog.TryGet(id, out ActionCatalog.Entry entry));
            Assert.AreEqual(0.16f, entry.attack3D.projectileRadius);
            Assert.AreEqual(Vector3.up, entry.attack3D.localCenter);
            Assert.AreEqual(BulletMoveType.Homing, entry.CreateMagic().bulletProfile.moveType);
        }

        [Test]
        public void ActionCatalog_3DGroundProjectile_RequiresPairedRangeAndRadiusAndACommittedTrajectory()
        {
            AttackInfo attack = CreateAttack();
            attack.attack3D = new AttackProfile3D { shape = AttackShape3D.Projectile, projectileRadius = 0.16f };
            attack.projectileInfo = new ProjectileInfo
            {
                hasProjectile = true,
                trajectory = BulletMoveType.Homing,
                speed = 8f,
                lifetime = 1.5f,
                groundImpactRange = 6f,
            };
            Assert.Throws<ArgumentException>(() => _catalog.Register(attack));
            attack.projectileInfo.aoeRadius = 1.4f;
            Assert.Throws<ArgumentException>(() => _catalog.Register(attack));

            attack.projectileInfo.trajectory = BulletMoveType.Straight;
            int id = _catalog.Register(attack);
            attack.projectileInfo = default;
            Assert.IsTrue(_catalog.TryGet(id, out ActionCatalog.Entry entry));
            MagicDefinition magic = entry.CreateMagic();
            Assert.AreEqual(BulletMoveType.Straight, magic.bulletProfile.moveType);
            Assert.AreEqual(6f, magic.groundImpactRange);
            Assert.AreEqual(1.4f, magic.bulletProfile.explodeRadius);
        }

        [Test]
        public void ActionCatalog_PacketArraysAreMutated_LeavesLaterAttackAndMagicPacketsUnchanged()
        {
            int id = _catalog.Register(CreateAttack());
            Assert.IsTrue(_catalog.TryGet(id, out ActionCatalog.Entry entry));
            AttackMotionData attack = entry.CreateAttack();
            MagicDefinition magic = entry.CreateMagic();
            Assert.AreNotSame(attack.statusEffects, magic.statusEffects);
            attack.statusEffects[0] = default;
            magic.statusEffects[0] = default;

            AttackMotionData nextAttack = entry.CreateAttack();
            MagicDefinition nextMagic = entry.CreateMagic();

            Assert.AreEqual(StatusEffectId.Slow, nextAttack.statusEffects[0].info.effect);
            Assert.AreEqual(3f, nextAttack.statusEffects[0].info.duration);
            Assert.AreEqual(StatusEffectId.Slow, nextMagic.statusEffects[0].info.effect);
            Assert.AreEqual(3f, nextMagic.statusEffects[0].info.duration);
            Assert.AreNotSame(attack.statusEffects, nextAttack.statusEffects);
            Assert.AreNotSame(magic.statusEffects, nextMagic.statusEffects);
            Assert.AreNotSame(nextAttack.statusEffects, nextMagic.statusEffects);
        }

        [Test]
        public void ActionCatalog_VariantAndRecoveryAfterAuthoringChanges_InheritOriginalSnapshot()
        {
            AttackInfo attack = CreateAttack();
            int originalId = _catalog.Register(attack);
            attack.divingLandingRecoveryDuration = 8f;
            attack.airHangDuration = 9f;
            attack.damageMultiplier = 90f;
            attack.motion.animatorLayer = 9;
            int divingId = _catalog.Register(attack, ActionVariant.Diving);
            int recoveryId = _catalog.RegisterLandingRecovery(divingId);
            Assert.IsTrue(_catalog.TryGet(originalId, out ActionCatalog.Entry original));
            Assert.IsTrue(_catalog.TryGet(divingId, out ActionCatalog.Entry diving));
            Assert.IsTrue(_catalog.TryGet(recoveryId, out ActionCatalog.Entry recovery));

            Assert.AreEqual(ActionEndMode.Timed, original.definition.endMode);
            Assert.AreEqual(ActionEndMode.Manual, diving.definition.endMode);
            Assert.AreEqual(ActionVariant.Diving, diving.variant);
            Assert.AreEqual(0.4f, diving.attackSettings.DivingLandingRecoveryDuration);
            Assert.AreEqual(0.3f, diving.attackSettings.AirHangDuration);
            Assert.AreEqual(2f, diving.CreateAttack().motionValue);
            Assert.AreEqual(0.4f, recovery.definition.recoveryDuration);
            Assert.AreEqual(ActionResources.All, recovery.definition.resources);
            Assert.AreEqual(2, diving.presentation.AnimatorLayer);
            Assert.AreEqual(2, recovery.presentation.AnimatorLayer);
            Assert.AreEqual(divingId, _catalog.Register(attack, ActionVariant.Diving));
            Assert.AreEqual(recoveryId, _catalog.RegisterLandingRecovery(divingId));
        }

        [TestCase("child")]
        [TestCase("trigger")]
        [TestCase("elapsed")]
        [TestCase("missing")]
        public void ActionCatalog_InvalidGraph_RejectsWithoutRemovingExistingDefinitionsAndCanRetry(string invalidField)
        {
            ActionDefinition existing = CreateAction();
            int existingId = _catalog.Register(existing);
            ActionDefinition root = CreateAction();
            ActionDefinition child = CreateAction();
            ActionTransitionDefinition valid = new ActionTransitionDefinition
            {
                trigger = ActionSignalKind.Completed, next = child,
            };
            ActionTransitionDefinition invalid = valid;
            switch (invalidField)
            {
                case "child": child.staminaCost = float.NaN; break;
                case "trigger": invalid.trigger = (ActionSignalKind)255; break;
                case "elapsed": invalid.minElapsed = -1f; break;
                case "missing": invalid.next = null; break;
            }
            root.transitions = new[] { invalid };

            Assert.Throws<ArgumentException>(() => _catalog.Register(root));

            Assert.AreEqual(1, _execution.DefinitionCount);
            Assert.IsTrue(_catalog.TryGet(existingId, out ActionCatalog.Entry retained));
            Assert.AreEqual(0.25f, retained.definition.activeDuration);
            Assert.AreEqual(existingId, _catalog.Register(existing));
            child.staminaCost = 0f;
            root.transitions = new[] { valid };
            int repaired = _catalog.Register(root);
            Assert.IsTrue(_catalog.TryGet(repaired, out ActionCatalog.Entry repairedEntry));
            Assert.AreEqual(1, repairedEntry.definition.transitions.Count);
            Assert.AreEqual(3, _execution.DefinitionCount);
        }

        [Test]
        public void ActionCatalog_CyclicGraph_ResolvesBothDirectionsAndRegistersEachDefinitionOnce()
        {
            ActionDefinition first = CreateAction();
            ActionDefinition second = CreateAction();
            first.transitions = new[]
            {
                new ActionTransitionDefinition { trigger = ActionSignalKind.Completed, next = second },
            };
            second.transitions = new[]
            {
                new ActionTransitionDefinition { trigger = ActionSignalKind.Completed, next = first },
            };

            int firstId = _catalog.Register(first);
            int secondId = _catalog.Register(second);

            Assert.AreEqual(2, _execution.DefinitionCount);
            Assert.IsTrue(_catalog.TryGet(firstId, out ActionCatalog.Entry firstEntry));
            Assert.IsTrue(_catalog.TryGet(secondId, out ActionCatalog.Entry secondEntry));
            Assert.AreEqual(secondId, firstEntry.definition.transitions[0].nextDefinitionId);
            Assert.AreEqual(firstId, secondEntry.definition.transitions[0].nextDefinitionId);
        }

        [Test]
        public void ActionCatalog_MotionAuthoringChanges_PreservesTimingAndPresentationSettings()
        {
            MotionDefinition motion = CreateMotion();
            int id = _catalog.Register(motion);
            motion.info = new MotionInfo { activeMotionDuration = 8f };
            motion.animatorLayer = 8;
            motion.blendDuration = 8f;
            motion.loop = true;

            Assert.IsTrue(_catalog.TryGet(id, out ActionCatalog.Entry entry));
            Assert.IsTrue(entry.definition.motionOnly);
            Assert.AreEqual(0.25f, entry.definition.activeDuration);
            Assert.AreEqual(0.25f, entry.presentation.Motion.activeMotionDuration);
            Assert.AreEqual(2, entry.presentation.AnimatorLayer);
            Assert.AreEqual(0.15f, entry.presentation.BlendDuration);
            Assert.IsFalse(entry.presentation.Loop);
            Assert.AreEqual(ActionEndMode.Timed, entry.definition.endMode);
            Assert.AreEqual(id, _catalog.Register(motion));
        }

        [TestCase("distance")]
        [TestCase("gravity")]
        public void ActionCatalog_OverflowingDerivedMovement_RejectsBeforeRegistration(string field)
        {
            AttackInfo attack = CreateAttack();
            if (field == "distance")
            {
                attack.movement = new ActionMovementProfile { speed = float.MaxValue, duration = 2f };
            }
            else
            {
                attack.divingGravityMultiplier = float.MaxValue;
            }

            Assert.Throws<ArgumentException>(() => _catalog.Register(attack));
            Assert.AreEqual(0, _execution.DefinitionCount);
        }

        [TestCase(0f, false)]
        [TestCase(2f, true)]
        public void ActionCatalog_ProjectileRadius_EnablesExplosionAndPreservesFinitePierceCount(float radius, bool explodes)
        {
            AttackInfo attack = CreateAttack();
            ProjectileInfo projectile = attack.projectileInfo;
            projectile.aoeRadius = radius;
            projectile.pierceCount = 2;
            attack.projectileInfo = projectile;
            int id = _catalog.Register(attack);
            Assert.IsTrue(_catalog.TryGet(id, out ActionCatalog.Entry entry));
            MagicDefinition magic = entry.CreateMagic();

            Assert.AreEqual(radius, magic.bulletProfile.explodeRadius);
            Assert.AreEqual(explodes ? BulletFeature.Explode : BulletFeature.None, magic.bulletProfile.features);
            Assert.AreEqual(3, magic.bulletProfile.hitLimit);
            Projectile core = new Projectile();
            core.Initialize(1, magic.bulletProfile, Vector2.zero, Vector2.right);
            core.RegisterHit();
            core.RegisterHit();
            Assert.IsTrue(core.IsAlive);
            core.RegisterHit();
            Assert.IsFalse(core.IsAlive);
        }

        [Test]
        public void ActionCatalog_ProjectileAuthoringAndPacketChange_PreservesPrefabAndShotSnapshot()
        {
            GameObject prefab = new GameObject("Original projectile");
            GameObject replacement = new GameObject("Replacement projectile");
            _assets.Add(prefab);
            _assets.Add(replacement);
            AttackInfo attack = CreateAttack();
            ProjectileInfo projectile = attack.projectileInfo;
            projectile.projectilePrefab = prefab;
            projectile.aoeRadius = 2f;
            projectile.spreadAngle = 30f;
            attack.projectileInfo = projectile;
            int id = _catalog.Register(attack);
            Assert.IsTrue(_catalog.TryGet(id, out ActionCatalog.Entry entry));

            projectile.projectilePrefab = replacement;
            projectile.aoeRadius = 0f;
            projectile.projectileCount = 9;
            projectile.spreadAngle = 180f;
            attack.projectileInfo = projectile;
            MagicDefinition packet = entry.CreateMagic();
            packet.projectilePrefab = replacement;
            packet.bulletProfile = default;
            packet.bulletCount = 9;
            MagicDefinition next = entry.CreateMagic();

            Assert.AreSame(prefab, next.projectilePrefab);
            Assert.AreEqual(2f, next.bulletProfile.explodeRadius);
            Assert.AreEqual(BulletFeature.Explode, next.bulletProfile.features);
            Assert.AreEqual(2, next.bulletCount);
            Assert.AreEqual(30f, entry.spreadAngle);
            Assert.IsFalse(next.attackContext.IsCaptured);
        }

        [TestCase(0, 1)]
        [TestCase(3, 3)]
        public void ActionCatalog_ProjectileCount_CapturesLegacyZeroDefault(int count, int expected)
        {
            AttackInfo attack = CreateAttack();
            ProjectileInfo projectile = attack.projectileInfo;
            projectile.projectileCount = count;
            attack.projectileInfo = projectile;
            int id = _catalog.Register(attack);

            Assert.IsTrue(_catalog.TryGet(id, out ActionCatalog.Entry entry));
            Assert.AreEqual(expected, entry.CreateMagic().bulletCount);
            Assert.IsNull(entry.CreateMagic().projectilePrefab);
        }

        [TestCase("speed-negative")]
        [TestCase("speed-nan")]
        [TestCase("lifetime-infinity")]
        [TestCase("radius-negative")]
        [TestCase("radius-nan")]
        [TestCase("homing-negative")]
        [TestCase("homing-infinity")]
        [TestCase("spread-negative")]
        [TestCase("spread-overflow")]
        [TestCase("spread-nan")]
        [TestCase("pierce-negative")]
        [TestCase("pierce-overflow")]
        [TestCase("count-negative")]
        [TestCase("trajectory")]
        public void ActionCatalog_InvalidProjectileInput_RejectsBeforeRegistration(string field)
        {
            AttackInfo attack = CreateAttack();
            ProjectileInfo projectile = attack.projectileInfo;
            switch (field)
            {
                case "speed-negative": projectile.speed = -1f; break;
                case "speed-nan": projectile.speed = float.NaN; break;
                case "lifetime-infinity": projectile.lifetime = float.PositiveInfinity; break;
                case "radius-negative": projectile.aoeRadius = -1f; break;
                case "radius-nan": projectile.aoeRadius = float.NaN; break;
                case "homing-negative": projectile.homingStrength = -1f; break;
                case "homing-infinity": projectile.homingStrength = float.PositiveInfinity; break;
                case "spread-negative": projectile.spreadAngle = -1f; break;
                case "spread-overflow": projectile.spreadAngle = 361f; break;
                case "spread-nan": projectile.spreadAngle = float.NaN; break;
                case "pierce-negative": projectile.pierceCount = -1; break;
                case "pierce-overflow": projectile.pierceCount = int.MaxValue; break;
                case "count-negative": projectile.projectileCount = -1; break;
                case "trajectory": projectile.trajectory = (BulletMoveType)255; break;
            }
            attack.projectileInfo = projectile;

            Assert.Throws<ArgumentException>(() => _catalog.Register(attack));
            Assert.AreEqual(0, _execution.DefinitionCount);
        }

        private ActionDefinition CreateAction()
        {
            ActionDefinition action = Create<ActionDefinition>();
            action.motion = CreateMotion();
            return action;
        }

        private MotionDefinition CreateMotion()
        {
            MotionDefinition motion = Create<MotionDefinition>();
            motion.info = new MotionInfo { activeMotionDuration = 0.25f };
            motion.animatorLayer = 2;
            motion.blendDuration = 0.15f;
            return motion;
        }

        private AttackInfo CreateAttack()
        {
            StatusEffectDefinition effect = Create<StatusEffectDefinition>();
            effect.effect = StatusEffectId.Slow;
            effect.duration = 3f;
            effect.modifier = 0.25f;
            AttackInfo attack = Create<AttackInfo>();
            attack.motion = CreateMotion();
            attack.staminaCost = 5f;
            attack.damageMultiplier = 2f;
            attack.inputWindow = 0.12f;
            attack.divingLandingRecoveryDuration = 0.4f;
            attack.actionEffects = new[]
            {
                new ActionEffect { type = ActionEffectType.Armor, duration = 0.2f, value = 7f },
            };
            attack.statusEffects = new[]
            {
                new StatusEffectApplication { definition = effect, mode = StatusEffectApplicationMode.Immediate },
            };
            attack.projectileInfo = new ProjectileInfo
            {
                hasProjectile = true, speed = 4f, lifetime = 1f, projectileCount = 2,
            };
            return attack;
        }

        [TestCase(ActionVariant.Default)]
        [TestCase(ActionVariant.Diving)]
        public void ActionCatalog_RegisteredIdentityLookup_ReturnsCapturedSettingsWithoutReregistering(ActionVariant variant)
        {
            AttackInfo attack = CreateAttack();
            Assert.IsFalse(_catalog.TryGetRegistered(attack, variant, out _));
            Assert.AreEqual(0, _execution.DefinitionCount);
            int id = _catalog.Register(attack, variant);
            int count = _execution.DefinitionCount;
            attack.inputWindow = float.NaN;
            attack.staminaCost = float.NaN;

            Assert.IsTrue(_catalog.TryGetRegistered(attack, variant, out ActionCatalog.Entry entry));

            Assert.AreEqual(id, entry.definition.id);
            Assert.AreEqual(0.12f, entry.attackSettings.InputWindow);
            Assert.AreEqual(count, _execution.DefinitionCount);
        }

        [TestCase("anticipation")]
        [TestCase("active")]
        [TestCase("recovery")]
        [TestCase("blend")]
        public void ActionCatalog_InvalidMotionTiming_RejectsBeforeRegistration(string field)
        {
            MotionDefinition motion = CreateMotion();
            MotionInfo info = motion.info;
            switch (field)
            {
                case "anticipation": info.preMotionDuration = float.NaN; break;
                case "active": info.activeMotionDuration = -1f; break;
                case "recovery": info.recoveryDuration = float.PositiveInfinity; break;
                case "blend": motion.blendDuration = float.NaN; break;
            }
            motion.info = info;

            Assert.Throws<ArgumentException>(() => _catalog.Register(motion));
            Assert.AreEqual(0, _execution.DefinitionCount);
        }

        private T Create<T>() where T : ScriptableObject
        {
            T asset = ScriptableObject.CreateInstance<T>();
            _assets.Add(asset);
            return asset;
        }
    }
}
