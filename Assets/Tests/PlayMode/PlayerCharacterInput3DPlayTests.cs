using System.Collections;
using Game.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests.PlayMode
{
    public class PlayerCharacterInput3DPlayTests
    {
        private readonly InputTestFixture _devices = new InputTestFixture();
        private Scene _previousScene;
        private Scene _testScene;
        private InputActionAsset _actions;
        private Keyboard _keyboard;
        private PlayerInput _playerInput;
        private CharacterController _controller;
        private CharacterMotor3D _motor;

        [UnitySetUp]
        public IEnumerator CreatePlayer()
        {
#if UNITY_EDITOR
            InputActionAsset source = UnityEditor.AssetDatabase.LoadAssetAtPath<InputActionAsset>(
                "Assets/InputSystem_Actions.inputactions");
#else
            InputActionAsset source = InputSystem.actions;
#endif
            Assert.IsNotNull(source);
            string shippedActions = source.ToJson();
            _devices.Setup();
            _actions = InputActionAsset.FromJson(shippedActions);
            _keyboard = InputSystem.AddDevice<Keyboard>();
            Mouse mouse = InputSystem.AddDevice<Mouse>();

            _previousScene = SceneManager.GetActiveScene();
            _testScene = SceneManager.CreateScene("PlayerCharacterInput3D", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            SceneManager.SetActiveScene(_testScene);
            GameObject floor = new GameObject("Floor");
            floor.transform.position = Vector3.down * 0.5f;
            floor.AddComponent<BoxCollider>().size = new Vector3(20f, 1f, 20f);
            Camera camera = new GameObject("Player camera").AddComponent<Camera>();
            camera.transform.rotation = Quaternion.Euler(35f, 90f, 0f);

            GameObject actor = new GameObject("3D player");
            actor.SetActive(false);
            actor.transform.position = Vector3.up * 0.12f;
            _controller = actor.AddComponent<CharacterController>();
            _controller.center = Vector3.up * 0.8f;
            _controller.height = 1.6f;
            _controller.radius = 0.3f;
            _controller.skinWidth = 0.03f;
            _motor = actor.AddComponent<CharacterMotor3D>();
            _playerInput = actor.AddComponent<PlayerInput>();
            _playerInput.actions = _actions;
            _playerInput.defaultActionMap = "Player";
            _playerInput.camera = camera;
            actor.AddComponent<PlayerCharacterInput3D>();
            actor.SetActive(true);
            _playerInput.SwitchCurrentControlScheme("Keyboard&Mouse", _keyboard, mouse);
            Physics.SyncTransforms();
            for (int i = 0; i < 15 && !_motor.IsGrounded; i++) { yield return new WaitForFixedUpdate(); }
            Assert.IsTrue(_motor.IsGrounded);
        }

        [UnityTearDown]
        public IEnumerator DestroyPlayer()
        {
            SceneManager.SetActiveScene(_previousScene);
            if (_testScene.IsValid()) { yield return SceneManager.UnloadSceneAsync(_testScene); }
            Object.Destroy(_actions);
            yield return null;
            _devices.TearDown();
        }

        [UnityTest]
        public IEnumerator ShippedMove_WKeyUsesCameraYaw_AndStopsWhenPlayerMapIsDisabled()
        {
            SetButton(_keyboard.wKey, true);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Vector3 moved = _motor.transform.position;
            Assert.That(moved.x, Is.GreaterThan(0.05f));
            Assert.That(moved.z, Is.EqualTo(0f).Within(0.001f));
            Assert.That(Vector3.Dot(_motor.Facing, Vector3.right), Is.GreaterThan(0.999f));

            _playerInput.SwitchCurrentActionMap("UI");
            yield return new WaitForFixedUpdate();
            Assert.That(_motor.transform.position.x, Is.EqualTo(moved.x).Within(0.001f));
        }

        [UnityTest]
        public IEnumerator ShippedJump_QueuedInAirTriggersOnLanding_AndMapChangeClearsAnEarlierPress()
        {
            SetButton(_keyboard.spaceKey, true);
            _playerInput.SwitchCurrentActionMap("UI");
            _playerInput.SwitchCurrentActionMap("Player");
            SetButton(_keyboard.spaceKey, false);
            yield return new WaitForFixedUpdate();
            Assert.IsTrue(_motor.IsGrounded);

            _motor.enabled = false;
            _controller.enabled = false;
            _motor.transform.position = Vector3.up * 0.08f;
            _controller.enabled = true;
            _motor.enabled = true;
            Physics.SyncTransforms();
            Assert.IsFalse(_motor.IsGrounded);
            SetButton(_keyboard.spaceKey, true);
            SetButton(_keyboard.spaceKey, false);

            for (int i = 0; i < 12 && _motor.Velocity.y < 2f; i++) { yield return new WaitForFixedUpdate(); }
            Assert.IsFalse(_motor.IsGrounded);
            Assert.That(_motor.Velocity.y, Is.GreaterThan(2f));
        }

        private void SetButton(ButtonControl button, bool pressed)
        {
            if (pressed) { _devices.Press(button, queueEventOnly: true); }
            else { _devices.Release(button, queueEventOnly: true); }
            InputSystem.Update();
        }
    }
}
