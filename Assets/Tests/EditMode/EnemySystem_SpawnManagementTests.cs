using NUnit.Framework;
using UnityEngine;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class EnemySystem_SpawnManagementTests
    {
        [Test]
        public void EnemySpawner_SpawnEnemy_ReturnsHash()
        {
            EnemySpawner spawner = new EnemySpawner();
            spawner.AddSpawnPoint(new SpawnPointData
            {
                position = Vector2.zero,
                activateRange = 10f
            });

            int hash = spawner.SpawnEnemy(0);

            Assert.AreNotEqual(0, hash);
            Assert.AreEqual(1, spawner.ActiveCount);
        }

        [Test]
        public void EnemySpawner_MaxActive_RejectsSpawn()
        {
            EnemySpawner spawner = new EnemySpawner(2);
            spawner.AddSpawnPoint(new SpawnPointData { position = Vector2.zero });

            spawner.SpawnEnemy(0);
            spawner.SpawnEnemy(0);
            int hash = spawner.SpawnEnemy(0);

            Assert.AreEqual(0, hash);
            Assert.AreEqual(2, spawner.ActiveCount);
        }

        [Test]
        public void EnemySpawner_RefreshAll_ClearsAll()
        {
            EnemySpawner spawner = new EnemySpawner();
            spawner.AddSpawnPoint(new SpawnPointData { position = Vector2.zero });
            spawner.SpawnEnemy(0);
            spawner.SpawnEnemy(0);

            spawner.RefreshAll();

            Assert.AreEqual(0, spawner.ActiveCount);
        }

        [Test]
        public void EnemySpawner_DespawnEnemy_RemovesHash()
        {
            EnemySpawner spawner = new EnemySpawner();
            spawner.AddSpawnPoint(new SpawnPointData { position = Vector2.zero });
            int hash = spawner.SpawnEnemy(0);

            spawner.DespawnEnemy(hash);

            Assert.AreEqual(0, spawner.ActiveCount);
        }

        [Test]
        public void EnemySpawner_EvaluateSpawnPoints_SpawnsInRange()
        {
            EnemySpawner spawner = new EnemySpawner();
            spawner.AddSpawnPoint(new SpawnPointData
            {
                position = new Vector2(5f, 0f),
                activateRange = 10f,
                respawnDelay = 5f
            });

            spawner.EvaluateSpawnPoints(Vector2.zero, 0f);

            Assert.AreEqual(1, spawner.ActiveCount);
        }

        [Test]
        public void EnemySpawner_InvalidIndex_ReturnsZero()
        {
            EnemySpawner spawner = new EnemySpawner();

            int hash = spawner.SpawnEnemy(-1);
            Assert.AreEqual(0, hash);

            hash = spawner.SpawnEnemy(0);
            Assert.AreEqual(0, hash);
        }

        [Test]
        public void EnemySpawner_OnEnemySpawned_FiresEvent()
        {
            EnemySpawner spawner = new EnemySpawner();
            spawner.AddSpawnPoint(new SpawnPointData { position = Vector2.zero });

            int firedHash = 0;
            spawner.OnEnemySpawned += (hash, point) => firedHash = hash;

            int spawnedHash = spawner.SpawnEnemy(0);

            Assert.AreEqual(spawnedHash, firedHash);
        }

        [Test]
        public void EnemySpawner_OnEnemyDespawned_FiresEvent()
        {
            EnemySpawner spawner = new EnemySpawner();
            spawner.AddSpawnPoint(new SpawnPointData { position = Vector2.zero });
            int spawnedHash = spawner.SpawnEnemy(0);

            int firedHash = 0;
            spawner.OnEnemyDespawned += (hash) => firedHash = hash;

            spawner.DespawnEnemy(spawnedHash);

            Assert.AreEqual(spawnedHash, firedHash);
        }
    }
}
