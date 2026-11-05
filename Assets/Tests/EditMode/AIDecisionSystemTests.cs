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
