using NUnit.Framework;
using UnityEngine;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class ChallengeMode_RunnerTests
    {
        private ChallengeDefinition _definition;
        private ChallengeRunner _runner;

        [SetUp]
        public void SetUp()
        {
            _definition = ScriptableObject.CreateInstance<ChallengeDefinition>();
            _definition.SetForTest(
                challengeId: "test_challenge_01",
                challengeName: "テストチャレンジ",
                challengeType: ChallengeType.BossRush,
                timeLimit: 60f,
                maxDeathCount: 3,
                bossIds: new string[] { "boss_01", "boss_02" },
                waveCount: 3,
                enemiesPerWave: 5,
                silverTimeThreshold: 50f,
                goldTimeThreshold: 30f,
                goldScoreThreshold: 5000,
                platinumScoreThreshold: 10000,
                currencyReward: 100,
                itemRewardId: "item_reward_01"
            );
            _runner = new ChallengeRunner();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_definition);
        }

        // ----- Test 1: Start -----

        [Test]
        public void Start_WhenCalled_ShouldSetStateToRunning()
        {
            Assert.AreEqual(ChallengeState.Ready, _runner.State);

            _runner.Start(_definition);

            Assert.AreEqual(ChallengeState.Running, _runner.State);
            Assert.AreEqual(0f, _runner.ElapsedTime);
            Assert.AreEqual(0, _runner.CurrentWave);
            Assert.AreEqual(0, _runner.DeathCount);
            Assert.AreEqual(0, _runner.BossesDefeated);
            Assert.AreEqual(0, _runner.TotalDamageDealt);
            Assert.AreEqual(0, _runner.TotalDamageTaken);
        }

        // ----- Test 2: Tick time limit -----

        [Test]
        public void Tick_WhenTimeLimitExceeded_ShouldSetStateToFailed()
        {
            _runner.Start(_definition);

            // 59秒経過 — まだ Running
            _runner.Tick(59f);
            Assert.AreEqual(ChallengeState.Running, _runner.State);

            // 合計60秒 — timeLimit到達 → Failed
            _runner.Tick(1f);
            Assert.AreEqual(ChallengeState.Failed, _runner.State);
            Assert.AreEqual(60f, _runner.ElapsedTime, 0.001f);
        }

        [Test]
        public void Tick_WhenTimeLimitIsZero_ShouldNotFail()
        {
            ChallengeDefinition noLimit = ScriptableObject.CreateInstance<ChallengeDefinition>();
            noLimit.SetForTest(
                challengeId: "no_limit",
                challengeName: "制限なし",
                challengeType: ChallengeType.Survival,
                timeLimit: 0f
            );

            _runner.Start(noLimit);
            _runner.Tick(9999f);

            Assert.AreEqual(ChallengeState.Running, _runner.State);

            Object.DestroyImmediate(noLimit);
        }

        [Test]
        public void Tick_WhenStateIsNotRunning_ShouldNotUpdateElapsedTime()
        {
            // Ready状態ではTickしてもElapsedTimeは増えない
            _runner.Tick(5f);
            Assert.AreEqual(0f, _runner.ElapsedTime);

            // Failedになったら以降のTickは無視される
            _runner.Start(_definition);
            _runner.Tick(60f);
            Assert.AreEqual(ChallengeState.Failed, _runner.State);

            _runner.Tick(10f);
            Assert.AreEqual(60f, _runner.ElapsedTime, 0.001f);
        }

        // ----- Test 3: OnPlayerDeath -----

        [Test]
        public void OnPlayerDeath_WhenExceedsMaxDeathCount_ShouldSetStateToFailed()
        {
            _runner.Start(_definition);

            // maxDeathCount = 3
            _runner.OnPlayerDeath();
            Assert.AreEqual(1, _runner.DeathCount);
            Assert.AreEqual(ChallengeState.Running, _runner.State);

            _runner.OnPlayerDeath();
            Assert.AreEqual(2, _runner.DeathCount);
            Assert.AreEqual(ChallengeState.Running, _runner.State);

            _runner.OnPlayerDeath();
            Assert.AreEqual(3, _runner.DeathCount);
            Assert.AreEqual(ChallengeState.Running, _runner.State);

            // 4回目 — maxDeathCount超過 → Failed
            _runner.OnPlayerDeath();
            Assert.AreEqual(4, _runner.DeathCount);
            Assert.AreEqual(ChallengeState.Failed, _runner.State);
        }

        [Test]
        public void OnPlayerDeath_WhenMaxDeathCountIsZero_ShouldNeverFail()
        {
            ChallengeDefinition noDeath = ScriptableObject.CreateInstance<ChallengeDefinition>();
            noDeath.SetForTest(
                challengeId: "no_death_limit",
                challengeName: "死亡無制限",
                challengeType: ChallengeType.Survival,
                maxDeathCount: 0
            );

            _runner.Start(noDeath);

            for (int i = 0; i < 100; i++)
            {
                _runner.OnPlayerDeath();
            }

            Assert.AreEqual(ChallengeState.Running, _runner.State);
            Assert.AreEqual(100, _runner.DeathCount);

            Object.DestroyImmediate(noDeath);
        }

        // ----- Test 4: OnBossDefeated -----

        [Test]
        public void OnBossDefeated_WhenCalled_ShouldIncrementBossesDefeated()
        {
            _runner.Start(_definition);

            _runner.OnBossDefeated("boss_01");
            Assert.AreEqual(1, _runner.BossesDefeated);

            _runner.OnBossDefeated("boss_02");
            Assert.AreEqual(2, _runner.BossesDefeated);
        }

        [Test]
        public void OnWaveCleared_WhenCalled_ShouldIncrementCurrentWave()
        {
            _runner.Start(_definition);

            _runner.OnWaveCleared();
            Assert.AreEqual(1, _runner.CurrentWave);

            _runner.OnWaveCleared();
            Assert.AreEqual(2, _runner.CurrentWave);
        }

        // ----- Test 5: GetResult -----

        [Test]
        public void GetResult_WhenCompleted_ShouldReturnCorrectResult()
        {
            _runner.Start(_definition);

            // ダメージを蓄積
            int playerHash = 1;
            _runner.SetPlayerHash(playerHash);

            DamageResult dealtResult = new DamageResult { totalDamage = 500 };
            _runner.OnDamageDealt(dealtResult, attackerHash: playerHash, defenderHash: 2);

            DamageResult takenResult = new DamageResult { totalDamage = 100 };
            _runner.OnDamageDealt(takenResult, attackerHash: 3, defenderHash: playerHash);

            // ボス撃破
            _runner.OnBossDefeated("boss_01");
            _runner.OnBossDefeated("boss_02");

            // 時間経過
            _runner.Tick(25f);

            // 完了
            _runner.Complete();

            Assert.AreEqual(ChallengeState.Completed, _runner.State);

            ChallengeResult result = _runner.GetResult();
            Assert.AreEqual("test_challenge_01", result.challengeId);
            Assert.AreEqual(25f, result.clearTime, 0.001f);
            Assert.AreEqual(0, result.deathCount);
            Assert.AreEqual(500, result.totalDamageDealt);
            Assert.AreEqual(100, result.totalDamageTaken);
        }

        [Test]
        public void GetResult_WhenGoldTime_ShouldReturnGoldRank()
        {
            _runner.Start(_definition);
            _runner.Tick(25f); // goldTimeThreshold = 30f 以下
            _runner.Complete();

            ChallengeResult result = _runner.GetResult();
            Assert.AreEqual(ChallengeRank.Gold, result.rank);
        }

        [Test]
        public void GetResult_WhenSilverTime_ShouldReturnSilverRank()
        {
            _runner.Start(_definition);
            _runner.Tick(45f); // silverTimeThreshold = 50f 以下、goldTimeThreshold = 30f 超過
            _runner.Complete();

            ChallengeResult result = _runner.GetResult();
            Assert.AreEqual(ChallengeRank.Silver, result.rank);
        }

        [Test]
        public void GetResult_WhenBronzeTime_ShouldReturnBronzeRank()
        {
            _runner.Start(_definition);
            _runner.Tick(55f); // silverTimeThreshold = 50f 超過
            _runner.Complete();

            ChallengeResult result = _runner.GetResult();
            Assert.AreEqual(ChallengeRank.Bronze, result.rank);
        }

        [Test]
        public void OnDamageDealt_WhenPlayerIsAttacker_ShouldAccumulateDamageDealt()
        {
            int playerHash = 42;
            _runner.Start(_definition);
            _runner.SetPlayerHash(playerHash);

            DamageResult result1 = new DamageResult { totalDamage = 200 };
            DamageResult result2 = new DamageResult { totalDamage = 300 };

            _runner.OnDamageDealt(result1, attackerHash: playerHash, defenderHash: 10);
            _runner.OnDamageDealt(result2, attackerHash: playerHash, defenderHash: 20);

            Assert.AreEqual(500, _runner.TotalDamageDealt);
            Assert.AreEqual(0, _runner.TotalDamageTaken);
        }

        [Test]
        public void OnDamageDealt_WhenPlayerIsDefender_ShouldAccumulateDamageTaken()
        {
            int playerHash = 42;
            _runner.Start(_definition);
            _runner.SetPlayerHash(playerHash);

            DamageResult result1 = new DamageResult { totalDamage = 50 };
            DamageResult result2 = new DamageResult { totalDamage = 75 };

            _runner.OnDamageDealt(result1, attackerHash: 10, defenderHash: playerHash);
            _runner.OnDamageDealt(result2, attackerHash: 20, defenderHash: playerHash);

            Assert.AreEqual(0, _runner.TotalDamageDealt);
            Assert.AreEqual(125, _runner.TotalDamageTaken);
        }
    }
}
