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
    /// A3: LevelStreamingController のシーンロード失敗時フォールバック動作を検証する。
    /// SceneManager を直接使わず、InjectOrchestratorForTest + SetTestHooks で失敗シミュレーション。
    /// </summary>
    public class LevelStreamingControllerFallbackTests
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

        private LevelStreamingController CreateController()
        {
            GameObject go = new GameObject("TestLevelStreamingController");
            _spawnedObjects.Add(go);
            LevelStreamingController controller = go.AddComponent<LevelStreamingController>();

            // テスト用Orchestratorを注入（Game.Core 所属で MonoBehaviour 非依存）
            LevelStreamingOrchestrator orch = new LevelStreamingOrchestrator(
                "PersistentScene",
                GameManager.Events,
                sceneName => { },
                sceneName => { });
            controller.InjectOrchestratorForTest(orch);

            return controller;
        }

        [UnityTest]
        public IEnumerator HandleLoadComplete_InvalidScene_LogsErrorAndInvokesFallback()
        {
            LevelStreamingController controller = CreateController();

            bool fallbackCalled = false;
            string fallbackScene = null;
            controller.SetTestHooks(
                sceneValidityChecker: (sceneName) => false, // 必ず無効
                fallbackLoader: (sceneName) =>
                {
                    fallbackCalled = true;
                    fallbackScene = sceneName;
                });

            // Unity の LogError を検知するため LogAssert で期待ログを登録
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                "Scene load failed"));

            controller.InvokeHandleLoadCompleteForTest("Area_Broken");

            Assert.IsTrue(fallbackCalled, "失敗時はフォールバックローダーが呼ばれるべき");
            Assert.AreEqual(LevelStreamingController.FallbackSceneNameForTest, fallbackScene,
                "フォールバックは k_FallbackSceneName を渡すべき");

            yield return null;
        }

        [UnityTest]
        public IEnumerator HandleLoadComplete_ValidScene_DoesNotInvokeFallback()
        {
            LevelStreamingController controller = CreateController();

            bool fallbackCalled = false;
            controller.SetTestHooks(
                sceneValidityChecker: (sceneName) => true,
                fallbackLoader: (sceneName) => fallbackCalled = true);

            // まずロード要求してキュー→Loading状態にする
            controller.RequestAreaLoad("Area_Forest");
            controller.Orchestrator.ProcessQueue();

            controller.InvokeHandleLoadCompleteForTest("Area_Forest");

            Assert.IsFalse(fallbackCalled, "有効シーンではフォールバックを呼ばないべき");
            Assert.IsTrue(controller.Orchestrator.IsLoaded("Area_Forest"),
                "有効シーンは通常通り Loaded に遷移するべき");

            yield return null;
        }
    }
}
