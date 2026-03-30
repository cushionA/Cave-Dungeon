using NUnit.Framework;
using R3;
using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// LevelStreaming Runtime結合テスト。
    /// OrchestratorとGameEventsの連携、状態シーケンスの整合性を検証する。
    /// </summary>
    [TestFixture]
    public class Integration_LevelStreamingRuntimeTests
    {
        private GameEvents _events;

        [SetUp]
        public void SetUp()
        {
            _events = new GameEvents();
        }

        [TearDown]
        public void TearDown()
        {
            _events.Dispose();
        }

        [Test]
        public void Orchestrator_LoadUnloadLoad_StateSequenceConsistent()
        {
            // Load→Unload→同じシーンを再Load で状態が壊れないか
            int loadCount = 0;
            int unloadCount = 0;
            LevelStreamingOrchestrator orchestrator = new LevelStreamingOrchestrator(
                "Persistent", _events,
                s => loadCount++,
                s => unloadCount++
            );

            // 1回目ロード
            orchestrator.RequestAreaLoad("AreaA");
            orchestrator.ProcessQueue();
            orchestrator.NotifyLoadComplete("AreaA");
            Assert.AreEqual(StreamingState.Loaded, orchestrator.State);
            Assert.AreEqual(1, loadCount);

            // 別エリアをアクティブにしてAreaAをアンロード可能にする
            orchestrator.RequestAreaLoad("AreaB");
            orchestrator.ProcessQueue();
            orchestrator.NotifyLoadComplete("AreaB");

            orchestrator.RequestAreaUnload("AreaA");
            orchestrator.NotifyUnloadComplete("AreaA");
            Assert.AreEqual(StreamingState.Idle, orchestrator.State);
            Assert.AreEqual(1, unloadCount);
            Assert.IsFalse(orchestrator.IsLoaded("AreaA"));

            // 同じシーンを再ロード
            bool reloadResult = orchestrator.RequestAreaLoad("AreaA");
            Assert.IsTrue(reloadResult, "アンロード済みシーンは再ロード可能");
            orchestrator.ProcessQueue();
            orchestrator.NotifyLoadComplete("AreaA");
            Assert.IsTrue(orchestrator.IsLoaded("AreaA"));
            Assert.AreEqual(3, loadCount);
        }

        [Test]
        public void Orchestrator_GameEventsSequence_FiresInCorrectOrder()
        {
            // SceneLoadStarted → SceneLoadCompleted → AreaTransition の順に発火するか
            int eventIndex = 0;
            int startedOrder = -1;
            int completedOrder = -1;
            int transitionOrder = -1;

            LevelStreamingOrchestrator orchestrator = new LevelStreamingOrchestrator(
                "Persistent", _events, s => { }, s => { }
            );

            _events.OnSceneLoadStarted.Subscribe(s => startedOrder = eventIndex++);
            _events.OnSceneLoadCompleted.Subscribe(s => completedOrder = eventIndex++);
            _events.OnAreaTransition.Subscribe(t => transitionOrder = eventIndex++);

            orchestrator.RequestAreaLoad("AreaA");
            orchestrator.ProcessQueue();
            orchestrator.NotifyLoadComplete("AreaA");

            Assert.AreEqual(0, startedOrder, "SceneLoadStartedが最初");
            Assert.AreEqual(1, completedOrder, "SceneLoadCompletedが2番目");
            Assert.AreEqual(2, transitionOrder, "AreaTransitionが最後");
        }

        [Test]
        public void Orchestrator_AreaTransitionEvent_TracksFromAndTo()
        {
            // エリア遷移イベントが正しいfrom/toを持つか（連続遷移で確認）
            (string from, string to) lastTransition = (null, null);
            LevelStreamingOrchestrator orchestrator = new LevelStreamingOrchestrator(
                "Persistent", _events, s => { }, s => { }
            );

            _events.OnAreaTransition.Subscribe(t => lastTransition = t);

            // Persistent → AreaA
            orchestrator.RequestAreaLoad("AreaA");
            orchestrator.ProcessQueue();
            orchestrator.NotifyLoadComplete("AreaA");
            Assert.AreEqual("Persistent", lastTransition.from);
            Assert.AreEqual("AreaA", lastTransition.to);

            // AreaA → AreaB
            orchestrator.RequestAreaLoad("AreaB");
            orchestrator.ProcessQueue();
            orchestrator.NotifyLoadComplete("AreaB");
            Assert.AreEqual("AreaA", lastTransition.from);
            Assert.AreEqual("AreaB", lastTransition.to);
        }

        [Test]
        public void Orchestrator_UnloadDuringLoading_BlockedByState()
        {
            // Loading中にアンロード要求が拒否されるか
            LevelStreamingOrchestrator orchestrator = new LevelStreamingOrchestrator(
                "Persistent", _events, s => { }, s => { }
            );

            orchestrator.RequestAreaLoad("AreaA");
            orchestrator.ProcessQueue();
            // Loading状態（NotifyLoadComplete前）

            // Persistentは初期ロード済みだがアクティブなので拒否される
            bool result = orchestrator.RequestAreaUnload("Persistent");
            Assert.IsFalse(result);
        }

        [Test]
        public void Orchestrator_RapidLoadRequests_NoDuplicateEvents()
        {
            // 同じシーンへの連続リクエストでイベントが重複しないか
            int startedCount = 0;
            LevelStreamingOrchestrator orchestrator = new LevelStreamingOrchestrator(
                "Persistent", _events, s => { }, s => { }
            );

            _events.OnSceneLoadStarted.Subscribe(s => startedCount++);

            orchestrator.RequestAreaLoad("AreaA");
            bool duplicate = orchestrator.RequestAreaLoad("AreaA");
            orchestrator.ProcessQueue();

            Assert.IsFalse(duplicate, "重複リクエストは拒否される");
            Assert.AreEqual(1, startedCount, "SceneLoadStartedは1回だけ発火");
        }
    }
}
