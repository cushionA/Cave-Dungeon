using System.Collections;
using System.Collections.Generic;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Game.Tests.PlayMode
{
    public class AIObservationFlowTests
    {
        private readonly List<Object> _created = new List<Object>();
        private BaseCharacter _owner;
        private BaseCharacter _target;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            TestSceneHelper.CreateGameManager();
            GameManager.Instance.enabled = false;
            _owner = CreateActor(CharacterBelong.Ally, CharacterFeature.Player);
            _target = CreateActor(CharacterBelong.Enemy, CharacterFeature.Minion);
            yield return null;
            CharacterRegistry.RegisterPlayer(_owner.ObjectHash);
            _owner.Rigidbody.simulated = false;
            _target.Rigidbody.simulated = false;
        }

        private BaseCharacter CreateActor(CharacterBelong belong, CharacterFeature feature)
        {
            Game.Core.CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(belong, feature);
            _created.Add(info);
            GameObject actor = TestSceneHelper.CreateBaseCharacterObject(info, Vector3.zero);
            _created.Add(actor);
            return actor.GetComponent<BaseCharacter>();
        }

        [UnityTest]
        public IEnumerator Observation_CentralCaptureFreezesPositionsDespiteLaterLivePublications()
        {
            _owner.SetPhysicsPosition(new Vector2(1f, 2f));
            _target.SetPhysicsPosition(new Vector2(4f, 6f));
            GameManager.Instance.SimulationTick(0.02f, 1f);
            Assert.IsTrue(GameManager.TryGetObservation(_owner.ObjectHash, out AIObservationContext observation));
            long tick = observation.Tick;
            AICondition distance = new AICondition { conditionType = AIConditionType.Distance, compareOp = CompareOp.Equal, operandA = 5 };
            Assert.IsTrue(ConditionEvaluator.Evaluate(distance, _owner.ObjectHash, _target.ObjectHash, observation));

            _target.SetPhysicsPosition(new Vector2(100f, 2f));
            _target.PublishPositionForObservation();
            Assert.AreEqual(100f, GameManager.Data.GetVitals(_target.ObjectHash).position.x);
            Assert.IsTrue(ConditionEvaluator.Evaluate(distance, _owner.ObjectHash, _target.ObjectHash, observation));
            Assert.AreEqual(tick, observation.Tick);

            GameManager.Instance.SimulationTick(0.02f, 1.02f);
            Assert.AreEqual(tick + 1, observation.Tick);
            Assert.IsFalse(ConditionEvaluator.Evaluate(distance, _owner.ObjectHash, _target.ObjectHash, observation));
            yield break;
        }

        [UnityTest]
        public IEnumerator Observation_DamageAndTargetRelationsAreConnectedAndClearedOnDeath()
        {
            BaseCharacter recentAttacker = CreateActor(CharacterBelong.Enemy, CharacterFeature.Minion);
            yield return null;
            recentAttacker.Rigidbody.simulated = false;
            Assert.IsTrue(GameManager.Actors.TryGet(_owner.ObjectHash, out ActorRef owner));
            Assert.IsTrue(GameManager.Actors.TryGet(_target.ObjectHash, out ActorRef target));
            Assert.IsTrue(GameManager.Actors.TryGet(recentAttacker.ObjectHash, out ActorRef recent));
            GameManager.Instance.SimulationTick(0.02f, 1f);
            GameManager.TargetRelations.SetTarget(owner, target);
            GameManager.Events.FireDamageDealt(new DamageResult
            {
                totalDamage = 20, attackerRef = target, defenderRef = owner,
            }, target.Hash, owner.Hash);
            GameManager.Events.FireDamageDealt(new DamageResult
            {
                totalDamage = 5, attackerRef = recent, defenderRef = owner,
            }, recent.Hash, owner.Hash);
            GameManager.Instance.SimulationTick(0.02f, 1.02f);
            Assert.IsTrue(GameManager.Observation.TryGetDamageScore(owner.Hash, target.Hash, out float score));
            Assert.Greater(score, 0f);
            Assert.IsTrue(GameManager.Observation.TryGetTargetingCount(target.Hash, out int count));
            Assert.AreEqual(1, count);
            Assert.AreEqual(recent.Hash, TargetSelector.SelectTarget(new AITargetSelect
            {
                filter = new TargetFilter { belong = CharacterBelong.Enemy },
                sortKey = TargetSortKey.LastAttacker, isDescending = true,
            }, owner.Hash, GameManager.Observation));

            GameManager.Data.GetVitals(target.Hash).currentHp = 0;
            GameManager.Events.FireCharacterDeath(target.Hash, owner.Hash);
            Assert.IsFalse(GameManager.TargetRelations.TryGetTarget(owner, out _));
            GameManager.Instance.SimulationTick(0.02f, 1.04f);
            Assert.IsFalse(GameManager.Observation.TryGetActor(target.Hash, out _));

            _target.OnPoolReturn();
            _target.OnPoolAcquire();
            Assert.IsTrue(GameManager.Actors.TryGet(target.Hash, out ActorRef reused));
            Assert.AreNotEqual(target.Generation, reused.Generation);
            GameManager.Events.FireDamageDealt(new DamageResult
            {
                totalDamage = 99, attackerRef = target, defenderRef = owner,
            }, target.Hash, owner.Hash);
            GameManager.Instance.SimulationTick(0.02f, 1.06f);
            Assert.IsTrue(GameManager.Observation.TryGetDamageScore(owner.Hash, reused.Hash, out float reusedScore));
            Assert.AreEqual(0f, reusedScore);
            yield break;
        }

        [UnityTest]
        public IEnumerator Observation_ReusedActorCannotBindAnOldBrainUntilItsLifetimeIsReset()
        {
            using EnemyController controller = new EnemyController(_owner.ObjectHash, GameManager.Data, GameManager.Events);
            GameManager.Instance.SimulationTick(0f, 1f);
            Assert.IsTrue(controller.JudgmentLoop.UseObservation(GameManager.Observation, GameManager.TargetRelations));
            _owner.OnPoolReturn();
            _owner.OnPoolAcquire();
            GameManager.Instance.SimulationTick(0f, 1.02f);
            Assert.IsFalse(controller.JudgmentLoop.UseObservation(GameManager.Observation, GameManager.TargetRelations));
            controller.Activate();
            Assert.IsTrue(controller.JudgmentLoop.UseObservation(GameManager.Observation, GameManager.TargetRelations));
            yield break;
        }

        [UnityTest]
        public IEnumerator CompanionAvailability_RestoresPriorPhysicsAndPresentationAcrossDisableReturnAndPool()
        {
            Game.Core.CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(CharacterBelong.Ally, CharacterFeature.Companion);
            _created.Add(info);
            GameObject actor = new GameObject("AvailabilityCompanion");
            _created.Add(actor);
            actor.AddComponent<Rigidbody2D>();
            BoxCollider2D collider = actor.AddComponent<BoxCollider2D>();
            SpriteRenderer sprite = actor.AddComponent<SpriteRenderer>();
            CompanionCharacter companion = actor.AddComponent<CompanionCharacter>();
            companion.SetCharacterInfoForTest(info);
            companion.SetMpSettings(new CompanionMpSettings
            {
                baseRecoveryRate = 5f, vanishRecoveryMultiplier = 1.3f,
                returnThresholdRatio = 0.5f, maxReserveMp = 100,
            });
            yield return null;
            collider.enabled = true;
            companion.Rigidbody.simulated = true;
            companion.Rigidbody.constraints = RigidbodyConstraints2D.FreezeRotation;
            companion.Rigidbody.gravityScale = 2.5f;
            GameObject child = new GameObject("AcceptedAttackTrigger");
            child.transform.SetParent(actor.transform);
            BoxCollider2D attackTrigger = child.AddComponent<BoxCollider2D>();
            attackTrigger.isTrigger = true;
            HitBox hitBox = child.AddComponent<HitBox>();
            actor.AddComponent<ActionExecutorController>();
            MotionDefinition motion = ScriptableObject.CreateInstance<MotionDefinition>();
            motion.info = new MotionInfo { activeMotionDuration = 2f };
            _created.Add(motion);
            AttackInfo attack = ScriptableObject.CreateInstance<AttackInfo>();
            attack.motion = motion;
            attack.mpCost = info.maxMp;
            _created.Add(attack);
            Assert.IsTrue(GameManager.Actions.Execute(companion.ObjectHash, attack, 0, Vector2.right,
                1f, ActionVariant.Default, out _, out long acceptedGroup));
            GameManager.Actions.Execution.Tick(0.01f);
            Assert.IsTrue(hitBox.IsActive);
            Color color = sprite.color;
            color.a = 0.6f;
            sprite.color = color;
            CompanionMpManager mp = companion.AIController.MpManager;
            mp.ApplyBarrierDamage(mp.CurrentMp);
            Assert.IsTrue(GameManager.Actions.IsGroupActive(acceptedGroup));
            Assert.IsTrue(hitBox.IsActive);
            Assert.IsTrue(GameManager.Data.GetFlags(companion.ObjectHash).IsVanished);
            Assert.IsFalse(collider.enabled);
            Assert.IsTrue(companion.Rigidbody.simulated);
            Assert.AreEqual(RigidbodyConstraints2D.FreezeAll, companion.Rigidbody.constraints);
            Assert.IsTrue(attackTrigger.enabled);
            Assert.AreEqual(0.18f, sprite.color.a, 0.001f);
            GameManager.Instance.SimulationTick(0f, 1f);
            Assert.IsFalse(GameManager.Observation.TryGetActor(companion.ObjectHash, out _));

            companion.enabled = false;
            Assert.IsFalse(collider.enabled);
            Assert.IsTrue(companion.Rigidbody.simulated);
            Assert.AreEqual(RigidbodyConstraints2D.FreezeAll, companion.Rigidbody.constraints);
            Assert.AreEqual(0f, companion.Rigidbody.gravityScale);
            Assert.AreEqual(0.18f, sprite.color.a, 0.001f);
            companion.enabled = true;
            Assert.IsTrue(companion.Rigidbody.simulated);
            Assert.AreEqual(RigidbodyConstraints2D.FreezeAll, companion.Rigidbody.constraints);
            Assert.IsTrue(attackTrigger.enabled);
            mp.Tick(8f);
            Assert.IsFalse(GameManager.Data.GetFlags(companion.ObjectHash).IsVanished);
            Assert.IsTrue(companion.Rigidbody.simulated);
            Assert.AreEqual(RigidbodyConstraints2D.FreezeRotation, companion.Rigidbody.constraints);
            Assert.AreEqual(2.5f, companion.Rigidbody.gravityScale);
            Assert.IsTrue(collider.enabled);
            Assert.AreEqual(0.6f, sprite.color.a, 0.001f);

            collider.enabled = false;
            mp.ApplyBarrierDamage(mp.CurrentMp);
            companion.OnPoolReturn();
            Assert.IsTrue(companion.Rigidbody.simulated);
            Assert.AreEqual(RigidbodyConstraints2D.FreezeRotation, companion.Rigidbody.constraints);
            Assert.AreEqual(2.5f, companion.Rigidbody.gravityScale);
            Assert.IsFalse(collider.enabled);
            Assert.AreEqual(0.6f, sprite.color.a, 0.001f);
            yield break;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
            {
                if (_created[i] != null) { Object.Destroy(_created[i]); }
            }
            _created.Clear();
            yield return null;
            TestSceneHelper.Cleanup();
            yield return null;
        }
    }
}
