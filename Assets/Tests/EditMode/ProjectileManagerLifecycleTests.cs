using System.Collections.Generic;
using System.Reflection;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    public class ProjectileManagerLifecycleTests
    {
        private GameObject _managerObject;
        private GameObject _projectileObject;
        private GameManagerCore _world;
        private ProjectileManager _projectiles;
        private ActorRef _owner;

        [SetUp]
        public void SetUp()
        {
            Assert.IsNull(GameManager.Instance, "The fixture must own the GameManager singleton.");
            _world = new GameManagerCore();
            _world.Initialize(16);
            _world.RegisterCharacter(17,
                new CharacterVitals { currentHp = 100, maxHp = 100, currentMp = 10, maxMp = 10 },
                new CombatStats { attack = new ElementalStatus { slash = 10 } },
                CharacterFlags.Pack(CharacterBelong.Ally, CharacterFeature.Player, ActState.Neutral, AbilityFlag.None),
                default);
            Assert.IsTrue(_world.Actors.TryGet(17, out _owner));

            // The inactive wrapper exposes the real Core world without running scene startup in EditMode.
            _managerObject = new GameObject("ProjectileLifecycleWorld");
            _managerObject.SetActive(false);
            GameManager wrapper = _managerObject.AddComponent<GameManager>();
            typeof(GameManager).GetField("_core", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(wrapper, _world);
            SetInstance(wrapper);
            _projectileObject = new GameObject("ProjectileLifecycleManager");
            _projectiles = _projectileObject.AddComponent<ProjectileManager>();
            typeof(ProjectileManager).GetField("_preWarmCount", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(_projectiles, 0);
            _projectiles.Initialize();
        }

        [TearDown]
        public void TearDown()
        {
            if (_projectiles != null) { _projectiles.ClearAll(); }
            if (_projectileObject != null) { Object.DestroyImmediate(_projectileObject); }
            SetInstance(null);
            if (_managerObject != null) { Object.DestroyImmediate(_managerObject); }
            _world?.Dispose();
        }

        private static void SetInstance(GameManager manager)
        {
            typeof(GameManager).GetProperty(nameof(GameManager.Instance)).GetSetMethod(true)
                .Invoke(null, new object[] { manager });
        }

        private ProjectilePool CorePool => (ProjectilePool)typeof(ProjectileManager)
            .GetField("_corePool", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_projectiles);

        private static MagicDefinition Magic(ChildBulletTrigger trigger = ChildBulletTrigger.None,
            int childCount = 1, float childSpread = 0f)
        {
            return new MagicDefinition
            {
                magicType = MagicType.Attack,
                motionValue = 1f,
                attackElement = Element.Slash,
                bulletProfile = new BulletProfile
                {
                    moveType = BulletMoveType.Straight, speed = 0f, lifeTime = 5f,
                    hitLimit = 1, emitInterval = 0.1f,
                },
                childBullet = trigger == ChildBulletTrigger.None ? null : new ChildBulletConfig
                {
                    trigger = trigger,
                    count = childCount,
                    spreadAngle = childSpread,
                    profile = new BulletProfile
                    {
                        moveType = BulletMoveType.Straight, speed = 2f, lifeTime = 5f,
                        hitLimit = 1, features = BulletFeature.Gravity,
                    },
                },
            };
        }

        private List<ProjectileController> ActiveControllers()
        {
            List<ProjectileController> active = new List<ProjectileController>();
            ProjectileController[] controllers = _projectileObject.GetComponentsInChildren<ProjectileController>(true);
            for (int i = 0; i < controllers.Length; i++)
            {
                if (controllers[i].IsActive) { active.Add(controllers[i]); }
            }
            return active;
        }

        [TestCase(0)]
        [TestCase(-3)]
        public void ProjectileSpread_NonPositiveCount_DoesNotSpawn(int count)
        {
            _projectiles.SpawnSpread(_owner, Magic(), Vector2.zero, Vector2.right, count, 30f);

            Assert.AreEqual(0, _projectiles.ActiveCount);
            Assert.AreEqual(0, CorePool.ActiveCount);
        }

        [Test]
        public void ProjectileChildren_ZeroSpread_SpawnsAllRequestedChildren()
        {
            ProjectileController parent = _projectiles.SpawnProjectile(_owner,
                Magic(ChildBulletTrigger.OnActivate, 3), Vector2.zero, Vector2.right);

            Assert.IsNotNull(parent);
            List<ProjectileController> active = ActiveControllers();
            Assert.AreEqual(4, active.Count);
            Assert.AreEqual(4, CorePool.ActiveCount);
            for (int i = 0; i < active.Count; i++)
            {
                if (active[i] == parent) { continue; }
                Assert.AreEqual(new Vector2(2f, 0f), active[i].CoreProjectile.Velocity);
                Assert.IsNull(active[i].Magic.childBullet);
                Assert.AreEqual(parent.Magic.attackContext.Owner, active[i].Magic.attackContext.Owner);
            }
        }

        [Test]
        public void ProjectileReturn_LiveProjectile_ReleasesBothPoolsBeforeReuse()
        {
            ProjectileController first = _projectiles.SpawnProjectile(_owner, Magic(), Vector2.zero, Vector2.right);
            Projectile core = first.CoreProjectile;
            ulong lease = core.LeaseId;

            _projectiles.ReturnProjectile(first);
            _projectiles.SimulationTick(0f);

            Assert.AreEqual(0, _projectiles.ActiveCount);
            Assert.AreEqual(0, CorePool.ActiveCount);
            Assert.IsFalse(CorePool.IsActive(core, lease));
            ProjectileController second = _projectiles.SpawnProjectile(_owner, Magic(), Vector2.zero, Vector2.right);
            Assert.AreSame(first, second);
            Assert.AreNotEqual(lease, second.CoreProjectile.LeaseId);
            Assert.AreEqual(1, CorePool.ActiveCount);
            Assert.AreEqual(1, _projectiles.ActiveCount);
        }

        [Test]
        public void ProjectileReturn_DuplicateRequests_RecyclesEachLeaseOnce()
        {
            ProjectileController first = _projectiles.SpawnProjectile(_owner, Magic(), Vector2.zero, Vector2.right);
            _projectiles.ReturnProjectile(first);
            _projectiles.ReturnProjectile(first);
            _projectiles.SimulationTick(0f);

            ProjectileController second = _projectiles.SpawnProjectile(_owner, Magic(), Vector2.zero, Vector2.right);
            ProjectileController third = _projectiles.SpawnProjectile(_owner, Magic(), Vector2.zero, Vector2.right);

            Assert.AreNotSame(second, third);
            Assert.AreNotSame(second.CoreProjectile, third.CoreProjectile);
            Assert.AreEqual(2, _projectiles.ActiveCount);
            Assert.AreEqual(2, CorePool.ActiveCount);
        }

        [Test]
        public void ProjectileReturn_TwoDeadParentsWithDestroyChildren_DoesNotReturnReusedChildLease()
        {
            MagicDefinition magic = Magic(ChildBulletTrigger.OnDestroy);
            ProjectileController first = _projectiles.SpawnProjectile(_owner, magic, Vector2.zero, Vector2.right);
            ProjectileController second = _projectiles.SpawnProjectile(_owner, magic, Vector2.up, Vector2.right);
            first.CoreProjectile.Kill();
            second.CoreProjectile.Kill();
            _projectiles.ReturnProjectile(first);
            _projectiles.ReturnProjectile(second);

            _projectiles.SimulationTick(0f);

            List<ProjectileController> active = ActiveControllers();
            Assert.AreEqual(2, active.Count);
            Assert.AreEqual(2, _projectiles.ActiveCount);
            Assert.AreEqual(2, CorePool.ActiveCount);
            for (int i = 0; i < active.Count; i++)
            {
                Assert.IsTrue(active[i].CoreProjectile.IsAlive);
                Assert.IsNull(active[i].Magic.childBullet);
            }
        }

        [Test]
        public void ProjectileTimer_NewChild_StartsSimulationOnFollowingTick()
        {
            ProjectileController parent = _projectiles.SpawnProjectile(_owner,
                Magic(ChildBulletTrigger.OnTimer), Vector2.zero, Vector2.right);

            _projectiles.SimulationTick(0.1f);

            List<ProjectileController> active = ActiveControllers();
            Assert.AreEqual(2, active.Count);
            Projectile child = active[0] == parent ? active[1].CoreProjectile : active[0].CoreProjectile;
            Assert.AreEqual(0f, child.ElapsedTime);
            Assert.AreEqual(Vector2.zero, child.Position);
            Assert.AreEqual(new Vector2(2f, 0f), child.Velocity);

            _projectiles.SimulationTick(0.05f);

            Assert.AreEqual(0.05f, child.ElapsedTime, 0.00001f);
            Assert.Greater(child.Position.x, 0f);
            Assert.Less(child.Velocity.y, 0f);
        }

        [Test]
        public void ProjectileClear_DestroyChildrenConfigured_DoesNotEmitDuringSceneCleanup()
        {
            _projectiles.SpawnProjectile(_owner, Magic(ChildBulletTrigger.OnDestroy, 3), Vector2.zero, Vector2.right);

            _projectiles.ClearAll();
            _projectiles.SimulationTick(0.1f);

            Assert.AreEqual(0, _projectiles.ActiveCount);
            Assert.AreEqual(0, CorePool.ActiveCount);
            Assert.IsEmpty(ActiveControllers());
        }

        [Test]
        public void ProjectilePool_AlternatingPrefabs_ReusesOnlyMatchingVisuals()
        {
            GameObject redPrefab = new GameObject("RedProjectileTemplate");
            redPrefab.transform.SetParent(_managerObject.transform, false);
            redPrefab.AddComponent<SpriteRenderer>().color = Color.red;
            GameObject bluePrefab = new GameObject("BlueProjectileTemplate");
            bluePrefab.transform.SetParent(_managerObject.transform, false);
            bluePrefab.AddComponent<SpriteRenderer>().color = Color.blue;
            MagicDefinition redMagic = Magic();
            redMagic.projectilePrefab = redPrefab;
            MagicDefinition blueMagic = Magic();
            blueMagic.projectilePrefab = bluePrefab;

            ProjectileController red = _projectiles.SpawnProjectile(_owner, redMagic, Vector2.zero, Vector2.right);
            _projectiles.ReturnProjectile(red);
            _projectiles.SimulationTick(0f);
            ProjectileController blue = _projectiles.SpawnProjectile(_owner, blueMagic, Vector2.zero, Vector2.right);

            Assert.AreNotSame(red, blue);
            Assert.AreEqual(Color.blue, blue.GetComponentInChildren<SpriteRenderer>().color);
            _projectiles.ReturnProjectile(blue);
            _projectiles.SimulationTick(0f);
            ProjectileController redAgain = _projectiles.SpawnProjectile(_owner, redMagic, Vector2.zero, Vector2.right);
            Assert.AreSame(red, redAgain);
            Assert.AreEqual(Color.red, redAgain.GetComponentInChildren<SpriteRenderer>().color);
            Assert.AreEqual(1, _projectiles.ActiveCount);
            Assert.AreEqual(1, CorePool.ActiveCount);
        }

        [Test]
        public void ProjectileClear_ControllerDestroyedExternally_ReleasesCoreWithoutRepoolingDestroyedObject()
        {
            ProjectileController projectile = _projectiles.SpawnProjectile(_owner, Magic(), Vector2.zero, Vector2.right);
            Projectile core = projectile.CoreProjectile;
            ulong lease = core.LeaseId;
            Object.DestroyImmediate(projectile.gameObject);
            Assert.IsTrue(projectile == null, "The managed lease remains while Unity reports a destroyed object.");

            Assert.DoesNotThrow(() => _projectiles.ClearAll());

            Assert.AreEqual(0, _projectiles.ActiveCount);
            Assert.AreEqual(0, CorePool.ActiveCount);
            Assert.IsFalse(CorePool.IsActive(core, lease));
            ProjectileController replacement = _projectiles.SpawnProjectile(_owner, Magic(), Vector2.zero, Vector2.right);
            Assert.IsTrue(replacement != null);
            Assert.IsTrue(replacement.IsActive);
            Assert.AreEqual(1, _projectiles.ActiveCount);
            Assert.AreEqual(1, CorePool.ActiveCount);
        }

        [Test]
        public void ProjectileChildren_InputConfigMutatedAfterSpawn_KeepsCapturedTimerCountAndProfile()
        {
            MagicDefinition magic = Magic(ChildBulletTrigger.OnTimer, 2);
            ChildBulletConfig sourceConfig = magic.childBullet;
            ProjectileController parent = _projectiles.SpawnProjectile(_owner, magic, Vector2.zero, Vector2.right);
            sourceConfig.trigger = ChildBulletTrigger.OnDestroy;
            sourceConfig.count = 7;
            sourceConfig.profile = new BulletProfile { speed = 999f, lifeTime = 1f };

            _projectiles.SimulationTick(0.1f);

            List<ProjectileController> active = ActiveControllers();
            Assert.AreEqual(3, active.Count);
            Assert.AreEqual(3, CorePool.ActiveCount);
            for (int i = 0; i < active.Count; i++)
            {
                if (active[i] == parent) { continue; }
                Projectile child = active[i].CoreProjectile;
                Assert.AreEqual(2f, child.Profile.speed);
                Assert.AreEqual(5f, child.Profile.lifeTime);
                Assert.AreEqual(BulletFeature.Gravity, child.Profile.features);
                Assert.AreEqual(0f, child.ElapsedTime);
                Assert.AreEqual(new Vector2(2f, 0f), child.Velocity);
            }
        }

        [Test]
        public void ProjectileChildren_PublicInspectionMutated_KeepsCapturedTimerCountAndProfile()
        {
            ProjectileController parent = _projectiles.SpawnProjectile(_owner,
                Magic(ChildBulletTrigger.OnTimer, 2), Vector2.zero, Vector2.right);
            MagicDefinition inspection = parent.Magic;
            inspection.childBullet.trigger = ChildBulletTrigger.OnDestroy;
            inspection.childBullet.count = 7;
            inspection.childBullet.profile = new BulletProfile { speed = 999f, lifeTime = 1f };

            _projectiles.SimulationTick(0.1f);

            List<ProjectileController> active = ActiveControllers();
            Assert.AreEqual(3, active.Count);
            Assert.AreEqual(3, CorePool.ActiveCount);
            Assert.AreEqual(ChildBulletTrigger.OnTimer, parent.Magic.childBullet.trigger);
            Assert.AreEqual(2, parent.Magic.childBullet.count);
            for (int i = 0; i < active.Count; i++)
            {
                if (active[i] == parent) { continue; }
                Assert.AreEqual(2f, active[i].CoreProjectile.Profile.speed);
                Assert.AreEqual(5f, active[i].CoreProjectile.Profile.lifeTime);
                Assert.AreEqual(0f, active[i].CoreProjectile.ElapsedTime);
            }
        }

        [Test]
        public void ProjectileExplosion_ReceiverStartsLargerNestedExplosion_PreservesBothTargetSnapshots()
        {
            ProjectileExplosionTestCharacter first = CreateExplosionTarget(new Vector2(0.5f, 0f));
            ProjectileExplosionTestCharacter second = CreateExplosionTarget(new Vector2(3f, 0f));
            ProjectileExplosionTestCharacter third = CreateExplosionTarget(new Vector2(5f, 0f));
            MagicDefinition outerMagic = Magic();
            outerMagic.bulletProfile.explodeRadius = 1f;
            MagicDefinition innerMagic = Magic();
            innerMagic.bulletProfile.explodeRadius = 10f;
            ProjectileController outer = _projectiles.SpawnProjectile(_owner, outerMagic, Vector2.zero, Vector2.right);
            ProjectileController inner = _projectiles.SpawnProjectile(_owner, innerMagic, Vector2.zero, Vector2.right);
            bool emittedNestedExplosion = false;
            first.OnHit = () =>
            {
                if (emittedNestedExplosion) { return; }
                emittedNestedExplosion = true;
                _projectiles.ProcessExplosion(inner.CoreProjectile, inner.Magic);
            };

            Assert.DoesNotThrow(() => _projectiles.ProcessExplosion(outer.CoreProjectile, outer.Magic));

            Assert.IsTrue(emittedNestedExplosion);
            Assert.AreEqual(2, first.Hits, "The first receiver belongs to both explosion snapshots.");
            Assert.AreEqual(1, second.Hits, "The second receiver belongs only to the inner explosion.");
            Assert.AreEqual(1, third.Hits, "The third receiver belongs only to the inner explosion.");
            Assert.AreEqual(2, _projectiles.ActiveCount);
            Assert.AreEqual(2, CorePool.ActiveCount);
        }

        private ProjectileExplosionTestCharacter CreateExplosionTarget(Vector2 position)
        {
            GameObject actor = new GameObject("ExplosionTarget");
            actor.transform.SetParent(_managerObject.transform, false);
            actor.transform.position = position;
            ProjectileExplosionTestCharacter receiver = actor.AddComponent<ProjectileExplosionTestCharacter>();
            _world.RegisterCharacter(receiver.ObjectHash,
                new CharacterVitals { currentHp = 100, maxHp = 100, position = position },
                default,
                CharacterFlags.Pack(CharacterBelong.Enemy, CharacterFeature.Minion, ActState.Neutral, AbilityFlag.None),
                default, receiver);
            return receiver;
        }
    }

    public sealed class ProjectileExplosionTestCharacter : ManagedCharacter, IDamageable
    {
        public int Hits { get; private set; }
        public System.Action OnHit { get; set; }
        public override int ObjectHash => gameObject.GetHashCode();
        public override IDamageable Damageable => this;
        public bool IsAlive => true;

        public DamageResult ReceiveDamage(DamageData data)
        {
            Hits++;
            OnHit?.Invoke();
            return default;
        }
    }
}
