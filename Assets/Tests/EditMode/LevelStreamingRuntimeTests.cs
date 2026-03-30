using NUnit.Framework;
using R3;
using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// LevelStreaming Runtime橋渡しテスト。
    /// LevelStreamingOrchestratorの状態管理・キュー処理・イベント発火を検証する。
    /// </summary>
    [TestFixture]
    public class LevelStreamingRuntimeTests
    {
        private GameEvents _events;
        private LevelStreamingOrchestrator _orchestrator;
        private string _lastLoadedScene;
        private string _lastUnloadedScene;
        private int _loadCallCount;
        private int _unloadCallCount;

        [SetUp]
        public void SetUp()
        {
            _events = new GameEvents();
            _lastLoadedScene = null;
            _lastUnloadedScene = null;
            _loadCallCount = 0;
            _unloadCallCount = 0;

            _orchestrator = new LevelStreamingOrchestrator(
                "PersistentScene",
                _events,
                (sceneName) =>
                {
                    _lastLoadedScene = sceneName;
                    _loadCallCount++;
                },
                (sceneName) =>
                {
                    _lastUnloadedScene = sceneName;
                    _unloadCallCount++;
                }
            );
        }

        [TearDown]
        public void TearDown()
        {
            _events.Dispose();
        }

        [Test]
        public void Orchestrator_RequestAreaLoad_CallsLoadCallback()
        {
            _orchestrator.RequestAreaLoad("Area_Forest");
            _orchestrator.ProcessQueue();

            Assert.AreEqual("Area_Forest", _lastLoadedScene);
            Assert.AreEqual(1, _loadCallCount);
        }

        [Test]
        public void Orchestrator_RequestAreaLoad_FiresSceneLoadStartedEvent()
        {
            string firedScene = null;
            _events.OnSceneLoadStarted.Subscribe(s => firedScene = s);

            _orchestrator.RequestAreaLoad("Area_Forest");
            _orchestrator.ProcessQueue();

            Assert.AreEqual("Area_Forest", firedScene);
        }

        [Test]
        public void Orchestrator_CompleteLoad_FiresSceneLoadCompletedAndAreaTransition()
        {
            string completedScene = null;
            (string from, string to) areaTransition = (null, null);
            _events.OnSceneLoadCompleted.Subscribe(s => completedScene = s);
            _events.OnAreaTransition.Subscribe(t => areaTransition = t);

            _orchestrator.RequestAreaLoad("Area_Forest");
            _orchestrator.ProcessQueue();
            _orchestrator.NotifyLoadComplete("Area_Forest");

            Assert.AreEqual("Area_Forest", completedScene);
            Assert.AreEqual("PersistentScene", areaTransition.from);
            Assert.AreEqual("Area_Forest", areaTransition.to);
        }

        [Test]
        public void Orchestrator_RequestAreaUnload_CallsUnloadCallback()
        {
            // まずロード完了させてからアンロード
            _orchestrator.RequestAreaLoad("Area_Forest");
            _orchestrator.ProcessQueue();
            _orchestrator.NotifyLoadComplete("Area_Forest");

            // 新しいエリアをアクティブにする
            _orchestrator.RequestAreaLoad("Area_Cave");
            _orchestrator.ProcessQueue();
            _orchestrator.NotifyLoadComplete("Area_Cave");

            // 古いエリアをアンロード
            _orchestrator.RequestAreaUnload("Area_Forest");

            Assert.AreEqual("Area_Forest", _lastUnloadedScene);
            Assert.AreEqual(1, _unloadCallCount);
        }

        [Test]
        public void Orchestrator_RequestAreaUnload_ActiveScene_DoesNotUnload()
        {
            _orchestrator.RequestAreaLoad("Area_Forest");
            _orchestrator.ProcessQueue();
            _orchestrator.NotifyLoadComplete("Area_Forest");

            bool result = _orchestrator.RequestAreaUnload("Area_Forest");

            Assert.IsFalse(result);
            Assert.AreEqual(0, _unloadCallCount);
        }

        [Test]
        public void Orchestrator_CompleteUnload_ReturnsToIdle()
        {
            _orchestrator.RequestAreaLoad("Area_Forest");
            _orchestrator.ProcessQueue();
            _orchestrator.NotifyLoadComplete("Area_Forest");

            _orchestrator.RequestAreaLoad("Area_Cave");
            _orchestrator.ProcessQueue();
            _orchestrator.NotifyLoadComplete("Area_Cave");

            _orchestrator.RequestAreaUnload("Area_Forest");
            _orchestrator.NotifyUnloadComplete("Area_Forest");

            Assert.AreEqual(StreamingState.Idle, _orchestrator.State);
            Assert.IsFalse(_orchestrator.IsLoaded("Area_Forest"));
        }

        [Test]
        public void Orchestrator_MultipleLoadRequests_ProcessedSequentially()
        {
            _orchestrator.RequestAreaLoad("Area_Forest");
            _orchestrator.RequestAreaLoad("Area_Cave");

            // 1回目のProcessQueueで最初だけ処理
            _orchestrator.ProcessQueue();
            Assert.AreEqual("Area_Forest", _lastLoadedScene);
            Assert.AreEqual(1, _loadCallCount);

            // ロード完了前に2回目はスキップ（Loading状態中）
            _orchestrator.ProcessQueue();
            Assert.AreEqual(1, _loadCallCount, "Loading中はキューを進めない");

            // 完了後に次を処理
            _orchestrator.NotifyLoadComplete("Area_Forest");
            _orchestrator.ProcessQueue();
            Assert.AreEqual("Area_Cave", _lastLoadedScene);
            Assert.AreEqual(2, _loadCallCount);
        }

        [Test]
        public void Orchestrator_DuplicateLoadRequest_Ignored()
        {
            _orchestrator.RequestAreaLoad("Area_Forest");
            _orchestrator.ProcessQueue();
            _orchestrator.NotifyLoadComplete("Area_Forest");

            bool result = _orchestrator.RequestAreaLoad("Area_Forest");

            Assert.IsFalse(result);
        }

        [Test]
        public void Orchestrator_UnloadPersistentScene_Blocked()
        {
            bool result = _orchestrator.RequestAreaUnload("PersistentScene");

            Assert.IsFalse(result);
            Assert.AreEqual(0, _unloadCallCount);
        }

        [Test]
        public void Orchestrator_ActiveScene_UpdatesAfterLoad()
        {
            Assert.AreEqual("PersistentScene", _orchestrator.ActiveScene);

            _orchestrator.RequestAreaLoad("Area_Forest");
            _orchestrator.ProcessQueue();
            _orchestrator.NotifyLoadComplete("Area_Forest");

            Assert.AreEqual("Area_Forest", _orchestrator.ActiveScene);
        }
    }
}
