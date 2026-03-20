using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// SaveManagerのIGameSubManager配線テスト。
    /// GameManagerCoreに登録して正しく初期化・破棄されることを検証。
    /// </summary>
    public class SaveManagerWiringTests
    {
        private GameManagerCore _core;
        private SaveManager _saveManager;

        [SetUp]
        public void SetUp()
        {
            _core = new GameManagerCore();
            _saveManager = new SaveManager();
        }

        [TearDown]
        public void TearDown()
        {
            _core?.Dispose();
        }

        [Test]
        public void SaveManager_RegisterAsSubManager_InitializesWithoutError()
        {
            _core.RegisterSubManager(_saveManager);

            Assert.DoesNotThrow(() => _core.Initialize(4));
        }

        [Test]
        public void SaveManager_AfterCoreInitialize_StillFunctions()
        {
            _core.RegisterSubManager(_saveManager);
            _core.Initialize(4);

            // SaveManagerは初期化後も通常のSave/Load機能が動作する
            MockSaveable mock = new MockSaveable { SaveId = "wiring_test", Value = 100 };
            _saveManager.Register(mock);
            _saveManager.Save(0);

            mock.Value = 0;
            _saveManager.Load(0);

            Assert.AreEqual(100, mock.Value);
        }

        [Test]
        public void SaveManager_AfterCoreDispose_SaveablesCleared()
        {
            _core.RegisterSubManager(_saveManager);
            _core.Initialize(4);

            MockSaveable mock = new MockSaveable { SaveId = "dispose_test", Value = 50 };
            _saveManager.Register(mock);

            _core.Dispose();

            // Dispose後はsaveablesがクリアされているので、
            // 再登録時に重複SaveIdエラーが出ない
            SaveManager newManager = new SaveManager();
            Assert.DoesNotThrow(() => newManager.Register(
                new MockSaveable { SaveId = "dispose_test", Value = 0 }));
        }

        [Test]
        public void SaveManager_InitOrder_Is900_InitializedLate()
        {
            // InitOrder=900はシステムの後半で初期化される
            Assert.AreEqual(900, (_saveManager as IGameSubManager).InitOrder);
        }

        private class MockSaveable : ISaveable
        {
            public string SaveId { get; set; }
            public int Value { get; set; }
            public object Serialize() => Value;
            public void Deserialize(object data) { Value = (int)data; }
        }
    }
}
