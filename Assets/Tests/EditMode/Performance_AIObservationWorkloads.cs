using System;
using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;
using Unity.PerformanceTesting;
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
