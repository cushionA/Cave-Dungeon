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
