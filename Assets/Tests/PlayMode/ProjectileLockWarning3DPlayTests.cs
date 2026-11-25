using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using CharacterInfo = Game.Core.CharacterInfo;

namespace Game.Tests.PlayMode
{
    public sealed class ProjectileLockWarning3DPlayTests
    {
        private readonly List<ScriptableObject> _assets = new List<ScriptableObject>();
        private readonly List<ProjectileLockWarning> _warnings = new List<ProjectileLockWarning>();
        private Scene _room;
        private Camera _camera;
        private CharacterActor3D _knight;
        private CharacterActor3D _caster;
        private CharacterActor3D _otherCaster;
        private UIDocument _document;
        private float _time;

        private ProjectileWarningOverlay Overlay =>
            _document.rootVisualElement.Q<ProjectileWarningOverlay>("projectile-lock-warnings");

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            TestSceneHelper.Cleanup();
            yield return null;

            GameObject world = new GameObject("3D warning world");
            world.SetActive(false);
            world.AddComponent<ProjectileManager3D>();
            world.AddComponent<GameManager>();
            world.SetActive(true);
            GameManager.Instance.enabled = false;
            _room = SceneManager.CreateScene(nameof(ProjectileLockWarning3DPlayTests),
                new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            _time = 0f;

            _camera = CreateObject("Knight camera").AddComponent<Camera>();
            _camera.transform.position = new Vector3(0f, 1f, -10f);
            _knight = CreateActor("Knight", Vector3.zero, CharacterBelong.Ally, asPlayer: true);
            _caster = CreateEnemy("Northern caster", new Vector3(0f, 0f, 7f));
            _otherCaster = CreateEnemy("Eastern caster", new Vector3(3f, 0f, 7f));

            GameObject hud = CreateObject("3D HUD");
            hud.SetActive(false);
            _document = hud.AddComponent<UIDocument>();
            _document.panelSettings = CreateAsset<PanelSettings>();
            ProjectileLockWarningHud3D presenter = hud.AddComponent<ProjectileLockWarningHud3D>();
            typeof(ProjectileLockWarningHud3D).GetField("character", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(presenter, _knight);
            hud.SetActive(true);
            yield return null;

            Assert.That(Overlay, Is.Not.Null);
            Assert.That(Overlay.panel, Is.Not.Null);
            Assert.That(CharacterRegistry.PlayerHash, Is.Zero);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_room.isLoaded) { yield return SceneManager.UnloadSceneAsync(_room); }
            foreach (ScriptableObject asset in _assets) { Object.Destroy(asset); }
            _assets.Clear();
            TestSceneHelper.Cleanup();
            yield return null;
        }

        [UnityTest]
        public IEnumerator WarningHud3D_OnlyExplicitKnightShot_DrawsOneArcUsingAssignedCamera()
        {
            CharacterActor3D sister = CreateActor("Sister", new Vector3(2f, 0f, 0f), CharacterBelong.Ally);
            CharacterActor3D ally = CreateActor("Other ally", new Vector3(-2f, 0f, 0f), CharacterBelong.Ally);
            ActorRef[] selected = { sister.Actor, _knight.Actor, ally.Actor };
            Assert.That(GameManager.Actions.Execute3D(_caster, Projectile(multilock: true), selected, out _), Is.True);
            Assert.That(GameManager.Actions.Execute3D(_otherCaster, Projectile(), out _), Is.True);
            Step(0.1f);
            yield return null;

            Assert.That(GameManager.Projectiles3D.ActiveCount, Is.EqualTo(4));
            Assert.That(Overlay.Directions.Count, Is.EqualTo(1));
            Assert.That(Overlay.Directions[0].y, Is.LessThan(-0.95f));
            ulong knightLease = GetWarningLease(_knight.Actor);
            Assert.That(GetWarningLease(sister.Actor), Is.Not.EqualTo(knightLease));
            Assert.That(GetWarningLease(ally.Actor), Is.Not.EqualTo(knightLease));

            _camera.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            yield return null;
            Assert.That(Overlay.Directions.Count, Is.EqualTo(1));
            Assert.That(Overlay.Directions[0].x, Is.LessThan(-0.95f));

            Assert.That(GameManager.Projectiles3D.ReleaseExplicitLock(knightLease), Is.True);
            yield return null;
            Assert.That(Overlay.Directions, Is.Empty);
            Assert.That(GameManager.Projectiles3D.ActiveCount, Is.EqualTo(4));
            Assert.That(GetWarningLease(sister.Actor), Is.Not.EqualTo(knightLease));
        }

        [UnityTest]
        public IEnumerator WarningHud3D_AnExplicitStraightShotPassesTheKnight_HidesItsArcBeforeItExpires()
        {
            Assert.That(GameManager.Actions.Execute3D(_caster, Projectile(multilock: true),
                new[] { _knight.Actor }, out _), Is.True);
            Step(0.1f);
            yield return null;
            Assert.That(Overlay.Directions.Count, Is.EqualTo(1));

            _knight.transform.position += Vector3.right * 2f;
            Step(0.7f);
            yield return null;
            Assert.That(GameManager.Projectiles3D.ActiveCount, Is.EqualTo(1));
            Assert.That(Overlay.Directions, Is.Empty);

            Step(3f);
            yield return null;
            Assert.That(GameManager.Projectiles3D.ActiveCount, Is.Zero);
            Assert.That(Overlay.Directions, Is.Empty);
        }

        [UnityTest]
        public IEnumerator WarningHud3D_KnightReregisters_OnlyTheNewLeaseCanWarnForTheNewGeneration()
        {
            ActorRef before = _knight.Actor;
            Assert.That(GameManager.Actions.Execute3D(_caster, Projectile(multilock: true), new[] { before }, out _), Is.True);
            Step(0.1f);
            yield return null;
            ulong priorLease = GetWarningLease(before);
            Assert.That(Overlay.Directions.Count, Is.EqualTo(1));

            _knight.enabled = false;
            _knight.enabled = true;
            ActorRef after = _knight.Actor;
            Assert.That(after.Hash, Is.EqualTo(before.Hash));
            Assert.That(after.Generation, Is.Not.EqualTo(before.Generation));
            yield return null;
            Assert.That(GameManager.Projectiles3D.ActiveCount, Is.EqualTo(1));
            Assert.That(Overlay.Directions, Is.Empty);

            Assert.That(GameManager.Actions.Execute3D(_otherCaster, Projectile(multilock: true, lifetime: 0.5f),
                new[] { after }, out _), Is.True);
            Step(0.1f);
            yield return null;
            Assert.That(Overlay.Directions.Count, Is.EqualTo(1));
            Assert.That(GetWarningLease(after), Is.Not.EqualTo(priorLease));

            Step(0.6f);
            yield return null;
            Assert.That(Overlay.Directions, Is.Empty);
        }

        private void Step(float deltaTime)
        {
            Physics.SyncTransforms();
            _room.GetPhysicsScene().Simulate(0.02f);
            _time += deltaTime;
            GameManager.Instance.SimulationTick(deltaTime, _time);
        }

        private ulong GetWarningLease(ActorRef target)
        {
            GameManager.Projectiles3D.CollectLockWarnings(target, _warnings);
            Assert.That(_warnings.Count, Is.EqualTo(1));
            return _warnings[0].LeaseId;
        }

        private CharacterActor3D CreateEnemy(string name, Vector3 position)
        {
            CharacterActor3D character = CreateActor(name, position, CharacterBelong.Enemy);
            character.transform.rotation = Quaternion.LookRotation(-position, Vector3.up);
            return character;
        }

        private CharacterActor3D CreateActor(string name, Vector3 position, CharacterBelong side, bool asPlayer = false)
        {
            GameObject actor = CreateObject(name);
            actor.SetActive(false);
            actor.transform.position = position;
            actor.layer = LayerMask.NameToLayer(side == CharacterBelong.Ally ? "Player" : "Enemy");
            CharacterController body = actor.AddComponent<CharacterController>();
            body.height = 1.8f;
            body.center = Vector3.up * 0.9f;
            body.radius = 0.3f;
            actor.AddComponent<CharacterMotor3D>();
            actor.AddComponent<DamageReceiver>();
            CharacterActor3D character = actor.AddComponent<CharacterActor3D>();
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(side,
                asPlayer ? CharacterFeature.Player : CharacterFeature.Minion);
            _assets.Add(info);
            character.SetCharacterInfoForTest(info);
            if (asPlayer)
            {
                actor.AddComponent<PlayerInput>().camera = _camera;
                actor.AddComponent<PlayerCharacterInput3D>().enabled = false;
            }
            actor.SetActive(true);
            Assert.That(character.Actor.IsValid, Is.True);
            return character;
        }

        private GameObject CreateObject(string name)
        {
            GameObject obj = new GameObject(name);
            SceneManager.MoveGameObjectToScene(obj, _room);
            return obj;
        }

        private AttackInfo Projectile(bool multilock = false, float lifetime = 3f)
        {
            MotionDefinition motion = CreateAsset<MotionDefinition>();
            motion.info = new MotionInfo { preMotionDuration = 0.05f, activeMotionDuration = 0.1f, recoveryDuration = 0.1f };
            AttackInfo attack = CreateAsset<AttackInfo>();
            attack.category = AttackCategory.Magic;
            attack.motion = motion;
            attack.multiLockRange = multilock ? 15f : 0f;
            attack.attackElement = Element.Slash;
            attack.damageMultiplier = 1f;
            attack.attack3D = new AttackProfile3D
            {
                shape = AttackShape3D.Projectile,
                localCenter = Vector3.up * 0.9f,
                projectileRadius = 0.12f,
                targetLayers = LayerMask.GetMask("Player"),
            };
            attack.projectileInfo = new ProjectileInfo
            {
                hasProjectile = true,
                trajectory = BulletMoveType.Straight,
                speed = 10f,
                lifetime = lifetime,
                projectileCount = 1,
            };
            return attack;
        }

        private T CreateAsset<T>() where T : ScriptableObject
        {
            T asset = ScriptableObject.CreateInstance<T>();
            _assets.Add(asset);
            return asset;
        }
    }
}
