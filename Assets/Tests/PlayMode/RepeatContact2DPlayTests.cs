using System;
using System.Collections;
using System.Collections.Generic;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using CharacterInfo = Game.Core.CharacterInfo;
using Object = UnityEngine.Object;

namespace Game.Tests.PlayMode
{
    public sealed class RepeatContact2DPlayTests
    {
        private const int k_MaxFixedSteps = 30;
        private readonly List<CharacterInfo> _assets = new List<CharacterInfo>();
        private Scene _room;
        private PhysicsScene2D _world;

        [UnitySetUp]
        public IEnumerator CreateRoom()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            TestSceneHelper.CreateGameManager();
            _room = SceneManager.CreateScene(nameof(RepeatContact2DPlayTests),
                new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            _world = _room.GetPhysicsScene2D();
        }

        [UnityTearDown]
        public IEnumerator UnloadRoom()
        {
            if (_room.isLoaded) { yield return SceneManager.UnloadSceneAsync(_room); }
            foreach (CharacterInfo asset in _assets) { Object.Destroy(asset); }
            _assets.Clear();
            TestSceneHelper.Cleanup();
            yield return null;
        }

        [UnityTest]
        public IEnumerator HitBox_OverlappingAndReentering_RespectsRepeatIntervalAndStopsOnDeactivate()
            => VerifyRepeatContact<HitBox>();

        [UnityTest]
        public IEnumerator DamageDealer_OverlappingAndReentering_RespectsRepeatIntervalAndStopsOnDeactivate()
            => VerifyRepeatContact<DamageDealer>();

        [UnityTest]
        public IEnumerator HitBox_WithoutRepeatConfiguration_ReentryCannotHitSameActorAgain()
        {
            BaseCharacter owner = CreateActor(CharacterBelong.Ally, Vector2.left * 6f);
            BaseCharacter target = CreateActor(CharacterBelong.Enemy, Vector2.zero);
            HitBox hitBox = CreateHitComponent<HitBox>();
            yield return null;

            // The spare distinct-target slot leaves the hitbox active through re-entry.
            AttackMotionData motion = new AttackMotionData
            {
                attackElement = Element.Slash, motionValue = 1f, maxHitCount = 2,
            };
            hitBox.Activate(motion, owner.ObjectHash);
            yield return AdvancePhysics(2);
            Assert.AreEqual(1, hitBox.HitCount);
            Assert.IsTrue(hitBox.IsActive);

            target.Rigidbody.position = Vector2.right * 6f;
            yield return AdvancePhysics(2);
            target.Rigidbody.position = Vector2.zero;
            yield return AdvancePhysics(12);
            Assert.AreEqual(1, hitBox.HitCount);
            Assert.IsTrue(hitBox.IsActive);
        }

        private IEnumerator VerifyRepeatContact<T>() where T : MonoBehaviour
        {
            BaseCharacter owner = CreateActor(CharacterBelong.Ally, Vector2.left * 6f);
            BaseCharacter target = CreateActor(CharacterBelong.Enemy, Vector2.zero);
            T component = CreateHitComponent<T>();
            yield return null;

            float interval = Time.fixedDeltaTime * 8f;
            AttackMotionData motion = new AttackMotionData
            {
                attackElement = Element.Slash, motionValue = 1f, maxHitCount = 1,
                repeatHits = new HitRepeatSettings { maxHitsPerTarget = 2, interval = interval },
            };
            List<double> hitTimes = new List<double>();
            Action<DamageResult, int, int> record = (_, source, defender) =>
            {
                if (source == owner.ObjectHash && defender == target.ObjectHash) { hitTimes.Add(Time.fixedTimeAsDouble); }
            };
            GameManager.Events.OnDamageDealtEvent += record;
            try
            {
                Activate(component, motion, owner.ObjectHash);
                yield return AdvanceUntilHits(hitTimes, 1);
                Assert.AreEqual(1, hitTimes.Count);

                target.Rigidbody.position = Vector2.right * 6f;
                yield return AdvancePhysics(1);
                target.Rigidbody.position = Vector2.zero;
                yield return AdvancePhysics(2);
                Assert.AreEqual(1, hitTimes.Count, "Re-entry does not clear the target's repeat interval.");

                yield return AdvanceUntilHits(hitTimes, 2);
                Assert.AreEqual(2, hitTimes.Count);
                Assert.That(hitTimes[1] - hitTimes[0], Is.GreaterThanOrEqualTo(interval - 0.000001f));
                yield return AdvancePhysics(12);
                Assert.AreEqual(2, hitTimes.Count, "The active overlap finishes at the per-target limit.");

                Deactivate(component);
                yield return AdvancePhysics(1);
                Activate(component, motion, owner.ObjectHash);
                yield return AdvanceUntilHits(hitTimes, 3);
                Assert.AreEqual(3, hitTimes.Count, "A new activation has its own target bookkeeping.");
                Deactivate(component);
                yield return AdvancePhysics(12);
                Assert.AreEqual(3, hitTimes.Count, "An interrupted activation cannot produce its scheduled repeat.");

                Activate(component, motion, owner.ObjectHash);
                yield return AdvanceUntilHits(hitTimes, 4);
                Assert.AreEqual(4, hitTimes.Count);
                component.enabled = false;
                yield return AdvancePhysics(12);
                component.enabled = true;
                yield return AdvancePhysics(2);
                Assert.AreEqual(4, hitTimes.Count, "Disabling a component clears its repeated hit window.");
            }
            finally
            {
                GameManager.Events.OnDamageDealtEvent -= record;
            }
        }

        private IEnumerator AdvanceUntilHits(List<double> hits, int expected)
        {
            for (int i = 0; i < k_MaxFixedSteps && hits.Count < expected; i++)
            {
                yield return AdvancePhysics(1);
            }
        }

        private IEnumerator AdvancePhysics(int steps)
        {
            for (int i = 0; i < steps; i++)
            {
                yield return new WaitForFixedUpdate();
                Physics2D.SyncTransforms();
                _world.Simulate(Time.fixedDeltaTime);
            }
        }

        private BaseCharacter CreateActor(CharacterBelong belong, Vector2 position)
        {
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(belong, maxHp: 1000);
            info.name = belong.ToString();
            _assets.Add(info);
            GameObject actor = TestSceneHelper.CreateBaseCharacterObjectWithDamageReceiver(info, position);
            SceneManager.MoveGameObjectToScene(actor, _room);
            BaseCharacter character = actor.GetComponent<BaseCharacter>();
            character.Rigidbody.gravityScale = 0f;
            return character;
        }

        private T CreateHitComponent<T>() where T : Component
        {
            GameObject hit = new GameObject(typeof(T).Name);
            SceneManager.MoveGameObjectToScene(hit, _room);
            hit.AddComponent<BoxCollider2D>().size = new Vector2(2f, 2f);
            return hit.AddComponent<T>();
        }

        private static void Activate(Component hit, in AttackMotionData motion, int ownerHash)
        {
            if (hit is HitBox hitBox) { hitBox.Activate(motion, ownerHash); }
            else { ((DamageDealer)hit).Activate(motion, ownerHash); }
        }

        private static void Deactivate(Component hit)
        {
            if (hit is HitBox hitBox) { hitBox.Deactivate(); }
            else { ((DamageDealer)hit).Deactivate(); }
        }
    }
}
