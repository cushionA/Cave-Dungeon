using System.Collections;
using System.Collections.Generic;
using System.Reflection;
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
    public sealed class PlayerCooperationInput3DPlayTests
    {
        private readonly InputTestFixture _devices = new InputTestFixture();
        private readonly List<CharacterInfo> _actorSettings = new List<CharacterInfo>();
        private Scene _scene;
        private Scene _previousScene;
        private InputActionAsset _actions;
        private CharacterActor3D _knight;
        private CompanionController3D _companion;
        private PlayerInput _input;
        private PlayerCharacterInput3D _adapter;
        private Keyboard _keyboard;
        private Mouse _mouse;
        private Gamepad _pad;

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
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _mouse = InputSystem.AddDevice<Mouse>();
            _pad = InputSystem.AddDevice<Gamepad>();

            _previousScene = SceneManager.GetActiveScene();
            _scene = SceneManager.CreateScene("PlayerCooperationInput3D", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            SceneManager.SetActiveScene(_scene);
            TestSceneHelper.CreateGameManager();
            GameObject floor = new GameObject("Floor");
            floor.transform.position = Vector3.down * 0.5f;
            floor.AddComponent<BoxCollider>().size = new Vector3(25f, 1f, 25f);

            _knight = CreateActor("Knight", Vector3.zero, CharacterFeature.Player);
            CharacterActor3D sister = CreateActor("Sister", Vector3.right * 1.5f, CharacterFeature.Companion);
            _companion = sister.gameObject.AddComponent<CompanionController3D>();
            _companion.ConfigureForTest(_knight);

            _input = _knight.gameObject.AddComponent<PlayerInput>();
            _input.actions = _actions;
            _input.defaultActionMap = "Player";
            _input.neverAutoSwitchControlSchemes = true;
            _adapter = _knight.gameObject.AddComponent<PlayerCharacterInput3D>();
            typeof(PlayerCharacterInput3D).GetField("companion", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(_adapter, _companion);
            _knight.gameObject.SetActive(true);
            sister.gameObject.SetActive(true);
            _input.SwitchCurrentControlScheme("Keyboard&Mouse", _keyboard, _mouse);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            SceneManager.SetActiveScene(_previousScene);
            if (_scene.isLoaded) { yield return SceneManager.UnloadSceneAsync(_scene); }
            TestSceneHelper.Cleanup();
            Object.Destroy(_actions);
            foreach (CharacterInfo info in _actorSettings) { Object.Destroy(info); }
            _actorSettings.Clear();
            yield return null;
            _devices.TearDown();
        }

        [UnityTest]
        public IEnumerator GamepadCooperation_DpadUpStartsSupportOnTheKnight()
        {
            _input.SwitchCurrentControlScheme("Gamepad", _pad);
            Assert.IsFalse(_companion.IsCooperating);
            InputSystem.QueueStateEvent(_pad, new GamepadState().WithButton(GamepadButton.DpadUp));
            InputSystem.Update();
            Assert.IsFalse(_companion.IsCooperating);
            yield return new WaitForFixedUpdate();
            Assert.IsTrue(_companion.IsCooperating);

            InputSystem.QueueStateEvent(_pad, new GamepadState());
            InputSystem.Update();
            for (int i = 0; i < 30 && DefenseMultiplier() <= 1f; i++) { yield return new WaitForFixedUpdate(); }
            Assert.That(DefenseMultiplier(), Is.GreaterThan(1f));
        }

        [UnityTest]
        public IEnumerator KeyboardCooperation_MapFocusDisableAndOwnerReplacementCancelThePendingPress()
        {
            PressKeyboard(true);
            _input.SwitchCurrentActionMap("UI");
            _input.SwitchCurrentActionMap("Player");
            PressKeyboard(false);
            yield return new WaitForFixedUpdate();
            Assert.IsFalse(_companion.IsCooperating);

            PressKeyboard(true);
            _knight.gameObject.SendMessage("OnApplicationFocus", false);
            PressKeyboard(false);
            yield return new WaitForFixedUpdate();
            Assert.IsFalse(_companion.IsCooperating);

            PressKeyboard(true);
            _adapter.enabled = false;
            _adapter.enabled = true;
            PressKeyboard(false);
            yield return new WaitForFixedUpdate();
            Assert.IsFalse(_companion.IsCooperating);

            PressKeyboard(true);
            ActorRef previous = _knight.Actor;
            _knight.enabled = false;
            _knight.enabled = true;
            Assert.AreNotEqual(previous, _knight.Actor);
            PressKeyboard(false);
            yield return new WaitForFixedUpdate();
            Assert.IsFalse(_companion.IsCooperating);

            PressKeyboard(true);
            PressKeyboard(false);
            yield return new WaitForFixedUpdate();
            Assert.IsTrue(_companion.IsCooperating);
        }

        private float DefenseMultiplier() => GameManager.Data.GetStatusEffects(_knight.Actor.Hash).DefenseMultiplier;

        private CharacterActor3D CreateActor(string name, Vector3 position, CharacterFeature feature)
        {
            GameObject body = new GameObject(name);
            body.SetActive(false);
            body.transform.position = position;
            CharacterController controller = body.AddComponent<CharacterController>();
            controller.center = Vector3.up * 0.8f;
            controller.height = 1.6f;
            controller.radius = 0.3f;
            CharacterActor3D actor = body.AddComponent<CharacterActor3D>();
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(CharacterBelong.Ally, feature);
            _actorSettings.Add(info);
            actor.SetCharacterInfoForTest(info);
            return actor;
        }

        private void PressKeyboard(bool pressed)
        {
            InputSystem.QueueStateEvent(_keyboard, pressed ? new KeyboardState(Key.V) : new KeyboardState());
            InputSystem.Update();
        }
    }
}
