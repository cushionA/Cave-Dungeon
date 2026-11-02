using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using R3;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Game.Tests.PlayMode
{
    public class CombatTargetPolicyPlayTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();
        private readonly List<ScriptableObject> _assets = new List<ScriptableObject>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            TestSceneHelper.CreateGameManager();
            GameManager.Instance.enabled = false;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            for (int i = _objects.Count - 1; i >= 0; i--)
            {
                if (_objects[i] != null) { Object.Destroy(_objects[i]); }
            }
            _objects.Clear();
            yield return null;
            for (int i = _assets.Count - 1; i >= 0; i--)
            {
                if (_assets[i] != null) { Object.Destroy(_assets[i]); }
            }
            _assets.Clear();
            TestSceneHelper.Cleanup();
            yield return null;
        }

        private BaseCharacter Actor(CharacterBelong belong)
        {
            Game.Core.CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(belong, maxHp: 1000);
            info.baseAttack = new ElementalStatus { slash = 100 };
            _assets.Add(info);
            GameObject actor = TestSceneHelper.CreateBaseCharacterObjectWithDamageReceiver(info,
                new Vector3(_objects.Count * 10f, 5f, 0f));
            _objects.Add(actor);
            BaseCharacter character = actor.GetComponent<BaseCharacter>();
            character.Rigidbody.gravityScale = 0f;
            return character;
        }

        private T HitComponent<T>() where T : Component
        {
            GameObject hit = new GameObject(typeof(T).Name);
            hit.transform.position = new Vector3(100f, 100f, 0f);
            _objects.Add(hit);
            return hit.AddComponent<T>();
        }

        private static AttackMotionData Motion(AttackTargetPolicy policy = default)
        {
            return new AttackMotionData
            {
                motionValue = 1f, attackElement = Element.Slash, maxHitCount = 1, targetPolicy = policy,
            };
        }

        private static AttackTargetPolicy Policy(AttackTargetRelation targets)
            => new AttackTargetPolicy { overrideTargets = true, targets = targets };

        private static void Contact(Component hit, BaseCharacter target)
        {
            MethodInfo trigger = hit.GetType().GetMethod("OnTriggerEnter2D", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(trigger);
            trigger.Invoke(hit, new object[] { target.GetComponent<BoxCollider2D>() });
        }

        private void RegisterReplacement(BaseCharacter character, CharacterBelong belong)
        {
            Game.Core.CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(belong, maxHp: 1000);
            _assets.Add(info);
            GameManager.Instance.UnregisterCharacter(character.ObjectHash);
            GameManager.Instance.RegisterCharacter(character, info);
        }

        private static void PrepareGuard(BaseCharacter defender)
        {
            GameManager.Data.GetDefenseState(defender.ObjectHash).isGuarding = true;
            GameManager.Data.GetDefenseState(defender.ObjectHash).guardElapsedTime = 1f;
        }

        private static DamageData GuardedAttack(BaseCharacter owner, BaseCharacter defender)
            => new DamageData
            {
                attackerHash = owner.ObjectHash, defenderHash = defender.ObjectHash,
                damage = new ElementalStatus { slash = 10 }, motionValue = 1f,
                feature = AttackFeature.JustGuardImmune,
            };

        [UnityTest]
        public IEnumerator GuardNotification_DefenderReplacedDuringDamage_DoesNotNotifyReplacement()
        {
            BaseCharacter owner = Actor(CharacterBelong.Ally);
            BaseCharacter defender = Actor(CharacterBelong.Enemy);
            yield return null;
            PrepareGuard(defender);
            Assert.IsTrue(GameManager.Actors.TryGet(defender.ObjectHash, out ActorRef original));
            int guards = 0;
            using IDisposable guardSubscription = GameManager.Events.OnGuardEvent.Subscribe(_ => guards++);
            Action<DamageResult, int, int> replace = (result, attacker, target) =>
            {
                if (target == defender.ObjectHash) { RegisterReplacement(defender, CharacterBelong.Enemy); }
            };
            GameManager.Events.OnDamageDealtEvent += replace;
            try
            {
                DamageResult result = defender.DamageReceiver.ReceiveDamage(GuardedAttack(owner, defender));
                Assert.AreNotEqual(GuardResult.NoGuard, result.guardResult);
                Assert.IsFalse(GameManager.Actors.IsCurrent(original));
                Assert.AreEqual(0, guards);
            }
            finally { GameManager.Events.OnDamageDealtEvent -= replace; }
        }

        [UnityTest]
        public IEnumerator GuardNotification_AttackerReplacedDuringDamage_DoesNotAttributeReplacement()
        {
            BaseCharacter owner = Actor(CharacterBelong.Ally);
            BaseCharacter defender = Actor(CharacterBelong.Enemy);
            yield return null;
            PrepareGuard(defender);
            Assert.IsTrue(GameManager.Actors.TryGet(owner.ObjectHash, out ActorRef original));
            int guards = 0;
            int observedAttacker = -1;
            using IDisposable guardSubscription = GameManager.Events.OnGuardEvent.Subscribe(notification =>
            {
                guards++;
                observedAttacker = notification.attackerHash;
            });
            Action<DamageResult, int, int> replace = (result, attacker, target) =>
            {
                if (target == defender.ObjectHash) { RegisterReplacement(owner, CharacterBelong.Ally); }
            };
            GameManager.Events.OnDamageDealtEvent += replace;
            try
            {
                DamageResult result = defender.DamageReceiver.ReceiveDamage(GuardedAttack(owner, defender));
                Assert.AreNotEqual(GuardResult.NoGuard, result.guardResult);
                Assert.IsFalse(GameManager.Actors.IsCurrent(original));
                Assert.AreEqual(1, guards);
                Assert.AreEqual(0, observedAttacker);
            }
            finally { GameManager.Events.OnDamageDealtEvent -= replace; }
        }

        [UnityTest]
        public IEnumerator HitBox_FriendlyContact_DoesNotConsumeLimitBeforeHostileHit()
        {
            BaseCharacter owner = Actor(CharacterBelong.Ally);
            BaseCharacter friend = Actor(CharacterBelong.Ally);
            BaseCharacter enemy = Actor(CharacterBelong.Enemy);
            HitBox hit = HitComponent<HitBox>();
            yield return null;
            hit.Activate(Motion(), owner.ObjectHash);

            Contact(hit, friend);
            Assert.AreEqual(1000, GameManager.Data.GetVitals(friend.ObjectHash).currentHp);
            Assert.AreEqual(0, hit.HitCount);
            Assert.IsTrue(hit.IsActive);
            Contact(hit, enemy);

            Assert.Less(GameManager.Data.GetVitals(enemy.ObjectHash).currentHp, 1000);
            Assert.AreEqual(1, hit.HitCount);
            Assert.IsFalse(hit.IsActive);
        }

        [UnityTest]
        public IEnumerator DamageDealer_RejectedTargetBecomesHostile_AllowsFirstHit()
        {
            BaseCharacter owner = Actor(CharacterBelong.Ally);
            BaseCharacter target = Actor(CharacterBelong.Ally);
            DamageDealer hit = HitComponent<DamageDealer>();
            yield return null;
            hit.Activate(Motion(), owner.ObjectHash);
            Contact(hit, target);
            Assert.AreEqual(1000, GameManager.Data.GetVitals(target.ObjectHash).currentHp);

            GameManager.Data.GetFlags(target.ObjectHash).Belong = CharacterBelong.Enemy;
            Contact(hit, target);
            hit.Deactivate();

            Assert.Less(GameManager.Data.GetVitals(target.ObjectHash).currentHp, 1000);
        }

        [UnityTest]
        public IEnumerator HitBox_SourceChangesFactionAfterActivation_UsesCapturedFaction()
        {
            BaseCharacter owner = Actor(CharacterBelong.Ally);
            BaseCharacter enemy = Actor(CharacterBelong.Enemy);
            HitBox hit = HitComponent<HitBox>();
            yield return null;
            hit.Activate(Motion(), owner.ObjectHash);
            GameManager.Data.GetFlags(owner.ObjectHash).Belong = CharacterBelong.Enemy;

            Contact(hit, enemy);

            Assert.Less(GameManager.Data.GetVitals(enemy.ObjectHash).currentHp, 1000);
            Assert.AreEqual(1, hit.HitCount);
        }

        [UnityTest]
        public IEnumerator DamageDealer_ExplicitFriendlyPolicy_HitsFriendlyTarget()
        {
            BaseCharacter owner = Actor(CharacterBelong.Ally);
            BaseCharacter friend = Actor(CharacterBelong.Ally);
            DamageDealer hit = HitComponent<DamageDealer>();
            yield return null;
            hit.Activate(Motion(Policy(AttackTargetRelation.Friendly)), owner.ObjectHash);

            Contact(hit, friend);
            hit.Deactivate();

            Assert.Less(GameManager.Data.GetVitals(friend.ObjectHash).currentHp, 1000);
        }

        [UnityTest]
        public IEnumerator DamageReceiver_CapturedHostilePolicyRejectsFriend_DoesNotPublishDamage()
        {
            BaseCharacter owner = Actor(CharacterBelong.Ally);
            BaseCharacter friend = Actor(CharacterBelong.Ally);
            yield return null;
            Assert.IsTrue(GameManager.Actors.TryGet(owner.ObjectHash, out ActorRef source));
            Assert.IsTrue(GameManager.Actors.TryGet(friend.ObjectHash, out ActorRef target));
            AttackContext context = AttackContext.Capture(source,
                new ElementalStatus { slash = 100 }, 1f, CharacterBelong.Ally, null);
            DamageData attack = new DamageData { motionValue = 1f, knockbackForce = Vector2.right * 5f };
            context.ApplyTo(ref attack, target);
            int notifications = 0;
            Action<DamageResult, int, int> onDamage = (result, attacker, defender) => notifications++;
            GameManager.Events.OnDamageDealtEvent += onDamage;
            try
            {
                DamageResult result = friend.DamageReceiver.ReceiveDamage(attack);
                Assert.AreEqual(0, result.totalDamage);
                Assert.AreEqual(1000, GameManager.Data.GetVitals(friend.ObjectHash).currentHp);
                Assert.AreEqual(0, notifications);
                Assert.AreEqual(Vector2.zero, friend.Rigidbody.linearVelocity);
            }
            finally { GameManager.Events.OnDamageDealtEvent -= onDamage; }
        }

        [UnityTest]
        public IEnumerator DamageReceiver_RawExplicitPolicy_IsEnforcedIncludingUnknownSource()
        {
            BaseCharacter owner = Actor(CharacterBelong.Ally);
            BaseCharacter enemy = Actor(CharacterBelong.Enemy);
            yield return null;
            DamageData attack = new DamageData
            {
                attackerHash = owner.ObjectHash, defenderHash = enemy.ObjectHash,
                damage = new ElementalStatus { slash = 100 }, motionValue = 1f,
                targetPolicy = Policy(AttackTargetRelation.None),
            };

            enemy.DamageReceiver.ReceiveDamage(attack);
            Assert.AreEqual(1000, GameManager.Data.GetVitals(enemy.ObjectHash).currentHp);
            attack.attackerHash = 0;
            attack.targetPolicy = Policy(AttackTargetRelation.All);
            enemy.DamageReceiver.ReceiveDamage(attack);
            Assert.AreEqual(1000, GameManager.Data.GetVitals(enemy.ObjectHash).currentHp);

            attack.attackerHash = owner.ObjectHash;
            attack.targetPolicy = Policy(AttackTargetRelation.Hostile);
            DamageResult accepted = enemy.DamageReceiver.ReceiveDamage(attack);
            Assert.Greater(accepted.totalDamage, 0);
            Assert.Less(GameManager.Data.GetVitals(enemy.ObjectHash).currentHp, 1000);
        }
    }
}
