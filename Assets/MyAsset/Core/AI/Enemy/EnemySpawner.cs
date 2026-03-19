using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// Data describing a single spawn point for enemies.
    /// </summary>
    [Serializable]
    public struct SpawnPointData
    {
        public Vector2 position;
        public int enemyTypeId;
        public float activateRange;
        public float respawnDelay;
    }

    /// <summary>
    /// Manages enemy spawn points, enforces max active limit,
    /// handles range-based spawning and despawning.
    /// </summary>
    public class EnemySpawner
    {
        private List<SpawnPointData> _spawnPoints;
        private List<int> _spawnedEnemyHashes;
        private Dictionary<int, float> _respawnTimers;
        private int _maxActive;
        private int _nextHashSeed;

        public IReadOnlyList<int> SpawnedEnemyHashes => _spawnedEnemyHashes;
        public int ActiveCount => _spawnedEnemyHashes.Count;

        public event Action<int, SpawnPointData> OnEnemySpawned;
        public event Action<int> OnEnemyDespawned;

        public EnemySpawner(int maxActive = 20)
        {
            _spawnPoints = new List<SpawnPointData>();
            _spawnedEnemyHashes = new List<int>();
            _respawnTimers = new Dictionary<int, float>();
            _maxActive = maxActive;
            _nextHashSeed = 1000;
        }

        /// <summary>
        /// Registers a spawn point for later evaluation.
        /// </summary>
        public void AddSpawnPoint(SpawnPointData point)
        {
            _spawnPoints.Add(point);
        }

        /// <summary>
        /// Spawns an enemy at the given spawn point index.
        /// Returns the generated hash, or 0 if spawn is rejected
        /// (invalid index or max active reached).
        /// </summary>
        public int SpawnEnemy(int spawnPointIndex)
        {
            if (spawnPointIndex < 0 || spawnPointIndex >= _spawnPoints.Count)
            {
                return 0;
            }

            if (_spawnedEnemyHashes.Count >= _maxActive)
            {
                return 0;
            }

            int hash = _nextHashSeed++;
            _spawnedEnemyHashes.Add(hash);
            OnEnemySpawned?.Invoke(hash, _spawnPoints[spawnPointIndex]);
            return hash;
        }

        /// <summary>
        /// Removes an enemy by hash. Fires OnEnemyDespawned if found.
        /// </summary>
        public void DespawnEnemy(int hash)
        {
            if (_spawnedEnemyHashes.Remove(hash))
            {
                OnEnemyDespawned?.Invoke(hash);
            }
        }

        /// <summary>
        /// Checks all spawn points against the player position.
        /// Spawns enemies at points within activateRange, respecting
        /// respawnDelay and maxActive limits.
        /// </summary>
        public void EvaluateSpawnPoints(Vector2 playerPosition, float currentTime)
        {
            for (int i = 0; i < _spawnPoints.Count; i++)
            {
                SpawnPointData point = _spawnPoints[i];
                float distance = Vector2.Distance(playerPosition, point.position);

                if (distance <= point.activateRange
                    && _spawnedEnemyHashes.Count < _maxActive)
                {
                    if (!_respawnTimers.TryGetValue(i, out float nextSpawnTime)
                        || currentTime >= nextSpawnTime)
                    {
                        SpawnEnemy(i);
                        _respawnTimers[i] = currentTime + point.respawnDelay;
                    }
                }
            }
        }

        /// <summary>
        /// Despawns all active enemies and resets respawn timers.
        /// Used for rest-point respawn or area transitions.
        /// </summary>
        public void RefreshAll()
        {
            for (int i = _spawnedEnemyHashes.Count - 1; i >= 0; i--)
            {
                OnEnemyDespawned?.Invoke(_spawnedEnemyHashes[i]);
            }
            _spawnedEnemyHashes.Clear();
            _respawnTimers.Clear();
        }
    }
}
