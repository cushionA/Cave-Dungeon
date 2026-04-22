using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// A2: LevelStreamingOrchestrator の AreaLoad 完了イベント subscribe で
    /// FlagManager.SwitchMap が呼ばれることを検証する。
    /// </summary>
    [TestFixture]
    public class LevelStreamingFlagManagerWiringTests
    {
        private GameEvents _events;
        private LevelStreamingOrchestrator _orchestrator;
        private FlagManager _flagManager;

        [SetUp]
        public void SetUp()
        {
            _events = new GameEvents();
            _flagManager = new FlagManager();
            _orchestrator = new LevelStreamingOrchestrator(
                "PersistentScene",
                _events,
                sceneName => { },
                sceneName => { });
        }

        [TearDown]
        public void TearDown()
        {
            _events.Dispose();
        }

        [Test]
        public void AttachFlagManager_AreaLoadComplete_CallsSwitchMap()
        {
            _orchestrator.AttachFlagManager(_flagManager);

            _orchestrator.RequestAreaLoad("Area_Forest");
            _orchestrator.ProcessQueue();
            _orchestrator.NotifyLoadComplete("Area_Forest");

            Assert.AreEqual("Area_Forest", _flagManager.CurrentMapId);
        }

        [Test]
        public void AttachFlagManager_MultipleAreaLoads_UpdatesCurrentMap()
        {
            _orchestrator.AttachFlagManager(_flagManager);

            _orchestrator.RequestAreaLoad("Area_Forest");
            _orchestrator.ProcessQueue();
            _orchestrator.NotifyLoadComplete("Area_Forest");

            _orchestrator.RequestAreaLoad("Area_Cave");
            _orchestrator.ProcessQueue();
            _orchestrator.NotifyLoadComplete("Area_Cave");

            Assert.AreEqual("Area_Cave", _flagManager.CurrentMapId);
        }

        [Test]
        public void NoFlagManager_AreaLoadComplete_DoesNotThrow()
        {
            // FlagManager 未接続時は何も呼ばれないことを検証（NullRef ガード）
            Assert.DoesNotThrow(() =>
            {
                _orchestrator.RequestAreaLoad("Area_Forest");
                _orchestrator.ProcessQueue();
                _orchestrator.NotifyLoadComplete("Area_Forest");
            });
        }

        [Test]
        public void AttachFlagManager_OnAreaLoadCompletedEvent_Fires()
        {
            _orchestrator.AttachFlagManager(_flagManager);
            string capturedScene = null;
            _orchestrator.OnAreaLoadCompleted += s => capturedScene = s;

            _orchestrator.RequestAreaLoad("Area_Forest");
            _orchestrator.ProcessQueue();
            _orchestrator.NotifyLoadComplete("Area_Forest");

            Assert.AreEqual("Area_Forest", capturedScene);
        }

        [Test]
        public void AttachFlagManager_LocalFlagsScopedToMap()
        {
            _orchestrator.AttachFlagManager(_flagManager);

            // Forest マップに切替後ローカルフラグ設定
            _orchestrator.RequestAreaLoad("Area_Forest");
            _orchestrator.ProcessQueue();
            _orchestrator.NotifyLoadComplete("Area_Forest");
            _flagManager.SetLocalFlag("chest_1", true);
            Assert.IsTrue(_flagManager.GetLocalFlag("chest_1"));

            // Cave へ切替 → Cave に chest_1 はない
            _orchestrator.RequestAreaLoad("Area_Cave");
            _orchestrator.ProcessQueue();
            _orchestrator.NotifyLoadComplete("Area_Cave");
            Assert.IsFalse(_flagManager.GetLocalFlag("chest_1"));

            // FlagManager 自体の SwitchMap で Forest に戻せばフラグ復活
            _flagManager.SwitchMap("Area_Forest");
            Assert.IsTrue(_flagManager.GetLocalFlag("chest_1"));
        }
    }
}
