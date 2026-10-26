using System;
using System.Collections;
using System.Collections.Generic;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using R3;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Game.Tests.PlayMode
{
    public class ActionRuntimeFlowTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();
        private readonly List<Object> _assets = new List<Object>();
        private ProjectileManager _projectiles;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            GameObject manager = new GameObject("ActionRuntimeFlowManager");
            manager.SetActive(false);
            GameObject projectiles = new GameObject("Projectiles");
            projectiles.transform.SetParent(manager.transform, false);
            _projectiles = projectiles.AddComponent<ProjectileManager>();
            manager.AddComponent<GameManager>();
            manager.SetActive(true);
            GameManager.Instance.enabled = false;
            _objects.Add(TestSceneHelper.CreateGround());
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_projectiles != null) { _projectiles.ClearAll(); }
            for (int i = _objects.Count - 1; i >= 0; i--)
            {
                if (_objects[i] != null) { Object.Destroy(_objects[i]); }
            }
            _objects.Clear();
            yield return null;
            for (int i = _assets.Count - 1; i >= 0; i--)
            {
                if (_assets[i] != null) { Object.Destroy(_assets[i]); }
            }
            _assets.Clear();
            TestSceneHelper.Cleanup();
            yield return null;
        }

        private T CreateAsset<T>() where T : ScriptableObject
        {
            T asset = ScriptableObject.CreateInstance<T>();
            _assets.Add(asset);
            return asset;
        }

        private BaseCharacter CreateActor(bool presentation = false)
        {
            Game.Core.CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(maxHp: 1000);
            info.name = "ActionRuntimeActor" + _objects.Count;
            _assets.Add(info);
            Vector3 position = new Vector3(_objects.Count * 3f, 2f, 0f);
            GameObject actor = presentation
                ? TestSceneHelper.CreateBaseCharacterObjectWithDamageReceiver(info, position)
                : TestSceneHelper.CreateBaseCharacterObject(info, position);
            _objects.Add(actor);
            BaseCharacter character = actor.GetComponent<BaseCharacter>();
            character.Rigidbody.gravityScale = 0f;
            return character;
        }

        private ActionDefinition CreateAction(float duration, ActionResources resources, ActState state = ActState.Attacking)
        {
            ActionDefinition action = CreateAsset<ActionDefinition>();
            action.durationMode = ActionDurationMode.Fixed;
            action.duration = duration;
            action.resources = resources;
            action.actState = state;
            return action;
        }

        private static void Advance(float duration)
        {
            GameManager.Actions.Execution.Tick(duration);
            GameManager.Actions.Tick();
        }

        [UnityTest]
        public IEnumerator ActionRuntime_GenericMovement_AppliesTimedInvincibilityWithoutDodge()
        {
            BaseCharacter actor = CreateActor();
            yield return null;
            int owner = actor.ObjectHash;
            ActionDefinition action = CreateAction(1f, ActionResources.HorizontalMovement, ActState.Running);
            action.staminaCost = 5f;
            action.mpCost = 2;
            action.movement = new ActionMovementProfile { speed = 4f, duration = 1f };
            action.actionEffects = new[]
            {
                new ActionEffect { type = ActionEffectType.Invincible, startTime = 0.1f, duration = 0.2f },
            };
            actor.Rigidbody.linearVelocity = new Vector2(0f, 3f);

            Assert.IsTrue(GameManager.Actions.Execute(owner, action, 0, Vector2.right, out long id));
            GameManager.Actions.ApplyMovement(owner, actor.Rigidbody);
            Assert.AreEqual(4f, actor.Rigidbody.linearVelocity.x);
            Assert.AreEqual(3f, actor.Rigidbody.linearVelocity.y);
            Assert.AreEqual(95f, GameManager.Data.GetVitals(owner).currentStamina);
            Assert.AreEqual(48, GameManager.Data.GetVitals(owner).currentMp);
            Assert.IsFalse(GameManager.Actions.Execution.EvaluateEffects(owner).isInvincible);
            Assert.IsFalse(GameManager.Actions.IsDodging(owner));

            Advance(0.15f);
            Assert.IsTrue(GameManager.Actions.Execution.EvaluateEffects(owner).isInvincible);
            Advance(0.21f);
            Assert.IsFalse(GameManager.Actions.Execution.EvaluateEffects(owner).isInvincible);
            Advance(0.7f);
            Assert.IsFalse(GameManager.Actions.Execution.TryGetInstance(id, out _));
            Assert.AreEqual(ActState.Neutral, GameManager.Data.GetFlags(owner).ActState);
        }

        [UnityTest]
        public IEnumerator ActionRuntime_RegisteredMeleeChangedToProjectile_UsesCapturedMeleeWithoutProjectileManager()
        {
            BaseCharacter actor = CreateActor();
            yield return null;
            AttackInfo attack = CreateAsset<AttackInfo>();
            attack.durationMode = ActionDurationMode.Fixed;
            attack.duration = 1f;
            attack.staminaCost = 5f;
            GameManager.Actions.RegisterDefinition(attack);
            attack.projectileInfo = new ProjectileInfo { hasProjectile = true };
            Object.Destroy(_projectiles.gameObject);
            yield return null;
            Assert.IsTrue(GameManager.Projectiles == null);

            Assert.IsTrue(GameManager.Actions.Execute(actor.ObjectHash, attack, 0, Vector2.right, out _));
            Assert.AreEqual(95f, GameManager.Data.GetVitals(actor.ObjectHash).currentStamina);
        }

        [UnityTest]
        public IEnumerator ActionRuntime_RegisteredProjectileChangedToMelee_StillRequiresProjectileManagerWithoutSpendingCost()
        {
            BaseCharacter actor = CreateActor();
            yield return null;
            AttackInfo attack = CreateAsset<AttackInfo>();
            attack.durationMode = ActionDurationMode.Fixed;
            attack.duration = 1f;
            attack.staminaCost = 5f;
            attack.projectileInfo = new ProjectileInfo { hasProjectile = true };
            GameManager.Actions.RegisterDefinition(attack);
            attack.projectileInfo = default;
            Object.Destroy(_projectiles.gameObject);
            yield return null;
            Assert.IsTrue(GameManager.Projectiles == null);

            Assert.IsFalse(GameManager.Actions.Execute(actor.ObjectHash, attack, 0, Vector2.right, out _));
            Assert.AreEqual(100f, GameManager.Data.GetVitals(actor.ObjectHash).currentStamina);
        }

        [UnityTest]
        public IEnumerator ActionRuntime_MotionOnlyAlongsideGameplay_PreservesCostAndPrimaryState()
        {
            BaseCharacter actor = CreateActor(true);
            yield return null;
            int owner = actor.ObjectHash;
            ActionDefinition action = CreateAction(2f, ActionResources.Hands | ActionResources.HorizontalMovement);
            action.staminaCost = 7f;
            action.mpCost = 3;
            Assert.IsTrue(GameManager.Actions.Execute(owner, action, 0, Vector2.right, out long gameplayId));
            GameManager.Actions.Present();
            AnimationStateData before = GameManager.Data.GetAnimationState(owner);
            float stamina = GameManager.Data.GetVitals(owner).currentStamina;
            float mp = GameManager.Data.GetVitals(owner).currentMp;
            MotionDefinition motion = CreateAsset<MotionDefinition>();
            AnimationClip clip = new AnimationClip { name = "RuntimeFlowEmote" };
            _assets.Add(clip);
            motion.info = new MotionInfo { activeMotionDuration = 1f, activeClip = clip };
            motion.resources = ActionResources.UpperPose;
            motion.blendDuration = 0f;

            Assert.IsTrue(GameManager.Actions.PlayMotion(owner, motion, out long motionId));
            Advance(0.2f);
            GameManager.Actions.Present();

            Assert.IsTrue(GameManager.Actions.Execution.TryGetInstance(gameplayId, out _));
            Assert.IsTrue(GameManager.Actions.Execution.TryGetInstance(motionId, out _));
            Assert.IsTrue(GameManager.Actions.Execution.GetPrimary(owner, out ActionExecution primary));
            Assert.AreEqual(gameplayId, primary.instanceId);
            Assert.AreEqual(stamina, GameManager.Data.GetVitals(owner).currentStamina);
            Assert.AreEqual(mp, GameManager.Data.GetVitals(owner).currentMp);
            Assert.AreEqual(ActState.Attacking, GameManager.Data.GetFlags(owner).ActState);
            Assert.AreEqual(before.currentPhase, GameManager.Data.GetAnimationState(owner).currentPhase);
            Assert.AreEqual(before.currentMoveId, GameManager.Data.GetAnimationState(owner).currentMoveId);
            Assert.IsTrue(actor.AnimationController.HasMotion(motionId));
        }

        [UnityTest]
        public IEnumerator ActionRuntime_HitConfirmedForInstance_ChainsOnlyMatchingSource()
        {
            BaseCharacter actor = CreateActor();
            BaseCharacter target = CreateActor();
            yield return null;
            int owner = actor.ObjectHash;
            ActionDefinition followUp = CreateAction(1f, ActionResources.Hands);
            followUp.staminaCost = 9f;
            ActionDefinition first = CreateAction(1f, ActionResources.Hands);
            first.transitions = new[]
            {
                new ActionTransitionDefinition
                {
                    trigger = ActionSignalKind.HitConfirmed, requiresHit = true, next = followUp,
                },
            };
            ActionDefinition unrelated = CreateAction(1f, ActionResources.LowerPose);
            Assert.IsTrue(GameManager.Actions.Execute(owner, first, target.ObjectHash, Vector2.right, out long firstId));
            Assert.IsTrue(GameManager.Actions.Execute(owner, unrelated, target.ObjectHash, Vector2.right, out long otherId));
            long chainedId = 0;
            int chains = 0;
            using IDisposable subscription = GameManager.Events.OnActionExecution.Subscribe(change =>
            {
                if (change.kind == ActionSignalKind.Started && change.execution.sourceInstanceId == firstId)
                {
                    chains++;
                    chainedId = change.execution.instanceId;
                }
            });

            GameManager.Events.FireDamageDealt(new DamageResult { actionInstanceId = otherId, totalDamage = 1 }, owner, target.ObjectHash);
            Advance(0f);
            Assert.AreEqual(0, chains);
            Assert.IsTrue(GameManager.Actions.Execution.TryGetInstance(firstId, out _));
            GameManager.Events.FireDamageDealt(new DamageResult { actionInstanceId = firstId, totalDamage = 1 }, owner, target.ObjectHash);
            Advance(0f);

            Assert.AreEqual(1, chains);
            Assert.IsTrue(GameManager.Actions.Execution.TryGetInstance(chainedId, out _));
            Assert.IsFalse(GameManager.Actions.Execution.TryGetInstance(firstId, out _));
            Assert.IsTrue(GameManager.Actions.Execution.TryGetInstance(otherId, out _));
            Assert.AreEqual(91f, GameManager.Data.GetVitals(owner).currentStamina);
        }

        [UnityTest]
        public IEnumerator ActionRuntime_CastEntersActive_SpawnsProjectilesOnceWithExecutionId()
        {
            BaseCharacter actor = CreateActor();
            yield return null;
            int owner = actor.ObjectHash;
            AttackInfo cast = CreateAsset<AttackInfo>();
            MotionDefinition motion = CreateAsset<MotionDefinition>();
            motion.info = new MotionInfo { preMotionDuration = 0.1f, activeMotionDuration = 0.2f, recoveryDuration = 0.1f };
            cast.motion = motion;
            cast.mpCost = 5;
            cast.damageMultiplier = 1f;
            cast.projectileInfo = new ProjectileInfo
            {
                hasProjectile = true, projectileCount = 2, speed = 3f, lifetime = 5f, spreadAngle = 10f,
            };

            Assert.AreSame(_projectiles, GameManager.Projectiles);
            Assert.IsTrue(GameManager.Actions.Execute(owner, cast, 0, Vector2.right, out long id));
            Assert.AreEqual(0, _projectiles.ActiveCount);
            Assert.AreEqual(45, GameManager.Data.GetVitals(owner).currentMp);
            Advance(0.11f);
            Assert.AreEqual(2, _projectiles.ActiveCount);
            ProjectileController[] controllers = Object.FindObjectsByType<ProjectileController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int spawned = 0;
            for (int i = 0; i < controllers.Length; i++)
            {
                if (!controllers[i].IsActive || controllers[i].Magic.actionInstanceId != id) { continue; }
                spawned++;
                _objects.Add(controllers[i].gameObject);
                Assert.IsTrue(controllers[i].IsActive);
                Assert.IsTrue(controllers[i].gameObject.activeInHierarchy);
                Assert.IsTrue(controllers[i].GetComponent<CircleCollider2D>().enabled);
                Assert.AreEqual(id, controllers[i].Magic.actionInstanceId);
                Assert.AreEqual(owner, controllers[i].CoreProjectile.CasterHash);
            }
            Assert.AreEqual(2, spawned);
            Advance(0.05f);
            Assert.AreEqual(2, _projectiles.ActiveCount);
        }

        [UnityTest]
        public IEnumerator ActionRuntime_LastMpAcceptedCast_FiresWhileVanishedButNewPublicSpawnIsRejected()
        {
            BaseCharacter actor = CreateActor();
            yield return null;
            int owner = actor.ObjectHash;
            AttackInfo cast = CreateAsset<AttackInfo>();
            MotionDefinition motion = CreateAsset<MotionDefinition>();
            motion.info = new MotionInfo { preMotionDuration = 0.1f, activeMotionDuration = 0.2f, recoveryDuration = 0.1f };
            cast.motion = motion;
            cast.mpCost = 50;
            cast.damageMultiplier = 1f;
            cast.projectileInfo = new ProjectileInfo
            {
                hasProjectile = true, projectileCount = 1, speed = 3f, lifetime = 5f,
            };
            Assert.IsTrue(GameManager.Actions.Execute(owner, cast, 0, Vector2.right, 1.5f, ActionVariant.Default,
                out long id, out _));
            using CompanionMpManager mp = new CompanionMpManager(new SoAExecutionWorld(GameManager.Data), owner, 0,
                new CompanionMpSettings { returnThresholdRatio = 0.5f, vanishRecoveryMultiplier = 1f });
            mp.Tick(0f);
            Assert.IsTrue(mp.IsVanished);
            Assert.AreEqual(0f, GameManager.Data.GetVitals(owner).currentMp);

            Advance(0.11f);

            Assert.AreEqual(1, _projectiles.ActiveCount);
            ProjectileController[] controllers = Object.FindObjectsByType<ProjectileController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int matching = 0;
            for (int i = 0; i < controllers.Length; i++)
            {
                if (!controllers[i].IsActive || controllers[i].Magic.actionInstanceId != id) { continue; }
                matching++;
                Assert.AreEqual(1.5f, controllers[i].Magic.motionValue);
                _objects.Add(controllers[i].gameObject);
            }
            Assert.AreEqual(1, matching);
            Assert.IsNull(_projectiles.SpawnProjectile(owner, new MagicDefinition
            {
                actionInstanceId = id, magicType = MagicType.Attack,
                bulletProfile = new BulletProfile { speed = 3f, lifeTime = 5f },
            }, Vector2.zero, Vector2.right));
            Assert.AreEqual(1, _projectiles.ActiveCount);
        }

        [UnityTest]
        public IEnumerator ActionRuntime_ReactionExpires_PreservesOtherActorsHeldAction()
        {
            BaseCharacter actor = CreateActor();
            BaseCharacter other = CreateActor();
            yield return null;
            int owner = actor.ObjectHash;
            int otherOwner = other.ObjectHash;
            ActionDefinition attack = CreateAction(2f, ActionResources.Hands);
            Assert.IsTrue(GameManager.Actions.Execute(owner, attack, 0, Vector2.right, out long attackId));
            Assert.IsTrue(GameManager.Actions.TrySetGuarding(otherOwner, true, out long guardId));
            float otherStamina = GameManager.Data.GetVitals(otherOwner).currentStamina;

            GameManager.Actions.React(owner, HitReaction.Flinch);
            Assert.IsFalse(GameManager.Actions.Execution.TryGetInstance(attackId, out _));
            Assert.AreEqual(ActState.Flinch, GameManager.Data.GetFlags(owner).ActState);
            Advance(HitReactionLogic.k_FlinchDuration - 0.01f);
            Assert.AreEqual(ActState.Flinch, GameManager.Data.GetFlags(owner).ActState);
            Advance(0.02f);

            Assert.AreEqual(ActState.Neutral, GameManager.Data.GetFlags(owner).ActState);
            Assert.IsTrue(GameManager.Actions.Execution.TryGetInstance(guardId, out _));
            Assert.AreEqual(ActState.Guarding, GameManager.Data.GetFlags(otherOwner).ActState);
            Assert.IsTrue(GameManager.Data.GetDefenseState(otherOwner).isGuarding);
            Assert.AreEqual(otherStamina, GameManager.Data.GetVitals(otherOwner).currentStamina);
        }

        [UnityTest]
        public IEnumerator ActionRuntime_TimedMoveReplacesLocomotion_UsesRequestedLifetime()
        {
            BaseCharacter actor = CreateActor();
            yield return null;
            int owner = actor.ObjectHash;
            GameManager.Actions.SetLocomotion(owner, Vector2.right, 2f);
            Assert.IsTrue(GameManager.Actions.TrySetLocomotion(owner, Vector2.left, 3f, out long id, 0.2f));
            Advance(0.21f);
            Assert.IsFalse(GameManager.Actions.Execution.TryGetInstance(id, out _));
            Assert.AreEqual(ActState.Neutral, GameManager.Data.GetFlags(owner).ActState);
        }

        [UnityTest]
        public IEnumerator ActionRuntime_ExplicitVerticalMoveAndDodge_PreserveConfiguredAxes()
        {
            BaseCharacter actor = CreateActor();
            yield return null;
            int owner = actor.ObjectHash;
            ActionDefinition vertical = CreateAction(0.2f, ActionResources.VerticalMovement);
            vertical.movement = new ActionMovementProfile { speed = 5f, duration = 0.2f };
            actor.Rigidbody.linearVelocity = new Vector2(2f, -1f);
            Assert.IsTrue(GameManager.Actions.Execute(owner, vertical, 0, Vector2.up, out _));
            GameManager.Actions.ApplyMovement(owner, actor.Rigidbody);
            Assert.AreEqual(new Vector2(2f, 5f), actor.Rigidbody.linearVelocity);
            Advance(0.21f);
            GameManager.Data.GetMoveParams(owner).dashSpeed = 9f;
            actor.Rigidbody.linearVelocity = new Vector2(0f, -3f);
            Assert.IsTrue(GameManager.Actions.TryDodge(owner, Vector2.right));
            GameManager.Actions.ApplyMovement(owner, actor.Rigidbody);
            Assert.AreEqual(new Vector2(9f, -3f), actor.Rigidbody.linearVelocity);
        }

        [UnityTest]
        public IEnumerator ActionRuntime_KinematicOutput_IsOwnedByItsExecution()
        {
            BaseCharacter actor = CreateActor();
            yield return null;
            int owner = actor.ObjectHash;
            ActionDefinition action = CreateAction(1f, ActionResources.HorizontalMovement);
            Assert.IsTrue(GameManager.Actions.Execute(owner, action, 0, Vector2.right, out long id));
            Assert.IsTrue(GameManager.Actions.SetMovementOutput(owner, id, new Vector2(7f, 2f)));
            actor.Rigidbody.linearVelocity = new Vector2(0f, 3f);
            GameManager.Actions.ApplyMovement(owner, actor.Rigidbody);
            Assert.AreEqual(new Vector2(7f, 3f), actor.Rigidbody.linearVelocity);
            GameManager.Actions.Execution.Cancel(id, true);
            Assert.IsFalse(GameManager.Actions.SetMovementOutput(owner, id, Vector2.one));
        }
    }
}
