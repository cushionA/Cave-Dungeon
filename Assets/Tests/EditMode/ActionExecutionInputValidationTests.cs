using Game.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    public class ActionExecutionInputValidationTests
    {
        private const int k_Owner = 10;
        private sealed class World : IExecutionWorld
        {
            public ActionActorState actor;
            public bool TryGetActor(int hash, out ActionActorState value)
            {
                value = actor;
                return hash == k_Owner;
            }
            public void CommitActor(int hash, in ActionActorState value) { actor = value; }
        }

        private World _world;
        private ActionExecutionSystem _system;
        private int _definition;

        [SetUp]
        public void SetUp()
        {
            _world = new World
            {
                actor = new ActionActorState
                {
                    vitals = new CharacterVitals
                    {
                        currentHp = 100, maxHp = 100, currentStamina = 100f,
                        maxStamina = 100f, currentMp = 30f, maxMp = 30,
                    },
                },
            };
            _system = new ActionExecutionSystem(_world);
            _system.RegisterActor(k_Owner);
            _definition = _system.RegisterDefinition(new ActionDefinitionData
            {
                resources = ActionResources.HorizontalMovement, gameplay = ActionGameplayFlags.Move,
                endMode = ActionEndMode.Held, moveSpeed = 3f, movementUsesInput = true,
                staminaCost = 5f, mpCost = 2, staminaPerSecond = 1f,
                cancelAfter = 0f, cancelPoint = -1f,
            });
        }

        [TearDown]
        public void TearDown() => _system.Dispose();

        private ActionRequest Request()
            => new ActionRequest
            {
                ownerHash = k_Owner, definitionId = _definition,
                direction = Vector2.right, speedMultiplier = 1f,
            };

        [TestCase(0, float.NaN)]
        [TestCase(0, float.PositiveInfinity)]
        [TestCase(0, float.NegativeInfinity)]
        [TestCase(1, float.NaN)]
        [TestCase(1, float.PositiveInfinity)]
        [TestCase(1, float.NegativeInfinity)]
        [TestCase(2, float.NaN)]
        [TestCase(2, float.PositiveInfinity)]
        [TestCase(2, float.NegativeInfinity)]
        [TestCase(2, -1f)]
        public void ActionInput_InvalidRequest_RejectsBeforeCostsGroupsAndNotifications(int field, float value)
        {
            ActionRequest request = Request();
            if (field == 0) { request.direction.x = value; }
            else if (field == 1) { request.targetPosition.y = value; }
            else { request.speedMultiplier = value; }
            int notifications = 0;
            _system.Changed += change => notifications++;

            Assert.IsFalse(_system.TryStart(request, out long instance, out long group));

            Assert.AreEqual(0, instance);
            Assert.AreEqual(0, group);
            Assert.AreEqual(0, _system.ActiveCount);
            Assert.AreEqual(0, _system.ActiveGroupCount);
            Assert.AreEqual(100f, _world.actor.vitals.currentStamina);
            Assert.AreEqual(30f, _world.actor.vitals.currentMp);
            Assert.AreEqual(0, notifications);
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void ActionInput_InvalidQueuedRequest_DoesNotRetainPendingGroup(float value)
        {
            ActionRequest request = Request();
            request.direction.y = value;

            _system.Queue(request);

            Assert.AreEqual(0, _system.PendingCount);
            Assert.AreEqual(0, _system.ActiveGroupCount);
            _system.Tick(0.1f);
            Assert.AreEqual(0, _system.ActiveCount);
            Assert.AreEqual(100f, _world.actor.vitals.currentStamina);
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        [TestCase(-1f)]
        public void ActionInput_InvalidSpeedUpdate_PreservesAcceptedMovement(float value)
        {
            Assert.IsTrue(_system.TryStart(Request(), out long instance));

            Assert.IsFalse(_system.UpdateInput(instance, Vector2.left, value));

            Assert.IsTrue(_system.TryGetMovement(k_Owner, true, out Vector2 velocity));
            Assert.AreEqual(Vector2.right * 3f, velocity);
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void ActionInput_InvalidDirectionUpdate_PreservesAcceptedMovement(float value)
        {
            Assert.IsTrue(_system.TryStart(Request(), out long instance));

            Assert.IsFalse(_system.UpdateInput(instance, new Vector2(1f, value), 2f));

            Assert.IsTrue(_system.TryGetMovement(k_Owner, true, out Vector2 velocity));
            Assert.AreEqual(Vector2.right * 3f, velocity);
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void ActionInput_InvalidTick_PreservesQueuedWorkClockAndResources(float value)
        {
            Assert.IsTrue(_system.TryStart(Request(), out long instance));
            _system.Tick(0.25f);
            _system.Queue(Request());
            int pending = _system.PendingCount;
            float stamina = _world.actor.vitals.currentStamina;
            int notifications = 0;
            _system.Changed += change => notifications++;

            _system.Tick(value);

            Assert.IsTrue(_system.TryGetInstance(instance, out ActionExecution current));
            Assert.AreEqual(0.25f, current.elapsed);
            Assert.AreEqual(stamina, _world.actor.vitals.currentStamina);
            Assert.AreEqual(pending, _system.PendingCount);
            Assert.AreEqual(0, notifications);
        }

        [Test]
        public void ActionInput_FiniteInputsWhoseProductOverflows_RejectsBeforeCosts()
        {
            ActionRequest request = Request();
            request.speedMultiplier = float.MaxValue;

            Assert.IsFalse(_system.TryStart(request, out _));

            Assert.AreEqual(100f, _world.actor.vitals.currentStamina);
            Assert.AreEqual(0, _system.ActiveCount);
            _system.Queue(request);
            Assert.AreEqual(0, _system.PendingCount);
        }

        [Test]
        public void ActionInput_OverflowingInputUpdate_PreservesAcceptedMovement()
        {
            Assert.IsTrue(_system.TryStart(Request(), out long instance));

            Assert.IsFalse(_system.UpdateInput(instance, Vector2.right, float.MaxValue));

            Assert.IsTrue(_system.TryGetMovement(k_Owner, true, out Vector2 velocity));
            Assert.AreEqual(Vector2.right * 3f, velocity);
        }

        [Test]
        public void ActionInput_ZeroRequestMultiplier_KeepsLegacyDefaultAndZeroUpdateCanStop()
        {
            ActionRequest request = Request();
            request.speedMultiplier = 0f;
            Assert.IsTrue(_system.TryStart(request, out long instance));
            Assert.IsTrue(_system.TryGetMovement(k_Owner, true, out Vector2 velocity));
            Assert.AreEqual(Vector2.right * 3f, velocity);
            Assert.IsTrue(_system.UpdateInput(instance, Vector2.right, 0f));
            Assert.IsTrue(_system.TryGetMovement(k_Owner, true, out velocity));
            Assert.AreEqual(Vector2.zero, velocity);
        }
    }
}
