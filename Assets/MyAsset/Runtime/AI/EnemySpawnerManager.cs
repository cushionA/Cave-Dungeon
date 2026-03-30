using System.Collections.Generic;
using UnityEngine;
using Game.Core;

namespace Game.Runtime
{
    /// <summary>
    /// Core EnemySpawnerのMonoBehaviourラッパー。
    /// スポーンポイント評価 → GameObjectプール管理 → SoA登録を橋渡しする。
    /// GameManagerの子として配置し、Initialize()で起動する。
    /// </summary>
    public class EnemySpawnerManager : MonoBehaviour
    {
        [Header("Prefab")]
        [SerializeField] private GameObject _defaultEnemyPrefab;

        [Header("Settings")]
        [SerializeField] private int _maxActive = 20;
        [SerializeField] private int _preWarmCount = 8;

        [Header("Spawn Points")]
        [SerializeField] private SpawnPointData[] _spawnPoints;

        private EnemySpawner _coreSpawner;
        private Queue<GameObject> _goPool;
        private Dictionary<int, GameObject> _activeEnemies; // spawnerHash → GameObject
        private Transform _poolParent;

        public EnemySpawner CoreSpawner => _coreSpawner;
        public int ActiveCount => _activeEnemies != null ? _activeEnemies.Count : 0;
        public int PoolCount => _goPool != null ? _goPool.Count : 0;

        /// <summary>
        /// マネージャーを初期化する。GameManagerから呼ばれる。
        /// </summary>
        public void Initialize()
        {
            _coreSpawner = new EnemySpawner(_maxActive);
            _goPool = new Queue<GameObject>();
            _activeEnemies = new Dictionary<int, GameObject>();

            // プール用の非表示親オブジェクト
            GameObject poolObj = new GameObject("[EnemyPool]");
            poolObj.transform.SetParent(transform);
            poolObj.SetActive(false);
            _poolParent = poolObj.transform;

            // GameObjectプレウォーム
            if (_defaultEnemyPrefab != null)
            {
                for (int i = 0; i < _preWarmCount; i++)
                {
                    GameObject enemy = CreatePooledEnemy();
                    _goPool.Enqueue(enemy);
                }
            }

            // スポーンポイント登録
            if (_spawnPoints != null)
            {
                for (int i = 0; i < _spawnPoints.Length; i++)
                {
                    _coreSpawner.AddSpawnPoint(_spawnPoints[i]);
                }
            }

            // Coreイベント購読
            _coreSpawner.OnEnemySpawned += HandleEnemySpawned;
            _coreSpawner.OnEnemyDespawned += HandleEnemyDespawned;
        }

        private void Update()
        {
            if (_coreSpawner == null)
            {
                return;
            }

            int playerHash = CharacterRegistry.PlayerHash;
            if (playerHash == 0 || !GameManager.IsCharacterValid(playerHash))
            {
                return;
            }

            ref CharacterVitals vitals = ref GameManager.Data.GetVitals(playerHash);
            _coreSpawner.EvaluateSpawnPoints(vitals.position, Time.time);
        }

        /// <summary>
        /// テストやイベントから直接Evaluateを呼ぶ。
        /// </summary>
        public void EvaluateNow()
        {
            if (_coreSpawner == null)
            {
                return;
            }

            int playerHash = CharacterRegistry.PlayerHash;
            if (playerHash == 0 || !GameManager.IsCharacterValid(playerHash))
            {
                return;
            }

            ref CharacterVitals vitals = ref GameManager.Data.GetVitals(playerHash);
            _coreSpawner.EvaluateSpawnPoints(vitals.position, Time.time);
        }

        /// <summary>
        /// 全敵をデスポーンし、リスポーンタイマーをリセットする。
        /// セーブポイント休息やエリア遷移時に呼ぶ。
        /// </summary>
        public void RefreshAll()
        {
            if (_coreSpawner != null)
            {
                _coreSpawner.RefreshAll();
            }
        }

        /// <summary>
        /// 指定spawnerHashに対応するアクティブ敵GameObjectを返す。
        /// </summary>
        public GameObject GetActiveEnemyObject(int spawnerHash)
        {
            if (_activeEnemies != null && _activeEnemies.TryGetValue(spawnerHash, out GameObject go))
            {
                return go;
            }
            return null;
        }

        /// <summary>
        /// スポーンポイントを動的に追加する。ステージロード時に使用。
        /// </summary>
        public void AddSpawnPoint(SpawnPointData point)
        {
            if (_coreSpawner != null)
            {
                _coreSpawner.AddSpawnPoint(point);
            }
        }

        private void HandleEnemySpawned(int spawnerHash, SpawnPointData point)
        {
            GameObject enemyGo = GetOrCreateEnemy();
            enemyGo.transform.position = new Vector3(point.position.x, point.position.y, 0f);
            enemyGo.SetActive(true);

            _activeEnemies[spawnerHash] = enemyGo;
        }

        private void HandleEnemyDespawned(int spawnerHash)
        {
            if (!_activeEnemies.TryGetValue(spawnerHash, out GameObject enemyGo))
            {
                return;
            }

            _activeEnemies.Remove(spawnerHash);
            ReturnToPool(enemyGo);
        }

        private GameObject GetOrCreateEnemy()
        {
            if (_goPool.Count > 0)
            {
                GameObject pooled = _goPool.Dequeue();
                pooled.transform.SetParent(null);
                return pooled;
            }

            return CreatePooledEnemy();
        }

        private void ReturnToPool(GameObject enemyGo)
        {
            if (enemyGo == null)
            {
                return;
            }

            enemyGo.SetActive(false);
            enemyGo.transform.SetParent(_poolParent);
            _goPool.Enqueue(enemyGo);
        }

        private GameObject CreatePooledEnemy()
        {
            GameObject go;
            if (_defaultEnemyPrefab != null)
            {
                go = Instantiate(_defaultEnemyPrefab, _poolParent);
            }
            else
            {
                go = new GameObject("[PLACEHOLDER]Enemy");
                go.transform.SetParent(_poolParent);

                Rigidbody2D rb = go.AddComponent<Rigidbody2D>();
                rb.gravityScale = GameConstants.k_GravityScale;
                rb.constraints = RigidbodyConstraints2D.FreezeRotation;

                go.AddComponent<BoxCollider2D>();
                go.AddComponent<EnemyCharacter>();
            }

            go.SetActive(false);
            return go;
        }

        /// <summary>
        /// 全敵をプールに返却する。シーン遷移時に呼ぶ。
        /// </summary>
        public void ClearAll()
        {
            if (_activeEnemies == null)
            {
                return;
            }

            foreach (KeyValuePair<int, GameObject> kvp in _activeEnemies)
            {
                ReturnToPool(kvp.Value);
            }
            _activeEnemies.Clear();

            if (_coreSpawner != null)
            {
                _coreSpawner.RefreshAll();
            }
        }

        private void OnDestroy()
        {
            if (_coreSpawner != null)
            {
                _coreSpawner.OnEnemySpawned -= HandleEnemySpawned;
                _coreSpawner.OnEnemyDespawned -= HandleEnemyDespawned;
            }
        }

#if UNITY_INCLUDE_TESTS
        /// <summary>テスト専用: SerializeFieldを外部から設定する。</summary>
        public void SetupForTest(GameObject prefab, SpawnPointData[] points,
            int maxActive = 20, int preWarmCount = 8)
        {
            _defaultEnemyPrefab = prefab;
            _spawnPoints = points;
            _maxActive = maxActive;
            _preWarmCount = preWarmCount;
        }
#endif
    }
}
