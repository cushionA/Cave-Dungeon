using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Game.Core;
using Game.Runtime;
using CharacterInfo = Game.Core.CharacterInfo;

namespace Game.Tests.PlayMode
{
    public class EnemySpawnerManagerTests
    {
        private List<Object> _spawnedObjects;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            _spawnedObjects = new List<Object>();
            TestSceneHelper.CreateGameManager();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            for (int i = _spawnedObjects.Count - 1; i >= 0; i--)
            {
                if (_spawnedObjects[i] != null)
                {
                    Object.DestroyImmediate(_spawnedObjects[i]);
                }
            }
            _spawnedObjects.Clear();
            TestSceneHelper.Cleanup();
            yield return null;
        }

        private EnemySpawnerManager CreateManager(SpawnPointData[] spawnPoints = null)
        {
            GameObject go = new GameObject("TestEnemySpawnerManager");
            _spawnedObjects.Add(go);

            EnemySpawnerManager manager = go.AddComponent<EnemySpawnerManager>();

            // デフォルト敵プレハブを作成
            GameObject prefab = CreateEnemyPrefab();

            manager.SetupForTest(prefab, spawnPoints, 20, 0);
            manager.Initialize();
            return manager;
        }

        private GameObject CreateEnemyPrefab()
        {
            GameObject prefab = new GameObject("[PLACEHOLDER]EnemyPrefab");
            _spawnedObjects.Add(prefab);

            Rigidbody2D rb = prefab.AddComponent<Rigidbody2D>();
            rb.gravityScale = GameConstants.k_GravityScale;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;

            BoxCollider2D col = prefab.AddComponent<BoxCollider2D>();
            col.size = new Vector2(0.6f, 0.9f);

            EnemyCharacter enemy = prefab.AddComponent<EnemyCharacter>();
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(
                belong: CharacterBelong.Enemy,
                feature: CharacterFeature.Minion,
                maxHp: 50);
            TestSceneHelper.SetCharacterInfo(enemy, info);
            _spawnedObjects.Add(info);

            prefab.SetActive(false);
            return prefab;
        }

        private GameObject CreatePlayer(Vector3 position)
        {
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(
                belong: CharacterBelong.Ally,
                feature: CharacterFeature.Player);
            _spawnedObjects.Add(info);

            // PlayerCharacter付きGameObjectを直接構築（BaseCharacterとの重複登録を避ける）
            GameObject go = new GameObject("TestPlayer");
            go.transform.position = position;
            _spawnedObjects.Add(go);

            Rigidbody2D rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = GameConstants.k_GravityScale;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;

            BoxCollider2D col = go.AddComponent<BoxCollider2D>();
            col.size = new Vector2(0.6f, 0.9f);

            PlayerCharacter pc = go.AddComponent<PlayerCharacter>();
            TestSceneHelper.SetCharacterInfo(pc, info);

            return go;
        }

        // ─────────────────────────────────────────────
        //  テストケース
        // ─────────────────────────────────────────────

        [UnityTest]
        public IEnumerator EnemySpawnerManager_Initialize_CreatesCoreSPawner()
        {
            EnemySpawnerManager manager = CreateManager();
            yield return null;

            Assert.IsNotNull(manager.CoreSpawner);
            Assert.AreEqual(0, manager.ActiveCount);
        }

        [UnityTest]
        public IEnumerator EnemySpawnerManager_SpawnOnEvent_CreatesEnemyAtPosition()
        {
            Vector2 spawnPos = new Vector2(5f, 2f);
            SpawnPointData[] points = new SpawnPointData[]
            {
                new SpawnPointData
                {
                    position = spawnPos,
                    enemyTypeId = 0,
                    activateRange = 20f,
                    respawnDelay = 60f
                }
            };

            EnemySpawnerManager manager = CreateManager(points);
            yield return null;

            // Core経由で直接スポーン
            int hash = manager.CoreSpawner.SpawnEnemy(0);

            Assert.AreNotEqual(0, hash);
            Assert.AreEqual(1, manager.ActiveCount);

            // 生成された敵のGameObjectが正しい位置にいるか
            GameObject enemyGo = manager.GetActiveEnemyObject(hash);
            Assert.IsNotNull(enemyGo);
            Assert.IsTrue(enemyGo.activeSelf);
            Assert.AreEqual(spawnPos.x, enemyGo.transform.position.x, 0.01f);
            Assert.AreEqual(spawnPos.y, enemyGo.transform.position.y, 0.01f);
        }

        [UnityTest]
        public IEnumerator EnemySpawnerManager_DespawnEnemy_DeactivatesAndReturnsToPool()
        {
            SpawnPointData[] points = new SpawnPointData[]
            {
                new SpawnPointData
                {
                    position = Vector2.zero,
                    activateRange = 20f,
                    respawnDelay = 60f
                }
            };

            EnemySpawnerManager manager = CreateManager(points);
            yield return null;

            int spawnerHash = manager.CoreSpawner.SpawnEnemy(0);
            Assert.AreEqual(1, manager.ActiveCount);

            // デスポーン
            manager.CoreSpawner.DespawnEnemy(spawnerHash);

            Assert.AreEqual(0, manager.ActiveCount);
            // プールに返却されているか
            Assert.AreEqual(1, manager.PoolCount);
        }

        [UnityTest]
        public IEnumerator EnemySpawnerManager_EvaluateWithPlayerNearby_SpawnsEnemy()
        {
            SpawnPointData[] points = new SpawnPointData[]
            {
                new SpawnPointData
                {
                    position = new Vector2(3f, 0f),
                    enemyTypeId = 0,
                    activateRange = 10f,
                    respawnDelay = 60f
                }
            };

            EnemySpawnerManager manager = CreateManager(points);
            GameObject player = CreatePlayer(Vector3.zero);
            yield return null; // Awake + Start
            yield return null; // GameManager登録完了

            // プレイヤーがスポーンポイントの範囲内 → Update内でEvaluate → スポーン
            // 手動でEvaluateを呼ぶ（Updateの代替）
            manager.EvaluateNow();

            Assert.AreEqual(1, manager.ActiveCount);
        }

        [UnityTest]
        public IEnumerator EnemySpawnerManager_RefreshAll_ClearsAllEnemies()
        {
            SpawnPointData[] points = new SpawnPointData[]
            {
                new SpawnPointData { position = Vector2.zero, activateRange = 20f, respawnDelay = 60f },
                new SpawnPointData { position = new Vector2(3f, 0f), activateRange = 20f, respawnDelay = 60f }
            };

            EnemySpawnerManager manager = CreateManager(points);
            yield return null;

            manager.CoreSpawner.SpawnEnemy(0);
            manager.CoreSpawner.SpawnEnemy(1);
            Assert.AreEqual(2, manager.ActiveCount);

            // RefreshAll
            manager.RefreshAll();

            Assert.AreEqual(0, manager.ActiveCount);
            Assert.AreEqual(2, manager.PoolCount);
        }
    }
}
