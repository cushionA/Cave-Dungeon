using System.Collections;
using System.Collections.Generic;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using CharacterInfo = Game.Core.CharacterInfo;

namespace Game.Tests.PlayMode
{
    public class ProjectileLockWarningPlayTests
    {
        private readonly List<CharacterInfo> _infos = new List<CharacterInfo>();
        private readonly List<ProjectileLockWarning> _warnings = new List<ProjectileLockWarning>();
        private GameObject _world;
        private ProjectileManager _projectiles;
        private PlayerCharacter _knight;
        private BaseCharacter _sister;
        private BaseCharacter _otherAlly;
        private BaseCharacter _caster;
        private Camera _camera;
        private PanelSettings _panel;
        private UIDocument _document;

        private ProjectileWarningOverlay Overlay =>
            _document.rootVisualElement.Q<ProjectileWarningOverlay>("projectile-lock-warnings");

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            TestSceneHelper.Cleanup();
            yield return null;

            _world = new GameObject("Projectile lock warning test");
            _world.SetActive(false);
            _projectiles = CreateChild("Projectiles").AddComponent<ProjectileManager>();
            _world.AddComponent<GameManager>();
            _world.SetActive(true);
            GameManager.Instance.enabled = false;

            _camera = CreateChild("Knight camera").AddComponent<Camera>();
            _camera.transform.position = new Vector3(0f, 0f, -10f);
            _camera.orthographic = true;

            GameObject player = CreateChild("Knight");
            player.SetActive(false);
            PlayerInput input = player.AddComponent<PlayerInput>();
            input.camera = _camera;
            player.AddComponent<PlayerInputHandler>();
            _knight = player.AddComponent<PlayerCharacter>();
            _knight.SetCharacterInfoForTest(CreateInfo(CharacterBelong.Ally, CharacterFeature.Player, player.name));
            player.SetActive(true);
            _knight.Rigidbody.gravityScale = 0f;

            _sister = CreateActor("Sister", CharacterBelong.Ally, new Vector2(1f, 4f));
            _otherAlly = CreateActor("Other ally", CharacterBelong.Ally, new Vector2(-1f, -4f));
            _caster = CreateActor("Enemy caster", CharacterBelong.Enemy, new Vector2(9f, 3f));

            GameObject hud = CreateChild("HUD");
            hud.SetActive(false);
            _panel = ScriptableObject.CreateInstance<PanelSettings>();
            _document = hud.AddComponent<UIDocument>();
            _document.panelSettings = _panel;
            hud.AddComponent<HudController>();
            hud.SetActive(true);
            yield return null;

            _knight.PublishPositionForObservation();
            _sister.PublishPositionForObservation();
            _otherAlly.PublishPositionForObservation();
            _caster.PublishPositionForObservation();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_projectiles != null) { _projectiles.ClearAll(); }
            Object.Destroy(_world);
            yield return null;

            Object.Destroy(_panel);
            foreach (CharacterInfo info in _infos) { Object.Destroy(info); }
            _infos.Clear();
            TestSceneHelper.Cleanup();
            yield return null;
        }

        [UnityTest]
        public IEnumerator ProjectileLockWarning_OnlyKnightShot_AddsOneArcAndTracksHisCamera()
        {
            ProjectileController forSister = Spawn(Actor(_sister));
            ProjectileController forKnight = Spawn(Actor(_knight));
            ProjectileController forOtherAlly = Spawn(Actor(_otherAlly));
            ProjectileController unassigned = Spawn(default, move: BulletMoveType.Homing);
            _projectiles.SimulationTick(0.02f);
            yield return null;

            Assert.IsNotNull(Overlay);
            Assert.IsNotNull(Overlay.panel);
            Assert.That(Overlay.Directions.Count, Is.EqualTo(1));
            Assert.That(Overlay.Directions[0].x, Is.GreaterThan(0.95f));
            _projectiles.CollectLockWarnings(Actor(_knight), _warnings);
            Assert.That(_warnings[0].LeaseId, Is.EqualTo(forKnight.CoreProjectile.LeaseId));
            Assert.That(forSister.CoreProjectile.ExplicitTargetRef, Is.EqualTo(Actor(_sister)));
            Assert.That(forOtherAlly.CoreProjectile.ExplicitTargetRef, Is.EqualTo(Actor(_otherAlly)));
            Assert.That(unassigned.CoreProjectile.ExplicitTargetRef, Is.EqualTo(default(ActorRef)));
            Assert.That(unassigned.CoreProjectile.TargetPosition, Is.EqualTo(_knight.PhysicsPosition));

            _camera.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
            yield return null;
            Assert.That(Overlay.Directions.Count, Is.EqualTo(1));
            Assert.That(Overlay.Directions[0].y, Is.GreaterThan(0.95f));
        }

        [UnityTest]
        public IEnumerator ProjectileLockWarning_WhenShotPassesOrUnlocks_RemovesOnlyItsArc()
        {
            ProjectileController passing = Spawn(Actor(_knight), new Vector2(5f, 1.25f));
            ProjectileController released = Spawn(Actor(_knight), new Vector2(12f, 2f));
            yield return null;
            Assert.That(Overlay.Directions.Count, Is.EqualTo(2));

            _projectiles.SimulationTick(0.7f);
            yield return null;
            Assert.IsTrue(passing.CoreProjectile.IsAlive);
            Assert.That(passing.CoreProjectile.Position.x, Is.LessThan(0f));
            Assert.That(Overlay.Directions.Count, Is.EqualTo(1));
            _projectiles.CollectLockWarnings(Actor(_knight), _warnings);
            Assert.That(_warnings[0].LeaseId, Is.EqualTo(released.CoreProjectile.LeaseId));

            Assert.IsTrue(released.CoreProjectile.TrySetTarget(default, GameManager.Actors));
            yield return null;
            Assert.IsEmpty(Overlay.Directions);
        }

        [UnityTest]
        public IEnumerator ProjectileLockWarning_TargetGenerationOrProjectileLeaseChanges_ClearsTheArc()
        {
            ActorRef originalKnight = Actor(_knight);
            ProjectileController original = Spawn(originalKnight);
            Projectile originalCore = original.CoreProjectile;
            ulong previousLease = originalCore.LeaseId;
            yield return null;
            Assert.That(Overlay.Directions.Count, Is.EqualTo(1));

            _knight.OnPoolReturn();
            _knight.OnPoolAcquire();
            _knight.Rigidbody.gravityScale = 0f;
            ActorRef nextKnight = Actor(_knight);
            Assert.That(nextKnight, Is.Not.EqualTo(originalKnight));
            yield return null;
            Assert.IsEmpty(Overlay.Directions);

            _projectiles.ReturnProjectile(original);
            _projectiles.SimulationTick(0.02f);
            ProjectileController unassigned = Spawn(default);
            Assert.AreSame(originalCore, unassigned.CoreProjectile);
            Assert.That(unassigned.CoreProjectile.LeaseId, Is.Not.EqualTo(previousLease));
            yield return null;
            Assert.IsEmpty(Overlay.Directions);

            ProjectileController current = Spawn(nextKnight);
            yield return null;
            Assert.That(Overlay.Directions.Count, Is.EqualTo(1));
            _projectiles.ReturnProjectile(current);
            yield return null;
            Assert.IsEmpty(Overlay.Directions);
        }

        private GameObject CreateChild(string name)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(_world.transform, false);
            return obj;
        }

        private BaseCharacter CreateActor(string name, CharacterBelong belong, Vector2 position)
        {
            CharacterInfo info = CreateInfo(belong, CharacterFeature.Companion, name);
            GameObject obj = TestSceneHelper.CreateBaseCharacterObjectWithDamageReceiver(info, position);
            obj.transform.SetParent(_world.transform, false);
            BaseCharacter actor = obj.GetComponent<BaseCharacter>();
            actor.Rigidbody.gravityScale = 0f;
            return actor;
        }

        private CharacterInfo CreateInfo(CharacterBelong belong, CharacterFeature feature, string name)
        {
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(belong, feature);
            info.name = name;
            _infos.Add(info);
            return info;
        }

        private static ActorRef Actor(BaseCharacter actor)
        {
            Assert.IsTrue(GameManager.Actors.TryGet(actor.ObjectHash, out ActorRef actorRef));
            return actorRef;
        }

        private ProjectileController Spawn(ActorRef target, Vector2? position = null, BulletMoveType move = BulletMoveType.Straight)
        {
            MagicDefinition magic = new MagicDefinition
            {
                magicType = MagicType.Attack,
                bulletProfile = new BulletProfile { moveType = move, speed = 10f, lifeTime = 5f },
            };
            ProjectileController projectile = _projectiles.SpawnProjectile(Actor(_caster), magic,
                position ?? new Vector2(7f, 1f), Vector2.left, target);
            Assert.IsNotNull(projectile);
            return projectile;
        }
    }
}
