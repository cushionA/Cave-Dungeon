using System.Collections;
using System.Collections.Generic;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using CharacterInfo = Game.Core.CharacterInfo;

namespace Game.Tests.PlayMode
{
    public class CharacterPhysicsCombatBridgeTests
    {
        private sealed class PhysicsBackend : ICharacterPhysics2D
        {
            public Vector2 Position { get; private set; }
            public Vector2 Velocity { get; private set; }
            public float GravityScale { get; private set; }
            public int CollisionLayer { get; private set; }
            public bool IsSuspended { get; private set; }
            public bool BodyColliderEnabled => !IsSuspended;
            public int PositionWrites { get; private set; }
            public int ImpulseWrites { get; private set; }
            public Vector2 LastImpulse { get; private set; }

            public PhysicsBackend(Vector2 position, Vector2 velocity)
            {
                Position = position;
                Velocity = velocity;
            }

            public void SetVelocity(Vector2 value) => Velocity = value;
            public void ResetMotion() => Velocity = Vector2.zero;
            public void SetGravityScale(float value) => GravityScale = value;
            public void ApplyImpulse(Vector2 value) { LastImpulse = value; ImpulseWrites++; }
            public void SetPosition(Vector2 value) { Position = value; PositionWrites++; }
            public void SetCollisionLayer(int value) => CollisionLayer = value;
            public void SetBodySuspended(bool suspended) => IsSuspended = suspended;
            public GroundInfo QueryGround(int mask) => GroundInfo.NotGrounded;
            public bool QueryWall(float facing, int mask, out int wallId) { wallId = 0; return false; }
            public bool TryGetDropThroughDuration(int mask, out float duration) { duration = 0f; return false; }
            public void BeginDropThrough() { }
            public void EndDropThrough() { }
            public void Dispose() { }
        }

        private readonly List<GameObject> _objects = new List<GameObject>();
        private readonly List<ScriptableObject> _assets = new List<ScriptableObject>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            TestSceneHelper.CreateGameManager();
            GameManager.Instance.enabled = false;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
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

        [UnityTest]
        public IEnumerator CharacterPhysicsBridge_ActionMovement_UsesInjectedBackendAndPreservesVerticalVelocity()
        {
            BaseCharacter actor = CreateActor(CharacterBelong.Ally);
            yield return null;
            PhysicsBackend physics = new PhysicsBackend(Vector2.zero, new Vector2(0f, 3f));
            actor.SetPhysicsBackendForTest(physics);
            Vector2 nativeVelocity = actor.Rigidbody.linearVelocity;
            ActionDefinition action = ScriptableObject.CreateInstance<ActionDefinition>();
            _assets.Add(action);
            action.durationMode = ActionDurationMode.Fixed;
            action.duration = 1f;
            action.resources = ActionResources.HorizontalMovement;
            action.movement = new ActionMovementProfile { speed = 4f, duration = 1f };

            Assert.IsTrue(GameManager.Actions.Execute(actor.ObjectHash, action, 0, Vector2.right, out _));
            GameManager.Actions.ApplyMovement(actor.ObjectHash, actor);

            Assert.AreEqual(new Vector2(4f, 3f), physics.Velocity);
            Assert.AreEqual(nativeVelocity, actor.Rigidbody.linearVelocity);
        }

        [UnityTest]
        public IEnumerator CharacterPhysicsBridge_Knockback_UpdatesSharedDamageAndCallsInjectedImpulseOnce()
        {
            BaseCharacter target = CreateActor(CharacterBelong.Enemy);
            yield return null;
            PhysicsBackend physics = new PhysicsBackend(Vector2.zero, Vector2.zero);
            target.SetPhysicsBackendForTest(physics);
            Vector2 impulse = new Vector2(4f, 2f);

            DamageResult result = target.DamageReceiver.ReceiveDamage(new DamageData
            {
                defenderHash = target.ObjectHash,
                damage = new ElementalStatus { slash = 10 },
                motionValue = 1f,
                attackElement = Element.Slash,
                knockbackForce = impulse,
            });

            Assert.Greater(result.totalDamage, 0);
            Assert.Less(GameManager.Data.GetVitals(target.ObjectHash).currentHp, 1000);
            Assert.AreEqual(1, physics.ImpulseWrites);
            Assert.AreEqual(impulse, physics.LastImpulse);
        }

        [UnityTest]
        public IEnumerator CharacterPhysicsBridge_Carry_UsesBackendPositionAndPublishesTheSamePosition()
        {
            BaseCharacter carrier = CreateActor(CharacterBelong.Ally);
            BaseCharacter target = CreateActor(CharacterBelong.Enemy);
            yield return null;
            PhysicsBackend physics = new PhysicsBackend(Vector2.zero, Vector2.zero);
            target.SetPhysicsBackendForTest(physics);
            CharacterCollisionController controller = carrier.gameObject.AddComponent<CharacterCollisionController>();
            target.gameObject.AddComponent<CharacterCollisionController>();
            GameManager.Data.GetFlags(target.ObjectHash).ActState = ActState.Flinch;
            Vector3 originalViewPosition = target.transform.position;

            Assert.IsTrue(controller.TryStartCarry(target.ObjectHash));
            controller.SyncCarriedPosition(new Vector2(5f, 2f), Vector2.right);

            Assert.AreEqual(new Vector2(6f, 2f), physics.Position);
            Assert.AreEqual(1, physics.PositionWrites);
            Assert.AreEqual(physics.Position, GameManager.Data.GetVitals(target.ObjectHash).position);
            Assert.AreEqual(originalViewPosition, target.transform.position);
        }

        [UnityTest]
        public IEnumerator CharacterPhysicsBridge_CarryTargetReused_DoesNotMoveTheNewActorGeneration()
        {
            BaseCharacter carrier = CreateActor(CharacterBelong.Ally);
            BaseCharacter target = CreateActor(CharacterBelong.Enemy);
            yield return null;
            PhysicsBackend physics = new PhysicsBackend(Vector2.zero, Vector2.zero);
            target.SetPhysicsBackendForTest(physics);
            CharacterCollisionController controller = carrier.gameObject.AddComponent<CharacterCollisionController>();
            target.gameObject.AddComponent<CharacterCollisionController>();
            GameManager.Data.GetFlags(target.ObjectHash).ActState = ActState.Flinch;
            Assert.IsTrue(controller.TryStartCarry(target.ObjectHash));
            target.OnPoolReturn();
            target.OnPoolAcquire();

            controller.SyncCarriedPosition(new Vector2(5f, 2f), Vector2.right);

            Assert.IsFalse(controller.CarryState.IsActive);
            Assert.AreEqual(0, physics.PositionWrites);
        }

        private BaseCharacter CreateActor(CharacterBelong belong)
        {
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(belong: belong, maxHp: 1000);
            _assets.Add(info);
            GameObject actor = TestSceneHelper.CreateBaseCharacterObjectWithDamageReceiver(info, new Vector3(_objects.Count * 3f, 2f, 0f));
            _objects.Add(actor);
            BaseCharacter character = actor.GetComponent<BaseCharacter>();
            character.Rigidbody.gravityScale = 0f;
            return character;
        }
    }
}
