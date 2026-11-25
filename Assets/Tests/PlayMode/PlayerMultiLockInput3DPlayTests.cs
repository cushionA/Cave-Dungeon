using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using CharacterInfo = Game.Core.CharacterInfo;

namespace Game.Tests.PlayMode
{
    public sealed class PlayerMultiLockInput3DPlayTests
    {
        private const int k_ActorLayer = 8;
        private readonly InputTestFixture _devices = new InputTestFixture();
        private readonly List<ScriptableObject> _assets = new List<ScriptableObject>();
        private InputActionAsset _actions;
        private Scene _previousScene;
        private Scene _scene;
        private Gamepad _pad;
        private PlayerInput _playerInput;
        private PlayerCharacterInput3D _adapter;
        private CharacterActor3D _knight;
        private Camera _camera;

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

            _previousScene = SceneManager.GetActiveScene();
            _scene = SceneManager.CreateScene("PlayerMultiLock3D", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            SceneManager.SetActiveScene(_scene);
            GameObject world = new GameObject("Multi-lock game manager");
            world.SetActive(false);
            world.AddComponent<ProjectileManager3D>();
            world.AddComponent<GameManager>();
            world.SetActive(true);
            GameManager.Instance.enabled = false;

            _camera = new GameObject("Multi-lock camera").AddComponent<Camera>();
            _camera.transform.position = new Vector3(0f, 1.2f, -10f);
            _camera.orthographic = true;
            _camera.orthographicSize = 5f;
            _camera.pixelRect = new Rect(0f, 0f, 640f, 480f);

            GameObject floor = new GameObject("Floor");
            floor.transform.position = Vector3.down * 0.5f;
            floor.AddComponent<BoxCollider>().size = new Vector3(20f, 1f, 20f);
            _knight = CreateActor("Knight", Vector3.zero, CharacterBelong.Ally);
            _playerInput = _knight.gameObject.AddComponent<PlayerInput>();
            _playerInput.actions = _actions;
            _playerInput.defaultActionMap = "Player";
            _playerInput.camera = _camera;
            _adapter = _knight.gameObject.AddComponent<PlayerCharacterInput3D>();
            typeof(PlayerCharacterInput3D).GetField("skill3D", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(_adapter, CreateSkill());
            _knight.gameObject.SetActive(true);
            _playerInput.SwitchCurrentControlScheme("Gamepad", _pad);
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
            foreach (ScriptableObject asset in _assets) { Object.Destroy(asset); }
            _assets.Clear();
            yield return null;
            _devices.TearDown();
        }

        [UnityTest]
        public IEnumerator SkillHold_GamepadMovesFromLastMark_ReleasesOneProjectileForEachSelectedActor()
        {
            CharacterActor3D first = AddEnemy("First", new Vector3(0f, 1.2f, 3f));
            CharacterActor3D diagonal = AddEnemy("Diagonal", new Vector3(2f, 3.2f, 3f));
            CharacterActor3D right = AddEnemy("Right", new Vector3(4f, 3.2f, 3f));
            CharacterActor3D fourth = AddEnemy("Above cap", new Vector3(6f, 3.2f, 3f));
            yield return null;
            Physics.SyncTransforms();

            SetButton(_pad.rightShoulder, true);
            CollectionAssert.AreEqual(new[] { first.Actor }, _adapter.MultiLockTargets);
            SetStick(Vector2.up);
            CollectionAssert.AreEqual(new[] { first.Actor }, _adapter.MultiLockTargets);
            SetStick(Vector2.zero);
            SetStick(new Vector2(0.8f, 0.8f));
            CollectionAssert.AreEqual(new[] { first.Actor, diagonal.Actor }, _adapter.MultiLockTargets);
            SetStick(Vector2.right);
            CollectionAssert.AreEqual(new[] { first.Actor, diagonal.Actor }, _adapter.MultiLockTargets);
            SetStick(Vector2.zero);
            SetStick(Vector2.right);
            CollectionAssert.AreEqual(new[] { first.Actor, diagonal.Actor, right.Actor }, _adapter.MultiLockTargets);
            SetStick(Vector2.zero);
            SetStick(Vector2.right);
            CollectionAssert.AreEqual(new[] { first.Actor, diagonal.Actor, right.Actor }, _adapter.MultiLockTargets);
            Assert.That(GameManager.Projectiles3D.ActiveCount, Is.Zero);

            SetButton(_pad.rightShoulder, false);
            Assert.That(_adapter.MultiLockTargets, Is.Empty);
            yield return new WaitForFixedUpdate();
            GameManager.Actions.Execution.Tick(0.22f);
            Assert.That(GameManager.Projectiles3D.ActiveCount, Is.EqualTo(3));

            List<ProjectileLockWarning> warnings = new List<ProjectileLockWarning>();
            foreach (CharacterActor3D selected in new[] { first, diagonal, right })
            {
                GameManager.Projectiles3D.CollectLockWarnings(selected.Actor, warnings);
                Assert.That(warnings.Count, Is.EqualTo(1), selected.name);
            }
            GameManager.Projectiles3D.CollectLockWarnings(fourth.Actor, warnings);
            Assert.That(warnings, Is.Empty);
        }

        [UnityTest]
        public IEnumerator SkillHold_DisablingMapOrActor_CancelsTheSelectedAttack()
        {
            CharacterActor3D target = AddEnemy("Target", new Vector3(0f, 1f, 3f));
            yield return null;
            Physics.SyncTransforms();
            SetButton(_pad.rightShoulder, true);
            CollectionAssert.AreEqual(new[] { target.Actor }, _adapter.MultiLockTargets);
            _playerInput.SwitchCurrentActionMap("UI");
            _playerInput.SwitchCurrentActionMap("Player");
            SetButton(_pad.rightShoulder, false);
            Assert.That(_adapter.MultiLockTargets, Is.Empty);
            yield return new WaitForFixedUpdate();
            GameManager.Actions.Execution.Tick(0.25f);
            Assert.That(GameManager.Projectiles3D.ActiveCount, Is.Zero);

            SetButton(_pad.rightShoulder, true);
            CollectionAssert.AreEqual(new[] { target.Actor }, _adapter.MultiLockTargets);
            _knight.enabled = false;
            _knight.enabled = true;
            SetButton(_pad.rightShoulder, false);
            Assert.That(_adapter.MultiLockTargets, Is.Empty);
            yield return new WaitForFixedUpdate();
            GameManager.Actions.Execution.Tick(0.25f);
            Assert.That(GameManager.Projectiles3D.ActiveCount, Is.Zero);

            SetButton(_pad.rightShoulder, true);
            SetButton(_pad.rightShoulder, false);
            yield return new WaitForFixedUpdate();
            GameManager.Actions.Execution.Tick(0.25f);
            Assert.That(GameManager.Projectiles3D.ActiveCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator AttackHold_WhenTheCameraRolls_UsesTheNewScreenDirections()
        {
            CharacterActor3D first = AddEnemy("First", new Vector3(0f, 1.2f, 3f));
            CharacterActor3D above = AddEnemy("World up", new Vector3(0f, 3.2f, 3f));
            CharacterActor3D beside = AddEnemy("World right", new Vector3(2f, 1.2f, 3f));
            typeof(PlayerCharacterInput3D).GetField("attack3D", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(_adapter, CreateSkill());
            yield return null;
            Physics.SyncTransforms();

            SetButton(_pad.buttonWest, true);
            CollectionAssert.AreEqual(new[] { first.Actor }, _adapter.MultiLockTargets);
            _camera.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
            SetStick(Vector2.right);
            CollectionAssert.AreEqual(new[] { first.Actor, above.Actor }, _adapter.MultiLockTargets);
            SetStick(Vector2.zero);
            SetStick(new Vector2(-0.8f, -0.8f));
            CollectionAssert.AreEqual(new[] { first.Actor, above.Actor, beside.Actor }, _adapter.MultiLockTargets);
            Assert.That(GameManager.Projectiles3D.ActiveCount, Is.Zero);
        }

        private CharacterActor3D AddEnemy(string name, Vector3 position)
        {
            CharacterActor3D actor = CreateActor(name, position, CharacterBelong.Enemy);
            actor.gameObject.SetActive(true);
            return actor;
        }

        private CharacterActor3D CreateActor(string name, Vector3 position, CharacterBelong belong)
        {
            GameObject actor = new GameObject(name);
            actor.SetActive(false);
            actor.layer = k_ActorLayer;
            actor.transform.position = position;
            CharacterController body = actor.AddComponent<CharacterController>();
            body.center = Vector3.up * 0.8f;
            body.height = 1.6f;
            body.radius = 0.3f;
            CharacterActor3D registered = actor.AddComponent<CharacterActor3D>();
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(belong);
            _assets.Add(info);
            registered.SetCharacterInfoForTest(info);
            return registered;
        }

        private AttackInfo CreateSkill()
        {
            MotionDefinition motion = ScriptableObject.CreateInstance<MotionDefinition>();
            motion.info = new MotionInfo { preMotionDuration = 0.2f, activeMotionDuration = 0.2f, recoveryDuration = 0.1f };
            _assets.Add(motion);
            AttackInfo definition = ScriptableObject.CreateInstance<AttackInfo>();
            definition.motion = motion;
            definition.damageMultiplier = 1f;
            definition.multiLockRange = 12f;
            definition.attack3D = new AttackProfile3D
            {
                shape = AttackShape3D.Projectile,
                projectileRadius = 0.15f,
                localCenter = new Vector3(0f, 0.8f, 0.5f),
                targetLayers = 1 << k_ActorLayer,
                obstructionLayers = 1,
            };
            definition.projectileInfo = new ProjectileInfo
            {
                hasProjectile = true, trajectory = BulletMoveType.Homing,
                lifetime = 5f, speed = 6f, homingStrength = 8f,
            };
            _assets.Add(definition);
            return definition;
        }

        private void SetButton(ButtonControl control, bool pressed)
        {
            if (pressed) { _devices.Press(control, queueEventOnly: true); }
            else { _devices.Release(control, queueEventOnly: true); }
            InputSystem.Update();
        }

        private void SetStick(Vector2 stick)
        {
            _devices.Set(_pad.rightStick, stick, queueEventOnly: true);
            InputSystem.Update();
        }
    }
}
