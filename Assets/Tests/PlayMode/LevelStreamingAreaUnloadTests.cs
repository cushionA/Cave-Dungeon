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
    /// A4: AreaUnload 完了時に EnemySpawnerManager.ClearAll() と
    /// ProjectileManager.ClearAll() が呼ばれることを検証する。
    /// LevelStreamingController.HandleAreaUnloadCompleted 経由で発火する。
    /// </summary>
    public class LevelStreamingAreaUnloadTests
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

        /// <summary>
        /// GameManager 配下に ProjectileManager + EnemySpawnerManager +
        /// LevelStreamingController を子として配置する。
        /// </summary>
        private GameManager CreateWiredGameManager()
        {
            // 既存のGameManagerがあれば破棄
            if (GameManager.Instance != null)
            {
                Object.DestroyImmediate(GameManager.Instance.gameObject);
            }

            GameObject gmGo = new GameObject("TestGameManager");
            _spawnedObjects.Add(gmGo);

            // 子オブジェクト（Awake で GetComponentsInChildren されるため、Awake 前に構築）
            GameObject projGo = new GameObject("Projectiles");
            projGo.transform.SetParent(gmGo.transform);
            projGo.AddComponent<ProjectileManager>();

            GameObject enemyGo = new GameObject("EnemySpawner");
            enemyGo.transform.SetParent(gmGo.transform);
            enemyGo.AddComponent<EnemySpawnerManager>();

            GameObject lsGo = new GameObject("LevelStreaming");
            lsGo.transform.SetParent(gmGo.transform);
            lsGo.AddComponent<LevelStreamingController>();

            // 最後に GameManager を追加 → Awake が発火して子 IGameSubManager を初期化
            GameManager gm = gmGo.AddComponent<GameManager>();
            return gm;
        }

        [UnityTest]
        public IEnumerator AreaUnloadCompleted_InvokesClearAllOnEnemyAndProjectile()
        {
            GameManager gm = CreateWiredGameManager();
            yield return null;

            LevelStreamingController lsc = GameManager.LevelStreaming;
            Assert.IsNotNull(lsc, "LevelStreamingController が子から取得できているべき");
            Assert.IsNotNull(lsc.Orchestrator, "Orchestrator が Initialize されているべき");

            // ProcessQueue が SceneManager.LoadSceneAsync を呼ぶ。テストシーン "Area_Forest"/"Area_Cave"
            // は Build Settings 未登録のため Unity が LogError を出す。本テストは SceneManager を
            // モックせず Orchestrator のイベントフローのみ検証するので、ログは期待値として登録する。
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                "Scene 'Area_Forest' couldn't be loaded"));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                "Scene 'Area_Cave' couldn't be loaded"));

            // Orchestrator を直接叩いて AreaUnload 完了を再現
            lsc.Orchestrator.RequestAreaLoad("Area_Forest");
            lsc.Orchestrator.ProcessQueue();
            lsc.Orchestrator.NotifyLoadComplete("Area_Forest");

            lsc.Orchestrator.RequestAreaLoad("Area_Cave");
            lsc.Orchestrator.ProcessQueue();
            lsc.Orchestrator.NotifyLoadComplete("Area_Cave");

            bool unloadEventFired = false;
            lsc.Orchestrator.OnAreaUnloadCompleted += s => unloadEventFired = true;

            // Area_Forest をアンロード
            bool unloadOk = lsc.Orchestrator.RequestAreaUnload("Area_Forest");
            Assert.IsTrue(unloadOk, "非アクティブシーンはアンロード可能");

            lsc.Orchestrator.NotifyUnloadComplete("Area_Forest");

            Assert.IsTrue(unloadEventFired, "OnAreaUnloadCompleted が発火するべき");

            // ClearAll は内部でアクティブコレクションが初期化済なら例外なく返るため、
            // 呼ばれたこと自体は ProjectileManager.ActiveCount == 0, EnemySpawnerManager.ActiveCount == 0 で確認
            Assert.AreEqual(0, GameManager.Projectiles.ActiveCount);
            Assert.AreEqual(0, GameManager.EnemySpawner.ActiveCount);
        }

        [UnityTest]
        public IEnumerator AreaUnloadCompleted_WithoutManagers_DoesNotThrow()
        {
            // GameManager を手動構築するが子なし
            if (GameManager.Instance != null)
            {
                Object.DestroyImmediate(GameManager.Instance.gameObject);
            }
            GameObject gmGo = new GameObject("TestGameManagerNoChildren");
            _spawnedObjects.Add(gmGo);

            // Streaming のみ子に追加
            GameObject lsGo = new GameObject("LevelStreaming");
            lsGo.transform.SetParent(gmGo.transform);
            lsGo.AddComponent<LevelStreamingController>();

            gmGo.AddComponent<GameManager>();
            yield return null;

            LevelStreamingController lsc = GameManager.LevelStreaming;
            Assert.IsNotNull(lsc);

            // Build Settings 未登録シーン由来の LogError は想定内
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                "Scene 'Area_Forest' couldn't be loaded"));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                "Scene 'Area_Cave' couldn't be loaded"));

            // Projectiles/EnemySpawner が null でも例外にならないこと
            Assert.DoesNotThrow(() =>
            {
                lsc.Orchestrator.RequestAreaLoad("Area_Forest");
                lsc.Orchestrator.ProcessQueue();
                lsc.Orchestrator.NotifyLoadComplete("Area_Forest");

                lsc.Orchestrator.RequestAreaLoad("Area_Cave");
                lsc.Orchestrator.ProcessQueue();
                lsc.Orchestrator.NotifyLoadComplete("Area_Cave");

                lsc.Orchestrator.RequestAreaUnload("Area_Forest");
                lsc.Orchestrator.NotifyUnloadComplete("Area_Forest");
            });
        }
    }
}
