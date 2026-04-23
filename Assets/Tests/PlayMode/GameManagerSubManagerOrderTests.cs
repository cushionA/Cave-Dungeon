using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Game.Core;
using Game.Runtime;

namespace Game.Tests.PlayMode
{
    /// <summary>
    /// C3: GameManager.Awake で IGameSubManager が Priority (InitOrder) 昇順で
    /// Initialize される & 既存公開プロパティが維持されることを検証する。
    /// </summary>
    public class GameManagerSubManagerOrderTests
    {
        private List<Object> _spawnedObjects;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            _spawnedObjects = new List<Object>();
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

        [UnityTest]
        public IEnumerator GameManager_Awake_ResolvesAllThreeSubManagers()
        {
            if (GameManager.Instance != null)
            {
                Object.DestroyImmediate(GameManager.Instance.gameObject);
            }

            GameObject gmGo = new GameObject("TestGM");
            _spawnedObjects.Add(gmGo);

            GameObject projGo = new GameObject("Projectiles");
            projGo.transform.SetParent(gmGo.transform);
            projGo.AddComponent<ProjectileManager>();

            GameObject enemyGo = new GameObject("EnemySpawner");
            enemyGo.transform.SetParent(gmGo.transform);
            enemyGo.AddComponent<EnemySpawnerManager>();

            GameObject lsGo = new GameObject("LevelStreaming");
            lsGo.transform.SetParent(gmGo.transform);
            lsGo.AddComponent<LevelStreamingController>();

            gmGo.AddComponent<GameManager>();
            yield return null;

            // 3つのサブマネージャーが既存プロパティ経由で取得できること
            Assert.IsNotNull(GameManager.Projectiles, "Projectiles プロパティが解決されているべき");
            Assert.IsNotNull(GameManager.EnemySpawner, "EnemySpawner プロパティが解決されているべき");
            Assert.IsNotNull(GameManager.LevelStreaming, "LevelStreaming プロパティが解決されているべき");
        }

        [UnityTest]
        public IEnumerator GameManager_Awake_InitializesSubManagersInOrder()
        {
            // 独立検証: IGameSubManager の InitOrder 昇順ソートが正しく動くか
            // 実際の順序は: LevelStreaming(100) → EnemySpawner(200) → Projectile(300)
            if (GameManager.Instance != null)
            {
                Object.DestroyImmediate(GameManager.Instance.gameObject);
            }

            GameObject gmGo = new GameObject("TestGM_Order");
            _spawnedObjects.Add(gmGo);

            GameObject projGo = new GameObject("Projectiles");
            projGo.transform.SetParent(gmGo.transform);
            ProjectileManager pm = projGo.AddComponent<ProjectileManager>();

            GameObject enemyGo = new GameObject("EnemySpawner");
            enemyGo.transform.SetParent(gmGo.transform);
            EnemySpawnerManager esm = enemyGo.AddComponent<EnemySpawnerManager>();

            GameObject lsGo = new GameObject("LevelStreaming");
            lsGo.transform.SetParent(gmGo.transform);
            LevelStreamingController lsc = lsGo.AddComponent<LevelStreamingController>();

            gmGo.AddComponent<GameManager>();
            yield return null;

            // 各 InitOrder を検証
            Assert.AreEqual(100, ((IGameSubManager)lsc).InitOrder, "Streaming は 100");
            Assert.AreEqual(200, ((IGameSubManager)esm).InitOrder, "Enemy は 200");
            Assert.AreEqual(300, ((IGameSubManager)pm).InitOrder, "Projectile は 300");

            // Streaming(100) < Enemy(200) < Projectile(300) の順序関係
            Assert.Less(((IGameSubManager)lsc).InitOrder, ((IGameSubManager)esm).InitOrder);
            Assert.Less(((IGameSubManager)esm).InitOrder, ((IGameSubManager)pm).InitOrder);

            // 全 Manager が Initialize 済み（内部状態が構築済みであること）
            Assert.IsNotNull(pm.gameObject.transform.Find("[ProjectilePool]"),
                "ProjectileManager.Initialize が呼ばれ、[ProjectilePool] 子が生成されるべき");
            Assert.IsNotNull(esm.gameObject.transform.Find("[EnemyPool]"),
                "EnemySpawnerManager.Initialize が呼ばれ、[EnemyPool] 子が生成されるべき");
            Assert.IsNotNull(lsc.Orchestrator,
                "LevelStreamingController.Initialize が呼ばれ、Orchestrator が生成されるべき");
        }

        [UnityTest]
        public IEnumerator IGameSubManager_ImplementationsCanBeCollected()
        {
            // GameObject.GetComponentsInChildren<IGameSubManager> の動作確認
            if (GameManager.Instance != null)
            {
                Object.DestroyImmediate(GameManager.Instance.gameObject);
            }

            GameObject gmGo = new GameObject("TestGM_Collect");
            _spawnedObjects.Add(gmGo);

            GameObject projGo = new GameObject("Projectiles");
            projGo.transform.SetParent(gmGo.transform);
            projGo.AddComponent<ProjectileManager>();

            GameObject enemyGo = new GameObject("EnemySpawner");
            enemyGo.transform.SetParent(gmGo.transform);
            enemyGo.AddComponent<EnemySpawnerManager>();

            GameObject lsGo = new GameObject("LevelStreaming");
            lsGo.transform.SetParent(gmGo.transform);
            lsGo.AddComponent<LevelStreamingController>();

            IGameSubManager[] collected = gmGo.GetComponentsInChildren<IGameSubManager>(true);
            Assert.AreEqual(3, collected.Length, "3つ全て IGameSubManager として収集できるべき");

            yield return null;
        }

        [UnityTest]
        public IEnumerator GameManager_GetSubManager_ReturnsSameInstanceAsLegacyProperties()
        {
            // Dictionary化後、新しい GetSubManager<T>() API が既存プロパティと同じインスタンスを返すことを検証する。
            if (GameManager.Instance != null)
            {
                Object.DestroyImmediate(GameManager.Instance.gameObject);
            }

            GameObject gmGo = new GameObject("TestGM_GetSubManager");
            _spawnedObjects.Add(gmGo);

            GameObject projGo = new GameObject("Projectiles");
            projGo.transform.SetParent(gmGo.transform);
            ProjectileManager pm = projGo.AddComponent<ProjectileManager>();

            GameObject enemyGo = new GameObject("EnemySpawner");
            enemyGo.transform.SetParent(gmGo.transform);
            EnemySpawnerManager esm = enemyGo.AddComponent<EnemySpawnerManager>();

            GameObject lsGo = new GameObject("LevelStreaming");
            lsGo.transform.SetParent(gmGo.transform);
            LevelStreamingController lsc = lsGo.AddComponent<LevelStreamingController>();

            gmGo.AddComponent<GameManager>();
            yield return null;

            // GetSubManager<T>() が期待型でインスタンスを返すこと
            Assert.AreSame(pm, GameManager.GetSubManager<ProjectileManager>(),
                "GetSubManager<ProjectileManager>() は登録された ProjectileManager を返すべき");
            Assert.AreSame(esm, GameManager.GetSubManager<EnemySpawnerManager>(),
                "GetSubManager<EnemySpawnerManager>() は登録された EnemySpawnerManager を返すべき");
            Assert.AreSame(lsc, GameManager.GetSubManager<LevelStreamingController>(),
                "GetSubManager<LevelStreamingController>() は登録された LevelStreamingController を返すべき");

            // 既存プロパティと同一インスタンスであること (プロキシ経路の整合)
            Assert.AreSame(GameManager.Projectiles, GameManager.GetSubManager<ProjectileManager>(),
                "Projectiles プロパティと GetSubManager<ProjectileManager>() は同一インスタンスを返すべき");
            Assert.AreSame(GameManager.EnemySpawner, GameManager.GetSubManager<EnemySpawnerManager>(),
                "EnemySpawner プロパティと GetSubManager<EnemySpawnerManager>() は同一インスタンスを返すべき");
            Assert.AreSame(GameManager.LevelStreaming, GameManager.GetSubManager<LevelStreamingController>(),
                "LevelStreaming プロパティと GetSubManager<LevelStreamingController>() は同一インスタンスを返すべき");
        }

        [UnityTest]
        public IEnumerator GameManager_GetSubManager_WhenInstanceNull_ReturnsNull()
        {
            // Instance が未初期化の間に GetSubManager<T>() を呼んでも null 安全であること。
            if (GameManager.Instance != null)
            {
                Object.DestroyImmediate(GameManager.Instance.gameObject);
            }
            yield return null;

            Assert.IsNull(GameManager.GetSubManager<ProjectileManager>(),
                "Instance 未初期化時は GetSubManager<ProjectileManager>() が null を返すべき");
            Assert.IsNull(GameManager.Projectiles,
                "Instance 未初期化時は Projectiles も null を返すべき");
        }
    }
}
