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

        private readonly List<ScriptableObject> _assets = new List<ScriptableObject>();
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
