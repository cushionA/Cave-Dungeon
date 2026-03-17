using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    [TestFixture]
    public class LevelStreamingCoreTests
    {
        [Test]
        public void LevelStreamingLogic_RequestLoad_AddsToQueue()
        {
            LevelStreamingLogic logic = new LevelStreamingLogic("MainMenu");

            bool result = logic.RequestLoad("Stage1");
            string nextScene = logic.BeginNextLoad();

            Assert.IsTrue(result);
            Assert.AreEqual("Stage1", nextScene);
            Assert.AreEqual(StreamingState.Loading, logic.State);
        }

        [Test]
        public void LevelStreamingLogic_CompleteLoad_UpdatesActiveScene()
        {
            LevelStreamingLogic logic = new LevelStreamingLogic("MainMenu");

            logic.RequestLoad("Stage1");
            logic.BeginNextLoad();
            logic.CompleteLoad("Stage1");

            Assert.AreEqual("Stage1", logic.ActiveScene);
            Assert.IsTrue(logic.IsLoaded("Stage1"));
            Assert.AreEqual(StreamingState.Loaded, logic.State);
        }

        [Test]
        public void LevelStreamingLogic_RequestLoad_WhenAlreadyLoaded_ReturnsFalse()
        {
            LevelStreamingLogic logic = new LevelStreamingLogic("MainMenu");

            logic.RequestLoad("Stage1");
            logic.BeginNextLoad();
            logic.CompleteLoad("Stage1");

            bool duplicateResult = logic.RequestLoad("Stage1");

            Assert.IsFalse(duplicateResult);
        }

        [Test]
        public void LevelStreamingLogic_RequestUnload_ActiveScene_ReturnsFalse()
        {
            LevelStreamingLogic logic = new LevelStreamingLogic("MainMenu");

            logic.RequestLoad("Stage1");
            logic.BeginNextLoad();
            logic.CompleteLoad("Stage1");

            bool unloadResult = logic.RequestUnload("Stage1");

            Assert.IsFalse(unloadResult);
        }
    }
}
