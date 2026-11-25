using System.Collections;
using Game.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests.PlayMode
{
    public sealed class CharacterMotor3DPlayTests
    {
        private const float k_Step = 0.02f;
        private const float k_ControllerHeight = 1.9f;
        private const float k_ControllerRadius = 0.35f;
        private const int k_SettleSteps = 30;

        private Scene _scene;
        private PhysicsScene _physics;

        [UnitySetUp]
        public IEnumerator CreatePhysicsRoom()
        {
            _scene = SceneManager.CreateScene(nameof(CharacterMotor3DPlayTests),
                new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            _physics = _scene.GetPhysicsScene();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator UnloadPhysicsRoom()
        {
            if (_scene.isLoaded) { yield return SceneManager.UnloadSceneAsync(_scene); }
        }

        [UnityTest]
        public IEnumerator Motor_WhenMovingDiagonallyOrAgainstAWall_ReportsPlanarMotionAndActualVelocity()
        {
            CreateFloor("Flat floor", 0f);
            CreateBox("Wall", new Vector3(0f, 1.5f, 2.35f), new Vector3(3f, 3f, 0.7f));
            CharacterMotor3D straight = CreateMotor("Straight", new Vector3(-6f, 0.1f, -4f));
            CharacterMotor3D diagonal = CreateMotor("Diagonal", new Vector3(5f, 0.1f, -4f));
            CharacterMotor3D blocked = CreateMotor("Blocked", new Vector3(0f, 0.1f, 0f));
            Settle(straight);
            Settle(diagonal);
            Settle(blocked);

            Vector3 straightStart = straight.FeetPosition;
            Vector3 diagonalStart = diagonal.FeetPosition;
            for (int i = 0; i < 10; i++)
            {
                Tick(straight, Vector3.forward);
                Tick(diagonal, new Vector3(1f, 3f, 1f));
            }

            Vector3 straightMotion = Vector3.ProjectOnPlane(straight.FeetPosition - straightStart, Vector3.up);
            Vector3 diagonalMotion = Vector3.ProjectOnPlane(diagonal.FeetPosition - diagonalStart, Vector3.up);
            Assert.That(straightMotion.magnitude, Is.GreaterThan(0.2f));
            Assert.That(diagonalMotion.magnitude, Is.EqualTo(straightMotion.magnitude).Within(0.04f));
            Assert.That(diagonalMotion.x, Is.EqualTo(diagonalMotion.z).Within(0.04f));
            Assert.That(Vector3.Angle(diagonal.Facing, new Vector3(1f, 0f, 1f)), Is.LessThan(2f));

            for (int i = 0; i < 70; i++)
            {
                Tick(blocked, Vector3.forward);
            }
            Assert.That(blocked.FeetPosition.z, Is.InRange(0.8f, 2.001f));
            Assert.That(Mathf.Abs(blocked.Velocity.z), Is.LessThan(0.1f));
            Assert.IsTrue(blocked.IsGrounded);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Motor_WhenJumpIsPressedAgainInTheAir_DoesNotGainAnExtraJump()
        {
            BoxCollider floor = CreateFloor("Jump floor", 0f);
            CharacterMotor3D motor = CreateMotor("Jumper", new Vector3(0f, 0.1f, 0f));
            Settle(motor);
            Assert.IsTrue(motor.IsGrounded);

            Tick(motor, jumpPressed: true);
            float firstVelocity = motor.Velocity.y;
            Assert.That(firstVelocity, Is.GreaterThan(1f));
            Assert.IsFalse(motor.IsGrounded);
            Assert.IsFalse(motor.Ground.IsGrounded);

            Tick(motor);
            float velocityBeforeSecondPress = motor.Velocity.y;
            Tick(motor, jumpPressed: true);
            Assert.That(motor.Velocity.y, Is.LessThan(velocityBeforeSecondPress));
            for (int i = 0; i < 120 && !motor.IsGrounded; i++)
            {
                Tick(motor);
            }

            Assert.IsTrue(motor.IsGrounded);
            Assert.AreSame(floor, motor.Ground.Surface);
            // The landing step still includes the displacement to the floor.
            Tick(motor);
            Assert.IsTrue(motor.IsGrounded);
            Assert.That(Mathf.Abs(motor.Velocity.y), Is.LessThan(0.1f));
            yield return null;
        }

        [UnityTest]
        public IEnumerator Motor_WhenJumpHitsACeiling_StopsAscendingAndLands()
        {
            BoxCollider floor = CreateFloor("Low room floor", 0f);
            const float ceilingUnderside = 2.25f;
            CreateBox("Ceiling", new Vector3(0f, ceilingUnderside + 0.2f, 0f), new Vector3(3f, 0.4f, 3f));
            CharacterMotor3D motor = CreateMotor("Low room jumper", new Vector3(0f, 0.1f, 0f));
            Settle(motor);

            float baseline = motor.FeetPosition.y;
            Tick(motor, jumpPressed: true);
            float peak = motor.FeetPosition.y;
            bool reversed = false;
            for (int i = 0; i < 45 && !motor.IsGrounded; i++)
            {
                Tick(motor);
                peak = Mathf.Max(peak, motor.FeetPosition.y);
                reversed |= motor.Velocity.y < -0.05f;
            }

            Assert.That(peak - baseline, Is.GreaterThan(0.1f));
            Assert.That(peak + k_ControllerHeight, Is.LessThan(ceilingUnderside + 0.09f));
            Assert.IsTrue(reversed);
            Assert.IsTrue(motor.IsGrounded);
            Assert.AreSame(floor, motor.Ground.Surface);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Motor_WhenAtTheSamePlanarPositionOnStackedFloors_ReportsTheCorrectSurface()
        {
            BoxCollider lowerFloor = CreateFloor("Lower floor", 0f);
            BoxCollider upperFloor = CreateFloor("Upper floor", 4f);
            CharacterMotor3D lower = CreateMotor("Under bridge", new Vector3(0f, 0.15f, 0f));
            CharacterMotor3D upper = CreateMotor("On bridge", new Vector3(0f, 4.15f, 0f));
            Settle(lower);
            Settle(upper);

            Assert.IsTrue(lower.IsGrounded && lower.Ground.IsGrounded);
            Assert.IsTrue(upper.IsGrounded && upper.Ground.IsGrounded);
            Assert.AreSame(lowerFloor, lower.Ground.Surface);
            Assert.AreSame(upperFloor, upper.Ground.Surface);
            Assert.That(lower.Ground.Point.y, Is.EqualTo(0f).Within(0.08f));
            Assert.That(upper.Ground.Point.y, Is.EqualTo(4f).Within(0.08f));
            Assert.That(Vector3.Dot(lower.Ground.Normal, Vector3.up), Is.GreaterThan(0.98f));
            Assert.That(Vector3.Dot(upper.Ground.Normal, Vector3.up), Is.GreaterThan(0.98f));
            Assert.That(lower.FeetPosition.y, Is.EqualTo(0f).Within(0.12f));
            Assert.That(upper.FeetPosition.y, Is.EqualTo(4f).Within(0.12f));
            yield return null;
        }

        [UnityTest]
        public IEnumerator Motor_WhenWalkingOnAnIncline_ReportsTheSurfaceNormalAndGainsHeight()
        {
            BoxCollider ramp = CreateBox("Ramp", new Vector3(0f, -0.25f, 0f), new Vector3(12f, 0.5f, 6f));
            ramp.transform.rotation = Quaternion.Euler(0f, 0f, 15f);
            CharacterMotor3D motor = CreateMotor("Uphill", new Vector3(0f, 0.2f, 0f));
            Settle(motor);
            Vector3 start = motor.FeetPosition;

            for (int i = 0; i < 20; i++)
            {
                Tick(motor, Vector3.right);
            }

            Assert.IsTrue(motor.IsGrounded);
            Assert.AreSame(ramp, motor.Ground.Surface);
            Assert.That(Vector3.Angle(motor.Ground.Normal, ramp.transform.up), Is.LessThan(3f));
            Assert.That(motor.FeetPosition.x, Is.GreaterThan(start.x + 0.4f));
            Assert.That(motor.FeetPosition.y, Is.GreaterThan(start.y + 0.07f));
            yield return null;
        }

        private CharacterMotor3D CreateMotor(string name, Vector3 feet)
        {
            GameObject actor = NewRoomObject(name, feet);
            actor.layer = LayerMask.NameToLayer("Player");
            CharacterController controller = actor.AddComponent<CharacterController>();
            controller.height = k_ControllerHeight;
            controller.center = Vector3.up * (k_ControllerHeight * 0.5f);
            controller.radius = k_ControllerRadius;
            controller.skinWidth = 0.04f;
            controller.stepOffset = 0.25f;
            controller.minMoveDistance = 0f;
            return actor.AddComponent<CharacterMotor3D>();
        }

        private BoxCollider CreateFloor(string name, float elevation)
        {
            const float floorThickness = 0.5f;
            return CreateBox(name, new Vector3(0f, elevation - floorThickness * 0.5f, 0f),
                new Vector3(20f, floorThickness, 16f));
        }

        private BoxCollider CreateBox(string name, Vector3 position, Vector3 size)
        {
            GameObject surface = NewRoomObject(name, position);
            surface.layer = LayerMask.NameToLayer("Ground");
            BoxCollider collider = surface.AddComponent<BoxCollider>();
            collider.size = size;
            return collider;
        }

        private GameObject NewRoomObject(string name, Vector3 position)
        {
            GameObject gameObject = new GameObject(name);
            SceneManager.MoveGameObjectToScene(gameObject, _scene);
            gameObject.transform.position = position;
            return gameObject;
        }

        private void Settle(CharacterMotor3D motor)
        {
            Physics.SyncTransforms();
            for (int i = 0; i < k_SettleSteps; i++)
            {
                Tick(motor);
            }
        }

        private void Tick(CharacterMotor3D motor, Vector3 direction = default, bool jumpPressed = false)
        {
            motor.Simulate(direction, jumpPressed, k_Step);
            _physics.Simulate(k_Step);
        }
    }
}
