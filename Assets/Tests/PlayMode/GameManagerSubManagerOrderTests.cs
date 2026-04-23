using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
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

        [UnityTest]
        public IEnumerator GameManager_InitializeSubManagers_WhenDuplicateTypeRegistered_LogsWarningAndKeepsFirst()
        {
            // 同一具象型の IGameSubManager が 2 つ存在する場合、warning が出て最初の 1 個だけが採用されることを検証する。
            // Dictionary 登録の衝突ハンドリング (先勝ち + LogWarning) のガードレール。
            if (GameManager.Instance != null)
            {
                Object.DestroyImmediate(GameManager.Instance.gameObject);
            }

            GameObject gmGo = new GameObject("TestGM_Duplicate");
            _spawnedObjects.Add(gmGo);

            // ProjectileManager を 2 つ、別々の子オブジェクトに付与
            GameObject projGo1 = new GameObject("Projectiles_1");
            projGo1.transform.SetParent(gmGo.transform);
            ProjectileManager pm1 = projGo1.AddComponent<ProjectileManager>();

            GameObject projGo2 = new GameObject("Projectiles_2");
            projGo2.transform.SetParent(gmGo.transform);
            ProjectileManager pm2 = projGo2.AddComponent<ProjectileManager>();

            GameObject enemyGo = new GameObject("EnemySpawner");
            enemyGo.transform.SetParent(gmGo.transform);
            enemyGo.AddComponent<EnemySpawnerManager>();

            GameObject lsGo = new GameObject("LevelStreaming");
            lsGo.transform.SetParent(gmGo.transform);
            lsGo.AddComponent<LevelStreamingController>();

            // 2 個目の ProjectileManager 登録時に警告が出ることを期待
            LogAssert.Expect(
                LogType.Warning,
                new Regex(@"IGameSubManager of type ProjectileManager already registered"));

            gmGo.AddComponent<GameManager>();
            yield return null;

            // GetSubManager で取得されるのは最初の 1 個のみ (登録順 = GetComponentsInChildren 走査順)
            ProjectileManager resolved = GameManager.GetSubManager<ProjectileManager>();
            Assert.IsNotNull(resolved, "重複登録でも 1 個目は正常に解決されるべき");
            Assert.IsTrue(resolved == pm1 || resolved == pm2,
                "解決されたインスタンスは登録した 2 つのどちらかであるべき");

            // 他のマネージャーは正常に登録されていること (重複処理が他型に波及しない)
            Assert.IsNotNull(GameManager.GetSubManager<EnemySpawnerManager>(),
                "重複があっても別型 EnemySpawnerManager は正常登録されるべき");
            Assert.IsNotNull(GameManager.GetSubManager<LevelStreamingController>(),
                "重複があっても別型 LevelStreamingController は正常登録されるべき");
        }
    }
}
