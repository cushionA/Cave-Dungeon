using System;
using System.Collections.Generic;
using UnityEngine;
using Game.Core;

namespace Game.Runtime
{
    /// <summary>
    /// アクティブ敵の追跡データ。
    /// </summary>
    internal struct ActiveEnemyData
    {
        public GameObject gameObject;
        public BaseCharacter character; // GetComponent回避用キャッシュ
        public SpawnPointData spawnPoint;
        public int characterHash; // GameObject.GetInstanceID
    }

    /// <summary>
    /// Core EnemySpawnerのMonoBehaviourラッパー。
    /// スポーンポイント評価 → GameObjectプール管理 → SoA登録を橋渡しする。
    /// GameManagerの子として配置し、Initialize()で起動する。
    /// </summary>
    public class EnemySpawnerManager : MonoBehaviour
    {
        private const int k_DespawnCheckInterval = 30;

        [Header("Prefab")]
        [SerializeField] private GameObject _defaultEnemyPrefab;
        [SerializeField] private EnemyTypeEntry[] _enemyTypes;

        [Header("Settings")]
        [SerializeField] private int _maxActive = 20;
        [SerializeField] private int _preWarmCount = 8;

        [Header("Despawn")]
        [Tooltip("activateRangeに対するデスポーン距離の倍率。2.0 = activateRangeの2倍で非活性化")]
        [SerializeField] private float _despawnRangeMultiplier = 2.0f;

        [Header("Spawn Points")]
        [SerializeField] private SpawnPointData[] _spawnPoints;

        private EnemySpawner _coreSpawner;
        private Dictionary<int, Queue<GameObject>> _goPoolByType; // enemyTypeId → pool
        private Dictionary<int, ActiveEnemyData> _activeEnemies;  // spawnerHash → data
        private Dictionary<int, int> _characterToSpawnerHash;     // characterHash → spawnerHash
        private Dictionary<int, GameObject> _prefabByType;        // enemyTypeId → prefab
        private Transform _poolParent;

        // デスポーン処理用の再利用バッファ（Update内アロケーション回避）
        private List<int> _despawnBuffer;
        private int _despawnCheckCounter;

        public EnemySpawner CoreSpawner => _coreSpawner;
        public int ActiveCount => _activeEnemies != null ? _activeEnemies.Count : 0;

#if UNITY_INCLUDE_TESTS
        /// <summary>テスト専用: 全プールの合計オブジェクト数。Dictionary foreachを含むためテスト限定。</summary>
        public int PoolCount
        {
            get
            {
                if (_goPoolByType == null)
                {
                    return 0;
                }
                int count = 0;
                foreach (KeyValuePair<int, Queue<GameObject>> kvp in _goPoolByType)
                {
                    count += kvp.Value.Count;
                }
                return count;
            }
        }
#endif

        /// <summary>
        /// マネージャーを初期化する。GameManagerから呼ばれる。
        /// </summary>
        public void Initialize()
        {
            _coreSpawner = new EnemySpawner(_maxActive);
            _goPoolByType = new Dictionary<int, Queue<GameObject>>();
            _activeEnemies = new Dictionary<int, ActiveEnemyData>();
            _characterToSpawnerHash = new Dictionary<int, int>();
            _prefabByType = new Dictionary<int, GameObject>();
            _despawnBuffer = new List<int>(16);

            // プール用の非表示親オブジェクト
            GameObject poolObj = new GameObject("[EnemyPool]");
            poolObj.transform.SetParent(transform);
            poolObj.SetActive(false);
            _poolParent = poolObj.transform;

            // タイプ別プレハブ登録
            if (_enemyTypes != null)
            {
                for (int i = 0; i < _enemyTypes.Length; i++)
                {
                    EnemyTypeEntry entry = _enemyTypes[i];
                    if (entry.prefab != null)
                    {
                        _prefabByType[entry.enemyTypeId] = entry.prefab;
                    }
                }
            }

            // デフォルトプレハブでプレウォーム（typeId = -1）
            if (_defaultEnemyPrefab != null)
            {
                Queue<GameObject> defaultPool = GetOrCreatePool(-1);
                for (int i = 0; i < _preWarmCount; i++)
                {
                    GameObject enemy = CreatePooledEnemy(_defaultEnemyPrefab);
                    defaultPool.Enqueue(enemy);
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

            // 敵死亡イベント購読
            if (GameManager.Events != null)
            {
                GameManager.Events.OnCharacterDeathEvent += HandleCharacterDeath;
            }
        }

        private void Update()
        {
            if (_coreSpawner == null || !TryGetPlayerPosition(out Vector2 playerPos))
            {
                return;
            }

            _coreSpawner.EvaluateSpawnPoints(playerPos, Time.time);

            // 範囲外の敵をデスポーン（フレームスロットリング）
            _despawnCheckCounter++;
            if (_despawnCheckCounter >= k_DespawnCheckInterval)
            {
                _despawnCheckCounter = 0;
                EvaluateDespawnRange(playerPos);
            }
        }

        /// <summary>
        /// テストやイベントから直接Evaluateを呼ぶ。
        /// </summary>
        public void EvaluateNow()
        {
            if (_coreSpawner == null || !TryGetPlayerPosition(out Vector2 playerPos))
            {
                return;
            }

            _coreSpawner.EvaluateSpawnPoints(playerPos, Time.time);
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
            if (_activeEnemies != null && _activeEnemies.TryGetValue(spawnerHash, out ActiveEnemyData data))
            {
                return data.gameObject;
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

        /// <summary>
        /// 範囲外デスポーン評価を即時実行する。テストやイベントから使用。
        /// </summary>
        public void EvaluateDespawnNow()
        {
            if (_coreSpawner == null || !TryGetPlayerPosition(out Vector2 playerPos))
            {
                return;
            }

            EvaluateDespawnRange(playerPos);
        }

        /// <summary>
        /// プレイヤー位置を取得する共通ヘルパー。プレイヤー未登録時はfalseを返す。
        /// </summary>
        private bool TryGetPlayerPosition(out Vector2 position)
        {
            int playerHash = CharacterRegistry.PlayerHash;
            if (playerHash == 0 || !GameManager.IsCharacterValid(playerHash))
            {
                position = default;
                return false;
            }

            ref CharacterVitals vitals = ref GameManager.Data.GetVitals(playerHash);
            position = vitals.position;
            return true;
        }

        /// <summary>
        /// activateRange × _despawnRangeMultiplier を超えた敵をデスポーンする。
        /// </summary>
        private void EvaluateDespawnRange(Vector2 playerPosition)
        {
            if (_activeEnemies.Count == 0)
            {
                return;
            }

            _despawnBuffer.Clear();

            foreach (KeyValuePair<int, ActiveEnemyData> kvp in _activeEnemies)
            {
                ActiveEnemyData data = kvp.Value;
                if (data.gameObject == null)
                {
                    _despawnBuffer.Add(kvp.Key);
                    continue;
                }

                float despawnRange = data.spawnPoint.activateRange * _despawnRangeMultiplier;
                float sqrDist = ((Vector2)data.gameObject.transform.position - playerPosition).sqrMagnitude;
                if (sqrDist > despawnRange * despawnRange)
                {
                    _despawnBuffer.Add(kvp.Key);
                }
            }

            for (int i = 0; i < _despawnBuffer.Count; i++)
            {
                _coreSpawner.DespawnEnemy(_despawnBuffer[i]);
            }
        }

        private void HandleEnemySpawned(int spawnerHash, SpawnPointData point)
        {
            GameObject enemyGo = GetOrCreateEnemy(point.enemyTypeId);
            // プール親から外してからアクティブ化（Awake()が確実に呼ばれるようにする）
            enemyGo.transform.SetParent(null);
            enemyGo.transform.position = new Vector3(point.position.x, point.position.y, 0f);
            enemyGo.SetActive(true);

            // BaseCharacterキャッシュ取得 + SoAコンテナ再登録
            BaseCharacter character = enemyGo.GetComponent<BaseCharacter>();
            if (character != null)
            {
                character.OnPoolAcquire();
            }

            int characterHash = enemyGo.GetInstanceID();
            ActiveEnemyData data = new ActiveEnemyData
            {
                gameObject = enemyGo,
                character = character,
                spawnPoint = point,
                characterHash = characterHash
            };

            _activeEnemies[spawnerHash] = data;
            _characterToSpawnerHash[characterHash] = spawnerHash;
        }

        private void HandleEnemyDespawned(int spawnerHash)
        {
            if (!_activeEnemies.TryGetValue(spawnerHash, out ActiveEnemyData data))
            {
                return;
            }

            _characterToSpawnerHash.Remove(data.characterHash);
            _activeEnemies.Remove(spawnerHash);
            ReturnToPool(data.gameObject, data.character, data.spawnPoint.enemyTypeId);
        }

        /// <summary>
        /// 敵死亡時にCoreスポーナーへデスポーンを通知する。
        /// </summary>
        private void HandleCharacterDeath(int deadHash, int killerHash)
        {
            if (!_characterToSpawnerHash.TryGetValue(deadHash, out int spawnerHash))
            {
                return;
            }

            _coreSpawner.DespawnEnemy(spawnerHash);
        }

        private GameObject GetOrCreateEnemy(int enemyTypeId)
        {
            // タイプ別プレハブが登録されている場合はそのプールから取得
            int poolKey = _prefabByType.ContainsKey(enemyTypeId) ? enemyTypeId : -1;
            Queue<GameObject> pool = GetOrCreatePool(poolKey);

            if (pool.Count > 0)
            {
                GameObject pooled = pool.Dequeue();
                pooled.transform.SetParent(null);
                return pooled;
            }

            // プレハブを解決してインスタンス化
            GameObject prefab = ResolvePrefab(enemyTypeId);
            return CreatePooledEnemy(prefab);
        }

        private void ReturnToPool(GameObject enemyGo, BaseCharacter character, int enemyTypeId)
        {
            if (enemyGo == null)
            {
                return;
            }

            // プール返却前にSoAコンテナから登録解除（キャッシュ済み参照を使用）
            if (character != null)
            {
                character.OnPoolReturn();
            }

            enemyGo.SetActive(false);
            enemyGo.transform.SetParent(_poolParent);

            int poolKey = _prefabByType.ContainsKey(enemyTypeId) ? enemyTypeId : -1;
            Queue<GameObject> pool = GetOrCreatePool(poolKey);
            pool.Enqueue(enemyGo);
        }

        private Queue<GameObject> GetOrCreatePool(int poolKey)
        {
            if (!_goPoolByType.TryGetValue(poolKey, out Queue<GameObject> pool))
            {
                pool = new Queue<GameObject>();
                _goPoolByType[poolKey] = pool;
            }
            return pool;
        }

        private GameObject ResolvePrefab(int enemyTypeId)
        {
            if (_prefabByType.TryGetValue(enemyTypeId, out GameObject prefab))
            {
                return prefab;
            }
            return _defaultEnemyPrefab;
        }

        private GameObject CreatePooledEnemy(GameObject prefab)
        {
            GameObject go;
            if (prefab != null)
            {
                go = Instantiate(prefab, _poolParent);
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

            // ReturnToPoolはDictionaryを変更しないため、foreach中の呼び出しは安全
            foreach (KeyValuePair<int, ActiveEnemyData> kvp in _activeEnemies)
            {
                ReturnToPool(kvp.Value.gameObject, kvp.Value.character, kvp.Value.spawnPoint.enemyTypeId);
            }
            _activeEnemies.Clear();
            _characterToSpawnerHash.Clear();

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

            if (GameManager.Events != null)
            {
                GameManager.Events.OnCharacterDeathEvent -= HandleCharacterDeath;
            }
        }

#if UNITY_INCLUDE_TESTS
        /// <summary>テスト専用: SerializeFieldを外部から設定する。</summary>
        public void SetupForTest(GameObject prefab, SpawnPointData[] points,
            int maxActive = 20, int preWarmCount = 8,
            EnemyTypeEntry[] enemyTypes = null, float despawnRangeMultiplier = 2.0f)
        {
            _defaultEnemyPrefab = prefab;
            _spawnPoints = points;
            _maxActive = maxActive;
            _preWarmCount = preWarmCount;
            _enemyTypes = enemyTypes;
            _despawnRangeMultiplier = despawnRangeMultiplier;
        }

        /// <summary>テスト専用: characterHash→spawnerHashマッピングを取得する。</summary>
        public bool TryGetSpawnerHash(int characterHash, out int spawnerHash)
        {
            return _characterToSpawnerHash.TryGetValue(characterHash, out spawnerHash);
        }
#endif
    }
}
