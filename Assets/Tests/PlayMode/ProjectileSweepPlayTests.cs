using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using CharacterInfo = Game.Core.CharacterInfo;
using Object = UnityEngine.Object;

namespace Game.Tests.PlayMode
{
    public class ProjectileSweepPlayTests
    {
        private readonly List<GameObject> _actors = new List<GameObject>();
        private readonly List<CharacterInfo> _infos = new List<CharacterInfo>();
        private ProjectileManager _projectiles;
        private BaseCharacter _owner;
        private BaseCharacter _target;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            GameObject world = new GameObject("ProjectileSweepWorld");
            world.SetActive(false);
            GameObject projectileObject = new GameObject("Projectiles");
            projectileObject.transform.SetParent(world.transform, false);
            _projectiles = projectileObject.AddComponent<ProjectileManager>();
            typeof(ProjectileManager).GetField("_preWarmCount", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(_projectiles, 0);
            world.AddComponent<GameManager>();
            world.SetActive(true);
            GameManager.Instance.enabled = false;
            _owner = CreateActor(CharacterBelong.Ally, new Vector2(-10f, 0f));
            _target = CreateActor(CharacterBelong.Enemy, new Vector2(3f, 0f));
            _target.GetComponent<BoxCollider2D>().size = new Vector2(0.05f, 1f);
            yield return null;
            Physics2D.SyncTransforms();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_projectiles != null) { _projectiles.ClearAll(); }
            for (int i = _actors.Count - 1; i >= 0; i--)
            {
                if (_actors[i] != null) { Object.Destroy(_actors[i]); }
            }
            _actors.Clear();
            yield return null;
            for (int i = _infos.Count - 1; i >= 0; i--)
            {
                if (_infos[i] != null) { Object.Destroy(_infos[i]); }
            }
            _infos.Clear();
            TestSceneHelper.Cleanup();
            yield return null;
        }

        private BaseCharacter CreateActor(CharacterBelong belong, Vector2 position)
        {
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(belong, maxHp: 1000);
            info.name = "ProjectileSweepActor" + _infos.Count;
            _infos.Add(info);
            GameObject actor = TestSceneHelper.CreateBaseCharacterObjectWithDamageReceiver(info, position);
            _actors.Add(actor);
            BaseCharacter character = actor.GetComponent<BaseCharacter>();
            character.Rigidbody.gravityScale = 0f;
            return character;
        }

        private ProjectileController Spawn(float speed, int hitLimit = 1, BulletFeature features = BulletFeature.None,
            int perTargetHitLimit = 1, float lifeTime = 5f)
        {
            return _projectiles.SpawnProjectile(_owner.ObjectHash, new MagicDefinition
            {
                magicType = MagicType.Attack,
                motionValue = 1f,
                attackElement = Element.Slash,
                bulletProfile = new BulletProfile
                {
                    moveType = BulletMoveType.Straight,
                    speed = speed,
                    lifeTime = lifeTime,
                    hitLimit = hitLimit,
                    perTargetHitLimit = perTargetHitLimit,
                    features = features,
                },
            }, Vector2.zero, Vector2.right);
        }

        [UnityTest]
        public IEnumerator ProjectileSweep_FastProjectileCrossesThinTarget_DealsDamageAndReleasesProjectile()
        {
            ProjectileController projectile = Spawn(1000f);
            Assert.IsNotNull(projectile);

            _projectiles.SimulationTick(0.01f);
            yield return null;

            Assert.Less(GameManager.Data.GetVitals(_target.ObjectHash).currentHp, 1000);
            Assert.GreaterOrEqual(GameManager.Data.GetVitals(_target.ObjectHash).currentHp, 0);
            Assert.AreEqual(0, _projectiles.ActiveCount);
        }

        [UnityTest]
        public IEnumerator ProjectileSweep_EndPositionAlsoTriggers_DoesNotHitSameTargetTwice()
        {
            ProjectileController projectile = Spawn(300f, 3, BulletFeature.Pierce);
            _projectiles.SimulationTick(0.01f);
            int hpAfterSweep = GameManager.Data.GetVitals(_target.ObjectHash).currentHp;
            Assert.Less(hpAfterSweep, 1000);
            Assert.IsTrue(projectile.IsActive);

            yield return new WaitForFixedUpdate();
            yield return null;

            Assert.AreEqual(hpAfterSweep, GameManager.Data.GetVitals(_target.ObjectHash).currentHp);
            Assert.AreEqual(2, projectile.CoreProjectile.RemainingHits);
        }

        [UnityTest]
        public IEnumerator ProjectileSweep_LifetimeEndsDuringStep_StillHitsAlongFinalSegment()
        {
            Spawn(1000f, lifeTime: 0.005f);

            _projectiles.SimulationTick(0.02f);
            yield return null;

            Assert.Less(GameManager.Data.GetVitals(_target.ObjectHash).currentHp, 1000);
            Assert.AreEqual(0, _projectiles.ActiveCount);
        }

        [UnityTest]
        public IEnumerator ProjectileSweep_RemainsInSameContact_DoesNotConsumeAdditionalPerTargetHits()
        {
            ProjectileController projectile = Spawn(300f, 10, BulletFeature.Pierce, perTargetHitLimit: 3);
            _projectiles.SimulationTick(0.01f);
            int hpAfterFirstContact = GameManager.Data.GetVitals(_target.ObjectHash).currentHp;
            Assert.Less(hpAfterFirstContact, 1000);
            projectile.CoreProjectile.Velocity = new Vector2(0.1f, 0f);

            yield return new WaitForFixedUpdate();
            yield return null;
            _projectiles.SimulationTick(0.01f);
            yield return new WaitForFixedUpdate();
            yield return null;
            _projectiles.SimulationTick(0.01f);
            yield return new WaitForFixedUpdate();
            yield return null;

            Assert.AreEqual(hpAfterFirstContact, GameManager.Data.GetVitals(_target.ObjectHash).currentHp);
            Assert.AreEqual(9, projectile.CoreProjectile.RemainingHits);
        }

        [UnityTest]
        public IEnumerator ProjectileSweep_DamageCallbackClearsAndReusesController_DoesNotKillReplacementLease()
        {
            ProjectileController original = Spawn(1000f);
            ProjectileController replacement = null;
            ulong originalLease = original.CoreProjectile.LeaseId;
            Action<DamageResult, int, int> callback = (result, attacker, defender) =>
            {
                if (defender != _target.ObjectHash || replacement != null) { return; }
                _projectiles.ClearAll();
                replacement = Spawn(0f);
            };
            GameManager.Events.OnDamageDealtEvent += callback;
            try
            {
                _projectiles.SimulationTick(0.01f);
                yield return null;

                Assert.IsNotNull(replacement);
                Assert.AreSame(original, replacement);
                Assert.AreNotEqual(originalLease, replacement.CoreProjectile.LeaseId);
                Assert.IsTrue(replacement.CoreProjectile.IsAlive);
                Assert.AreEqual(1, _projectiles.ActiveCount);
            }
            finally
            {
                GameManager.Events.OnDamageDealtEvent -= callback;
            }
        }
    }
}
