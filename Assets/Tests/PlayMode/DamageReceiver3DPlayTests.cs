using System.Collections;
using System.Collections.Generic;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests.PlayMode
{
    public sealed class DamageReceiver3DPlayTests
    {
        private readonly List<Object> _assets = new List<Object>();
        private Scene _room;
        private Scene _separateRoom;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            TestSceneHelper.Cleanup();
            yield return null;

            TestSceneHelper.CreateGameManager().GetComponent<GameManager>().enabled = false;
            CreateSceneParameters physics = new CreateSceneParameters(LocalPhysicsMode.Physics3D);
            _room = SceneManager.CreateScene("DamageReceiver3D", physics);
            _separateRoom = SceneManager.CreateScene("DamageReceiver3DSeparate", physics);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_room.isLoaded) { yield return SceneManager.UnloadSceneAsync(_room); }
            if (_separateRoom.isLoaded) { yield return SceneManager.UnloadSceneAsync(_separateRoom); }
            foreach (Object asset in _assets)
            {
                Object.Destroy(asset);
            }
            _assets.Clear();
            TestSceneHelper.Cleanup();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Damage3D_FrontOnlyGuard_UsesFacingOnTheWorldFloorPlane()
        {
            CharacterActor3D defender = CreateActor(_room, CharacterBelong.Ally, Vector3.zero, 90f);
            CharacterActor3D front = CreateActor(_room, CharacterBelong.Enemy, new Vector3(2f, 2f, 0f));
            CharacterActor3D rear = CreateActor(_room, CharacterBelong.Enemy, new Vector3(-2f, 0f, 0f));
            yield return null;

            int hp = GameManager.Data.GetVitals(defender.ObjectHash).currentHp;
            GameManager.Data.GetCombatStats(defender.ObjectHash).guardStats.guardDirection = GuardDirection.Front;
            DamageReceiver receiver = defender.GetComponent<DamageReceiver>();
            Assert.IsTrue(receiver.IsAlive);
            receiver.SetGuarding(true);

            DamageResult frontHit = receiver.ReceiveDamage3D(Capture(front, defender), front.transform.position);
            Assert.AreEqual(GuardResult.JustGuard, frontHit.guardResult);
            Assert.AreEqual(hp, GameManager.Data.GetVitals(defender.ObjectHash).currentHp);

            DamageResult rearHit = receiver.ReceiveDamage3D(Capture(rear, defender), rear.transform.position);
            Assert.AreEqual(GuardResult.NoGuard, rearHit.guardResult);
            Assert.That(rearHit.totalDamage, Is.Positive);
            Assert.AreEqual(hp - rearHit.totalDamage, GameManager.Data.GetVitals(defender.ObjectHash).currentHp);
        }

        [UnityTest]
        public IEnumerator Damage3D_GuardPoint_OnAnAuthoredAttackUsesWorldFrontAndExpires()
        {
            CharacterActor3D defender = CreateActor(_room, CharacterBelong.Ally, Vector3.zero, 90f);
            CharacterActor3D front = CreateActor(_room, CharacterBelong.Enemy, Vector3.right * 2f);
            CharacterActor3D rear = CreateActor(_room, CharacterBelong.Enemy, Vector3.left * 2f);
            yield return null;

            int hash = defender.Actor.Hash;
            GameManager.Data.GetVitals(hash).currentArmor = 50f;
            GameManager.Data.GetCombatStats(hash).guardStats = new GuardStats
            {
                guardDirection = GuardDirection.Front, guardStrength = 2f, slashCut = 0.5f,
            };
            MotionDefinition motion = ScriptableObject.CreateInstance<MotionDefinition>();
            motion.info = new MotionInfo { preMotionDuration = 0.5f, activeMotionDuration = 0.25f };
            _assets.Add(motion);
            AttackInfo guardPointAttack = ScriptableObject.CreateInstance<AttackInfo>();
            _assets.Add(guardPointAttack);
            guardPointAttack.motion = motion;
            guardPointAttack.attackElement = Element.Slash;
            guardPointAttack.actionEffects = new[]
            {
                new ActionEffect { type = ActionEffectType.GuardPoint, startTime = 0.125f, duration = 0.125f },
            };
            guardPointAttack.attack3D = new AttackProfile3D
            {
                shape = AttackShape3D.Melee, localCenter = Vector3.forward, halfExtents = Vector3.one,
                targetLayers = LayerMask.GetMask("Enemy"),
            };

            Assert.IsTrue(GameManager.Actions.Execute3D(defender, guardPointAttack, out long execution));
            GameManager.Actions.Execution.Tick(0.125f);
            DamageReceiver receiver = defender.GetComponent<DamageReceiver>();
            DamageData incoming = Capture(front, defender);
            incoming.guardDamage = 12f;
            DamageResult frontHit = receiver.ReceiveDamage3D(incoming, front.transform.position);
            Assert.AreEqual(GuardResult.Guarded, frontHit.guardResult);
            Assert.AreEqual(10f, frontHit.guardDamage);
            Assert.IsFalse(GameManager.Data.GetDefenseState(hash).isGuarding);

            DamageResult rearHit = receiver.ReceiveDamage3D(Capture(rear, defender), rear.transform.position);
            Assert.AreEqual(GuardResult.NoGuard, rearHit.guardResult);
            Assert.Greater(rearHit.totalDamage, frontHit.totalDamage);

            GameManager.Actions.Execution.Tick(0.125f);
            Assert.IsTrue(GameManager.Actions.Execution.TryGetInstance(execution, out ActionExecution windup));
            Assert.AreEqual(AnimationPhase.Anticipation, windup.phase);
            DamageResult expired = receiver.ReceiveDamage3D(incoming, front.transform.position);
            Assert.AreEqual(GuardResult.NoGuard, expired.guardResult);
            Assert.Greater(expired.totalDamage, frontHit.totalDamage);
        }

        [UnityTest]
        public IEnumerator Damage3D_KnockbackWakesUpWhenLandingOnTheTimeoutStep()
        {
            GameObject floor = new GameObject("Knockback floor");
            SceneManager.MoveGameObjectToScene(floor, _room);
            floor.transform.position = Vector3.down * 0.5f;
            floor.AddComponent<BoxCollider>().size = new Vector3(16f, 1f, 16f);
            CharacterActor3D defender = CreateActor(_room, CharacterBelong.Ally, Vector3.up * 0.04f);
            defender.Body.center = Vector3.up * 0.8f;
            defender.Body.height = 1.6f;
            defender.Body.radius = 0.3f;
            CharacterActor3D source = CreateActor(_room, CharacterBelong.Enemy, new Vector3(3f, 1f, 4f));
            yield return null;
            Physics.SyncTransforms();
            const float step = 0.02f;
            PhysicsScene physics = _room.GetPhysicsScene();
            for (int i = 0; i < 16 && !defender.Motor.IsGrounded; i++)
            {
                defender.Motor.Simulate(Vector3.zero, false, step);
                physics.Simulate(step);
            }
            Assert.IsTrue(defender.Motor.IsGrounded);

            int hash = defender.Actor.Hash;
            GameManager.Data.GetVitals(hash).currentArmor = 0f;
            GameManager.Data.GetCombatStats(hash).knockbackResistance = 0.25f;
            Vector3 start = defender.transform.position;
            Quaternion facing = defender.transform.rotation;
            DamageData attack = Capture(source, defender);
            attack.knockbackForce = new Vector2(6f, 5f);
            Assert.AreEqual(HitReaction.Knockback,
                defender.GetComponent<DamageReceiver>().ReceiveDamage3D(attack, source.transform.position).hitReaction);
            GameActionRuntime actions = GameManager.Actions;
            Assert.IsTrue(actions.IsPhysicsControlled(hash));
            Assert.AreEqual(ActState.Knockbacked, GameManager.Data.GetFlags(hash).ActState);

            yield return new WaitForFixedUpdate();
            actions.Execution.Tick(step);
            actions.Tick();
            Vector3 offset = defender.transform.position - start;
            Assert.That(offset.y, Is.GreaterThan(0f));
            Assert.That(Vector3.Dot(offset, new Vector3(-0.6f, 0f, -0.8f)), Is.GreaterThan(0f));
            Assert.That(Quaternion.Angle(facing, defender.transform.rotation), Is.LessThan(0.01f));
            Assert.IsFalse(defender.Motor.IsGrounded);

            actions.Execution.Tick(3.99f - step);
            Assert.IsTrue(actions.IsPhysicsControlled(hash));
            for (int i = 0; i < 80 && !defender.Motor.IsGrounded; i++)
            {
                yield return new WaitForFixedUpdate();
                physics.Simulate(step);
            }
            Assert.IsTrue(defender.Motor.IsGrounded);
            actions.Execution.Tick(step);
            actions.Tick();
            Assert.AreEqual(ActState.WakeUp, GameManager.Data.GetFlags(hash).ActState);
            Assert.IsFalse(actions.IsPhysicsControlled(hash));
            Assert.IsTrue(actions.IsMovementBlocked(hash));

            actions.Execution.Tick(WakeUpLogic.k_DefaultWakeUpDuration + step);
            actions.Tick();
            Assert.IsFalse(actions.IsMovementBlocked(hash));
            Assert.AreEqual(ActState.Neutral, GameManager.Data.GetFlags(hash).ActState);
        }

        [UnityTest]
        public IEnumerator Damage3D_SecondAirborneKnockbackDoesNotStackUpwardSpeed()
        {
            CharacterActor3D defender = CreateActor(_room, CharacterBelong.Ally, Vector3.zero);
            CharacterActor3D source = CreateActor(_room, CharacterBelong.Enemy, Vector3.forward * 3f);
            yield return null;

            int hash = defender.Actor.Hash;
            GameManager.Data.GetVitals(hash).currentArmor = 0f;
            DamageReceiver receiver = defender.GetComponent<DamageReceiver>();
            ActionExecutionSystem executions = GameManager.Actions.Execution;
            DamageData hit = Capture(source, defender);
            hit.knockbackForce = new Vector2(4f, 8f);
            Assert.AreEqual(HitReaction.Knockback, receiver.ReceiveDamage3D(hit, source.transform.position).hitReaction);
            Assert.IsTrue(executions.GetPrimary(hash, out ActionExecution first));
            yield return new WaitForFixedUpdate();
            float risingSpeed = defender.Motor.Velocity.y;
            Assert.Greater(risingSpeed, 3f);

            hit.knockbackForce = new Vector2(4f, 2f);
            Assert.AreEqual(HitReaction.Knockback, receiver.ReceiveDamage3D(hit, source.transform.position).hitReaction);
            Assert.IsTrue(executions.GetPrimary(hash, out ActionExecution second));
            Assert.AreNotEqual(first.instanceId, second.instanceId);
            Assert.IsFalse(executions.TryGetInstance(first.instanceId, out _));
            yield return new WaitForFixedUpdate();
            Assert.That(defender.Motor.Velocity.y, Is.GreaterThan(2f).And.LessThan(risingSpeed));
        }

        [UnityTest]
        public IEnumerator Damage3D_KnockbackWithoutAFloorExpiresAndCannotSurviveReregistration()
        {
            CharacterActor3D defender = CreateActor(_room, CharacterBelong.Ally, Vector3.zero);
            CharacterActor3D source = CreateActor(_room, CharacterBelong.Enemy, Vector3.forward * 3f);
            yield return null;
            ActorRef owner = defender.Actor;
            GameManager.Data.GetVitals(owner.Hash).currentArmor = 0f;
            GameActionRuntime actions = GameManager.Actions;
            DamageReceiver receiver = defender.GetComponent<DamageReceiver>();
            DamageData hit = Capture(source, defender);
            hit.knockbackForce = new Vector2(4f, -6f);

            Assert.AreEqual(HitReaction.Knockback, receiver.ReceiveDamage3D(hit, source.transform.position).hitReaction);
            yield return new WaitForFixedUpdate();
            float fallingSpeed = defender.Motor.Velocity.y;
            Assert.Less(fallingSpeed, -5f);
            actions.Execution.Tick(4.1f);
            actions.Tick();
            Assert.IsFalse(actions.IsPhysicsControlled(owner.Hash));
            Assert.AreEqual(ActState.Neutral, GameManager.Data.GetFlags(owner.Hash).ActState);
            defender.Motor.Simulate(Vector3.zero, false, 0.02f);
            Assert.Less(defender.Motor.Velocity.y, fallingSpeed);

            defender.enabled = false;
            defender.enabled = true;
            Assert.AreNotEqual(owner, defender.Actor);
            defender.Motor.Simulate(Vector3.zero, false, 0.02f);
            Assert.That(defender.Motor.Velocity.y, Is.LessThanOrEqualTo(0f));

            owner = defender.Actor;
            GameManager.Data.GetVitals(owner.Hash).currentArmor = 0f;
            hit = Capture(source, defender);
            hit.knockbackForce = new Vector2(4f, 6f);
            Assert.AreEqual(HitReaction.Knockback, receiver.ReceiveDamage3D(hit, source.transform.position).hitReaction);
            defender.enabled = false;
            defender.enabled = true;
            Assert.AreNotEqual(owner, defender.Actor);
            defender.Motor.Simulate(Vector3.zero, false, 0.02f);
            Assert.That(defender.Motor.Velocity.y, Is.LessThanOrEqualTo(0f));
            Assert.IsFalse(actions.IsMovementBlocked(defender.Actor.Hash));
        }

        [UnityTest]
        public IEnumerator Damage3D_ForeignSceneFriendlyActorOrReusedGeneration_RejectsTheHit()
        {
            CharacterActor3D defender = CreateActor(_room, CharacterBelong.Ally, Vector3.zero);
            CharacterActor3D source = CreateActor(_room, CharacterBelong.Enemy, Vector3.forward * 2f);
            CharacterActor3D friend = CreateActor(_room, CharacterBelong.Ally, Vector3.back * 2f);
            CharacterActor3D foreign = CreateActor(_separateRoom, CharacterBelong.Enemy, Vector3.forward * 2f);
            yield return null;

            int hp = GameManager.Data.GetVitals(defender.ObjectHash).currentHp;
            DamageReceiver receiver = defender.GetComponent<DamageReceiver>();
            Assert.That(receiver.ReceiveDamage3D(Capture(friend, defender), friend.transform.position).totalDamage, Is.Zero);
            Assert.That(receiver.ReceiveDamage3D(Capture(foreign, defender), foreign.transform.position).totalDamage, Is.Zero);

            DamageData stale = Capture(source, defender);
            source.gameObject.SetActive(false);
            source.gameObject.SetActive(true);
            yield return null;

            Assert.AreNotEqual(stale.attackerRef, source.Actor);
            Assert.That(receiver.ReceiveDamage3D(stale, source.transform.position).totalDamage, Is.Zero);

            DamageData oldTarget = Capture(source, defender);
            defender.gameObject.SetActive(false);
            defender.gameObject.SetActive(true);
            yield return null;

            Assert.AreNotEqual(oldTarget.defenderRef, defender.Actor);
            Assert.That(receiver.ReceiveDamage3D(oldTarget, source.transform.position).totalDamage, Is.Zero);
            Assert.That(receiver.ReceiveDamage(Capture(source, defender)).totalDamage, Is.Zero);
            Assert.AreEqual(hp, GameManager.Data.GetVitals(defender.ObjectHash).currentHp);
            Assert.That(receiver.ReceiveDamage3D(Capture(source, defender), source.transform.position).totalDamage, Is.Positive);
        }

        private CharacterActor3D CreateActor(Scene scene, CharacterBelong belong, Vector3 position, float yaw = 0f)
        {
            Game.Core.CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(belong, CharacterFeature.Minion);
            _assets.Add(info);
            GameObject body = new GameObject("Damage3D Actor");
            body.SetActive(false);
            SceneManager.MoveGameObjectToScene(body, scene);
            body.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            CharacterActor3D actor = body.AddComponent<CharacterActor3D>();
            actor.SetCharacterInfoForTest(info);
            body.SetActive(true);
            return actor;
        }

        private static DamageData Capture(CharacterActor3D source, CharacterActor3D defender)
        {
            AttackMotionData motion = new AttackMotionData { motionValue = 1f, attackElement = Element.Slash };
            Assert.IsTrue(CombatDataHelper.TryCaptureAttack(source.ObjectHash, motion,
                GameManager.Data, GameManager.Actors, out AttackContext context));
            DamageData attack = CombatDataHelper.BuildDamageData(
                source.ObjectHash, defender.ObjectHash, motion, context.AttackStats);
            context.ApplyTo(ref attack, defender.Actor);
            return attack;
        }
    }
}
