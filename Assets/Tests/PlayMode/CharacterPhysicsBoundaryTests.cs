using System;
using System.Collections;
using System.Collections.Generic;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Game.Tests.PlayMode
{
    public sealed class CharacterPhysicsTickProbe : BaseCharacter
    {
        public List<string> Trace;
        public string Label;
        public int DecisionCount;
        public float MoveSpeed;
        public Action DuringDecision;

        public override void CaptureSimulationPhysics()
        {
            Trace?.Add(Label + ":capture");
            base.CaptureSimulationPhysics();
        }

        public override void PrepareSimulationInput(float currentTime)
        {
            Trace?.Add(Label + ":input");
        }

        public override void SimulateCharacterTick(float deltaTime, float currentTime)
        {
            if (!IsAlive) { return; }
            Trace?.Add(Label + ":control");
            DecisionCount++;
            DuringDecision?.Invoke();
            if (MoveSpeed > 0f)
            {
                GameManager.Actions.TrySetLocomotion(ObjectHash, Vector2.right, MoveSpeed, out _);
            }
        }

        public override void ApplySimulationPhysics()
        {
            Trace?.Add(Label + ":apply");
            base.ApplySimulationPhysics();
        }
    }

    public class CharacterPhysicsBoundaryTests
    {
        private sealed class PhysicsProbe : ICharacterPhysics2D
        {
            private readonly List<string> _trace;
            private readonly string _label;
            private Vector2 _position;
            private Vector2 _velocity;
            private float _gravity;
            private int _layer;
            private bool _suspended;

            public PhysicsProbe(List<string> trace, string label, Vector2 position)
            {
                _trace = trace;
                _label = label;
                _position = position;
                _gravity = 3f;
            }

            public Vector2 Position => _position;
            public Vector2 Velocity => _velocity;
            public float GravityScale => _gravity;
            public int CollisionLayer => _layer;
            public bool IsSuspended => _suspended;
            public bool BodyColliderEnabled => !_suspended;
            public int VelocityWrites { get; private set; }
            public int GroundQueries { get; private set; }
            public int DisposeCount { get; private set; }

            public void SetVelocity(Vector2 velocity)
            {
                _trace.Add(_label + ":velocity");
                _velocity = velocity;
                VelocityWrites++;
            }

            public void SetGravityScale(float gravityScale) => _gravity = gravityScale;
            public void ResetMotion() => SetVelocity(Vector2.zero);
            public void ApplyImpulse(Vector2 impulse) => _velocity += impulse;
            public void SetPosition(Vector2 position) => _position = position;
            public void SetCollisionLayer(int layer) => _layer = layer;
            public void SetBodySuspended(bool suspended) => _suspended = suspended;
            public GroundInfo QueryGround(int layerMask)
            {
                GroundQueries++;
                return GroundInfo.Flat;
            }

            public bool QueryWall(float facing, int layerMask, out int colliderId)
            {
                colliderId = 0;
                return false;
            }

            public bool TryGetDropThroughDuration(int layerMask, out float duration)
            {
                duration = 0f;
                return false;
            }

            public void BeginDropThrough() { }
            public void EndDropThrough() { }
            public void Dispose() => DisposeCount++;
        }

        private readonly List<Object> _created = new List<Object>();
        private readonly List<string> _trace = new List<string>();
        private float _time;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            TestSceneHelper.CreateGameManager();
            GameManager.Instance.enabled = false;
            _time = Time.fixedTime;
            _trace.Clear();
        }

        private CharacterPhysicsTickProbe CreateActor(string label, Vector2 position)
        {
            Game.Core.CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo();
            info.name = "PhysicsBoundaryInfo" + label;
            info.staminaRecoveryRate = 0f;
            _created.Add(info);
            GameObject actor = new GameObject("PhysicsBoundaryActor" + label);
            actor.SetActive(false);
            actor.transform.position = new Vector3(position.x, position.y, 0f);
            actor.AddComponent<Rigidbody2D>();
            actor.AddComponent<BoxCollider2D>().size = new Vector2(0.6f, 0.9f);
            CharacterPhysicsTickProbe character = actor.AddComponent<CharacterPhysicsTickProbe>();
            character.SetCharacterInfoForTest(info);
            character.Trace = _trace;
            character.Label = label;
            actor.SetActive(true);
            character.Rigidbody.gravityScale = 0f;
            _created.Add(actor);
            return character;
        }

        private void Tick()
        {
            _time += Time.fixedDeltaTime;
            GameManager.Instance.SimulationTick(Time.fixedDeltaTime, _time);
        }

        [UnityTest]
        public IEnumerator CharacterPhysics_ExistingStageCollider_RemainsGroundedThroughCentralTick()
        {
            _created.Add(TestSceneHelper.CreateGround());
            CharacterPhysicsTickProbe character = CreateActor("Grounded", new Vector2(0f, 0.5f));
            yield return null;
            Physics2D.SyncTransforms();

            Tick();

            Assert.IsTrue(character.IsGrounded);
            Assert.IsTrue(character.GroundInfo.isGrounded);
            Assert.Greater(character.GroundInfo.normal.y, 0.9f);
            Assert.AreEqual(character.PhysicsPosition, GameManager.Data.GetVitals(character.ObjectHash).position);

            character.SetPhysicsGravityScale(GameConstants.k_GravityScale);
            for (int i = 0; i < 10; i++)
            {
                Tick();
                yield return new WaitForFixedUpdate();
            }
            Tick();
            Assert.IsTrue(character.IsGrounded);
            Assert.Greater(character.PhysicsPosition.y, 0.4f);
            Assert.Less(character.PhysicsPosition.y, 0.6f);
        }

        [UnityTest]
        public IEnumerator CharacterPhysics_InjectedBackend_DrivesObservationAndMovementWithoutWritingOriginalBody()
        {
            CharacterPhysicsTickProbe character = CreateActor("Injected", new Vector2(0f, 5f));
            yield return null;
            Rigidbody2D original = character.Rigidbody;
            original.linearVelocity = new Vector2(-2f, -3f);
            original.simulated = false;
            PhysicsProbe backend = new PhysicsProbe(_trace, "Injected", new Vector2(4f, 5f));
            character.SetPhysicsBackendForTest(backend);
            character.MoveSpeed = 6f;

            Tick();

            Assert.AreEqual(new Vector2(6f, 0f), backend.Velocity);
            Assert.AreEqual(1, backend.VelocityWrites);
            Assert.AreEqual(1, backend.GroundQueries);
            Assert.AreEqual(new Vector2(-2f, -3f), original.linearVelocity);
            Assert.AreEqual(new Vector2(4f, 5f), GameManager.Data.GetVitals(character.ObjectHash).position);
            Assert.IsTrue(GameManager.Observation.TryGetActor(character.ObjectHash, out AIActorObservation observed));
            Assert.AreEqual(new Vector2(4f, 5f), observed.vitals.position);
        }

        [UnityTest]
        public IEnumerator CharacterTick_AllInputsPrecedeControlAndAllControlPrecedesPhysicsApplication()
        {
            CharacterPhysicsTickProbe first = CreateActor("A", new Vector2(0f, 5f));
            CharacterPhysicsTickProbe second = CreateActor("B", new Vector2(3f, 5f));
            yield return null;
            PhysicsProbe firstBackend = new PhysicsProbe(_trace, "A", new Vector2(0f, 5f));
            PhysicsProbe secondBackend = new PhysicsProbe(_trace, "B", new Vector2(3f, 5f));
            first.SetPhysicsBackendForTest(firstBackend);
            second.SetPhysicsBackendForTest(secondBackend);
            first.MoveSpeed = second.MoveSpeed = 3f;
            _trace.Clear();

            Tick();

            int lastCapture = Math.Max(_trace.IndexOf("A:capture"), _trace.IndexOf("B:capture"));
            int firstInput = Math.Min(_trace.IndexOf("A:input"), _trace.IndexOf("B:input"));
            int lastInput = Math.Max(_trace.IndexOf("A:input"), _trace.IndexOf("B:input"));
            int firstControl = Math.Min(_trace.IndexOf("A:control"), _trace.IndexOf("B:control"));
            int lastControl = Math.Max(_trace.IndexOf("A:control"), _trace.IndexOf("B:control"));
            int firstApply = Math.Min(_trace.IndexOf("A:apply"), _trace.IndexOf("B:apply"));
            Assert.GreaterOrEqual(lastCapture, 0);
            Assert.Greater(firstInput, lastCapture);
            Assert.Greater(firstControl, lastInput);
            Assert.Greater(firstApply, lastControl);
            Assert.Greater(_trace.IndexOf("A:velocity"), _trace.IndexOf("A:apply"));
            Assert.Greater(_trace.IndexOf("B:velocity"), _trace.IndexOf("B:apply"));
            Assert.AreEqual(1, first.DecisionCount);
            Assert.AreEqual(1, second.DecisionCount);
            Assert.AreEqual(new Vector2(3f, 0f), firstBackend.Velocity);
            Assert.AreEqual(new Vector2(3f, 0f), secondBackend.Velocity);
        }

        [UnityTest]
        public IEnumerator CharacterTick_DisabledDeadAndReturnedActors_DoNotExecuteMovementDecisions()
        {
            CharacterPhysicsTickProbe character = CreateActor("Lifetime", new Vector2(0f, 5f));
            yield return null;
            character.SetPhysicsBackendForTest(new PhysicsProbe(_trace, "Lifetime", new Vector2(0f, 5f)));
            character.MoveSpeed = 3f;
            character.enabled = false;
            _trace.Clear();
            Tick();
            Assert.AreEqual(0, character.DecisionCount);
            CollectionAssert.Contains(_trace, "Lifetime:capture");
            CollectionAssert.DoesNotContain(_trace, "Lifetime:input");
            CollectionAssert.DoesNotContain(_trace, "Lifetime:control");
            CollectionAssert.DoesNotContain(_trace, "Lifetime:apply");

            character.enabled = true;
            GameManager.Data.GetVitals(character.ObjectHash).currentHp = 0;
            Tick();
            Assert.AreEqual(0, character.DecisionCount);

            character.OnPoolReturn();
            _trace.Clear();
            Tick();
            Assert.IsEmpty(_trace);
            character.OnPoolAcquire();
            Tick();
            Assert.AreEqual(1, character.DecisionCount);
        }

        [UnityTest]
        public IEnumerator CharacterTick_ReusedActorDuringEarlierDecision_WaitsUntilNextTick()
        {
            CharacterPhysicsTickProbe first = CreateActor("First", new Vector2(0f, 5f));
            CharacterPhysicsTickProbe recycled = CreateActor("Recycled", new Vector2(3f, 5f));
            yield return null;
            first.SetPhysicsBackendForTest(new PhysicsProbe(_trace, "First", new Vector2(0f, 5f)));
            recycled.SetPhysicsBackendForTest(new PhysicsProbe(_trace, "Recycled", new Vector2(3f, 5f)));
            Assert.IsTrue(GameManager.Actors.TryGet(recycled.ObjectHash, out ActorRef previous));
            first.DuringDecision = () =>
            {
                first.DuringDecision = null;
                recycled.OnPoolReturn();
                recycled.OnPoolAcquire();
            };

            Tick();

            Assert.IsTrue(GameManager.Actors.TryGet(recycled.ObjectHash, out ActorRef current));
            Assert.AreNotEqual(previous, current);
            Assert.AreEqual(0, recycled.DecisionCount);
            Tick();
            Assert.AreEqual(1, recycled.DecisionCount);
        }

        [UnityTest]
        public IEnumerator CharacterPhysics_SuspensionIsIdempotentAndRestoresOriginalBodySettings()
        {
            CharacterPhysicsTickProbe character = CreateActor("Suspend", new Vector2(0f, 5f));
            yield return null;
            Rigidbody2D body = character.Rigidbody;
            BoxCollider2D collider = character.GetComponent<BoxCollider2D>();
            RigidbodyConstraints2D constraints = RigidbodyConstraints2D.FreezeRotation | RigidbodyConstraints2D.FreezePositionX;
            body.constraints = constraints;
            body.gravityScale = 2.5f;
            body.linearVelocity = new Vector2(1f, 2f);
            collider.enabled = true;

            character.SetPhysicsBodySuspended(true);
            character.SetPhysicsBodySuspended(true);

            Assert.AreEqual(RigidbodyConstraints2D.FreezeAll, body.constraints);
            Assert.AreEqual(0f, body.gravityScale);
            Assert.AreEqual(Vector2.zero, body.linearVelocity);
            Assert.IsFalse(collider.enabled);
            Assert.IsTrue(body.simulated);
            character.SetPhysicsVelocity(new Vector2(8f, 9f));
            character.ApplyPhysicsImpulse(Vector2.right * 5f);
            Assert.AreEqual(Vector2.zero, body.linearVelocity);
            character.SetPhysicsBodySuspended(false);
            Assert.AreEqual(constraints, body.constraints);
            Assert.AreEqual(2.5f, body.gravityScale);
            Assert.IsTrue(collider.enabled);

            collider.enabled = false;
            character.SetPhysicsBodySuspended(true);
            character.SetPhysicsBodySuspended(false);
            Assert.IsFalse(collider.enabled);
        }

        [UnityTest]
        public IEnumerator CharacterPhysics_DropThroughEnds_UsesLatestRequestedCollisionLayer()
        {
            CharacterPhysicsTickProbe character = CreateActor("Drop", new Vector2(0f, 5f));
            yield return null;
            character.SetPhysicsCollisionLayer(GameConstants.k_LayerCharaPassThrough);
            character.BeginPhysicsDropThrough();
            character.SetPhysicsCollisionLayer(GameConstants.k_LayerCharaCollide);
            Assert.AreEqual(GameConstants.k_LayerCharaInvincible, character.gameObject.layer);

            character.EndPhysicsDropThrough();

            Assert.AreEqual(GameConstants.k_LayerCharaCollide, character.gameObject.layer);
            Assert.AreEqual(GameConstants.k_LayerCharaCollide, character.PhysicsCollisionLayer);
        }

        [UnityTest]
        public IEnumerator CharacterPhysics_ExistingDropThroughMarker_IsQueriedThroughPhysicsBoundary()
        {
            GameObject platform = new GameObject("PhysicsBoundaryDropThroughPlatform");
            platform.layer = 6;
            platform.AddComponent<BoxCollider2D>().size = new Vector2(4f, 0.2f);
            platform.AddComponent<DropThroughPlatform>();
            _created.Add(platform);
            CharacterPhysicsTickProbe character = CreateActor("DropQuery", new Vector2(0f, 0.6f));
            yield return null;
            Physics2D.SyncTransforms();

            Assert.IsTrue(character.TryGetDropThroughDuration(out float duration));

            Assert.AreEqual(0f, duration);
            character.BeginPhysicsDropThrough();
            Assert.AreEqual(GameConstants.k_LayerCharaInvincible, character.PhysicsCollisionLayer);
            character.EndPhysicsDropThrough();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
            {
                if (_created[i] != null) { Object.Destroy(_created[i]); }
            }
            _created.Clear();
            yield return null;
            TestSceneHelper.Cleanup();
            yield return null;
        }
    }
}
