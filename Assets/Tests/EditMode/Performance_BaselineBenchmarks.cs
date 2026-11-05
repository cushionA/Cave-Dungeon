using System;
using NUnit.Framework;
using Unity.PerformanceTesting;
using R3;
using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// パフォーマンスベースラインベンチマーク。
    /// SoAアクセス、GameEventsのR3 Subject発火、基本操作の性能を計測。
    /// </summary>
    public class Performance_BaselineBenchmarks
    {
        [Test, Performance]
        public void SoADataAccess_100Characters_ZeroAlloc()
        {
            SoACharaDataDic dic = new SoACharaDataDic(128);
            int[] hashes = new int[100];

            // 100キャラ登録
            for (int i = 0; i < 100; i++)
            {
                hashes[i] = i + 1;
                CharacterVitals vitals = new CharacterVitals { currentHp = 100, maxHp = 100 };
                CombatStats combat = new CombatStats();
                CharacterFlags flags = new CharacterFlags();
                MoveParams move = new MoveParams();
                dic.Add(hashes[i], vitals, combat, flags, move);
            }

            Measure.Method(() =>
            {
                for (int i = 0; i < 100; i++)
                {
                    dic.GetVitals(hashes[i]);
                }
            })
            .WarmupCount(5)
            .MeasurementCount(20)
            .IterationsPerMeasurement(100)
            .GC()
            .Run();

            dic.Dispose();
        }

        [Test, Performance]
        public void AIJudgment_100Characters_CompareSynchronousAndBurstBatch()
        {
            using (GameManagerCore core = new GameManagerCore())
            {
                core.Initialize(128);
                SoACharaDataDic data = core.Data;
                AIDecisionSystem decisions = core.AIDecisions;
                using AIObservationCoordinator observations = new AIObservationCoordinator(data, core.Actors, core.Events);
                System.Collections.Generic.List<int> candidates = new System.Collections.Generic.List<int>(100);
                JudgmentLoop[] loops = new JudgmentLoop[100];
                long tick = 0;
                AIMode mode = new AIMode
                {
                    targetRules = new[] { new AIRule { actionIndex = 0 } },
                    targetSelects = new[] { new AITargetSelect { sortKey = TargetSortKey.Distance } },
                    actionRules = Array.Empty<AIRule>(),
                    actions = Array.Empty<ActionSlot>(),
                    defaultActionIndex = -1,
                    judgeInterval = UnityEngine.Vector2.zero
                };
                for (int i = 0; i < loops.Length; i++)
                {
                    int hash = i + 1;
                    core.RegisterCharacter(hash, new CharacterVitals
                    {
                        currentHp = 100, maxHp = 100, position = new UnityEngine.Vector2(i, 0f)
                    }, default, default, default);
                    candidates.Add(hash);
                    loops[i] = new JudgmentLoop(new ActionExecutor(), data, hash);
                    loops[i].SetMode(mode);
                }

                Measure.Method(() =>
                {
                    PrepareObservation();
                    for (int i = 0; i < loops.Length; i++)
                    {
                        loops[i].Tick(0.02f, candidates, 0f);
                    }
                })
                .SampleGroup("Synchronous")
                .WarmupCount(5)
                .MeasurementCount(20)
                .IterationsPerMeasurement(10)
                .GC()
                .Run();

                int expectedTarget = loops[0].CurrentTargetHash;
                Measure.Method(() =>
                {
                    PrepareObservation();
                    decisions.BeginTick(observations.Observation);
                    for (int i = 0; i < loops.Length; i++)
                    {
                        loops[i].PrepareTick(0.02f, candidates, 0f, decisions);
                    }
                    decisions.Schedule();
                    decisions.CompleteAndApply();
                })
                .SampleGroup("BurstBatch")
                .WarmupCount(5)
                .MeasurementCount(20)
                .IterationsPerMeasurement(10)
                .GC()
                .Run();
                Assert.AreEqual(2, expectedTarget);
                Assert.AreEqual(expectedTarget, loops[0].CurrentTargetHash);
                for (int i = 0; i < loops.Length; i++) { loops[i].Dispose(); }

                void PrepareObservation()
                {
                    observations.Capture(++tick, 0f);
                    for (int i = 0; i < loops.Length; i++)
                    {
                        loops[i].UseObservation(observations.Observation, observations.Targets);
                    }
                }
            }
        }

        [Test, Performance]
        public void R3Subject_Fire_10Subscribers()
        {
            Subject<int> subject = new Subject<int>();
            int count = 0;

            // 10個の購読者を登録
            IDisposable[] subs = new System.IDisposable[10];
            for (int i = 0; i < 10; i++)
            {
                subs[i] = subject.Subscribe(v => count += v);
            }

            Measure.Method(() =>
            {
                for (int i = 0; i < 100; i++)
                {
                    subject.OnNext(1);
                }
            })
            .WarmupCount(5)
            .MeasurementCount(20)
            .IterationsPerMeasurement(100)
            .GC()
            .Run();

            foreach (System.IDisposable sub in subs)
            {
                sub.Dispose();
            }
            subject.Dispose();
        }

        [Test, Performance]
        public void GameEvents_FireDamageDealt_WithSubscriber()
        {
            GameEvents events = new GameEvents();
            int hitCount = 0;

            IDisposable sub = events.OnDamageDealt.Subscribe(e => hitCount++);

            DamageResult result = new DamageResult { totalDamage = 10 };

            Measure.Method(() =>
            {
                for (int i = 0; i < 100; i++)
                {
                    events.FireDamageDealt(result, 1, 2);
                }
            })
            .WarmupCount(5)
            .MeasurementCount(20)
            .IterationsPerMeasurement(100)
            .GC()
            .Run();

            sub.Dispose();
            events.Dispose();
        }

        [Test, Performance]
        public void GameManagerCore_RegisterUnregister_100Characters()
        {
            GameManagerCore core = new GameManagerCore();
            core.Initialize(128);

            Measure.Method(() =>
            {
                for (int i = 0; i < 100; i++)
                {
                    int hash = 10000 + i;
                    CharacterVitals vitals = new CharacterVitals { currentHp = 100, maxHp = 100 };
                    CombatStats combat = new CombatStats();
                    CharacterFlags flags = new CharacterFlags();
                    MoveParams move = new MoveParams();
                    core.RegisterCharacter(hash, vitals, combat, flags, move);
                }

                for (int i = 0; i < 100; i++)
                {
                    core.UnregisterCharacter(10000 + i);
                }
            })
            .WarmupCount(3)
            .MeasurementCount(10)
            .IterationsPerMeasurement(10)
            .GC()
            .Run();

            core.Dispose();
        }
    }
}
