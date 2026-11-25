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
    public sealed class PlayerFieldOrderInput3DPlayTests
    {
        private readonly InputTestFixture _devices = new InputTestFixture();
        private readonly List<CharacterInfo> _actorSettings = new List<CharacterInfo>();
        private Scene _scene;
        private Scene _previousScene;
        private InputActionAsset _actions;
        private PlayerInput _input;
        private PlayerCharacterInput3D _adapter;
        private EncounterCombat3D _encounter;
        private CharacterActor3D _enemy;
        private Camera _camera;
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
            _scene = SceneManager.CreateScene("PlayerFieldOrders3D", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            SceneManager.SetActiveScene(_scene);
            TestSceneHelper.CreateGameManager();
            CreateBox("Lower floor", Vector3.down * 0.5f, new Vector3(24f, 1f, 24f));

            _camera = new GameObject("Knight camera").AddComponent<Camera>();
            _camera.transform.position = new Vector3(0f, 0.85f, -8f);
            _camera.orthographic = true;
            _camera.orthographicSize = 5f;
            _camera.pixelRect = new Rect(0f, 0f, 640f, 480f);

            CharacterActor3D knight = CreateActor("Knight", Vector3.zero, CharacterBelong.Ally, CharacterFeature.Player);
            CharacterActor3D ally = CreateActor("Ally", new Vector3(-5f, 0f, 0f), CharacterBelong.Ally, CharacterFeature.Minion);
            _enemy = CreateActor("Enemy", new Vector3(0.05f, 0f, 6f), CharacterBelong.Enemy, CharacterFeature.Minion);
            ally.gameObject.SetActive(true);
            _enemy.gameObject.SetActive(true);
            _encounter = new GameObject("Encounter").AddComponent<EncounterCombat3D>();
            SetField(_encounter, "knight", knight);
            SetField(_encounter, "obstructionLayers", (LayerMask)1);
            Assert.IsTrue(_encounter.Register(ally.gameObject.AddComponent<EncounterCombatant3D>()));
            Assert.IsTrue(_encounter.Register(_enemy.gameObject.AddComponent<EncounterCombatant3D>()));

            _input = knight.gameObject.AddComponent<PlayerInput>();
            _input.actions = _actions;
            _input.defaultActionMap = "Player";
            _input.camera = _camera;
            _input.neverAutoSwitchControlSchemes = true;
            _adapter = knight.gameObject.AddComponent<PlayerCharacterInput3D>();
            SetField(_adapter, "encounterOrders", _encounter);
            knight.gameObject.SetActive(true);
            _input.SwitchCurrentControlScheme("Keyboard&Mouse", _keyboard, _mouse);
            yield return null;
            Physics.SyncTransforms();
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
            foreach (CharacterInfo info in _actorSettings) { Object.Destroy(info); }
            _actorSettings.Clear();
            yield return null;
            _devices.TearDown();
        }

        [UnityTest]
        public IEnumerator KeyboardFieldOrder_TapIssuesAndHoldCycles_RespectingCoverAndTheSelectedFloor()
        {
            PressKeyboard(true);
            Assert.IsNull(_encounter.ActiveOrder);
            ReleaseKeyboard(0.08);
            Assert.AreEqual(BroadcastAction.Rally, _encounter.ActiveOrder);
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(1, _encounter.AssignedOrderCount);

            HoldKeyboard();
            Assert.AreEqual(BroadcastAction.FocusFire, _adapter.ActiveFieldOrder);
            Assert.AreEqual(BroadcastAction.Rally, _encounter.ActiveOrder);

            BoxCollider cover = CreateBox("Solid cover", new Vector3(0f, 1f, 3f), new Vector3(2f, 2f, 0.2f));
            Physics.SyncTransforms();
            AimPointer(_enemy.Body.bounds.center);
            TapKeyboard();
            Assert.AreEqual(BroadcastAction.Rally, _encounter.ActiveOrder);
            cover.enabled = false;
            Physics.SyncTransforms();
            TapKeyboard();
            Assert.AreEqual(BroadcastAction.FocusFire, _encounter.ActiveOrder);

            HoldKeyboard();
            Assert.AreEqual(BroadcastAction.DefendPoint, _adapter.ActiveFieldOrder);
            BoxCollider bridge = CreateBox("Upper bridge", new Vector3(3f, 2.4f, 4f), new Vector3(3f, 0.2f, 3f));
            _camera.transform.position = new Vector3(3f, 8f, -4f);
            _camera.transform.LookAt(new Vector3(3f, 2.5f, 4f));
            Physics.SyncTransforms();
            AimPointer(new Vector3(3f, 2.5f, 4f));
            TapKeyboard();
            Assert.AreEqual(BroadcastAction.DefendPoint, _encounter.ActiveOrder);

            bridge.enabled = false;
            yield return new WaitForFixedUpdate();
            Assert.IsNull(_encounter.ActiveOrder);

            HoldKeyboard();
            Assert.AreEqual(BroadcastAction.ReleaseOrder, _adapter.ActiveFieldOrder);
            HoldKeyboard();
            TapKeyboard();
            HoldKeyboard();
            HoldKeyboard();
            HoldKeyboard();
            Assert.AreEqual(BroadcastAction.Rally, _encounter.ActiveOrder);
            TapKeyboard();
            Assert.IsNull(_encounter.ActiveOrder);
        }

        [UnityTest]
        public IEnumerator GamepadFieldOrder_MapChangeCancelsAndFocusUsesTheRightStick()
        {
            _input.SwitchCurrentControlScheme("Gamepad", _pad);
            PressPad(true);
            _input.SwitchCurrentActionMap("UI");
            _input.SwitchCurrentActionMap("Player");
            PressPad(false);
            Assert.IsNull(_encounter.ActiveOrder);

            PressPad(true);
            _devices.currentTime += 0.08;
            PressPad(false);
            Assert.AreEqual(BroadcastAction.Rally, _encounter.ActiveOrder);
            PressPad(true);
            _devices.currentTime += 0.45;
            PressPad(false);
            Assert.AreEqual(BroadcastAction.FocusFire, _adapter.ActiveFieldOrder);
            PressPad(true);
            _devices.currentTime += 0.08;
            PressPad(false);
            Assert.AreEqual(BroadcastAction.Rally, _encounter.ActiveOrder);

            Vector2 look = Vector2.right * 0.5f;
            PressPad(false, look);
            Vector2 processed = _input.currentActionMap.FindAction("Look").ReadValue<Vector2>();
            Ray aim = _camera.ViewportPointToRay(Vector2.one * 0.5f + processed * 0.5f);
            Vector3 center = _enemy.Body.bounds.center;
            Vector3 aimedCenter = aim.GetPoint(Vector3.Dot(center - aim.origin, aim.direction));
            _enemy.transform.position += aimedCenter - center;
            Physics.SyncTransforms();
            PressPad(true, look);
            _devices.currentTime += 0.08;
            PressPad(false, look);
            Assert.AreEqual(BroadcastAction.FocusFire, _encounter.ActiveOrder);
            yield return null;
        }

        private void TapKeyboard()
        {
            PressKeyboard(true);
            ReleaseKeyboard(0.08);
        }

        private void HoldKeyboard()
        {
            PressKeyboard(true);
            ReleaseKeyboard(0.45);
        }

        private void ReleaseKeyboard(double seconds)
        {
            _devices.currentTime += seconds;
            PressKeyboard(false);
        }

        private void PressKeyboard(bool pressed)
        {
            InputSystem.QueueStateEvent(_keyboard, pressed ? new KeyboardState(Key.F) : new KeyboardState());
            InputSystem.Update();
        }

        private void PressPad(bool pressed, Vector2 look = default)
        {
            GamepadState state = new GamepadState().WithButton(GamepadButton.DpadDown, pressed);
            state.rightStick = look;
            InputSystem.QueueStateEvent(_pad, state);
            InputSystem.Update();
        }

        private void AimPointer(Vector3 world)
        {
            InputSystem.QueueStateEvent(_mouse, new MouseState { position = _camera.WorldToScreenPoint(world) });
            InputSystem.Update();
        }

        private CharacterActor3D CreateActor(string name, Vector3 position, CharacterBelong belong, CharacterFeature feature)
        {
            GameObject body = new GameObject(name);
            body.SetActive(false);
            body.layer = 8;
            body.transform.position = position;
            CharacterController controller = body.AddComponent<CharacterController>();
            controller.center = Vector3.up * 0.8f;
            controller.height = 1.6f;
            controller.radius = 0.3f;
            CharacterActor3D actor = body.AddComponent<CharacterActor3D>();
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(belong, feature);
            _actorSettings.Add(info);
            actor.SetCharacterInfoForTest(info);
            return actor;
        }

        private static BoxCollider CreateBox(string name, Vector3 position, Vector3 size)
        {
            GameObject body = new GameObject(name);
            body.transform.position = position;
            BoxCollider collider = body.AddComponent<BoxCollider>();
            collider.size = size;
            return collider;
        }

        private static void SetField(object instance, string field, object value)
        {
            instance.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(instance, value);
        }
    }
}
