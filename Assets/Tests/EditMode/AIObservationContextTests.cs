using Game.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    public class AIObservationContextTests
    {
        private static AIActorObservation Actor(int hash, long generation, CharacterBelong belong,
            Vector2 position, ushort recognized = 0, bool vanished = false)
        {
            CharacterFlags flags = CharacterFlags.Pack(belong, 0, ActState.Neutral, AbilityFlag.None);
            flags.RecognizeObjectType = recognized;
            flags.IsVanished = vanished;
            return AIActorObservation.Capture(new ActorRef(hash, generation),
                new CharacterVitals { currentHp = 100, maxHp = 100, position = position }, default, flags);
        }

        [Test]
        public void AIObservation_LiveValuesChange_AllJudgmentsKeepTheCapturedVersion()
        {
            AIObservationContext observation = new AIObservationContext();
            observation.BeginSnapshot(7, 0.14f);
            AIActorObservation owner = Actor(1, 1, CharacterBelong.Ally, Vector2.zero);
            CharacterVitals targetVitals = new CharacterVitals { currentHp = 100, maxHp = 100, position = new Vector2(3f, 4f) };
            AIActorObservation target = AIActorObservation.Capture(new ActorRef(2, 2), targetVitals, default,
                CharacterFlags.Pack(CharacterBelong.Enemy, 0, ActState.Neutral, AbilityFlag.None));
            observation.AddActor(owner);
            observation.AddActor(target);
            targetVitals.position = new Vector2(100f, 0f);
            targetVitals.currentHp = 5;

            AICondition condition = new AICondition { conditionType = AIConditionType.Distance, compareOp = CompareOp.Equal, operandA = 5 };
            Assert.IsTrue(ConditionEvaluator.Evaluate(condition, 1, 2, observation));
            Assert.IsTrue(observation.TryGetActor(2, out AIActorObservation captured));
            Assert.AreEqual(100, captured.currentHp);
            Assert.AreEqual(new Vector2(3f, 4f), captured.position);
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
            AIActorObservation target = Actor(2, 2, CharacterBelong.Enemy, Vector2.right, vanished: true);
            observation.AddActor(target);
            AITargetSelect select = new AITargetSelect
            {
                sortKey = TargetSortKey.Distance,
                filter = new TargetFilter { belong = CharacterBelong.Enemy },
            };
            Assert.AreEqual(0, TargetSelector.SelectTarget(select, 1, observation));
            Assert.AreEqual(100, target.currentHp);
            Assert.AreEqual(ActState.Neutral, target.flags.ActState);
        }

        [TestCase(AIConditionType.HpRatio)]
        [TestCase(AIConditionType.MpRatio)]
        [TestCase(AIConditionType.StaminaRatio)]
        [TestCase(AIConditionType.ArmorRatio)]
        public void AIObservation_ResourceRatio_CapturesTheUnroundedPercent(AIConditionType conditionType)
        {
            CharacterVitals vitals = new CharacterVitals
            {
                currentHp = 17, maxHp = 53, currentMp = 17f, maxMp = 53,
                currentStamina = 17f, maxStamina = 53f, currentArmor = 17f, maxArmor = 53f,
            };
            AIObservationContext observation = new AIObservationContext();
            observation.BeginSnapshot(1, 0f);
            observation.AddActor(AIActorObservation.Capture(new ActorRef(1, 1), vitals, default, default));
            vitals.currentHp = 0;
            vitals.currentMp = vitals.currentStamina = vitals.currentArmor = 0f;

            AICondition condition = new AICondition { conditionType = conditionType, filter = new TargetFilter { includeSelf = true } };
            Assert.IsTrue(observation.TryGetConditionValue(condition, 1, 0, out float value));
            Assert.AreEqual(100f * 17f / 53f, value);
        }

        [TestCase(AIConditionType.HpRatio, 0)]
        [TestCase(AIConditionType.MpRatio, 0)]
        [TestCase(AIConditionType.StaminaRatio, 0)]
        [TestCase(AIConditionType.ArmorRatio, 0)]
        [TestCase(AIConditionType.HpRatio, -1)]
        [TestCase(AIConditionType.MpRatio, -1)]
        [TestCase(AIConditionType.StaminaRatio, -1)]
        [TestCase(AIConditionType.ArmorRatio, -1)]
        public void AIObservation_NonpositiveMaximum_KeepsTheZeroRatioFallback(AIConditionType conditionType, int maximum)
        {
            CharacterVitals vitals = new CharacterVitals
            {
                currentHp = 17, maxHp = maximum, currentMp = 17f, maxMp = maximum,
                currentStamina = 17f, maxStamina = maximum, currentArmor = 17f, maxArmor = maximum,
            };
            AIObservationContext observation = new AIObservationContext();
            observation.BeginSnapshot(1, 0f);
            observation.AddActor(AIActorObservation.Capture(new ActorRef(1, 1), vitals, default, default));
            AICondition condition = new AICondition { conditionType = conditionType, filter = new TargetFilter { includeSelf = true } };
            Assert.IsTrue(observation.TryGetConditionValue(condition, 1, 0, out float value));
            Assert.AreEqual(0f, value);
        }

        [Test]
        public void AIObservation_HpSort_UsesTheCapturedFractionWithoutPercentRoundTrip()
        {
            CharacterVitals vitals = new CharacterVitals { currentHp = 17, maxHp = 53 };
            AIObservationContext observation = new AIObservationContext();
            observation.BeginSnapshot(1, 0f);
            observation.AddActor(AIActorObservation.Capture(new ActorRef(1, 1), vitals, default, default));
            Assert.IsTrue(observation.TryGetSortValue(TargetSortKey.HpRatio, 1, 1, out float value));
            Assert.AreEqual(17f / 53f, value);
        }

        [TestCase(TargetSortKey.AttackPower)]
        [TestCase(TargetSortKey.DefensePower)]
        public void AIObservation_PowerSort_UsesAllCapturedElementChannels(TargetSortKey sortKey)
        {
            CharacterVitals vitals = new CharacterVitals { currentHp = 100, maxHp = 100, position = Vector2.right };
            CharacterFlags flags = CharacterFlags.Pack(CharacterBelong.Enemy, 0, ActState.Neutral, AbilityFlag.None);
            CombatStats strongest = new CombatStats
            {
                attack = new ElementalStatus { slash = 1, strike = 2, pierce = 3, fire = 4, thunder = 5, light = 6, dark = 7 },
                defense = new ElementalStatus { slash = 7, strike = 6, pierce = 5, fire = 4, thunder = 3, light = 2, dark = 1 },
            };
            CombatStats other = new CombatStats
            {
                attack = new ElementalStatus { slash = 27 },
                defense = new ElementalStatus { slash = 27 },
            };
            AIObservationContext observation = new AIObservationContext();
            observation.BeginSnapshot(1, 0f);
            observation.AddActor(Actor(1, 1, CharacterBelong.Ally, Vector2.zero));
            observation.AddActor(AIActorObservation.Capture(new ActorRef(2, 2), vitals, strongest, flags));
            observation.AddActor(AIActorObservation.Capture(new ActorRef(3, 3), vitals, other, flags));
            strongest = default;
            flags.IsVanished = true;

            AITargetSelect select = new AITargetSelect
            {
                sortKey = sortKey, isDescending = true,
                filter = new TargetFilter { belong = CharacterBelong.Enemy, distanceRange = new Vector2(0f, 10f) },
            };
            Assert.AreEqual(2, TargetSelector.SelectTarget(select, 1, observation));
        }

        [TestCase(Element.Fire, true)]
        [TestCase(Element.Slash, false)]
        [TestCase(Element.Fire | Element.Slash, true)]
        [TestCase(Element.Thunder | Element.Dark, true)]
        public void AIObservation_WeakPointFilter_KeepsCapturedNonpositiveDefenseSemantics(Element requested, bool expected)
        {
            CharacterVitals vitals = new CharacterVitals { currentHp = 100, maxHp = 100 };
            CharacterFlags flags = CharacterFlags.Pack(CharacterBelong.Enemy, 0, ActState.Neutral, AbilityFlag.None);
            CombatStats combat = new CombatStats
            {
                defense = new ElementalStatus { slash = 1, strike = 1, pierce = 1, fire = 0, thunder = 1, light = 1, dark = -2 },
            };
            AIActorObservation target = AIActorObservation.Capture(new ActorRef(2, 2), vitals, combat, flags);
            combat.defense.fire = 1;
            combat.defense.dark = 1;
            AIObservationContext observation = new AIObservationContext();
            observation.BeginSnapshot(1, 0f);
            observation.AddActor(Actor(1, 1, CharacterBelong.Ally, Vector2.zero));
            observation.AddActor(target);
            Assert.AreEqual(expected, observation.Matches(new TargetFilter { weakPoint = requested }, 1, target));
        }

        [Test]
        public void AIObservation_NewTickWithReusedHash_ReplacesResourcesAndCombatProjectionTogether()
        {
            AIObservationContext observation = new AIObservationContext();
            observation.BeginSnapshot(1, 0f);
            observation.AddActor(Actor(1, 1, CharacterBelong.Ally, Vector2.zero));
            observation.BeginSnapshot(2, 0.02f);
            observation.AddActor(AIActorObservation.Capture(new ActorRef(1, 2),
                new CharacterVitals { currentHp = 25, maxHp = 50, position = new Vector2(9f, 2f) },
                new CombatStats { attack = new ElementalStatus { fire = 11 } },
                CharacterFlags.Pack(CharacterBelong.Enemy, 0, ActState.Neutral, AbilityFlag.None)));

            Assert.IsFalse(observation.Contains(new ActorRef(1, 1)));
            Assert.IsTrue(observation.Contains(new ActorRef(1, 2)));
            Assert.IsTrue(observation.TryGetActor(1, out AIActorObservation replacement));
            Assert.AreEqual(new Vector2(9f, 2f), replacement.position);
            Assert.IsTrue(observation.TryGetSortValue(TargetSortKey.AttackPower, 1, 1, out float power));
            Assert.AreEqual(11f, power);
            Assert.IsTrue(observation.TryGetSortValue(TargetSortKey.HpRatio, 1, 1, out float fraction));
            Assert.AreEqual(0.5f, fraction);
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
