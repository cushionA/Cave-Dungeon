using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class SaveSystemCoreTests
    {
        private class MockSaveable : ISaveable
        {
            public string SaveId { get; set; }
            public int Value { get; set; }
            public object Serialize() => Value;
            public void Deserialize(object data) { Value = (int)data; }
        }

        private SaveManager _saveManager;

        [SetUp]
        public void SetUp()
        {
            _saveManager = new SaveManager();
        }

        [Test]
        public void SaveManager_Save_StoresData()
        {
            MockSaveable saveable = new MockSaveable { SaveId = "test", Value = 10 };
            _saveManager.Register(saveable);

            _saveManager.Save(0);

            Assert.IsTrue(_saveManager.HasSaveData(0));
        }

        [Test]
        public void SaveManager_Load_RestoresData()
        {
            MockSaveable saveable = new MockSaveable { SaveId = "test", Value = 42 };
            _saveManager.Register(saveable);
            _saveManager.Save(0);

            saveable.Value = 0;
            bool result = _saveManager.Load(0);

            Assert.IsTrue(result);
            Assert.AreEqual(42, saveable.Value);
        }

        [Test]
        public void SaveManager_HasSaveData_ReturnsFalseForEmptySlot()
        {
            Assert.IsFalse(_saveManager.HasSaveData(0));
            Assert.IsFalse(_saveManager.HasSaveData(1));
            Assert.IsFalse(_saveManager.HasSaveData(2));
        }

        [Test]
        public void SaveManager_Save_OverwritesExistingSlot()
        {
            MockSaveable saveable = new MockSaveable { SaveId = "test", Value = 10 };
            _saveManager.Register(saveable);
            _saveManager.Save(0);

            saveable.Value = 99;
            _saveManager.Save(0);

            saveable.Value = 0;
            _saveManager.Load(0);

            Assert.AreEqual(99, saveable.Value);
        }

        [Test]
        public void SaveManager_Load_InvalidSlot_ReturnsFalse()
        {
            Assert.IsFalse(_saveManager.Load(-1));
            Assert.IsFalse(_saveManager.Load(SaveManager.k_MaxSlots));
            Assert.IsFalse(_saveManager.Load(100));
        }
    }
}
