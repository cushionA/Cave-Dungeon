using System.Collections;
using System.Collections.Generic;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using CharacterInfo = Game.Core.CharacterInfo;

namespace Game.Tests.PlayMode
{
    public sealed class CharacterActor3DPlayTests
    {
        private readonly List<Object> _created = new List<Object>();
        private ProjectileManager _projectiles;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            GameObject world = new GameObject("Mixed Physics World");
            world.SetActive(false);
            _projectiles = world.AddComponent<ProjectileManager>();
            world.AddComponent<GameManager>();
            world.SetActive(true);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _projectiles?.ClearAll();
            foreach (Object item in _created)
            {
                if (item != null) { Object.Destroy(item); }
            }
            _created.Clear();
            yield return null;
            TestSceneHelper.Cleanup();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Registration_DisableAndReenable_RebindsGenerationAndSharedStats()
        {
            int legacyPlayer = CharacterRegistry.PlayerHash;
            CharacterActor3D actor = CreateActor3D(CharacterBelong.Ally, CharacterFeature.Player);
            ActorRef first = actor.Actor;
            Assert.IsTrue(first.IsValid);
            Assert.IsTrue(actor.IsAlive);
            Assert.AreSame(actor, GameManager.Data.GetManaged(first.Hash));
            Assert.AreSame(actor.GetComponent<DamageReceiver>(), actor.Damageable);
            Assert.AreSame(actor.GetComponent<CharacterMotor3D>(), actor.Motor);
            Assert.AreSame(actor.GetComponent<CharacterController>(), actor.Body);
            Assert.IsTrue(GameManager.Data.Uses3DPhysics(first.Hash));
            Assert.AreEqual(CharacterBelong.Ally, GameManager.Data.GetFlags(first.Hash).Belong);
            Assert.AreEqual(legacyPlayer, CharacterRegistry.PlayerHash);
            Assert.IsFalse(CharacterRegistry.AllHashes.Contains(first.Hash));

            GameManager.Data.GetVitals(first.Hash).currentHp = 20;
            actor.enabled = false;
            Assert.IsFalse(actor.Actor.IsValid);
            Assert.IsFalse(actor.IsAlive);
            Assert.IsFalse(GameManager.Actors.IsCurrent(first));
            Assert.IsFalse(GameManager.IsCharacterValid(first.Hash));

            actor.enabled = true;
            Assert.AreEqual(first.Hash, actor.Actor.Hash);
            Assert.AreNotEqual(first, actor.Actor);
            GameManager.Instance.UnregisterCharacter(actor, first);
            Assert.IsTrue(GameManager.Actors.IsCurrent(actor.Actor));
            Assert.AreEqual(100, GameManager.Data.GetVitals(actor.Actor.Hash).currentHp);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Registered3DActor_IsExcludedFromLegacyObservationAndProjectiles()
        {
            BaseCharacter caster = CreateActor2D(CharacterBelong.Ally, new Vector2(-6f, 0f));
            BaseCharacter nearby2D = CreateActor2D(CharacterBelong.Enemy, Vector2.zero);
            CharacterActor3D nearby3D = CreateActor3D(CharacterBelong.Enemy, CharacterFeature.Minion);
            yield return null;
            GameManager.Instance.SimulationTick(0f, Time.time);

            Assert.IsTrue(GameManager.Actors.TryGet(caster.ObjectHash, out ActorRef source));
            Assert.IsTrue(GameManager.Actors.TryGet(nearby2D.ObjectHash, out ActorRef other2D));
            Assert.IsTrue(GameManager.Observation.Contains(other2D));
            Assert.IsFalse(GameManager.Observation.Contains(nearby3D.Actor));
            Assert.IsFalse(GameManager.TargetRelations.SetTarget(source, nearby3D.Actor));
            Assert.IsFalse(GameManager.TryGetTargetOffset(caster.ObjectHash, nearby3D.ObjectHash, out _));

            MagicDefinition magic = new MagicDefinition
            {
                magicType = MagicType.Attack,
                attackElement = Element.Slash,
                motionValue = 1f,
                bulletProfile = new BulletProfile
                {
                    moveType = BulletMoveType.Homing,
                    lifeTime = 5f,
                    hitLimit = 4,
                    perTargetHitLimit = 1,
                    explodeRadius = 1.5f
                }
            };
            Assert.IsNull(_projectiles.SpawnProjectile(nearby3D.Actor, magic, Vector2.zero, Vector2.right));
            ProjectileController shot = _projectiles.SpawnProjectile(source, magic, Vector2.zero, Vector2.right, nearby3D.Actor);
            Assert.IsNotNull(shot);
            Assert.IsFalse(shot.CoreProjectile.TargetRef.IsValid);
            Assert.IsFalse(shot.CoreProjectile.ExplicitTargetRef.IsValid);
            _projectiles.ProcessExplosion(shot.CoreProjectile, shot.CapturedMagic);
            Assert.That(GameManager.Data.GetVitals(other2D.Hash).currentHp, Is.LessThan(100));
            Assert.AreEqual(100, GameManager.Data.GetVitals(nearby3D.Actor.Hash).currentHp);
        }

        private CharacterActor3D CreateActor3D(CharacterBelong belong, CharacterFeature feature)
        {
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(belong, feature);
            _created.Add(info);
            GameObject actorObject = new GameObject("3D actor");
            _created.Add(actorObject);
            actorObject.SetActive(false);
            CharacterActor3D actor = actorObject.AddComponent<CharacterActor3D>();
            actor.SetCharacterInfoForTest(info);
            actorObject.SetActive(true);
            return actor;
        }

        private BaseCharacter CreateActor2D(CharacterBelong belong, Vector2 position)
        {
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(belong, CharacterFeature.Minion);
            info.name = $"2D actor {_created.Count}";
            _created.Add(info);
            GameObject actorObject = TestSceneHelper.CreateBaseCharacterObjectWithDamageReceiver(info, position);
            _created.Add(actorObject);
            BaseCharacter actor = actorObject.GetComponent<BaseCharacter>();
            actor.Rigidbody.gravityScale = 0f;
            return actor;
        }
    }
}
