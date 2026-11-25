using System;
using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;
using Unity.PerformanceTesting;
using Unity.PerformanceTesting.Measurements;
using Unity.Profiling;
using UnityEngine;

namespace Game.Tests.EditMode
{
    public class Performance_AIObservationWorkloads
    {
        public enum Workload { Distance, GuardAndStun, DamageScore, Gimmick }
        public enum Traversal { Ordered, Shuffled }

        private const int OwnerCount = 100;
        private const int FirstEnemy = 1001;
        private const int Hazard = 1 << 4;
        private const float DeltaTime = 0.02f;
        private const string BatchPreparation = "AI.Workload.Burst.Prepare";
        private const string BatchSchedule = "AI.Workload.Burst.CopyAndSchedule";
        private static readonly ProfilerMarker s_BatchPreparation = new ProfilerMarker(BatchPreparation);
        private static readonly ProfilerMarker s_BatchSchedule = new ProfilerMarker(BatchSchedule);

        [Performance]
        [TestCase(Workload.Distance, Traversal.Ordered, 100)]
        [TestCase(Workload.Distance, Traversal.Shuffled, 100)]
        [TestCase(Workload.GuardAndStun, Traversal.Ordered, 100)]
        [TestCase(Workload.GuardAndStun, Traversal.Shuffled, 100)]
        [TestCase(Workload.DamageScore, Traversal.Ordered, 100)]
        [TestCase(Workload.DamageScore, Traversal.Shuffled, 100)]
        [TestCase(Workload.Gimmick, Traversal.Ordered, 100)]
        [TestCase(Workload.Gimmick, Traversal.Shuffled, 100)]
        [TestCase(Workload.DamageScore, Traversal.Ordered, 1000)]
        [TestCase(Workload.DamageScore, Traversal.Shuffled, 1000)]
        public void Compare(Workload workload, Traversal traversal, int candidateCount)
        {
            bool jobFirst = Environment.GetEnvironmentVariable("AI_BENCHMARK_BURST_FIRST") == "1";
            Run(jobFirst);
            Run(!jobFirst);

            void Run(bool scheduled)
            {
                using var world = new BenchmarkWorld(workload, traversal, candidateCount);
                world.Step(scheduled);
                world.AssertOutcomes();
                Measure.Method(() => world.Step(scheduled))
                    .SampleGroup(scheduled ? "BurstBatch" : "Synchronous")
                    .WarmupCount(3).MeasurementCount(12).IterationsPerMeasurement(3).GC().Run();
                world.AssertOutcomes();
            }
        }

        [Performance, Category("AITerrainProfile")]
        [TestCase(8, 32, 1)]
        [TestCase(32, 64, 4)]
        [TestCase(100, 100, 8)]
        public void CompareSharedSnapshot(int ownerCount, int candidateCount, int actionRuleCount)
        {
            using GameManagerCore core = new GameManagerCore();
            core.Initialize(ownerCount + candidateCount);
            using AIObservationCoordinator observations = new AIObservationCoordinator(core.Data, core.Actors, core.Events,
                statusEffects: core.StatusEffects.Store);
            AIDecisionSystem decisions = core.AIDecisions;
            AITargetRelations syncTargets = new AITargetRelations();
            AITargetRelations burstTargets = new AITargetRelations();
            List<int> candidates = new List<int>(candidateCount);
            ActorRef[] enemies = new ActorRef[candidateCount];
            ActorRef[] owners = new ActorRef[ownerCount];
            JudgmentLoop[] sync = new JudgmentLoop[ownerCount];
            JudgmentLoop[] batch = new JudgmentLoop[ownerCount];
            CommandRecorder[] syncCommands = new CommandRecorder[ownerCount];
            CommandRecorder[] batchCommands = new CommandRecorder[ownerCount];
            AIMode mode = CreateMultiRuleMode(actionRuleCount);

            try
            {
                for (int i = 0; i < candidateCount; i++)
                {
                    int hash = FirstEnemy + i;
                    enemies[i] = Register(hash, CharacterBelong.Enemy, new Vector2(i, 4f), 100, 100);
                    candidates.Add(hash);
                }
                for (int i = 0; i < ownerCount; i++)
                {
                    int hash = i + 1;
                    owners[i] = Register(hash, CharacterBelong.Ally, new Vector2(i, 0f), actionRuleCount - i % actionRuleCount, actionRuleCount);
                    sync[i] = CreateLoop(core.Data, hash, mode, out syncCommands[i]);
                    batch[i] = CreateLoop(core.Data, hash, mode, out batchCommands[i]);
                }

                long tick = 0;
                PrepareMeasurement(CaptureObservation, "ObservationCapture").Run();
                CaptureObservation();
                AIObservationContext snapshot = observations.Observation;
                RunSync();
                RunBatch();
                AssertOutcomes(minimumCommands: 1);

                bool jobFirst = Environment.GetEnvironmentVariable("AI_BENCHMARK_BURST_FIRST") == "1";
                if (jobFirst) { MeasureBatch(); }
                PrepareMeasurement(RunSync, "SynchronousDecision").Run();
                if (!jobFirst) { MeasureBatch(); }
                AssertOutcomes(minimumCommands: 2);

                void CaptureObservation() => observations.Capture(++tick, tick * DeltaTime);

                void RunSync()
                {
                    foreach (JudgmentLoop loop in sync)
                    {
                        loop.UseObservation(snapshot, syncTargets);
                        loop.Tick(DeltaTime, candidates, snapshot.Time);
                    }
                }

                void RunBatch()
                {
                    using (s_BatchPreparation.Auto())
                    {
                        decisions.BeginTick(snapshot);
                        foreach (JudgmentLoop loop in batch)
                        {
                            loop.UseObservation(snapshot, burstTargets);
                            loop.PrepareTick(DeltaTime, candidates, snapshot.Time, decisions);
                        }
                    }
                    using (s_BatchSchedule.Auto()) { decisions.Schedule(); }
                    decisions.CompleteAndApply();
                }

                void MeasureBatch() => PrepareMeasurement(RunBatch, "BurstDecision")
                    .ProfilerMarkers(BatchPreparation, BatchSchedule, "AI.Decision.Complete", "AI.Decision.Apply").Run();

                void AssertOutcomes(int minimumCommands)
                {
                    for (int i = 0; i < ownerCount; i++)
                    {
                        ActorRef expected = enemies[i];
                        Assert.IsTrue(syncTargets.TryGetTarget(owners[i], out ActorRef syncTarget));
                        Assert.IsTrue(burstTargets.TryGetTarget(owners[i], out ActorRef burstTarget));
                        Assert.AreEqual(expected, syncTarget);
                        Assert.AreEqual(syncTarget, burstTarget);
                        Assert.AreEqual(snapshot.Tick, sync[i].ObservationTick);
                        Assert.AreEqual(snapshot.Tick, batch[i].ObservationTick);
                        Assert.AreEqual(syncCommands[i].Count, batchCommands[i].Count);
                        AssertCommand(syncCommands[i], owners[i].Hash, expected.Hash, i % actionRuleCount, minimumCommands);
                        AssertCommand(batchCommands[i], owners[i].Hash, expected.Hash, i % actionRuleCount, minimumCommands);
                    }
                }
            }
            finally
            {
                foreach (JudgmentLoop loop in sync) { loop?.Dispose(); }
                foreach (JudgmentLoop loop in batch) { loop?.Dispose(); }
            }

            ActorRef Register(int hash, CharacterBelong belong, Vector2 position, int hp, int maxHp)
            {
                core.RegisterCharacter(hash, new CharacterVitals { position = position, currentHp = hp, maxHp = maxHp },
                    default, CharacterFlags.Pack(belong, default, ActState.Neutral, default), default);
                Assert.IsTrue(core.Actors.TryGet(hash, out ActorRef actor));
                // 両経路は同じ観測を読み、実行後の標的関係は個別に保持する。
                syncTargets.Register(actor);
                burstTargets.Register(actor);
                return actor;
            }
        }

        private static MethodMeasurement PrepareMeasurement(Action action, string sample)
            => Measure.Method(action).SampleGroup(sample).WarmupCount(2).MeasurementCount(8).IterationsPerMeasurement(1).GC();

        private static JudgmentLoop CreateLoop(SoACharaDataDic data, int hash, AIMode mode, out CommandRecorder recorder)
        {
            recorder = new CommandRecorder();
            ActionExecutor executor = new ActionExecutor();
            executor.SetExecutionBackend(recorder);
            JudgmentLoop loop = new JudgmentLoop(executor, data, hash);
            loop.SetMode(mode);
            return loop;
        }

        private static AIMode CreateMultiRuleMode(int ruleCount)
        {
            AIMode mode = CreateMode(Workload.Distance);
            mode.actionRules = new AIRule[ruleCount];
            mode.actions = new ActionSlot[ruleCount];
            for (int i = 0; i < ruleCount; i++)
            {
                mode.actionRules[i] = new AIRule { actionIndex = i, conditions = new[] { new AICondition
                {
                    conditionType = AIConditionType.HpRatio, compareOp = CompareOp.Greater,
                    filter = new TargetFilter { includeSelf = true },
                    operandA = (100 * (ruleCount - i) - 50) / ruleCount,
                } } };
                mode.actions[i] = new ActionSlot { execType = ActionExecType.Attack, paramId = i };
            }
            return mode;
        }

        private static void AssertCommand(CommandRecorder record, int owner, int target, int action, int minimumCommands)
        {
            Assert.GreaterOrEqual(record.Count, minimumCommands);
            Assert.AreEqual(owner, record.Owner);
            Assert.AreEqual(target, record.Target);
            Assert.AreEqual(ActionCommandKind.Attack, record.Command.kind);
            Assert.AreEqual(action, record.Command.attack.legacyIndex);
        }

        private sealed class BenchmarkWorld : IDisposable
        {
            private readonly Workload _workload;
            private readonly GameManagerCore _core = new GameManagerCore();
            private readonly AIObservationCoordinator _observations;
            private readonly JudgmentLoop[] _loops = new JudgmentLoop[OwnerCount];
            private readonly CommandRecorder[] _commands = new CommandRecorder[OwnerCount];
            private readonly ActorRef[] _enemies;
            private readonly ActorRef _player;
            private readonly List<int> _candidates;
            private long _tick;

            public BenchmarkWorld(Workload workload, Traversal traversal, int candidateCount)
            {
                _workload = workload;
                _core.Initialize(OwnerCount + candidateCount + 1);
                _observations = new AIObservationCoordinator(_core.Data, _core.Actors, _core.Events,
                    statusEffects: _core.StatusEffects.Store);
                Vector2 playerPosition = workload == Workload.Gimmick ? new Vector2(2f, 4f) : new Vector2(0f, -5f);
                _player = Register(501, CharacterBelong.Ally, playerPosition, CharacterFeature.Player);
                AIMode mode = CreateMode(workload);
                for (int i = 0; i < OwnerCount; i++)
                {
                    Register(i + 1, CharacterBelong.Ally, new Vector2(i + 0.25f, 0f));
                    _commands[i] = new CommandRecorder();
                    var executor = new ActionExecutor();
                    executor.SetExecutionBackend(_commands[i]);
                    _loops[i] = new JudgmentLoop(executor, _core.Data, i + 1);
                    _loops[i].SetMode(mode);
                }
                _enemies = new ActorRef[candidateCount];
                _candidates = new List<int>(candidateCount);
                for (int i = 0; i < candidateCount; i++)
                {
                    _enemies[i] = Register(FirstEnemy + i, CharacterBelong.Enemy, new Vector2(i, 4f));
                    _candidates.Add(_enemies[i].Hash);
                    if (workload == Workload.GuardAndStun)
                    {
                        if (i % 2 == 0) { _observations.Targets.SetTarget(_enemies[i], _player); }
                        // Each capture reads these generation-bound effects; advancing combat timers is outside the workload.
                        Assert.IsTrue(_core.StatusEffects.Apply(_enemies[i].Hash,
                            new StatusEffectInfo { effect = StatusEffectId.Stun, duration = i % 4 < 2 ? 2f : 0.5f }));
                    }
                }
                if (workload == Workload.DamageScore)
                {
                    for (int i = 0; i < OwnerCount; i++)
                    {
                        int winner = (37 * i + 11) % candidateCount;
                        for (int j = 0; j < 4; j++)
                        {
                            int attacker = _enemies[(winner + 13 * j) % candidateCount].Hash;
                            _core.Events.FireDamageDealt(new DamageResult { totalDamage = 40 - j * 10 }, attacker, i + 1);
                        }
                    }
                }
                if (traversal == Traversal.Shuffled)
                {
                    var random = new System.Random(20261124);
                    for (int i = candidateCount - 1; i > 0; i--)
                    {
                        int j = random.Next(i + 1);
                        (_candidates[i], _candidates[j]) = (_candidates[j], _candidates[i]);
                    }
                }
            }

            private ActorRef Register(int hash, CharacterBelong belong, Vector2 position, CharacterFeature feature = default)
            {
                _core.RegisterCharacter(hash, new CharacterVitals { currentHp = 100, maxHp = 100, position = position },
                    default, CharacterFlags.Pack(belong, feature, ActState.Neutral, default), default);
                if (!_core.Actors.TryGet(hash, out ActorRef actor)) { throw new InvalidOperationException(); }
                return actor;
            }

            // One measured call covers fresh capture, observation binding, judgment and command dispatch.
            public void Step(bool scheduled)
            {
                float time = ++_tick * DeltaTime;
                _observations.Capture(_tick, time);
                AIObservationContext snapshot = _observations.Observation;
                if (_workload == Workload.GuardAndStun)
                {
                    snapshot.SetProtectedPlayer(_player);
                }
                if (_workload == Workload.Gimmick)
                {
                    snapshot.BeginGimmickObservation();
                    for (int i = 0; i < 10; i++)
                    {
                        int x = i * 10 + 2;
                        snapshot.AddGimmick(new AISceneGimmick { id = i + 1, generation = 1, objectMask = Hazard,
                            operationPosition = new Vector2(x, 0f), operationRadius = 8f,
                            effectPosition = new Vector2(x, 4f), effectRadius = 2.25f });
                    }
                }
                AIDecisionSystem decisions = _core.AIDecisions;
                if (scheduled) { decisions.BeginTick(snapshot); }
                foreach (JudgmentLoop loop in _loops)
                {
                    loop.UseObservation(snapshot, _observations.Targets);
                    if (scheduled) { loop.PrepareTick(DeltaTime, _candidates, time, decisions); }
                    else { loop.Tick(DeltaTime, _candidates, time); }
                }
                if (scheduled)
                {
                    decisions.Schedule();
                    decisions.CompleteAndApply();
                }
            }

            public void AssertOutcomes()
            {
                for (int i = 0; i < OwnerCount; i++)
                {
                    int enemyIndex = i;
                    if (_workload == Workload.DamageScore) { enemyIndex = (37 * i + 11) % _enemies.Length; }
                    // Eligible enemies sit at x=0,4,...,96. The owner's +0.25 offset breaks distance ties.
                    if (_workload == Workload.GuardAndStun) { enemyIndex = Math.Min(96, (i + 2) / 4 * 4); }
                    int target = FirstEnemy + enemyIndex;
                    // The player stands inside the first switch's effect, blocking its five otherwise valid attacks.
                    bool usesSwitch = _workload == Workload.Gimmick && i >= 10 && i % 10 < 5;
                    CommandRecorder record = _commands[i];
                    Assert.AreEqual(target, _loops[i].CurrentTargetHash);
                    Assert.AreEqual(_tick, record.Count, "Every tick must dispatch one command for owner " + (i + 1));
                    Assert.AreEqual(i + 1, record.Owner);
                    Assert.AreEqual(target, record.Target);
                    Assert.AreEqual(usesSwitch ? ActionCommandKind.InteractObject : ActionCommandKind.Attack, record.Command.kind);
                    if (usesSwitch)
                    {
                        Assert.AreEqual(i / 10 + 1, record.Command.gimmick.id);
                        Assert.AreEqual(1, record.Command.gimmick.generation);
                        Assert.AreEqual(_tick, record.Command.observationTick);
                        Assert.AreEqual(i + 1, record.Command.gimmickOwner.Hash);
                        Assert.AreEqual(_enemies[enemyIndex], record.Command.gimmickTarget);
                    }
                }
            }

            public void Dispose()
            {
                foreach (JudgmentLoop loop in _loops) { loop.Dispose(); }
                _observations.Dispose();
                _core.Dispose();
            }
        }

        private static AIMode CreateMode(Workload workload)
        {
            bool guard = workload == Workload.GuardAndStun;
            bool score = workload == Workload.DamageScore;
            var nearest = new AITargetSelect {
                sortKey = score ? TargetSortKey.DamageScore : TargetSortKey.Distance, isDescending = score,
                filter = new TargetFilter { belong = CharacterBelong.Enemy,
                    targetsProtectedPlayer = guard, stunMustLastBeyondSeconds = guard ? 1f : 0f }
            };
            var attack = new ActionSlot { execType = ActionExecType.Attack };
            var mode = new AIMode {
                targetRules = new[] { new AIRule { actionIndex = 0 } }, targetSelects = new[] { nearest },
                actionRules = new[] { new AIRule { actionIndex = 0 } }, actions = new[] { attack },
                defaultActionIndex = -1, judgeInterval = Vector2.zero
            };
            if (workload == Workload.Gimmick)
            {
                mode.actionRules[0].conditions = new[] { new AICondition {
                    conditionType = AIConditionType.ObjectNearby, compareOp = CompareOp.HasAny, operandA = Hazard } };
                mode.actions = new[] { new ActionSlot { execType = ActionExecType.Instant,
                    paramId = (int)InstantAction.InteractObject }, attack };
                mode.defaultActionIndex = 1;
            }
            return mode;
        }

        private sealed class CommandRecorder : IActionCommandBackend
        {
            public int Count, Owner, Target;
            public ActionCommand Command;
            public bool IsExecuting => false;
            public event Action OnActionCompleted { add { } remove { } }
            public bool Supports(ActionCommandKind kind) =>
                kind == ActionCommandKind.Attack || kind == ActionCommandKind.InteractObject;
            public ActionDispatchResult ExecuteCommand(int ownerHash, int targetHash, in ActionCommand command)
            {
                Count++;
                Owner = ownerHash;
                Target = targetHash;
                Command = command;
                return new ActionDispatchResult(ActionDispatchStatus.Accepted);
            }
            public bool Execute(int ownerHash, int targetHash, ActionSlot slot) => throw new InvalidOperationException();
            public void CancelCurrent() { }
            public void Tick(float deltaTime) { }
        }
    }
}
