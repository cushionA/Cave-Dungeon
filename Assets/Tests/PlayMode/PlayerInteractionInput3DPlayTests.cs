using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using CharacterInfo = Game.Core.CharacterInfo;

namespace Game.Tests.PlayMode
{
    public sealed class PlayerInteractionInput3DPlayTests
    {
        private readonly InputTestFixture _devices = new InputTestFixture();
        private readonly List<NpcInteraction3D> _activated = new List<NpcInteraction3D>();
        private Scene _scene;
        private Scene _otherScene;
        private Scene _previousScene;
        private InputActionAsset _actions;
        private PlayerInput _input;
        private PlayerCharacterInput3D _adapter;
        private CharacterActor3D _knight;
        private CharacterInfo _info;
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
            _scene = SceneManager.CreateScene("PlayerInteraction3D", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            _otherScene = SceneManager.CreateScene("OtherInteraction3D", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            SceneManager.SetActiveScene(_scene);
            TestSceneHelper.CreateGameManager();
            GameManager.Instance.enabled = false;

            GameObject floor = new GameObject("Floor");
            floor.transform.position = Vector3.down * 0.5f;
            floor.AddComponent<BoxCollider>().size = new Vector3(30f, 1f, 30f);
            GameObject body = new GameObject("Knight");
            body.SetActive(false);
            CharacterController controller = body.AddComponent<CharacterController>();
            controller.center = Vector3.up * 0.8f;
            controller.height = 1.6f;
            controller.radius = 0.3f;
            _knight = body.AddComponent<CharacterActor3D>();
            _info = TestSceneHelper.CreateTestCharacterInfo();
            _knight.SetCharacterInfoForTest(_info);
            _input = body.AddComponent<PlayerInput>();
            _input.actions = _actions;
            _input.defaultActionMap = "Player";
            _input.neverAutoSwitchControlSchemes = true;
            _adapter = body.AddComponent<PlayerCharacterInput3D>();
            body.SetActive(true);
            _input.SwitchCurrentControlScheme("Keyboard&Mouse", _keyboard, _mouse);
            _activated.Clear();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            SceneManager.SetActiveScene(_previousScene);
            if (_scene.isLoaded) { yield return SceneManager.UnloadSceneAsync(_scene); }
            if (_otherScene.isLoaded) { yield return SceneManager.UnloadSceneAsync(_otherScene); }
            TestSceneHelper.Cleanup();
            Object.Destroy(_actions);
            Object.Destroy(_info);
            yield return null;
            _devices.TearDown();
        }

        [UnityTest]
        public IEnumerator InteractHold_KeyboardChoosesTheNearestRegisteredSceneHook_PadCanUseItAgain()
        {
            NpcInteraction3D nearest = CreateNpc("Vendor", 1.1f);
            NpcInteraction3D secondary = CreateNpc("Another NPC", 1.5f);
            NpcInteraction3D unlinked = CreateNpc("Unlinked NPC", 0.1f);
            NpcInteraction3D disabled = CreateNpc("Disabled NPC", 0.2f);
            disabled.enabled = false;
            NpcInteraction3D crossScene = CreateNpc("Other room", 0.3f);
            SceneManager.MoveGameObjectToScene(crossScene.gameObject, _otherScene);
            NpcInteraction3D far = CreateNpc("Outside range", 2.8f);
            SetTargets(secondary, crossScene, disabled, far, nearest);
            yield return null;

            Assert.IsTrue(unlinked.CanInteract(_knight.Actor.Hash));
            Assert.IsFalse(crossScene.CanInteract(_knight.Actor.Hash));
            Assert.IsFalse(disabled.CanInteract(_knight.Actor.Hash));
            Assert.IsFalse(far.CanInteract(_knight.Actor.Hash));

            PressKeyboard(true);
            Assert.IsEmpty(_activated);
            CompleteHold();
            PressKeyboard(false);
            yield return new WaitForFixedUpdate();
            CollectionAssert.AreEqual(new[] { nearest }, _activated);

            _input.SwitchCurrentControlScheme("Gamepad", _pad);
            PressPad(true);
            CompleteHold();
            yield return new WaitForFixedUpdate();
            CollectionAssert.AreEqual(new[] { nearest, nearest }, _activated);
            yield return new WaitForFixedUpdate();
            Assert.That(_activated.Count, Is.EqualTo(2));
            PressPad(false);
        }

        [UnityTest]
        public IEnumerator InteractHold_CancelsOnMapChangeAndNewOwner_AndDoesNotRetargetOnDisable()
        {
            NpcInteraction3D nearest = CreateNpc("Vendor", 1f);
            NpcInteraction3D secondary = CreateNpc("Backup", 1.5f);
            SetTargets(nearest, secondary);
            yield return null;

            PressKeyboard(true);
            CompleteHold();
            _input.SwitchCurrentActionMap("UI");
            _input.SwitchCurrentActionMap("Player");
            PressKeyboard(false);
            yield return new WaitForFixedUpdate();
            Assert.IsEmpty(_activated);

            PressKeyboard(true);
            CompleteHold();
            ActorRef oldOwner = _knight.Actor;
            _knight.enabled = false;
            _knight.enabled = true;
            Assert.AreNotEqual(oldOwner, _knight.Actor);
            PressKeyboard(false);
            yield return new WaitForFixedUpdate();
            Assert.IsEmpty(_activated);

            PressKeyboard(true);
            CompleteHold();
            nearest.enabled = false;
            PressKeyboard(false);
            yield return new WaitForFixedUpdate();
            Assert.IsEmpty(_activated);

            PressKeyboard(true);
            CompleteHold();
            PressKeyboard(false);
            yield return new WaitForFixedUpdate();
            CollectionAssert.AreEqual(new[] { secondary }, _activated);
        }

        private NpcInteraction3D CreateNpc(string name, float distance)
        {
            GameObject body = new GameObject(name);
            body.transform.position = Vector3.forward * distance;
            NpcInteraction3D target = body.AddComponent<NpcInteraction3D>();
            UnityEvent interacted = (UnityEvent)typeof(NpcInteraction3D).GetField("onInteracted", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(target);
            interacted.AddListener(() => _activated.Add(target));
            return target;
        }

        private void SetTargets(params NpcInteraction3D[] targets)
        {
            typeof(PlayerCharacterInput3D).GetField("interactionTargets", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(_adapter, targets);
        }

        private void CompleteHold()
        {
            _devices.currentTime += InputSystem.settings.defaultHoldTime + 0.05;
            InputSystem.Update();
        }

        private void PressKeyboard(bool pressed)
        {
            InputSystem.QueueStateEvent(_keyboard, pressed ? new KeyboardState(Key.E) : new KeyboardState());
            InputSystem.Update();
        }

        private void PressPad(bool pressed)
        {
            InputSystem.QueueStateEvent(_pad, new GamepadState().WithButton(GamepadButton.North, pressed));
            InputSystem.Update();
        }
    }
}
