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

namespace Game.Tests.PlayMode
{
    public sealed class PlayerItemInput3DPlayTests
    {
        private const int k_FirstItem = 4101;
        private const int k_SecondItem = 4102;

        private readonly InputTestFixture _devices = new InputTestFixture();
        private readonly List<ScriptableObject> _assets = new List<ScriptableObject>();
        private Scene _scene;
        private Scene _previousScene;
        private InputActionAsset _actions;
        private Keyboard _keyboard;
        private Mouse _mouse;
        private Gamepad _pad;
        private CharacterActor3D _knight;
        private PlayerInput _input;

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
            _scene = SceneManager.CreateScene("PlayerItemInput3D", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
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
            Game.Core.CharacterInfo character = TestSceneHelper.CreateTestCharacterInfo();
            _assets.Add(character);
            _knight.SetCharacterInfoForTest(character);
            PlayerItemUse3D itemUse = body.AddComponent<PlayerItemUse3D>();
            SetField(itemUse, "items", new[] { CreateConsumable(k_FirstItem), CreateConsumable(k_SecondItem) });
            _input = body.AddComponent<PlayerInput>();
            _input.actions = _actions;
            _input.defaultActionMap = "Player";
            _input.neverAutoSwitchControlSchemes = true;
            body.AddComponent<PlayerCharacterInput3D>();
            body.SetActive(true);
            _input.SwitchCurrentControlScheme("Keyboard&Mouse", _keyboard, _mouse);

            GameManager.Inventory.Add(k_FirstItem, ItemCategory.Consumable, 2, 99);
            GameManager.Inventory.Add(k_SecondItem, ItemCategory.Consumable, 2, 99);
            Assert.IsTrue(GameManager.ConsumableBelt.TryAssign(0, k_FirstItem));
            Assert.IsTrue(GameManager.ConsumableBelt.TryAssign(1, k_SecondItem));
            GameManager.Data.GetVitals(_knight.Actor.Hash).currentHp = 50;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            SceneManager.SetActiveScene(_previousScene);
            if (_scene.isLoaded) { yield return SceneManager.UnloadSceneAsync(_scene); }
            TestSceneHelper.Cleanup();
            Object.Destroy(_actions);
            foreach (ScriptableObject asset in _assets) { Object.Destroy(asset); }
            _assets.Clear();
            yield return null;
            _devices.TearDown();
        }

        [UnityTest]
        public IEnumerator Keyboard_ItemSelectionAndUseInOneTick_ConsumeTheNewSlotAtActive()
        {
            TapKey(Key.Digit2);
            TapKey(Key.R);
            Assert.AreEqual(0, GameManager.ConsumableBelt.ActiveIndex);
            Assert.AreEqual(2, Count(k_SecondItem));

            yield return new WaitForFixedUpdate();
            Assert.AreEqual(1, GameManager.ConsumableBelt.ActiveIndex);
            Assert.IsTrue(GameManager.Actions.Execution.GetPrimary(_knight.Actor.Hash, out ActionExecution windup));
            Assert.AreEqual(AnimationPhase.Anticipation, windup.phase);
            Assert.AreEqual(2, Count(k_SecondItem));

            GameManager.Actions.Execution.Tick(0.26f);
            Assert.AreEqual(2, Count(k_FirstItem));
            Assert.AreEqual(1, Count(k_SecondItem));
            Assert.AreEqual(70, GameManager.Data.GetVitals(_knight.Actor.Hash).currentHp);
        }

        [UnityTest]
        public IEnumerator Gamepad_SelectionWrapsAndBUses_StaleRAndKeyboardCDoNotUse()
        {
            TapKey(Key.C);
            yield return new WaitForFixedUpdate();
            Assert.IsFalse(GameManager.Actions.Execution.GetPrimary(_knight.Actor.Hash, out _));

            TapKey(Key.R);
            _input.SwitchCurrentActionMap("UI");
            _input.SwitchCurrentActionMap("Player");
            yield return new WaitForFixedUpdate();
            Assert.IsFalse(GameManager.Actions.Execution.GetPrimary(_knight.Actor.Hash, out _));

            TapKey(Key.R);
            ActorRef oldOwner = _knight.Actor;
            _knight.enabled = false;
            _knight.enabled = true;
            Assert.AreNotEqual(oldOwner, _knight.Actor);
            GameManager.Data.GetVitals(_knight.Actor.Hash).currentHp = 50;
            yield return new WaitForFixedUpdate();
            Assert.IsFalse(GameManager.Actions.Execution.GetPrimary(_knight.Actor.Hash, out _));
            Assert.AreEqual(2, Count(k_FirstItem));

            _input.SwitchCurrentControlScheme("Gamepad", _pad);
            TapPad(GamepadButton.DpadLeft);
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(GameManager.ConsumableBelt.SlotCount - 1, GameManager.ConsumableBelt.ActiveIndex);
            TapPad(GamepadButton.DpadRight);
            TapPad(GamepadButton.East);
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(0, GameManager.ConsumableBelt.ActiveIndex);
            Assert.AreEqual(2, Count(k_FirstItem));
            GameManager.Actions.Execution.Tick(0.26f);
            Assert.AreEqual(1, Count(k_FirstItem));
            Assert.AreEqual(2, Count(k_SecondItem));
        }

        private ConsumableDefinition CreateConsumable(int id)
        {
            MotionDefinition motion = ScriptableObject.CreateInstance<MotionDefinition>();
            motion.info = new MotionInfo { preMotionDuration = 0.25f, activeMotionDuration = 0.1f, recoveryDuration = 0.1f };
            ActionDefinition action = ScriptableObject.CreateInstance<ActionDefinition>();
            action.motion = motion;
            ConsumableDefinition item = ScriptableObject.CreateInstance<ConsumableDefinition>();
            SetField(item, "itemId", id);
            SetField(item, "useAction", action);
            _assets.Add(motion);
            _assets.Add(action);
            _assets.Add(item);
            return item;
        }

        private static void SetField(object instance, string field, object value)
        {
            instance.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(instance, value);
        }

        private static int Count(int id) => GameManager.Inventory.GetConsumableCount(id);

        private void TapKey(Key key)
        {
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(key));
            InputSystem.Update();
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            InputSystem.Update();
        }

        private void TapPad(GamepadButton button)
        {
            InputSystem.QueueStateEvent(_pad, new GamepadState().WithButton(button));
            InputSystem.Update();
            InputSystem.QueueStateEvent(_pad, new GamepadState());
            InputSystem.Update();
        }
    }
}
