using System.Collections;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests.PlayMode
{
    public sealed class PlayerDodgeInput3DPlayTests
    {
        private const float k_DodgeCost = 16f;
        private const float k_DodgeSpeed = 9f;
        private const float k_DodgeDuration = 0.26f;

        private readonly InputTestFixture _devices = new InputTestFixture();
        private Scene _room;
        private Scene _previousScene;
        private InputActionAsset _actions;
        private Keyboard _keyboard;
        private Gamepad _gamepad;
        private PlayerInput _input;
        private CharacterActor3D _knight;
        private Game.Core.CharacterInfo _character;
        private bool _inputSetup;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            TestSceneHelper.Cleanup();
            yield return null;
#if UNITY_EDITOR
            InputActionAsset source = UnityEditor.AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions");
#else
            InputActionAsset source = InputSystem.actions;
#endif
            Assert.IsNotNull(source);
            string shipped = source.ToJson();
            _devices.Setup();
            _inputSetup = true;
            _actions = InputActionAsset.FromJson(shipped);
            _keyboard = InputSystem.AddDevice<Keyboard>();
            Mouse mouse = InputSystem.AddDevice<Mouse>();
            _gamepad = InputSystem.AddDevice<Gamepad>();

            _previousScene = SceneManager.GetActiveScene();
            _room = SceneManager.CreateScene(nameof(PlayerDodgeInput3DPlayTests), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            SceneManager.SetActiveScene(_room);
            TestSceneHelper.CreateGameManager();
            GameManager.Instance.enabled = false;
            Box("Floor", Vector3.down * 0.5f, new Vector3(20f, 1f, 20f));
            Camera camera = new GameObject("Player camera").AddComponent<Camera>();
            camera.transform.rotation = Quaternion.Euler(35f, 90f, 0f);

            GameObject body = new GameObject("Knight");
            body.SetActive(false);
            body.transform.position = Vector3.up * 0.05f;
            CharacterController controller = body.AddComponent<CharacterController>();
            controller.center = Vector3.up * 0.8f;
            controller.height = 1.6f;
            controller.radius = 0.3f;
            controller.stepOffset = 0.18f;
            controller.skinWidth = 0.03f;
            _knight = body.AddComponent<CharacterActor3D>();
            _character = TestSceneHelper.CreateTestCharacterInfo();
            _character.dodgeStaminaCost = k_DodgeCost;
            _character.dodgeDuration = k_DodgeDuration;
            _character.dashSpeed = k_DodgeSpeed;
            _character.staminaRecoveryRate = 16f;
            _character.staminaRecoveryDelay = 0.125f;
            _character.staminaExhaustionPenalty = 0.75f;
            _knight.SetCharacterInfoForTest(_character);
            _input = body.AddComponent<PlayerInput>();
            _input.actions = _actions;
            _input.defaultActionMap = "Player";
            _input.neverAutoSwitchControlSchemes = true;
            _input.camera = camera;
            body.AddComponent<PlayerCharacterInput3D>();
            body.SetActive(true);
            _input.SwitchCurrentControlScheme("Keyboard&Mouse", _keyboard, mouse);
            Physics.SyncTransforms();
            for (int i = 0; i < 20 && !_knight.Motor.IsGrounded; i++) { yield return new WaitForFixedUpdate(); }
            Assert.IsTrue(_knight.Motor.IsGrounded);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_room.isLoaded)
            {
                SceneManager.SetActiveScene(_previousScene);
                yield return SceneManager.UnloadSceneAsync(_room);
            }
            TestSceneHelper.Cleanup();
            if (_character != null) { Object.Destroy(_character); }
            if (_actions != null) { Object.Destroy(_actions); }
            yield return null;
            if (_inputSetup) { _devices.TearDown(); _inputSetup = false; }
        }

        [UnityTest]
        public IEnumerator KeyboardTap_GroundDodgeUsesCameraDirectionAndOneSharedCharge()
        {
            int owner = _knight.Actor.Hash;
            GameActionRuntime actions = GameManager.Actions;
            Vector3 start = _knight.transform.position;
            SetKeyboard(Key.W, Key.LeftShift);
            Assert.IsFalse(actions.IsDodging(owner));
            SetKeyboard(Key.W);
            SetKeyboard();

            yield return new WaitForFixedUpdate();
            Assert.IsTrue(actions.IsDodging(owner));
            Assert.IsTrue(actions.Execution.EvaluateEffects(owner).isInvincible);
            Assert.AreEqual(100f - k_DodgeCost, Stamina());
            Assert.That(_knight.Motor.Velocity.x, Is.EqualTo(k_DodgeSpeed).Within(0.025f));
            Assert.That(_knight.transform.position.z, Is.EqualTo(start.z).Within(0.001f));
            Assert.That(Vector3.Dot(_knight.transform.forward, Vector3.right), Is.GreaterThan(0.999f));

            actions.Execution.Tick(k_DodgeDuration + Time.fixedDeltaTime);
            yield return new WaitForFixedUpdate();
            Assert.IsFalse(actions.IsDodging(owner));
            Assert.IsFalse(actions.Execution.EvaluateEffects(owner).isInvincible);
            Assert.That(_knight.Motor.Velocity.x, Is.Zero.Within(0.001f));
            Assert.AreEqual(100f - k_DodgeCost, Stamina());
        }

        [UnityTest]
        public IEnumerator GamepadTap_WhenWallStopsDodgeAndMapChanges_CancelsTheSharedAction()
        {
            _input.SwitchCurrentControlScheme("Gamepad", _gamepad);
            Box("Blocking wall", new Vector3(0f, 1f, 1.1f), new Vector3(5f, 2f, 0.2f));
            Physics.SyncTransforms();
            yield return null;
            InputSystem.QueueStateEvent(_gamepad, new GamepadState().WithButton(GamepadButton.LeftStick));
            InputSystem.Update();
            InputSystem.QueueStateEvent(_gamepad, new GamepadState());
            InputSystem.Update();

            int owner = _knight.Actor.Hash;
            GameActionRuntime actions = GameManager.Actions;
            for (int i = 0; i < 10; i++) { yield return new WaitForFixedUpdate(); }
            Assert.IsTrue(actions.IsDodging(owner));
            Assert.That(_knight.transform.position.z, Is.InRange(0.55f, 0.85f));
            Assert.That(_knight.Motor.Collisions & CollisionFlags.Sides, Is.EqualTo(CollisionFlags.Sides));
            Assert.AreEqual(100f - k_DodgeCost, Stamina());

            _input.SwitchCurrentActionMap("UI");
            Assert.IsFalse(actions.IsDodging(owner));
            Assert.IsFalse(actions.Execution.EvaluateEffects(owner).isInvincible);
            _input.SwitchCurrentActionMap("Player");
            Vector3 stop = _knight.transform.position;
            yield return new WaitForFixedUpdate();
            Assert.That(_knight.transform.position.z, Is.EqualTo(stop.z).Within(0.001f));
            Assert.AreEqual(100f - k_DodgeCost, Stamina());
        }

        [UnityTest]
        public IEnumerator Keyboard_LongHoldStaleTapAndAirTap_DoNotStartAGroundDodge()
        {
            SetKeyboard(Key.LeftShift);
            _devices.currentTime += 0.35d;
            SetKeyboard();
            yield return new WaitForFixedUpdate();
            AssertNoDodge(100f);

            TapShift();
            _input.SwitchCurrentActionMap("UI");
            _input.SwitchCurrentActionMap("Player");
            yield return new WaitForFixedUpdate();
            AssertNoDodge(100f);

            TapShift();
            ActorRef previous = _knight.Actor;
            _knight.enabled = false;
            _knight.enabled = true;
            Assert.AreNotEqual(previous, _knight.Actor);
            yield return new WaitForFixedUpdate();
            AssertNoDodge(100f);

            GameManager.Data.GetVitals(_knight.Actor.Hash).currentStamina = k_DodgeCost - 1f;
            TapShift();
            yield return new WaitForFixedUpdate();
            AssertNoDodge(k_DodgeCost - 1f);
            GameManager.Data.GetVitals(_knight.Actor.Hash).currentStamina = 100f;

            SetKeyboard(Key.Space);
            SetKeyboard();
            yield return new WaitForFixedUpdate();
            Assert.IsFalse(_knight.Motor.IsGrounded);
            TapShift();
            yield return new WaitForFixedUpdate();
            AssertNoDodge(100f);
        }

        [UnityTest]
        public IEnumerator StaminaRecovery_WhenDodgeEnds_ResumesAtTheConfiguredRateAndClamps()
        {
            TapShift();
            yield return new WaitForFixedUpdate();
            Assert.IsTrue(GameManager.Actions.IsDodging(_knight.Actor.Hash));
            Assert.AreEqual(100f - k_DodgeCost, Stamina());

            GameManager world = GameManager.Instance;
            world.SimulationTick(0.0625f, 0.0625f);
            world.SimulationTick(0.125f, 0.1875f);
            world.SimulationTick(0.03125f, 0.21875f);
            Assert.AreEqual(100f - k_DodgeCost, Stamina(), "Recovery must stay paused through the dodge.");

            world.SimulationTick(0.0625f, 0.28125f);
            Assert.IsFalse(GameManager.Actions.IsDodging(_knight.Actor.Hash));
            Assert.AreEqual(101f - k_DodgeCost, Stamina());
            world.SimulationTick(1f, 1.28125f);
            Assert.AreEqual(100f, Stamina());
        }

        [UnityTest]
        public IEnumerator StaminaRecovery_WhenExhaustedOrReRegistered_UsesTheCurrentGenerationAndDelay()
        {
            GameManager world = GameManager.Instance;
            ActorRef first = _knight.Actor;
            GameManager.Data.GetVitals(first.Hash).currentStamina = 0f;
            world.SimulationTick(0.0625f, 0.0625f);
            world.SimulationTick(0.625f, 0.6875f);
            Assert.AreEqual(0f, Stamina());
            world.SimulationTick(0.25f, 0.9375f);
            Assert.AreEqual(2f, Stamina());

            _knight.enabled = false;
            _knight.enabled = true;
            Assert.AreNotEqual(first, _knight.Actor);
            GameManager.Data.GetVitals(_knight.Actor.Hash).currentStamina = 90f;
            world.SimulationTick(0.0625f, 1f);
            world.SimulationTick(0.125f, 1.125f);
            Assert.AreEqual(90f, Stamina());
            world.SimulationTick(0.0625f, 1.1875f);
            Assert.AreEqual(91f, Stamina());
            yield return null;
        }

        private void AssertNoDodge(float stamina)
        {
            Assert.IsFalse(GameManager.Actions.IsDodging(_knight.Actor.Hash));
            Assert.AreEqual(stamina, Stamina());
        }

        private float Stamina() => GameManager.Data.GetVitals(_knight.Actor.Hash).currentStamina;

        private void TapShift()
        {
            SetKeyboard(Key.LeftShift);
            SetKeyboard();
        }

        private void SetKeyboard(params Key[] keys)
        {
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(keys));
            InputSystem.Update();
        }

        private static void Box(string name, Vector3 center, Vector3 size)
        {
            GameObject box = new GameObject(name);
            box.transform.position = center;
            box.AddComponent<BoxCollider>().size = size;
        }
    }
}
