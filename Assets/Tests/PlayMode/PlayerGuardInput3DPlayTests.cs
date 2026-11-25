using System.Collections;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using CharacterInfo = Game.Core.CharacterInfo;

namespace Game.Tests.PlayMode
{
    public sealed class PlayerGuardInput3DPlayTests
    {
        private readonly InputTestFixture _devices = new InputTestFixture();
        private InputActionAsset _actions;
        private CharacterInfo _info;
        private Scene _scene;
        private Scene _previousScene;
        private PlayerInput _input;
        private PlayerCharacterInput3D _adapter;
        private CharacterActor3D _knight;
        private Gamepad _pad;
        private Keyboard _keyboard;
        private Mouse _mouse;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            TestSceneHelper.Cleanup();
            yield return null;
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
            _pad = InputSystem.AddDevice<Gamepad>();
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _mouse = InputSystem.AddDevice<Mouse>();

            _previousScene = SceneManager.GetActiveScene();
            _scene = SceneManager.CreateScene("PlayerGuardInput3D", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            SceneManager.SetActiveScene(_scene);
            TestSceneHelper.CreateGameManager();
            GameManager.Instance.enabled = false;

            GameObject actor = new GameObject("Knight");
            actor.SetActive(false);
            _knight = actor.AddComponent<CharacterActor3D>();
            _info = TestSceneHelper.CreateTestCharacterInfo();
            _knight.SetCharacterInfoForTest(_info);
            _input = actor.AddComponent<PlayerInput>();
            _input.actions = _actions;
            _input.defaultActionMap = "Player";
            _adapter = actor.AddComponent<PlayerCharacterInput3D>();
            actor.SetActive(true);
            _input.SwitchCurrentControlScheme("Gamepad", _pad);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_scene.isLoaded)
            {
                SceneManager.SetActiveScene(_previousScene);
                yield return SceneManager.UnloadSceneAsync(_scene);
            }
            TestSceneHelper.Cleanup();
            Object.Destroy(_actions);
            Object.Destroy(_info);
            yield return null;
            _devices.TearDown();
        }

        [UnityTest]
        public IEnumerator Guard_UsesShippedBindingsAndReleasesWhenInputIsInterrupted()
        {
            Assert.IsTrue(_knight.IsAlive);
            Assert.IsFalse(IsGuarding());

            SetPadGuard(true);
            yield return new WaitForFixedUpdate();
            Assert.IsTrue(IsGuarding());
            ActionExecutionSystem execution = GameManager.Actions.Execution;
            Assert.IsTrue(execution.GetPrimary(_knight.Actor.Hash, out ActionExecution firstGuard));
            execution.Tick(0.12f);
            yield return new WaitForFixedUpdate();
            Assert.IsTrue(execution.GetPrimary(_knight.Actor.Hash, out ActionExecution heldGuard));
            Assert.AreEqual(firstGuard.instanceId, heldGuard.instanceId);
            Assert.That(GameManager.Data.GetDefenseState(_knight.Actor.Hash).guardElapsedTime, Is.GreaterThan(0f));

            SetPadGuard(false);
            Assert.IsFalse(IsGuarding());
            Assert.IsFalse(execution.TryGetInstance(firstGuard.instanceId, out _));

            SetPadGuard(true);
            yield return new WaitForFixedUpdate();
            Assert.IsTrue(IsGuarding());
            _input.SwitchCurrentActionMap("UI");
            Assert.IsFalse(IsGuarding());
            _input.SwitchCurrentActionMap("Player");
            yield return new WaitForFixedUpdate();
            Assert.IsFalse(IsGuarding());
            SetPadGuard(false);

            SetPadGuard(true);
            yield return new WaitForFixedUpdate();
            Assert.IsTrue(IsGuarding());
            _adapter.enabled = false;
            Assert.IsFalse(IsGuarding());
            _adapter.enabled = true;
            yield return new WaitForFixedUpdate();
            Assert.IsFalse(IsGuarding());
            SetPadGuard(false);

            _input.SwitchCurrentControlScheme("Keyboard&Mouse", _keyboard, _mouse);
            SetMouseGuard(true);
            yield return new WaitForFixedUpdate();
            Assert.IsTrue(IsGuarding());
            _knight.gameObject.SendMessage("OnApplicationFocus", false);
            Assert.IsFalse(IsGuarding());
            _devices.ScheduleFocusChangedEvent(false);
            InputSystem.Update();
            _devices.ScheduleFocusChangedEvent(true);
            InputSystem.Update();
            SetMouseGuard(false);
            yield return new WaitForFixedUpdate();
            Assert.IsFalse(IsGuarding());

            SetMouseGuard(true);
            yield return new WaitForFixedUpdate();
            Assert.IsTrue(IsGuarding());
            ActorRef prior = _knight.Actor;
            _knight.enabled = false;
            _knight.enabled = true;
            Assert.AreNotEqual(prior, _knight.Actor);
            yield return new WaitForFixedUpdate();
            Assert.IsFalse(IsGuarding());
        }

        private bool IsGuarding() => GameManager.Data.GetDefenseState(_knight.Actor.Hash).isGuarding;

        private void SetPadGuard(bool pressed)
        {
            GamepadState state = new GamepadState().WithButton(GamepadButton.LeftShoulder, pressed);
            InputSystem.QueueStateEvent(_pad, state);
            InputSystem.Update();
        }

        private void SetMouseGuard(bool pressed)
        {
            MouseState state = new MouseState().WithButton(MouseButton.Right, pressed);
            InputSystem.QueueStateEvent(_mouse, state);
            InputSystem.Update();
        }
    }
}
