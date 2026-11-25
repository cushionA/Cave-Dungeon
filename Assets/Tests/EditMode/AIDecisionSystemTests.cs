using System;
using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    public class AIDecisionSystemTests
    {
        private SoACharaDataDic _data;
        private GameManagerCore _core;
        private AIObservationCoordinator _observations;
        private long _tick;

        [SetUp]
        public void SetUp()
        {
            _core = new GameManagerCore();
            _core.Initialize(8);
            _data = _core.Data;
            _observations = new AIObservationCoordinator(_data, _core.Actors, _core.Events);
            AddCharacter(1, CharacterBelong.Ally, Vector2.zero);
            AddCharacter(2, CharacterBelong.Ally, Vector2.zero);
            AddCharacter(10, CharacterBelong.Enemy, new Vector2(2f, 0f));
            AddCharacter(20, CharacterBelong.Enemy, new Vector2(8f, 0f));
        }

        [TearDown]
        public void TearDown()
        {
            _observations.Dispose();
            _core.Dispose();
        }

        [Test]
        public void AIDecisionSystem_MultipleActors_UsesOneSnapshotAndDefersExecution()
        {
            RecordingBackend first = new RecordingBackend();
            RecordingBackend second = new RecordingBackend();
            JudgmentLoop firstLoop = CreateLoop(1, first);
            JudgmentLoop secondLoop = CreateLoop(2, second);
            first.AfterExecute = () => _data.GetVitals(10).currentHp = 0;
            using (AIDecisionSystem decisions = new AIDecisionSystem(_core.Actors))
            {
                BeginTick(decisions, firstLoop, secondLoop);
                firstLoop.PrepareTick(0.02f, new List<int> { 10, 20 }, 0f, decisions);
                secondLoop.PrepareTick(0.02f, new List<int> { 10, 20 }, 0f, decisions);
                decisions.Schedule();
                Assert.AreEqual(0, first.Count);
                Assert.AreEqual(0, second.Count);
                _data.GetVitals(10).position = new Vector2(100f, 0f);
                decisions.CompleteAndApply();
                Assert.AreEqual(10, first.Target);
                Assert.AreEqual(10, second.Target);
                Assert.AreEqual(7, second.Action);
            }
        }

        [Test]
        public void AIDecisionSystem_DisposedDuringExecutorTick_DoesNotRetainLoop()
        {
            RecordingBackend backend = new RecordingBackend();
            JudgmentLoop loop = CreateLoop(1, backend);
            backend.OnTick = loop.Dispose;
            using (AIDecisionSystem decisions = new AIDecisionSystem(_core.Actors))
            {
                RunTick(decisions, loop, 0.02f);
                System.Collections.IDictionary programs = (System.Collections.IDictionary)typeof(AIDecisionSystem)
                    .GetField("_programs", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .GetValue(decisions);
                Assert.AreEqual(0, programs.Count);
                Assert.AreEqual(0, backend.Count);
            }
        }

        [Test]
        public void AIDecisionSystem_IntervalsAndForceEvaluate_PreservesJudgmentTiming()
        {
            RecordingBackend backend = new RecordingBackend();
            JudgmentLoop loop = CreateLoop(1, backend);
            AIMode mode = CreateMode();
            mode.judgeInterval = new Vector2(1f, 1f);
            loop.SetMode(mode);
            using (AIDecisionSystem decisions = new AIDecisionSystem(_core.Actors))
            {
                RunTick(decisions, loop, 0.02f);
                Assert.AreEqual(0, backend.Count);
                Assert.AreEqual(0, loop.CurrentTargetHash);
                RunTick(decisions, loop, 1f);
                Assert.AreEqual(1, backend.Count);
                RunTick(decisions, loop, 0.5f);
                Assert.AreEqual(1, backend.Count);
                loop.ForceEvaluate();
                RunTick(decisions, loop, 0.02f);
                Assert.AreEqual(2, backend.Count);
                Assert.AreEqual(10, loop.CurrentTargetHash);
            }
        }

        [Test]
        public void AIDecisionSystem_FirstMatchingInvalidAction_DoesNotExecuteDefault()
        {
            RecordingBackend backend = new RecordingBackend();
            JudgmentLoop loop = CreateLoop(1, backend);
            AIMode mode = CreateMode();
            mode.actionRules = new[] { new AIRule { actionIndex = 99 } };
            mode.defaultActionIndex = 0;
            loop.SetMode(mode);
            using (AIDecisionSystem decisions = new AIDecisionSystem(_core.Actors))
            {
                RunTick(decisions, loop, 0.02f);
                Assert.AreEqual(0, backend.Count);
                Assert.AreEqual(10, loop.CurrentTargetHash);
            }
        }

        [TestCase("legacy")]
        [TestCase("observed")]
        [TestCase("scheduled")]
        public void TargetRules_ExhaustedEmptySelectionClearsTargetAndFallbackReadsPriorTarget(string path)
        {
            using var loop = CreateLoop(1, new RecordingBackend());
            AIDecisionSystem decisions = _core.AIDecisions;
            int Decide(AIMode mode, int candidate)
            {
                loop.SetMode(mode);
                var candidates = new List<int> { candidate };
                if (path != "legacy") { BeginTick(decisions, loop); }
                if (path == "scheduled")
                {
                    loop.PrepareTick(0.02f, candidates, 0f, decisions);
                    decisions.Schedule();
                    decisions.CompleteAndApply();
                }
                else { loop.Tick(0.02f, candidates, 0f); }
                return loop.CurrentTargetHash;
            }

            AIMode ordinary = CreateMode();
            Assert.AreEqual(10, Decide(ordinary, 10));
            AIRule nearPreviousTarget = new AIRule { actionIndex = 1, conditions = new[] { new AICondition {
                conditionType = AIConditionType.Distance, compareOp = CompareOp.LessEqual, operandA = 2 } } };
            AIRule tryAlly = new AIRule { actionIndex = 0 };
            AIMode mode = CreateMode();
            mode.targetSelects = new[] { new AITargetSelect { continueIfEmpty = true,
                filter = new TargetFilter { belong = CharacterBelong.Ally } }, ordinary.targetSelects[0] };
            mode.targetRules = new[] { tryAlly, nearPreviousTarget };

            Assert.AreEqual(20, Decide(mode, 20), "The fallback condition reads target 10 at distance 2.");
            mode.targetRules = new[] { nearPreviousTarget };
            Assert.AreEqual(20, Decide(mode, 10), "No rule matches the prior target at distance 8.");
            mode.targetRules = new[] { tryAlly, nearPreviousTarget };
            Assert.AreEqual(0, Decide(mode, 10), "The first selection is empty and the fallback condition fails.");
            Assert.AreEqual(10, Decide(ordinary, 10));
            mode.targetRules = new[] { tryAlly };
            Assert.AreEqual(0, Decide(mode, 20), "A single exhausted preference clears the prior target.");
        }

        [Test]
        public void AIDecisionSystem_ModeChangesAfterScheduling_DiscardsStaleDecision()
        {
            RecordingBackend backend = new RecordingBackend();
            JudgmentLoop loop = CreateLoop(1, backend);
            using (AIDecisionSystem decisions = new AIDecisionSystem(_core.Actors))
            {
                BeginTick(decisions, loop);
                loop.PrepareTick(0.02f, new List<int> { 10 }, 0f, decisions);
                decisions.Schedule();
                AIMode changed = CreateMode();
                changed.actions[0] = new ActionSlot { execType = ActionExecType.Attack, paramId = 77 };
                loop.SetMode(changed);
                decisions.CompleteAndApply();
                Assert.AreEqual(0, backend.Count);
                RunTick(decisions, loop, 0.02f);
                Assert.AreEqual(77, backend.Action);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AIDecisionSystem_RemovedActorReusesHash_DiscardsOldLifetime(bool afterSchedule)
        {
            RecordingBackend backend = new RecordingBackend();
            JudgmentLoop loop = CreateLoop(1, backend);
            AIDecisionSystem decisions = _core.AIDecisions;
            BeginTick(decisions, loop);
            loop.PrepareTick(0.02f, new List<int> { 10 }, 0f, decisions);
            if (afterSchedule) { decisions.Schedule(); }
            _core.UnregisterCharacter(1);
            AddCharacter(1, CharacterBelong.Ally, Vector2.zero);
            if (!afterSchedule) { decisions.Schedule(); }
            decisions.CompleteAndApply();
            Assert.AreEqual(0, backend.Count);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AIDecisionSystem_DisposedLoop_ReplacesActorProgram(bool afterSchedule)
        {
            RecordingBackend oldBackend = new RecordingBackend();
            RecordingBackend replacementBackend = new RecordingBackend();
            JudgmentLoop oldLoop = CreateLoop(1, oldBackend);
            JudgmentLoop replacement = CreateLoop(1, replacementBackend);
            using (AIDecisionSystem decisions = new AIDecisionSystem(_core.Actors))
            {
                BeginTick(decisions, oldLoop, replacement);
                oldLoop.PrepareTick(0.02f, new List<int> { 10 }, 0f, decisions);
                if (afterSchedule)
                {
                    decisions.Schedule();
                }
                oldLoop.Dispose();
                if (!afterSchedule)
                {
                    replacement.PrepareTick(0.02f, new List<int> { 20 }, 0f, decisions);
                    decisions.Schedule();
                }
                decisions.CompleteAndApply();
                Assert.AreEqual(0, oldBackend.Count);
                RunTick(decisions, replacement, 0.02f);
                Assert.AreEqual(10, replacementBackend.Target);
            }
        }

        [Test]
        public void AIDecisionSystem_ModeSwitches_UsesBakedRulesForEachMode()
        {
            RecordingBackend backend = new RecordingBackend();
            ActionExecutor executor = new ActionExecutor();
            executor.SetExecutionBackend(backend);
            JudgmentLoop loop = new JudgmentLoop(executor, _data, 1);
            ModeController modes = new ModeController(loop);
            AIMode first = CreateMode();
            AIMode second = CreateMode();
            second.actions[0] = new ActionSlot { execType = ActionExecType.Attack, paramId = 88 };
            modes.SetModes(new[] { first, second }, null);
            using (AIDecisionSystem decisions = new AIDecisionSystem(_core.Actors))
            {
                RunTick(decisions, loop, 0.02f);
                Assert.AreEqual(7, backend.Action);
                modes.SwitchMode(1);
                RunTick(decisions, loop, 0.02f);
                Assert.AreEqual(88, backend.Action);
                modes.SwitchMode(0);
                RunTick(decisions, loop, 0.02f);
                Assert.AreEqual(7, backend.Action);
            }
        }

        [Test]
        public void AIDecisionSystem_TargetReusedDuringApply_DiscardsLaterDecision()
        {
            RecordingBackend first = new RecordingBackend();
            RecordingBackend second = new RecordingBackend();
            JudgmentLoop firstLoop = CreateLoop(1, first);
            JudgmentLoop secondLoop = CreateLoop(2, second);
            using (AIDecisionSystem decisions = new AIDecisionSystem(_core.Actors))
            {
                first.AfterExecute = () =>
                {
                    decisions.RemoveActor(10);
                    _core.UnregisterCharacter(10);
                    AddCharacter(10, CharacterBelong.Ally, new Vector2(100f, 0f));
                };
                BeginTick(decisions, firstLoop, secondLoop);
                firstLoop.PrepareTick(0.02f, new List<int> { 10, 20 }, 0f, decisions);
                secondLoop.PrepareTick(0.02f, new List<int> { 10, 20 }, 0f, decisions);
                decisions.Schedule();
                decisions.CompleteAndApply();
                Assert.AreEqual(1, first.Count);
                Assert.AreEqual(0, second.Count);
                Assert.AreEqual(0, secondLoop.CurrentTargetHash);
                RunTick(decisions, secondLoop, 0.02f);
                Assert.AreEqual(20, second.Target);
            }
        }

        [TestCase(TargetSortKey.DamageScore)]
        [TestCase(TargetSortKey.LastAttacker)]
        [TestCase(TargetSortKey.TargetingCount)]
        public void AIDecisionSystem_ObservationHistory_MatchesManagedSelection(TargetSortKey sortKey)
        {
            RecordingBackend backend = new RecordingBackend();
            JudgmentLoop loop = CreateLoop(1, backend);
            AIMode mode = CreateMode();
            mode.targetSelects[0].sortKey = sortKey;
            mode.targetSelects[0].isDescending = true;
            loop.SetMode(mode);
            using (AIDecisionSystem decisions = new AIDecisionSystem(_core.Actors))
            {
                BeginTick(decisions, loop);
                AIObservationContext observation = _observations.Observation;
                _core.Actors.TryGet(1, out ActorRef owner);
                _core.Actors.TryGet(2, out ActorRef second);
                _core.Actors.TryGet(10, out ActorRef firstTarget);
                _core.Actors.TryGet(20, out ActorRef secondTarget);
                observation.RecordDamageScore(owner, firstTarget, 100f);
                observation.RecordDamageScore(owner, secondTarget, 5f);
                observation.RecordLastAttacker(owner, secondTarget);
                observation.RecordTarget(second, secondTarget);
                List<int> candidates = new List<int> { 10, 20 };
                int expected = TargetSelector.SelectTarget(mode.targetSelects[0], 1, observation, candidates);
                Assert.AreEqual(sortKey == TargetSortKey.DamageScore ? 10 : 20, expected);
                loop.PrepareTick(0.02f, candidates, observation.Time, decisions);
                decisions.Schedule();
                decisions.CompleteAndApply();
                Assert.AreEqual(expected, loop.CurrentTargetHash);
            }
        }

        [TestCase(AIConditionType.ProjectileNear)]
        [TestCase(AIConditionType.ObjectNearby)]
        [TestCase(AIConditionType.EventFired)]
        public void AIDecisionSystem_UnknownCondition_DoesNotMatchZero(AIConditionType conditionType)
        {
            RecordingBackend backend = new RecordingBackend();
            JudgmentLoop loop = CreateLoop(1, backend);
            AIMode mode = CreateMode();
            mode.actionRules[0].conditions = new[] { new AICondition
                { conditionType = conditionType, compareOp = CompareOp.Equal, operandA = 0 } };
            mode.actions = new[] { mode.actions[0], new ActionSlot { execType = ActionExecType.Attack, paramId = 55 } };
            mode.defaultActionIndex = 1;
            loop.SetMode(mode);
            using (AIDecisionSystem decisions = new AIDecisionSystem(_core.Actors))
            {
                RunTick(decisions, loop, 0.02f);
                Assert.AreEqual(55, backend.Action);
            }
        }

        [TestCase(AIConditionType.Count)]
        [TestCase(AIConditionType.NearbyFaction)]
        public void AIDecisionSystem_PopulationCondition_UsesTheFullFilter(AIConditionType conditionType)
        {
            RecordingBackend backend = new RecordingBackend();
            JudgmentLoop loop = CreateLoop(1, backend);
            AIMode mode = CreateMode();
            mode.actionRules[0].conditions = new[] { new AICondition
            {
                conditionType = conditionType, compareOp = CompareOp.Equal, operandA = 1,
                filter = new TargetFilter { belong = CharacterBelong.Enemy, distanceRange = new Vector2(3f, 10f) }
            } };
            loop.SetMode(mode);
            using (AIDecisionSystem decisions = new AIDecisionSystem(_core.Actors))
            {
                RunTick(decisions, loop, 0.02f);
                Assert.AreEqual(1, backend.Count);
            }
        }

        [Test]
        public void AIDecisionSystem_UnknownSort_ClearsThePreviousTarget()
        {
            RecordingBackend backend = new RecordingBackend();
            JudgmentLoop loop = CreateLoop(1, backend);
            using (AIDecisionSystem decisions = new AIDecisionSystem(_core.Actors))
            {
                RunTick(decisions, loop, 0.02f);
                Assert.AreEqual(10, loop.CurrentTargetHash);
                AIMode mode = CreateMode();
                mode.targetSelects[0].sortKey = TargetSortKey.LastAttacker;
                loop.SetMode(mode);
                RunTick(decisions, loop, 0.02f);
                Assert.AreEqual(0, loop.CurrentTargetHash);
                Assert.AreEqual(1, backend.Count);
            }
        }

        [Test]
        public void AIDecisionSystem_ObservationAdvancesBeforeApply_DiscardsPreviousTick()
        {
            RecordingBackend backend = new RecordingBackend();
            JudgmentLoop loop = CreateLoop(1, backend);
            using (AIDecisionSystem decisions = new AIDecisionSystem(_core.Actors))
            {
                BeginTick(decisions, loop);
                loop.PrepareTick(0.02f, new List<int> { 10 }, 0f, decisions);
                decisions.Schedule();
                _observations.Observation.BeginSnapshot(_tick + 1, 0.02f);
                decisions.CompleteAndApply();
                Assert.AreEqual(0, backend.Count);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AIDecisionSystem_ProtectsTheRegisteredPlayerWhenCountsStayEqualAndAttackersSwap(bool scheduled)
        {
            Assert.IsTrue(_core.Actors.TryGet(1, out ActorRef sister));
            Assert.IsTrue(_core.Actors.TryGet(2, out ActorRef player));
            Assert.IsTrue(_core.Actors.TryGet(10, out ActorRef near));
            Assert.IsTrue(_core.Actors.TryGet(20, out ActorRef far));
            _observations.Targets.SetTarget(near, sister);
            _observations.Targets.SetTarget(far, player);
            _observations.Capture(++_tick, 1f);
            var observation = _observations.Observation;
            var filter = new TargetFilter { belong = CharacterBelong.Enemy };
            Assert.AreEqual(near.Hash, SelectFromCurrentObservation(filter, scheduled));
            filter.targetsProtectedPlayer = true;
            Assert.AreEqual(0, SelectFromCurrentObservation(filter, scheduled));
            observation.SetProtectedPlayer(player);
            _observations.Targets.SetTarget(near, player);
            _observations.Targets.SetTarget(far, sister);
            Assert.IsTrue(observation.TryGetTargetingCount(player.Hash, out int before));
            Assert.AreEqual(1, before);
            Assert.AreEqual(far.Hash, SelectFromCurrentObservation(filter, scheduled));

            _observations.Capture(++_tick, 2f);
            observation.SetProtectedPlayer(player);
            Assert.IsTrue(observation.TryGetTargetingCount(player.Hash, out int after));
            Assert.AreEqual(before, after);
            Assert.AreEqual(near.Hash, SelectFromCurrentObservation(filter, scheduled));
            // A long stun on a different enemy cannot satisfy the requirement for the attacker.
            filter.stunMustLastBeyondSeconds = 1f;
            observation.SetStunRemaining(near, 1f);
            observation.SetStunRemaining(far, 3f);
            Assert.AreEqual(0, SelectFromCurrentObservation(filter, scheduled));
            observation.SetStunRemaining(near, 1.25f);
            Assert.AreEqual(near.Hash, SelectFromCurrentObservation(filter, scheduled));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AIDecisionSystem_StunFilterRequiresKnownTimeStrictlyBeyondTheHorizon(bool scheduled)
        {
            _observations.Capture(++_tick, 0f);
            var observation = _observations.Observation;
            Assert.IsTrue(_core.Actors.TryGet(10, out ActorRef near));
            Assert.IsTrue(_core.Actors.TryGet(20, out ActorRef far));
            var filter = new TargetFilter { belong = CharacterBelong.Enemy, stunMustLastBeyondSeconds = 1.5f };
            Assert.AreEqual(0, SelectFromCurrentObservation(filter, scheduled));
            observation.SetStunRemaining(near, 1.5f);
            observation.SetStunRemaining(far, 0f);
            Assert.AreEqual(0, SelectFromCurrentObservation(filter, scheduled));
            observation.SetStunRemaining(far, 1.75f);
            Assert.AreEqual(far.Hash, SelectFromCurrentObservation(filter, scheduled));
            filter.stunMustLastBeyondSeconds = 0f;
            Assert.AreEqual(near.Hash, SelectFromCurrentObservation(filter, scheduled));
        }

        [TestCase(false, AIConditionType.Count)]
        [TestCase(true, AIConditionType.Count)]
        [TestCase(false, AIConditionType.NearbyFaction)]
        [TestCase(true, AIConditionType.NearbyFaction)]
        public void AIDecisionSystem_ZeroStunnedEnemiesRequiresEveryRelevantCandidateToBeKnown(
            bool scheduled, AIConditionType countType)
        {
            AIMode mode = CreateMode();
            mode.actionRules[0].conditions = new[] { new AICondition {
                conditionType = countType, compareOp = CompareOp.Equal, operandA = 0,
                filter = new TargetFilter { belong = CharacterBelong.Enemy, stunMustLastBeyondSeconds = 1f },
            } };
            mode.actions = new[] { mode.actions[0], new ActionSlot { execType = ActionExecType.Attack, paramId = 55 } };
            mode.defaultActionIndex = 1;
            _observations.Capture(++_tick, 0f);
            var observation = _observations.Observation;
            Assert.IsTrue(_core.Actors.TryGet(10, out ActorRef near));
            Assert.IsTrue(_core.Actors.TryGet(20, out ActorRef far));
            observation.SetStunRemaining(near, 0f);
            DecideFromCurrentObservation(mode, scheduled, out int action);
            Assert.AreEqual(55, action); // The other enemy has no sample.
            observation.SetStunRemaining(far, 0f);
            DecideFromCurrentObservation(mode, scheduled, out action);
            Assert.AreEqual(7, action); // Both sampled; zero match.
            observation.SetStunRemaining(far, 2f);
            DecideFromCurrentObservation(mode, scheduled, out action);
            Assert.AreEqual(55, action); // One match.
        }

        [Test]
        public void TargetLineOfSight_UsesTheSameSelectedPairAndFallbackInManagedAndBurstDecisions()
        {
            AIMode mode = CreateMode();
            mode.actionRules[0].conditions = new[] { new AICondition
            {
                conditionType = AIConditionType.TargetLineOfSight,
                compareOp = CompareOp.Equal,
                operandA = 1,
            } };
            mode.actions = new[] { mode.actions[0], new ActionSlot { execType = ActionExecType.Attack, paramId = 55 } };
            mode.defaultActionIndex = 1;

            _observations.Capture(++_tick, 0f);
            AIObservationContext observation = _observations.Observation;
            Assert.IsTrue(_core.Actors.TryGet(1, out ActorRef owner));
            Assert.IsTrue(_core.Actors.TryGet(10, out ActorRef near));
            Assert.IsTrue(_core.Actors.TryGet(20, out ActorRef far));
            observation.SetTargetLineOfSight(owner, far, true);

            AICondition blockedRule = mode.actionRules[0].conditions[0];
            blockedRule.operandA = 0;
            Assert.IsFalse(ConditionEvaluator.Evaluate(blockedRule, owner.Hash, near.Hash, _data, 0f));
            Assert.IsFalse(ConditionEvaluator.Evaluate(blockedRule, owner.Hash, near.Hash, observation));
            CheckDecision(near.Hash, 55);
            observation.SetTargetLineOfSight(owner, near, false);
            Assert.IsTrue(observation.TryGetTargetLineOfSight(owner, near, out bool blocked));
            Assert.IsFalse(blocked);
            Assert.IsTrue(ConditionEvaluator.Evaluate(blockedRule, owner.Hash, near.Hash, observation));
            CheckDecision(near.Hash, 55);
            mode.defaultActionIndex = 0;
            CheckDecision(near.Hash, 0); // A condition cannot be bypassed through the configured default.
            mode.defaultActionIndex = 1;
            observation.SetTargetLineOfSight(owner, near, true);
            CheckDecision(near.Hash, 7);

            _data.GetVitals(near.Hash).position = new Vector2(12f, 0f);
            _observations.Capture(++_tick, 0.02f);
            Assert.IsFalse(observation.TryGetTargetLineOfSight(owner, far, out _));
            CheckDecision(far.Hash, 55);
            observation.SetTargetLineOfSight(owner, far, true);
            CheckDecision(far.Hash, 7);

            void CheckDecision(int target, int action)
            {
                Assert.AreEqual(target, DecideFromCurrentObservation(mode, false, out int managed));
                Assert.AreEqual(target, DecideFromCurrentObservation(mode, true, out int burst));
                Assert.AreEqual(action, managed);
                Assert.AreEqual(managed, burst);
            }
        }

        [Test]
        public void ProjectileNear_WhenSnapshotChanges_ManagedAndBurstDistinguishUnknownZeroAndHostileCount()
        {
            AIMode mode = CreateMode();
            AICondition condition = new AICondition
            {
                conditionType = AIConditionType.ProjectileNear,
                compareOp = CompareOp.GreaterEqual,
                operandA = 1,
            };
            mode.actionRules[0].conditions = new[] { condition };
            mode.actions = new[] { mode.actions[0], new ActionSlot { execType = ActionExecType.Attack, paramId = 55 } };
            mode.defaultActionIndex = 1;

            _observations.Capture(++_tick, 0f);
            AIObservationContext observation = _observations.Observation;
            Assert.IsTrue(_core.Actors.TryGet(1, out ActorRef owner));
            Assert.IsTrue(_core.Actors.TryGet(10, out ActorRef target));
            condition.compareOp = CompareOp.Equal;
            condition.operandA = 0;
            mode.actionRules[0].conditions[0] = condition;
            Assert.IsFalse(ConditionEvaluator.Evaluate(condition, owner.Hash, target.Hash, observation));
            VerifyBoth(55);

            observation.SetNearbyHostileProjectileCount(owner, 0);
            observation.SetNearbyHostileProjectileCount(target, 2);
            Assert.IsTrue(ConditionEvaluator.Evaluate(condition, owner.Hash, target.Hash, observation));
            VerifyBoth(7);
            condition.compareOp = CompareOp.GreaterEqual;
            condition.operandA = 1;
            mode.actionRules[0].conditions[0] = condition;
            VerifyBoth(55);

            observation.SetNearbyHostileProjectileCount(owner, 2);
            VerifyBoth(7);

            _observations.Capture(++_tick, 0.02f);
            Assert.IsFalse(observation.TryGetNearbyHostileProjectileCount(owner, out _));
            VerifyBoth(55);

            void VerifyBoth(int action)
            {
                Assert.AreEqual(target.Hash, DecideFromCurrentObservation(mode, false, out int managed));
                Assert.AreEqual(target.Hash, DecideFromCurrentObservation(mode, true, out int burst));
                Assert.AreEqual(action, managed);
                Assert.AreEqual(action, burst);
            }
        }

        [Test]
        public void ProjectileNear_WhenUsedInAModeTransition_RequestsObservationEvenWithoutAnActionRule()
        {
            using JudgmentLoop judge = CreateLoop(1, new RecordingBackend());
            ModeController controller = new ModeController(judge);
            AIMode mode = CreateMode();
            AIMode[] modes = { mode, mode };
            Assert.IsFalse(judge.RequiresNearbyHostileProjectileCount);
            controller.SetModes(modes, new[] { new ModeTransitionRule
            {
                sourceModeIndex = 0,
                targetModeIndex = 1,
                conditions = new[] { new AICondition
                {
                    conditionType = AIConditionType.ProjectileNear,
                    compareOp = CompareOp.GreaterEqual,
                    operandA = 1,
                } },
            } });
            Assert.IsTrue(judge.RequiresNearbyHostileProjectileCount);
            _observations.Capture(++_tick, 0f);
            AIObservationContext snapshot = _observations.Observation;
            Assert.IsTrue(_core.Actors.TryGet(1, out ActorRef observer));
            controller.EvaluateTransitions(observer.Hash, 0, snapshot);
            Assert.AreEqual(0, controller.CurrentModeIndex);
            snapshot.SetNearbyHostileProjectileCount(observer, 1);
            controller.EvaluateTransitions(observer.Hash, 0, snapshot);
            Assert.AreEqual(1, controller.CurrentModeIndex);
            controller.SetModes(modes, null);
            Assert.IsFalse(judge.RequiresNearbyHostileProjectileCount);
        }

        private int SelectFromCurrentObservation(TargetFilter filter, bool scheduled)
        {
            AIMode mode = CreateMode();
            mode.targetSelects[0].filter = filter;
            return DecideFromCurrentObservation(mode, scheduled, out _);
        }

        private int DecideFromCurrentObservation(AIMode mode, bool scheduled, out int action)
        {
            var executor = new ActionExecutor();
            var backend = new RecordingBackend();
            executor.SetExecutionBackend(backend);
            using var loop = new JudgmentLoop(executor, _data, 1);
            loop.SetMode(mode);
            AIObservationContext observation = _observations.Observation;
            Assert.IsTrue(loop.UseObservation(observation, _observations.Targets));
            var candidates = new List<int> { 10, 20 };
            if (scheduled)
            {
                AIDecisionSystem decisions = _core.AIDecisions;
                decisions.BeginTick(observation);
                loop.PrepareTick(0.02f, candidates, observation.Time, decisions);
                decisions.Schedule();
                decisions.CompleteAndApply();
            }
            else
            {
                loop.Tick(0.02f, candidates, observation.Time);
            }
            action = backend.Action;
            return loop.CurrentTargetHash;
        }

        private void AddCharacter(int hash, CharacterBelong belong, Vector2 position)
        {
            _core.RegisterCharacter(hash, new CharacterVitals { position = position, currentHp = 100, maxHp = 100 },
                default, CharacterFlags.Pack(belong, 0, ActState.Neutral, AbilityFlag.None), default);
        }

        private JudgmentLoop CreateLoop(int owner, RecordingBackend backend)
        {
            ActionExecutor executor = new ActionExecutor();
            executor.SetExecutionBackend(backend);
            JudgmentLoop loop = new JudgmentLoop(executor, _data, owner);
            loop.SetMode(CreateMode());
            return loop;
        }

        private static AIMode CreateMode()
        {
            return new AIMode
            {
                targetRules = new[] { new AIRule { actionIndex = 0 } },
                targetSelects = new[] { new AITargetSelect { sortKey = TargetSortKey.Distance,
                    filter = new TargetFilter { belong = CharacterBelong.Enemy } } },
                actionRules = new[] { new AIRule { actionIndex = 0, conditions = new[] {
                    new AICondition { conditionType = AIConditionType.HpRatio,
                        compareOp = CompareOp.Greater, operandA = 0 } } } },
                actions = new[] { new ActionSlot { execType = ActionExecType.Attack, paramId = 7 } },
                defaultActionIndex = -1,
                judgeInterval = Vector2.zero
            };
        }

        private void RunTick(AIDecisionSystem decisions, JudgmentLoop loop, float deltaTime)
        {
            BeginTick(decisions, loop);
            loop.PrepareTick(deltaTime, new List<int> { 10, 20 }, 0f, decisions);
            decisions.Schedule();
            decisions.CompleteAndApply();
        }

        private void BeginTick(AIDecisionSystem decisions, params JudgmentLoop[] loops)
        {
            _observations.Capture(++_tick, 0f);
            decisions.BeginTick(_observations.Observation);
            for (int i = 0; i < loops.Length; i++)
            {
                Assert.IsTrue(loops[i].UseObservation(_observations.Observation, _observations.Targets));
            }
        }

        private sealed class RecordingBackend : IActionExecutionBackend
        {
            public int Count;
            public int Target;
            public int Action;
            public System.Action AfterExecute;
            public System.Action OnTick;
            public bool IsExecuting => false;
            public event System.Action OnActionCompleted { add { } remove { } }
            public bool Execute(int ownerHash, int targetHash, ActionSlot slot)
            {
                Count++;
                Target = targetHash;
                Action = slot.paramId;
                AfterExecute?.Invoke();
                return true;
            }
            public void CancelCurrent() { }
            public void Tick(float deltaTime) { OnTick?.Invoke(); }
        }
    }
}
