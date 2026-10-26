using Game.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    public class AIObservationContextTests
    {
        private static AIActorObservation Actor(int hash, long generation, CharacterBelong belong,
            Vector2 position, ushort recognized = 0)
        {
            CharacterFlags flags = CharacterFlags.Pack(belong, 0, ActState.Neutral, AbilityFlag.None);
            flags.RecognizeObjectType = recognized;
            return new AIActorObservation
            {
                actor = new ActorRef(hash, generation),
                vitals = new CharacterVitals { currentHp = 100, maxHp = 100, position = position },
                flags = flags,
            };
        }

        [Test]
        public void AIObservation_LiveValuesChange_AllJudgmentsKeepTheCapturedVersion()
        {
            AIObservationContext observation = new AIObservationContext();
            observation.BeginSnapshot(7, 0.14f);
            AIActorObservation owner = Actor(1, 1, CharacterBelong.Ally, Vector2.zero);
            AIActorObservation target = Actor(2, 2, CharacterBelong.Enemy, new Vector2(3f, 4f));
            observation.AddActor(owner);
            observation.AddActor(target);
            target.vitals.position = new Vector2(100f, 0f);
            target.vitals.currentHp = 5;

            AICondition condition = new AICondition { conditionType = AIConditionType.Distance, compareOp = CompareOp.Equal, operandA = 5 };
            Assert.IsTrue(ConditionEvaluator.Evaluate(condition, 1, 2, observation));
            Assert.IsTrue(observation.TryGetActor(2, out AIActorObservation captured));
            Assert.AreEqual(100, captured.vitals.currentHp);
            Assert.AreEqual(new Vector2(3f, 4f), captured.vitals.position);
            Assert.AreEqual(7, observation.Tick);
        }

        [Test]
        public void AIObservation_UnavailableSources_DoNotMatchZeroButAnObservedEmptyPopulationDoes()
        {
            AIObservationContext observation = new AIObservationContext();
            observation.BeginSnapshot(1, 0f);
            observation.AddActor(Actor(1, 1, CharacterBelong.Ally, Vector2.zero));
            AICondition missing = new AICondition { conditionType = AIConditionType.ProjectileNear, compareOp = CompareOp.Equal, operandA = 0 };
            Assert.IsFalse(ConditionEvaluator.Evaluate(missing, 1, 0, observation));
            missing.conditionType = AIConditionType.EventFired;
            Assert.IsFalse(ConditionEvaluator.Evaluate(missing, 1, 0, observation));
            missing.conditionType = AIConditionType.DamageScore;
            Assert.IsFalse(ConditionEvaluator.Evaluate(missing, 1, 1, observation));

            AICondition count = new AICondition
            {
                conditionType = AIConditionType.Count, compareOp = CompareOp.Equal, operandA = 0,
                filter = new TargetFilter { belong = CharacterBelong.Enemy },
            };
            Assert.IsTrue(ConditionEvaluator.Evaluate(count, 1, 0, observation));
        }

        [Test]
        public void AIObservation_DamageScores_AreOwnedByObserverAndFrozenForTheTick()
        {
            AIObservationContext observation = new AIObservationContext();
            observation.BeginSnapshot(1, 1f);
            observation.AddActor(Actor(1, 1, CharacterBelong.Ally, Vector2.zero));
            observation.AddActor(Actor(2, 2, CharacterBelong.Enemy, Vector2.right));
            observation.AddActor(Actor(3, 3, CharacterBelong.Enemy, Vector2.left));
            observation.SetDamageScoresAvailable();
            observation.RecordDamageScore(new ActorRef(1, 1), new ActorRef(2, 2), 20f);
            observation.RecordDamageScore(new ActorRef(1, 1), new ActorRef(3, 3), 5f);
            AITargetSelect select = new AITargetSelect { sortKey = TargetSortKey.DamageScore, isDescending = true, filter = new TargetFilter { belong = CharacterBelong.Enemy } };
            Assert.AreEqual(2, TargetSelector.SelectTarget(select, 1, observation));
            Assert.IsTrue(observation.TryGetDamageScore(2, 1, out float unrecorded));
            Assert.AreEqual(0f, unrecorded);
            Assert.IsTrue(observation.TryGetDamageScore(1, 2, out float recorded));
            Assert.AreEqual(20f, recorded);
        }

        [Test]
        public void AIObservation_TargetingCount_UsesActualRelationsAndKeepsItsSnapshot()
        {
            AITargetRelations targets = new AITargetRelations();
            ActorRef first = new ActorRef(1, 1);
            ActorRef second = new ActorRef(2, 2);
            ActorRef target = new ActorRef(3, 3);
            targets.Register(first); targets.Register(second); targets.Register(target);
            targets.SetTarget(first, target); targets.SetTarget(second, target);
            AIObservationContext observation = new AIObservationContext();
            observation.BeginSnapshot(1, 0f);
            observation.AddActor(Actor(1, 1, CharacterBelong.Enemy, Vector2.zero));
            observation.AddActor(Actor(2, 2, CharacterBelong.Enemy, Vector2.right));
            observation.AddActor(Actor(3, 3, CharacterBelong.Ally, Vector2.left));
            observation.AddActor(Actor(4, 4, CharacterBelong.Ally, Vector2.up, 0xffff));
            targets.CopyTo(observation);
            AITargetSelect select = new AITargetSelect
            {
                sortKey = TargetSortKey.TargetingCount, isDescending = true,
                filter = new TargetFilter { belong = CharacterBelong.Ally },
            };
            Assert.AreEqual(3, TargetSelector.SelectTarget(select, 1, observation));
            targets.SetTarget(first, default);
            Assert.IsTrue(observation.TryGetTargetingCount(3, out int count));
            Assert.AreEqual(2, count);
        }

        [Test]
        public void AIObservation_LastAttacker_UsesRecordedEventOrderInsteadOfHighestDamageScore()
        {
            AIObservationContext observation = new AIObservationContext();
            observation.BeginSnapshot(1, 1f);
            observation.AddActor(Actor(1, 1, CharacterBelong.Ally, Vector2.zero));
            observation.AddActor(Actor(2, 2, CharacterBelong.Enemy, Vector2.right));
            observation.AddActor(Actor(3, 3, CharacterBelong.Enemy, Vector2.left));
            observation.SetDamageScoresAvailable();
            AITargetSelect select = new AITargetSelect
            {
                sortKey = TargetSortKey.LastAttacker, isDescending = true,
                filter = new TargetFilter { belong = CharacterBelong.Enemy },
            };
            Assert.AreEqual(0, TargetSelector.SelectTarget(select, 1, observation));
            observation.RecordDamageScore(new ActorRef(1, 1), new ActorRef(2, 2), 100f);
            observation.RecordDamageScore(new ActorRef(1, 1), new ActorRef(3, 3), 5f);
            observation.RecordLastAttacker(new ActorRef(1, 1), new ActorRef(2, 2));
            observation.RecordLastAttacker(new ActorRef(1, 1), new ActorRef(3, 3));
            Assert.AreEqual(3, TargetSelector.SelectTarget(select, 1, observation));
        }


        [Test]
        public void AIObservation_NearbyCount_UsesFilterDistanceSeparatelyFromTheCountThreshold()
        {
            AIObservationContext observation = new AIObservationContext();
            observation.BeginSnapshot(1, 0f);
            observation.AddActor(Actor(1, 1, CharacterBelong.Ally, Vector2.zero));
            observation.AddActor(Actor(2, 2, CharacterBelong.Ally, new Vector2(5f, 0f)));
            observation.AddActor(Actor(3, 3, CharacterBelong.Ally, new Vector2(20f, 0f)));
            observation.AddActor(Actor(4, 4, CharacterBelong.Enemy, Vector2.right));
            AICondition condition = new AICondition
            {
                conditionType = AIConditionType.NearbyFaction, compareOp = CompareOp.GreaterEqual, operandA = 1,
                filter = new TargetFilter { distanceRange = new Vector2(0f, 10f) },
            };
            Assert.IsTrue(ConditionEvaluator.Evaluate(condition, 1, 0, observation));
            Assert.IsTrue(observation.TryGetConditionValue(condition, 1, 0, out float count));
            Assert.AreEqual(1f, count);
        }

        [Test]
        public void AIObservation_VanishedTarget_IsExcludedWithoutTreatingItAsDead()
        {
            AIObservationContext observation = new AIObservationContext();
            observation.BeginSnapshot(1, 0f);
            observation.AddActor(Actor(1, 1, CharacterBelong.Ally, Vector2.zero));
            AIActorObservation target = Actor(2, 2, CharacterBelong.Enemy, Vector2.right);
            target.flags.IsVanished = true;
            observation.AddActor(target);
            AITargetSelect select = new AITargetSelect
            {
                sortKey = TargetSortKey.Distance,
                filter = new TargetFilter { belong = CharacterBelong.Enemy },
            };
            Assert.AreEqual(0, TargetSelector.SelectTarget(select, 1, observation));
            Assert.AreEqual(100, target.vitals.currentHp);
            Assert.AreEqual(ActState.Neutral, target.flags.ActState);
        }

        [Test]
        public void AITargetRelations_DeathOrHashReuse_ClearsIncomingAndOutgoingReferences()
        {
            AITargetRelations targets = new AITargetRelations();
            ActorRef owner = new ActorRef(1, 1);
            ActorRef target = new ActorRef(2, 2);
            targets.Register(owner); targets.Register(target);
            Assert.IsTrue(targets.SetTarget(owner, target));
            targets.Remove(target);
            Assert.IsFalse(targets.TryGetTarget(owner, out _));
            targets.Register(new ActorRef(2, 3));
            Assert.IsFalse(targets.SetTarget(owner, target));
            Assert.IsTrue(targets.SetTarget(owner, new ActorRef(2, 3)));
            targets.Register(new ActorRef(1, 4));
            Assert.IsFalse(targets.TryGetTarget(new ActorRef(1, 4), out _));
        }
    }
}
