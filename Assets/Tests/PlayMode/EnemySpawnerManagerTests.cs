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
                    Object.Destroy(_spawnedObjects[i]);
                }
            }
            _spawnedObjects.Clear();
            TestSceneHelper.Cleanup();
            yield return null;
        }

        private EnemySpawnerManager CreateManager(
            SpawnPointData[] spawnPoints = null,
            EnemyTypeEntry[] enemyTypes = null,
            float despawnRangeMultiplier = 2.0f)
        {
            GameObject go = new GameObject("TestEnemySpawnerManager");
            _spawnedObjects.Add(go);

            EnemySpawnerManager manager = go.AddComponent<EnemySpawnerManager>();

            // デフォルト敵プレハブを作成
            GameObject prefab = CreateEnemyPrefab("Default");

            manager.SetupForTest(prefab, spawnPoints, 20, 0, enemyTypes, despawnRangeMultiplier);
            manager.Initialize();
            return manager;
        }

        private GameObject CreateEnemyPrefab(string name = "Default")
        {
            GameObject prefab = new GameObject($"[PLACEHOLDER]EnemyPrefab_{name}");
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
        //  基本テスト
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

            int hash = manager.CoreSpawner.SpawnEnemy(0);

            Assert.AreNotEqual(0, hash);
            Assert.AreEqual(1, manager.ActiveCount);

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

            manager.CoreSpawner.DespawnEnemy(spawnerHash);

            Assert.AreEqual(0, manager.ActiveCount);
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
            yield return null;
            yield return null;

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

            manager.RefreshAll();

            Assert.AreEqual(0, manager.ActiveCount);
            Assert.AreEqual(2, manager.PoolCount);
        }

        // ─────────────────────────────────────────────
        //  拡張1: enemyTypeId → プレハブマッピング
        // ─────────────────────────────────────────────

        [UnityTest]
        public IEnumerator EnemySpawnerManager_EnemyTypeMapping_UsesCorrectPrefab()
        {
            GameObject slimePrefab = CreateEnemyPrefab("Slime");
            slimePrefab.name = "[PLACEHOLDER]Slime";

            EnemyTypeEntry[] types = new EnemyTypeEntry[]
            {
                new EnemyTypeEntry { enemyTypeId = 1, prefab = slimePrefab }
            };

            SpawnPointData[] points = new SpawnPointData[]
            {
                new SpawnPointData
                {
                    position = Vector2.zero,
                    enemyTypeId = 1,
                    activateRange = 20f,
                    respawnDelay = 60f
                }
            };

            EnemySpawnerManager manager = CreateManager(points, types);
            yield return null;

            int hash = manager.CoreSpawner.SpawnEnemy(0);
            GameObject enemyGo = manager.GetActiveEnemyObject(hash);

            Assert.IsNotNull(enemyGo);
            // Instantiateで生成されるのでプレハブ名 + "(Clone)"
            Assert.IsTrue(enemyGo.name.Contains("Slime"),
                $"タイプ別プレハブが使用されるべき。実際の名前: {enemyGo.name}");
        }

        [UnityTest]
        public IEnumerator EnemySpawnerManager_UnknownTypeId_FallsBackToDefault()
        {
            SpawnPointData[] points = new SpawnPointData[]
            {
                new SpawnPointData
                {
                    position = Vector2.zero,
                    enemyTypeId = 999, // 未登録のタイプ
                    activateRange = 20f,
                    respawnDelay = 60f
                }
            };

            EnemySpawnerManager manager = CreateManager(points);
            yield return null;

            int hash = manager.CoreSpawner.SpawnEnemy(0);
            GameObject enemyGo = manager.GetActiveEnemyObject(hash);

            Assert.IsNotNull(enemyGo, "未登録タイプでもデフォルトプレハブでスポーンする");
        }

        // ─────────────────────────────────────────────
        //  拡張2: 敵死亡時の自動デスポーン
        // ─────────────────────────────────────────────

        [UnityTest]
        public IEnumerator EnemySpawnerManager_CharacterDeath_AutoDespawns()
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
            GameObject enemyGo = manager.GetActiveEnemyObject(spawnerHash);
            int characterHash = enemyGo.GetHashCode();

            Assert.AreEqual(1, manager.ActiveCount);

            // 死亡イベント発火
            GameManager.Events.FireCharacterDeath(characterHash, 0);

            Assert.AreEqual(0, manager.ActiveCount, "死亡イベントで自動デスポーンされる");
            Assert.AreEqual(1, manager.PoolCount, "デスポーン後はプールに返却される");
        }

        [UnityTest]
        public IEnumerator EnemySpawnerManager_UnrelatedDeath_DoesNotAffectSpawner()
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

            manager.CoreSpawner.SpawnEnemy(0);
            Assert.AreEqual(1, manager.ActiveCount);

            // 無関係なハッシュの死亡イベント
            GameManager.Events.FireCharacterDeath(99999, 0);

            Assert.AreEqual(1, manager.ActiveCount, "無関係な死亡イベントでデスポーンしない");
        }

        // ─────────────────────────────────────────────
        //  拡張3: activateRange外の自動デスポーン
        // ─────────────────────────────────────────────

        [UnityTest]
        public IEnumerator EnemySpawnerManager_EnemyOutOfRange_AutoDespawns()
        {
            // activateRange=10, despawnMultiplier=2.0 → despawnRange=20
            SpawnPointData[] points = new SpawnPointData[]
            {
                new SpawnPointData
                {
                    position = new Vector2(5f, 0f),
                    activateRange = 10f,
                    respawnDelay = 60f
                }
            };

            // despawnRangeMultiplier = 2.0
            EnemySpawnerManager manager = CreateManager(points, despawnRangeMultiplier: 2.0f);
            GameObject player = CreatePlayer(Vector3.zero);
            yield return null;
            yield return null;

            // プレイヤー近接でスポーン
            manager.EvaluateNow();
            Assert.AreEqual(1, manager.ActiveCount);

            // 敵を遠くに移動（despawnRange=20を超える位置）
            int spawnerHash = manager.CoreSpawner.SpawnedEnemyHashes[0];
            GameObject enemyGo = manager.GetActiveEnemyObject(spawnerHash);
            enemyGo.transform.position = new Vector3(25f, 0f, 0f); // プレイヤー(0,0)から25離れる > 20

            // 範囲外デスポーン評価を即時実行
            manager.EvaluateDespawnNow();

            Assert.AreEqual(0, manager.ActiveCount, "範囲外の敵は自動デスポーンされる");
        }

        [UnityTest]
        public IEnumerator EnemySpawnerManager_EnemyWithinRange_StaysActive()
        {
            SpawnPointData[] points = new SpawnPointData[]
            {
                new SpawnPointData
                {
                    position = new Vector2(5f, 0f),
                    activateRange = 10f,
                    respawnDelay = 60f
                }
            };

            EnemySpawnerManager manager = CreateManager(points, despawnRangeMultiplier: 2.0f);
            GameObject player = CreatePlayer(Vector3.zero);
            yield return null;
            yield return null;

            manager.EvaluateNow();
            Assert.AreEqual(1, manager.ActiveCount);

            // 敵をdespawnRange内（activateRange*2=20）に配置
            int spawnerHash = manager.CoreSpawner.SpawnedEnemyHashes[0];
            GameObject enemyGo = manager.GetActiveEnemyObject(spawnerHash);
            enemyGo.transform.position = new Vector3(15f, 0f, 0f); // 15 < 20 → 範囲内

            // 範囲外デスポーン評価を即時実行
            manager.EvaluateDespawnNow();

            Assert.AreEqual(1, manager.ActiveCount, "範囲内の敵はデスポーンされない");
        }
    }
}
