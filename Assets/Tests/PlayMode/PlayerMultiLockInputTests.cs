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
    public class PlayerMultiLockInputTests
    {
        private readonly InputTestFixture _devices = new InputTestFixture();
        private readonly List<ScriptableObject> _assets = new List<ScriptableObject>();
        private InputActionAsset _actionAsset;
        private Scene _previousScene;
        private Scene _scene;
        private Keyboard _keyboard;
        private Mouse _mouse;
        private Gamepad _pad;
        private Camera _camera;
        private CameraController _follow;
        private PlayerInput _playerInput;
        private PlayerInputHandler _input;
        private PlayerCharacter _player;
        private ProjectileManager _projectiles;
        private AttackInfo _skill;

        [UnitySetUp]
        public IEnumerator CreateScene()
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
            _actionAsset = InputActionAsset.FromJson(shippedActions);
            _previousScene = SceneManager.GetActiveScene();
            _scene = SceneManager.CreateScene("PlayerMultiLock", new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            SceneManager.SetActiveScene(_scene);

            GameObject world = new GameObject("Multi-lock world");
            world.SetActive(false);
            GameObject projectileObject = new GameObject("Projectiles");
            projectileObject.transform.SetParent(world.transform, false);
            _projectiles = projectileObject.AddComponent<ProjectileManager>();
            typeof(ProjectileManager).GetField("_preWarmCount", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(_projectiles, 0);
            world.AddComponent<GameManager>();
            world.SetActive(true);
            GameManager.Instance.enabled = false;

            _keyboard = InputSystem.AddDevice<Keyboard>();
            _mouse = InputSystem.AddDevice<Mouse>();
            _pad = InputSystem.AddDevice<Gamepad>();
            _camera = new GameObject("Multi-lock camera").AddComponent<Camera>();
            _follow = _camera.gameObject.AddComponent<CameraController>();

            GameObject actor = new GameObject("Knight");
            actor.SetActive(false);
            _playerInput = actor.AddComponent<PlayerInput>();
            _playerInput.actions = _actionAsset;
            _playerInput.defaultActionMap = "Player";
            _playerInput.notificationBehavior = PlayerNotifications.SendMessages;
            _playerInput.camera = _camera;
            _input = actor.AddComponent<PlayerInputHandler>();
            actor.AddComponent<ActionExecutorController>();
            _player = actor.AddComponent<PlayerCharacter>();
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo();
            _assets.Add(info);
            _player.SetCharacterInfoForTest(info);
            MotionDefinition motion = ScriptableObject.CreateInstance<MotionDefinition>();
            motion.info = new MotionInfo { preMotionDuration = 0.1f, activeMotionDuration = 0.2f, recoveryDuration = 0.1f };
            _assets.Add(motion);
            _skill = ScriptableObject.CreateInstance<AttackInfo>();
            _skill.motion = motion;
            _skill.mpCost = 5;
            _skill.damageMultiplier = 1f;
            _skill.multiLockRange = 12f;
            _skill.projectileInfo = new ProjectileInfo
            {
                hasProjectile = true,
                projectileCount = 3,
                trajectory = BulletMoveType.Homing,
                homingStrength = 8f,
                speed = 3f,
                lifetime = 5f,
            };
            _assets.Add(_skill);
            _player.SetMultiLockSkillForTest(_skill);
            actor.SetActive(true);
            _player.SetPhysicsGravityScale(0f);
            _playerInput.SwitchCurrentControlScheme("Gamepad", _pad);
            _follow.SetTarget(actor.transform);
            _camera.orthographicSize = 5f;
            _camera.pixelRect = new Rect(0, 0, 640, 480);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator DestroyScene()
        {
            _projectiles?.ClearAll();
            SceneManager.SetActiveScene(_previousScene);
            if (_scene.IsValid()) { yield return SceneManager.UnloadSceneAsync(_scene); }
            TestSceneHelper.Cleanup();
            Object.Destroy(_actionAsset);
            foreach (ScriptableObject asset in _assets) { Object.Destroy(asset); }
            _assets.Clear();
            yield return null;
            _devices.TearDown();
        }

        [UnityTest]
        public IEnumerator RightShoulder_UsesLastMarkAndOneDirectionPerTilt_ThenFiresThreeAssignedShots()
        {
            BaseCharacter a = CreateEnemy(new Vector2(0, 1.2f));
            BaseCharacter b = CreateEnemy(new Vector2(2, 3.2f));
            BaseCharacter c = CreateEnemy(new Vector2(4, 3.2f));
            CreateEnemy(new Vector2(6, 3.2f));
            BaseCharacter orderTarget = CreateEnemy(Vector2.left * 2f);
            yield return null;
            SyncPhysics();

            List<FieldOrder> delivered = new List<FieldOrder>();
            GameManager.Events.SetFieldOrderReceiver(issuer => issuer.Hash == _player.ObjectHash
                ? FieldOrderIssuer.Player : FieldOrderIssuer.None, order => { delivered.Add(order); return true; });
            Assert.IsTrue(_input.TryAssignFieldOrder(0, BroadcastAction.FocusFire));
            SetStick(Vector2.left);
            Press(_pad.dpad.down);
            Release(_pad.dpad.down);
            Assert.That(delivered.Count, Is.EqualTo(1));
            Assert.AreEqual(Reference(orderTarget), delivered[0].Target);
            SetStick(Vector2.zero);

            Press(_pad.rightShoulder);
            AssertMarks(a);
            SetStick(Vector2.up);
            AssertMarks(a); // Selected actor and empty sector do not change the mark.
            SetStick(Vector2.zero);
            SetStick(new Vector2(0.8f, 0.8f));
            AssertMarks(a, b);
            SetStick(Vector2.right);
            AssertMarks(a, b);
            SetStick(Vector2.zero);
            AssertMarks(a, b);
            SetStick(Vector2.right);
            AssertMarks(a, b, c);
            SetStick(Vector2.zero);
            SetStick(Vector2.right);
            AssertMarks(a, b, c);
            Assert.AreEqual(0, _projectiles.ActiveCount);

            Release(_pad.rightShoulder);
            AssertMarks();
            AdvanceCast();

            Assert.AreEqual(3, _projectiles.ActiveCount);
            ProjectileController[] shots = Object.FindObjectsByType<ProjectileController>(FindObjectsInactive.Exclude);
            List<ActorRef> firedAt = new List<ActorRef>();
            long id = GameManager.Actions.Execution.GetPrimary(_player.ObjectHash, out ActionExecution execution)
                ? execution.instanceId : 0;
            Assert.AreNotEqual(0, id);
            foreach (ProjectileController shot in shots)
            {
                if (!shot.IsActive || shot.Magic.actionInstanceId != id) { continue; }
                firedAt.Add(shot.CoreProjectile.TargetRef);
            }
            CollectionAssert.AreEquivalent(new[] { Reference(a), Reference(b), Reference(c) }, firedAt);
            Assert.AreEqual(45, GameManager.Data.GetVitals(_player.ObjectHash).currentMp);

            SetStick(Vector2.left);
            Press(_pad.dpad.down);
            Release(_pad.dpad.down);
            Assert.That(delivered.Count, Is.EqualTo(2));
            Assert.AreEqual(delivered[0].Target, delivered[1].Target);
            Assert.AreSame(_player.transform, typeof(CameraController)
                .GetField("_target", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_follow));
        }

        [UnityTest]
        public IEnumerator RightStick_UsesRotatedCameraAndFiltersScreenRangeAndOcclusion()
        {
            BaseCharacter a = CreateEnemy(new Vector2(0, 1.2f));
            BaseCharacter b = CreateEnemy(new Vector2(0, 3f));
            BaseCharacter c = CreateEnemy(new Vector2(2, -1f));
            CreateEnemy(new Vector2(-0.9f, 0f));
            CreateEnemy(new Vector2(7.4f, 0f));
            CreateEnemy(new Vector2(0f, 5f));
            GameObject wall = new GameObject("Occluding wall");
            wall.layer = 6;
            wall.transform.position = Vector2.left * 0.42f;
            wall.AddComponent<BoxCollider2D>().size = new Vector2(0.15f, 0.5f);
            yield return null;
            SyncPhysics();

            _skill.multiLockRange = 4f;
            _camera.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
            Press(_pad.rightShoulder);
            AssertMarks(a);
            SetStick(Vector2.right);
            AssertMarks(a, b); // World up is screen right.
            SetStick(Vector2.zero);
            SetStick(Vector2.right);
            AssertMarks(a, b); // The next on-screen enemy is outside attack range.
            SetStick(Vector2.zero);
            SetStick(Vector2.down);
            AssertMarks(a, b); // The candidate lies in the screen's down-left sector.
            SetStick(Vector2.zero);
            SetStick(new Vector2(-0.8f, -0.8f));
            AssertMarks(a, b, c);
        }

        [UnityTest]
        public IEnumerator SkillHold_MapChangePlayerDisableAndOverride_CancelWithoutCasting()
        {
            BaseCharacter target = CreateEnemy(Vector2.right * 2f);
            yield return null;
            SyncPhysics();

            Press(_pad.rightShoulder);
            AssertMarks(target);
            _playerInput.SwitchCurrentActionMap("UI");
            _playerInput.SwitchCurrentActionMap("Player");
            Release(_pad.rightShoulder);
            AssertMarks();
            AdvanceCast();

            Press(_pad.rightShoulder);
            AssertMarks(target);
            _player.enabled = false;
            _player.enabled = true;
            Release(_pad.rightShoulder);
            AssertMarks();
            AdvanceCast();

            Press(_pad.rightShoulder);
            AssertMarks(target);
            _input.SetOverrideInput(default);
            _input.ClearOverrideInput();
            Release(_pad.rightShoulder);
            AssertMarks();
            AdvanceCast();

            Press(_pad.rightShoulder);
            Release(_pad.rightShoulder);
            _playerInput.SwitchCurrentActionMap("UI");
            _playerInput.SwitchCurrentActionMap("Player");
            AdvanceCast();
            Assert.AreEqual(0, _projectiles.ActiveCount);
            Assert.AreEqual(50, GameManager.Data.GetVitals(_player.ObjectHash).currentMp);

            Press(_pad.rightShoulder);
            Release(_pad.rightShoulder);
            AdvanceCast();
            Assert.AreEqual(1, _projectiles.ActiveCount);
        }

        [UnityTest]
        public IEnumerator KeyboardSkill_WithAndWithoutCompatibleSkill_PreservesLegacyPress()
        {
            BaseCharacter target = CreateEnemy(Vector2.right * 2f);
            yield return null;
            SyncPhysics();
            _playerInput.SwitchCurrentControlScheme("Keyboard&Mouse", _keyboard, _mouse);
            _player.SetMultiLockSkillForTest(null);
            Press(_keyboard.qKey);
            _input.PrepareSimulationInput(Time.time);
            Assert.AreEqual(AttackInputType.Skill, _input.CurrentInput.attackInput);
            AssertMarks();
            Release(_keyboard.qKey);
            _input.ConsumeAttackInput();

            _player.SetMultiLockSkillForTest(_skill);
            Press(_keyboard.qKey);
            AssertMarks(target);
            _input.PrepareSimulationInput(Time.time);
            Assert.IsNull(_input.CurrentInput.attackInput);
            Release(_keyboard.qKey);
            AdvanceCast();
            Assert.AreEqual(1, _projectiles.ActiveCount);
        }

        [UnityTest]
        public IEnumerator Attacks_LightProjectileAndHeavyMeleeHoldMarks_UnmarkedLightIsImmediate()
        {
            BaseCharacter a = CreateEnemy(Vector2.right * 2f);
            BaseCharacter b = CreateEnemy(new Vector2(4f, 2f));
            yield return null;
            SyncPhysics();

            AttackInfo melee = ScriptableObject.CreateInstance<AttackInfo>();
            melee.motion = _skill.motion;
            melee.multiLockRange = _skill.multiLockRange;
            _assets.Add(melee);
            _player.SetLightAttacksForTest(new[] { _skill, melee });
            Press(_pad.buttonWest);
            AssertMarks(a);
            SetStick(new Vector2(0.8f, 0.8f));
            AssertMarks(a, b);
            GameManager.Instance.SimulationTick(0f, Time.time);
            _devices.currentTime += 0.75;
            Assert.AreEqual(50, GameManager.Data.GetVitals(_player.ObjectHash).currentMp);
            AssertMarks(a, b);
            Release(_pad.buttonWest);
            AdvanceCast();
            Assert.AreEqual(2, _projectiles.ActiveCount);
            Assert.AreEqual(45, GameManager.Data.GetVitals(_player.ObjectHash).currentMp);

            _projectiles.ClearAll();
            GameManager.Actions.Execution.Tick(1f);
            SetStick(Vector2.zero);
            Press(_pad.rightTrigger);
            AssertMarks(a);
            Release(_pad.rightTrigger);
            AdvanceCast();
            Assert.AreEqual(0, _projectiles.ActiveCount);
            Assert.IsTrue(GameManager.Actions.Execution.GetPrimary(_player.ObjectHash, out ActionExecution heavy));
            Assert.AreEqual(1, heavy.selectedTargets.Count);
            Assert.AreEqual(Reference(a), heavy.selectedTargets[0]);

            AttackInfo ordinary = ScriptableObject.CreateInstance<AttackInfo>();
            _assets.Add(ordinary);
            _player.SetLightAttacksForTest(new[] { ordinary });
            Press(_pad.buttonWest);
            _input.PrepareSimulationInput(Time.time);
            Assert.That(_input.CurrentInput.attackInput, Is.EqualTo(AttackInputType.LightAttack)
                .Or.EqualTo(AttackInputType.AerialLight));
            AssertMarks();
            Release(_pad.buttonWest);
        }

        private BaseCharacter CreateEnemy(Vector2 position)
        {
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(CharacterBelong.Enemy, CharacterFeature.Minion);
            _assets.Add(info);
            BaseCharacter enemy = TestSceneHelper.CreateBaseCharacterObject(info, position).GetComponent<BaseCharacter>();
            enemy.SetPhysicsGravityScale(0f);
            return enemy;
        }

        private void SyncPhysics()
        {
            Physics2D.SyncTransforms();
            GameManager.Instance.SimulationTick(0f, Time.time);
        }

        private void AdvanceCast()
        {
            GameManager.Instance.SimulationTick(0f, Time.time);
            GameManager.Actions.Execution.Tick(0.11f);
        }

        private static ActorRef Reference(BaseCharacter actor)
        {
            Assert.IsTrue(GameManager.Actors.TryGet(actor.ObjectHash, out ActorRef target));
            return target;
        }

        private void AssertMarks(params BaseCharacter[] actors)
        {
            List<ActorRef> expected = new List<ActorRef>(actors.Length);
            foreach (BaseCharacter actor in actors) { expected.Add(Reference(actor)); }
            CollectionAssert.AreEqual(expected, _input.MultiLockTargets);
        }

        private void Press(ButtonControl control)
        {
            _devices.Press(control, queueEventOnly: true);
            InputSystem.Update();
        }

        private void Release(ButtonControl control)
        {
            _devices.currentTime += 0.08;
            _devices.Release(control, queueEventOnly: true);
            InputSystem.Update();
        }

        private void SetStick(Vector2 value)
        {
            _devices.Set(_pad.rightStick, value, queueEventOnly: true);
            InputSystem.Update();
        }
    }
}
