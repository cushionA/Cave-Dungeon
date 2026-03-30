using NUnit.Framework;
using UnityEngine;
using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// EnemySpawnerManager結合テスト。
    /// Coreスポーンロジックとイベント駆動の整合性を検証する。
    /// </summary>
    public class Integration_EnemySpawnerManagerTests
    {
        [Test]
        public void EnemySpawner_SpawnDespawnSpawn_StateConsistent()
        {
            // 連続操作で状態が壊れないか検証
            EnemySpawner spawner = new EnemySpawner(5);
            spawner.AddSpawnPoint(new SpawnPointData
            {
                position = Vector2.zero,
                activateRange = 10f,
                respawnDelay = 0f
            });

            int spawnCount = 0;
            int despawnCount = 0;
            spawner.OnEnemySpawned += (hash, point) => spawnCount++;
            spawner.OnEnemyDespawned += (hash) => despawnCount++;

            // スポーン → デスポーン → 再スポーン
            int hash1 = spawner.SpawnEnemy(0);
            Assert.AreEqual(1, spawnCount);

            spawner.DespawnEnemy(hash1);
            Assert.AreEqual(1, despawnCount);
            Assert.AreEqual(0, spawner.ActiveCount);

            int hash2 = spawner.SpawnEnemy(0);
            Assert.AreEqual(2, spawnCount);
            Assert.AreEqual(1, spawner.ActiveCount);
            Assert.AreNotEqual(hash1, hash2, "再スポーンで異なるハッシュが割り振られる");
        }

        [Test]
        public void EnemySpawner_RefreshAll_FiresDespawnForEachActive()
        {
            // RefreshAllが各アクティブ敵に対してDespawnイベントを発火するか
            EnemySpawner spawner = new EnemySpawner(10);
            spawner.AddSpawnPoint(new SpawnPointData { position = Vector2.zero, activateRange = 10f });
            spawner.AddSpawnPoint(new SpawnPointData { position = new Vector2(5f, 0f), activateRange = 10f });

            spawner.SpawnEnemy(0);
            spawner.SpawnEnemy(1);
            spawner.SpawnEnemy(0);

            int despawnCount = 0;
            spawner.OnEnemyDespawned += (hash) => despawnCount++;

            spawner.RefreshAll();

            Assert.AreEqual(3, despawnCount, "RefreshAllは全アクティブ敵のDespawnイベントを発火する");
            Assert.AreEqual(0, spawner.ActiveCount);
        }

        [Test]
        public void EnemySpawner_EvaluateRespawnDelay_RespectsTimer()
        {
            // リスポーン遅延が尊重されるか
            EnemySpawner spawner = new EnemySpawner();
            spawner.AddSpawnPoint(new SpawnPointData
            {
                position = new Vector2(3f, 0f),
                activateRange = 10f,
                respawnDelay = 5f
            });

            // t=0: 初回スポーン
            spawner.EvaluateSpawnPoints(Vector2.zero, 0f);
            Assert.AreEqual(1, spawner.ActiveCount);

            int hash = spawner.SpawnedEnemyHashes[0];
            spawner.DespawnEnemy(hash);
            Assert.AreEqual(0, spawner.ActiveCount);

            // t=3: まだリスポーン遅延中
            spawner.EvaluateSpawnPoints(Vector2.zero, 3f);
            Assert.AreEqual(0, spawner.ActiveCount, "リスポーン遅延中はスポーンしない");

            // t=5: リスポーン遅延経過
            spawner.EvaluateSpawnPoints(Vector2.zero, 5f);
            Assert.AreEqual(1, spawner.ActiveCount, "リスポーン遅延経過後はスポーンする");
        }

        [Test]
        public void EnemySpawner_EventUnsubscribe_NoLeaks()
        {
            // イベント購読解除の対称性テスト
            EnemySpawner spawner = new EnemySpawner();
            spawner.AddSpawnPoint(new SpawnPointData { position = Vector2.zero, activateRange = 10f });

            int callCount = 0;
            System.Action<int, SpawnPointData> handler = (hash, point) => callCount++;

            spawner.OnEnemySpawned += handler;
            spawner.SpawnEnemy(0);
            Assert.AreEqual(1, callCount);

            // 購読解除後はイベントが発火しない
            spawner.OnEnemySpawned -= handler;
            spawner.SpawnEnemy(0);
            Assert.AreEqual(1, callCount, "購読解除後はイベントが発火しない");
        }
    }
}
