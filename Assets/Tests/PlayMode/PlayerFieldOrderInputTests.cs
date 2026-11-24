using System.Collections;
using System.Collections.Generic;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Game.Tests.PlayMode
{
    public class PlayerFieldOrderInputTests
    {
        private readonly InputTestFixture _devices = new InputTestFixture();
        private InputActionAsset _actionAsset;
        private Scene _previousScene;
        private Scene _testScene;
        private Keyboard _keyboard;
        private Mouse _mouse;
        private Gamepad _pad;
        private PlayerInput _playerInput;
        private PlayerInputHandler _input;
        private PlayerCharacter _player;
        private Camera _camera;
        private readonly List<FieldOrder> _delivered = new List<FieldOrder>();
        private readonly List<ScriptableObject> _assets = new List<ScriptableObject>();

        [UnitySetUp]
        public IEnumerator CreatePlayer()
        {
            TestSceneHelper.Cleanup();
            yield return null;
#if UNITY_EDITOR
            var source = UnityEditor.AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions");
#else
            var source = InputSystem.actions;
#endif
            Assert.IsNotNull(source);
            string shippedActions = source.ToJson();
            _devices.Setup();
            _actionAsset = InputActionAsset.FromJson(shippedActions);
            _previousScene = SceneManager.GetActiveScene();
            _testScene = SceneManager.CreateScene("FieldOrderInput", new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            SceneManager.SetActiveScene(_testScene);
            TestSceneHelper.CreateGameManager();
            GameManager.Instance.enabled = false;
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _mouse = InputSystem.AddDevice<Mouse>();
            _pad = InputSystem.AddDevice<Gamepad>();

            _camera = new GameObject("Order camera").AddComponent<Camera>();
            _camera.transform.position = new Vector3(0, 0, -10);
            _camera.orthographic = true;
            _camera.orthographicSize = 5f;
            _camera.pixelRect = new Rect(0, 0, 640, 480);

            var actor = new GameObject("Order player");
            actor.SetActive(false);
            actor.transform.position = Vector2.up;
            _playerInput = actor.AddComponent<PlayerInput>();
            _playerInput.actions = _actionAsset;
            _playerInput.defaultActionMap = "Player";
            _playerInput.notificationBehavior = PlayerNotifications.SendMessages;
            _playerInput.camera = _camera;
            _input = actor.AddComponent<PlayerInputHandler>();
            _player = actor.AddComponent<PlayerCharacter>();
            var info = TestSceneHelper.CreateTestCharacterInfo();
            _assets.Add(info);
            _player.SetCharacterInfoForTest(info);
            actor.SetActive(true);
            _playerInput.SwitchCurrentControlScheme("Keyboard&Mouse", _keyboard, _mouse);
            yield return null;
            _delivered.Clear();
        }

        private void CaptureSubmittedOrders()
        {
            GameManager.Events.SetFieldOrderReceiver(source => source.Hash == _player.ObjectHash
                ? FieldOrderIssuer.Player : FieldOrderIssuer.None,
                order => { _delivered.Add(order); return true; });
        }

        [UnityTearDown]
        public IEnumerator DestroyPlayer()
        {
            SceneManager.SetActiveScene(_previousScene);
            yield return SceneManager.UnloadSceneAsync(_testScene);
            TestSceneHelper.Cleanup();
            Object.Destroy(_actionAsset);
            foreach (var asset in _assets) { Object.Destroy(asset); }
            _assets.Clear();
            yield return null;
            _devices.TearDown();
        }

        private void Tap(ButtonControl button)
        {
            // The Input System fixture only queues device events in coroutine tests.
            _devices.Press(button, queueEventOnly: true);
            InputSystem.Update();
            _devices.currentTime += 0.08;
            _devices.Release(button, queueEventOnly: true);
            InputSystem.Update();
        }

        [Test]
        public void ShippedBindings_TapReleasesOnce_LongReleaseSelectsWithoutIssuing()
        {
            CaptureSubmittedOrders();
            Assert.AreEqual(BroadcastAction.Rally, _input.ActiveFieldOrder);
            _devices.Press(_keyboard.fKey);
            Assert.IsEmpty(_delivered);
            _devices.Release(_keyboard.fKey);
            InputSystem.Update();
            Assert.That(_delivered.Count, Is.EqualTo(1));
            Assert.That(_delivered[0].Action, Is.EqualTo(BroadcastAction.Rally));
            Assert.IsTrue(GameManager.Actors.TryGet(_player.ObjectHash, out ActorRef source));
            Assert.AreEqual(source, _delivered[0].Source);

            _playerInput.SwitchCurrentControlScheme("Gamepad", _pad);
            _devices.Press(_pad.dpad.down);
            _devices.currentTime += 0.45;
            _devices.Release(_pad.dpad.down);
            Assert.That(_input.ActiveFieldOrderSlot, Is.EqualTo(1));
            Assert.That(_delivered.Count, Is.EqualTo(1));
            Tap(_pad.dpad.down); // neutral right stick cannot select an actor
            Assert.That(_delivered.Count, Is.EqualTo(1));
        }

        [Test]
        public void ActionMapChangeDisableAndOverride_CancelPendingRelease()
        {
            CaptureSubmittedOrders();
            _devices.Press(_keyboard.fKey);
            _playerInput.SwitchCurrentActionMap("UI");
            _devices.Release(_keyboard.fKey);
            Assert.IsNull(_input.FieldOrderHint);
            _playerInput.SwitchCurrentActionMap("Player");
            _devices.Press(_keyboard.fKey);
            _input.enabled = false;
            _input.enabled = true;
            _devices.Release(_keyboard.fKey);
            _devices.Press(_keyboard.fKey);
            _input.SetOverrideInput(default);
            _input.ClearOverrideInput();
            _devices.Release(_keyboard.fKey);
            Assert.IsEmpty(_delivered);
            Tap(_keyboard.fKey);
            Assert.That(_delivered.Count, Is.EqualTo(1));
        }

        [Test]
        public void Assignment_SwapsEquipped_ReplacesUnequipped_RefusesEnemyOrUnknown()
        {
            Assert.IsTrue(_input.TryAssignFieldOrder(1, BroadcastAction.Rally));
            Assert.AreEqual(BroadcastAction.FocusFire, _input.ActiveFieldOrder);
            Assert.IsTrue(_input.TryAssignFieldOrder(0, BroadcastAction.DesignateTarget));
            Assert.IsFalse(_input.TryAssignFieldOrder(0, BroadcastAction.Taunt));
            Assert.IsFalse(_input.TryAssignFieldOrder(0, (BroadcastAction)255));
            Assert.IsFalse(_input.TryAssignFieldOrder(4, BroadcastAction.Rally));
            Assert.AreEqual(BroadcastAction.DesignateTarget, _input.ActiveFieldOrder);
            _devices.Press(_keyboard.fKey);
            _devices.currentTime += 0.45;
            _devices.Release(_keyboard.fKey);
            Assert.AreEqual(BroadcastAction.Rally, _input.ActiveFieldOrder);
        }

        [UnityTest]
        public IEnumerator RallyFromKeyboard_ReachesRealCompanionAndClosesNormalFollowGap()
        {
            var actor = new GameObject("Rally companion");
            actor.SetActive(false);
            actor.transform.position = new Vector2(-1.7f, 1f);
            actor.AddComponent<ActionExecutorController>();
            var sister = actor.AddComponent<CompanionCharacter>();
            var ai = ScriptableObject.CreateInstance<AIInfo>();
            ai.actDataList = new[] { new ActData { actType = ActType.Wait, weight = 10f } };
            ai.modes = new[] { new CharacterModeData { mode = CharacterMode.Combat,
                detectionRange = 20f, availableActIndices = new[] { 0 } } };
            _assets.Add(ai);
            sister.SetAIInfo(ai);
            var info = TestSceneHelper.CreateTestCharacterInfo(CharacterBelong.Ally, CharacterFeature.Companion);
            _assets.Add(info);
            sister.SetCharacterInfoForTest(info);
            actor.SetActive(true);
            yield return null;
            GameManager.Instance.SimulationTick(0.02f, 1f);
            Assert.That(sister.PhysicsVelocity.x, Is.EqualTo(0f).Within(0.01f), "Normal follow waits within 2 metres");

            Tap(_keyboard.fKey);

            Assert.That(_input.FieldOrderHint, Does.Contain("指示が届きました"));
            GameManager.Instance.SimulationTick(0.02f, 1.02f);
            Assert.That(sister.PhysicsVelocity.x, Is.GreaterThan(0f), "Rally closes the gap to the issuer");
        }

        [UnityTest]
        public IEnumerator Aim_MouseAndPadSelectActualBody_WallAndEmptyAimReject_GroundProvidesSurface()
        {
            CaptureSubmittedOrders();
            var info = TestSceneHelper.CreateTestCharacterInfo(CharacterBelong.Enemy, CharacterFeature.Minion);
            _assets.Add(info);
            GameObject foe = TestSceneHelper.CreateBaseCharacterObject(info, new Vector2(3, 1));
            GameObject ground = TestSceneHelper.CreateGround();
            yield return null;
            Physics2D.SyncTransforms();
            Assert.IsTrue(_input.TryAssignFieldOrder(0, BroadcastAction.FocusFire));
            _devices.Set(_mouse.position, (Vector2)_camera.WorldToScreenPoint(foe.transform.position + Vector3.up * 2f));
            Tap(_keyboard.fKey);
            Assert.IsEmpty(_delivered);
            _devices.Set(_mouse.position, (Vector2)_camera.WorldToScreenPoint(foe.transform.position));
            Tap(_keyboard.fKey);
            Assert.That(_delivered.Count, Is.EqualTo(1));
            Assert.IsTrue(GameManager.Actors.TryGet(foe.GetComponent<BaseCharacter>().ObjectHash, out ActorRef target));
            Assert.AreEqual(target, _delivered[0].Target);

            _playerInput.SwitchCurrentControlScheme("Gamepad", _pad);
            _devices.Set(_pad.rightStick, Vector2.right);
            Tap(_pad.dpad.down);
            Assert.That(_delivered.Count, Is.EqualTo(2));
            Assert.AreEqual(target, _delivered[1].Target);
            var wall = new GameObject("Solid wall");
            wall.AddComponent<BoxCollider2D>().size = new Vector2(0.5f, 4f);
            wall.transform.position = new Vector2(1.5f, 1);
            Physics2D.SyncTransforms();
            Tap(_pad.dpad.down);
            Assert.That(_delivered.Count, Is.EqualTo(2));

            Assert.IsTrue(_input.TryAssignFieldOrder(0, BroadcastAction.DefendPoint));
            _devices.Set(_pad.rightStick, Vector2.down);
            Tap(_pad.dpad.down);
            Assert.That(_delivered.Count, Is.EqualTo(3));
            Assert.IsTrue(_delivered[2].Position.HasValue);
            Assert.That(_delivered[2].Position.Value.y,
                Is.EqualTo(ground.GetComponent<BoxCollider2D>().bounds.max.y).Within(0.02f));

            _playerInput.SwitchCurrentControlScheme("Keyboard&Mouse", _keyboard, _mouse);
            _devices.Set(_mouse.position, (Vector2)_camera.WorldToScreenPoint(new Vector3(-1, -2, 0)));
            Tap(_keyboard.fKey);
            Assert.That(_delivered.Count, Is.EqualTo(3), "The cursor is behind the ground surface");
            _devices.Set(_mouse.position, (Vector2)_camera.WorldToScreenPoint(new Vector3(-1, 0, 0)));
            Tap(_keyboard.fKey);
            Assert.That(_delivered.Count, Is.EqualTo(4));
            Assert.That(_delivered[3].Position.Value.x, Is.EqualTo(-1f).Within(0.02f));
        }
    }
}
