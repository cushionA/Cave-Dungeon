using System;
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

        // ===== Step 1: Unregister, 重複防止, IGameSubManager =====

        [Test]
        public void SaveManager_Unregister_ExcludesFromSave()
        {
            MockSaveable saveable = new MockSaveable { SaveId = "test", Value = 42 };
            _saveManager.Register(saveable);
            _saveManager.Unregister(saveable);

            SaveSlotData slotData = _saveManager.Save(0);

            Assert.IsFalse(slotData.entries.ContainsKey("test"));
        }

        [Test]
        public void SaveManager_Unregister_ExcludesFromLoad()
        {
            MockSaveable saveable = new MockSaveable { SaveId = "test", Value = 42 };
            _saveManager.Register(saveable);
            _saveManager.Save(0);
            _saveManager.Unregister(saveable);

            saveable.Value = 0;
            _saveManager.Load(0);

            // Unregister後はLoad対象外なので値は0のまま
            Assert.AreEqual(0, saveable.Value);
        }

        [Test]
        public void SaveManager_Register_DuplicateSaveId_ThrowsException()
        {
            MockSaveable saveable1 = new MockSaveable { SaveId = "duplicate" };
            MockSaveable saveable2 = new MockSaveable { SaveId = "duplicate" };

            _saveManager.Register(saveable1);

            Assert.Throws<InvalidOperationException>(() => _saveManager.Register(saveable2));
        }

        [Test]
        public void SaveManager_Register_SameInstanceTwice_IgnoresSecond()
        {
            MockSaveable saveable = new MockSaveable { SaveId = "test", Value = 10 };
            _saveManager.Register(saveable);
            _saveManager.Register(saveable); // 同一インスタンスの再登録は無視

            saveable.Value = 42;
            SaveSlotData slotData = _saveManager.Save(0);

            // エントリは1つだけ
            Assert.AreEqual(1, slotData.entries.Count);
            Assert.AreEqual(42, slotData.entries["test"]);
        }

        [Test]
        public void SaveManager_ImplementsIGameSubManager()
        {
            Assert.IsInstanceOf<IGameSubManager>(_saveManager);
        }

        [Test]
        public void SaveManager_InitOrder_Returns900()
        {
            IGameSubManager subManager = _saveManager as IGameSubManager;
            Assert.AreEqual(900, subManager.InitOrder);
        }

        [Test]
        public void SaveManager_Unregister_Null_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _saveManager.Unregister(null));
        }

        [Test]
        public void SaveManager_Unregister_NotRegistered_DoesNotThrow()
        {
            MockSaveable saveable = new MockSaveable { SaveId = "notRegistered" };
            Assert.DoesNotThrow(() => _saveManager.Unregister(saveable));
        }
    }
}
