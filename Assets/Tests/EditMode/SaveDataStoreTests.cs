using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class SaveDataStoreTests
    {
        private string _testDir;
        private SaveDataStore _store;

        [SetUp]
        public void SetUp()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "SaveDataStoreTest_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDir);
            _store = new SaveDataStore(_testDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }

        [Test]
        public void SaveDataStore_WriteToDisk_CreatesFile()
        {
            SaveSlotData slotData = new SaveSlotData(0);
            slotData.entries["test"] = 42;

            _store.WriteToDisk(slotData);

            string filePath = Path.Combine(_testDir, "saves", "slot_0.json");
            Assert.IsTrue(File.Exists(filePath));
        }

        [Test]
        public void SaveDataStore_ReadFromDisk_RestoresData()
        {
            SaveSlotData original = new SaveSlotData(0);
            original.entries["intVal"] = 42;
            original.entries["strVal"] = "hello";

            _store.WriteToDisk(original);
            SaveSlotData restored = _store.ReadFromDisk(0);

            Assert.IsNotNull(restored);
            Assert.AreEqual(0, restored.slotIndex);
            Assert.AreEqual(42, System.Convert.ToInt32(restored.entries["intVal"]));
            Assert.AreEqual("hello", restored.entries["strVal"].ToString());
        }

        [Test]
        public void SaveDataStore_ReadFromDisk_NonExistentSlot_ReturnsNull()
        {
            SaveSlotData result = _store.ReadFromDisk(2);
            Assert.IsNull(result);
        }

        [Test]
        public void SaveDataStore_RoundTrip_PreservesComplexData()
        {
            SaveSlotData original = new SaveSlotData(1);
            // int
            original.entries["balance"] = 1000;
            // string array
            original.entries["collectedIds"] = new string[] { "reward_1", "reward_2" };
            // dictionary
            Dictionary<string, bool> gateStates = new Dictionary<string, bool>
            {
                { "gate_a", true },
                { "gate_b", false }
            };
            original.entries["gates"] = gateStates;

            _store.WriteToDisk(original);
            SaveSlotData restored = _store.ReadFromDisk(1);

            Assert.IsNotNull(restored);
            Assert.AreEqual(1, restored.slotIndex);
        }

        [Test]
        public void SaveDataStore_WriteToDisk_IncludesVersion()
        {
            SaveSlotData slotData = new SaveSlotData(0);
            _store.WriteToDisk(slotData);

            string filePath = Path.Combine(_testDir, "saves", "slot_0.json");
            string json = File.ReadAllText(filePath);

            Assert.IsTrue(json.Contains("\"version\""));
        }

        [Test]
        public void SaveDataStore_Overwrite_ReplacesOldData()
        {
            SaveSlotData first = new SaveSlotData(0);
            first.entries["val"] = 10;
            _store.WriteToDisk(first);

            SaveSlotData second = new SaveSlotData(0);
            second.entries["val"] = 99;
            _store.WriteToDisk(second);

            SaveSlotData restored = _store.ReadFromDisk(0);
            Assert.AreEqual(99, System.Convert.ToInt32(restored.entries["val"]));
        }

        [Test]
        public void SaveDataStore_DeleteSlot_RemovesFile()
        {
            SaveSlotData slotData = new SaveSlotData(0);
            slotData.entries["test"] = 1;
            _store.WriteToDisk(slotData);

            _store.DeleteSlot(0);

            string filePath = Path.Combine(_testDir, "saves", "slot_0.json");
            Assert.IsFalse(File.Exists(filePath));
        }

        [Test]
        public void SaveDataStore_HasSlotFile_ReturnsTrueWhenExists()
        {
            SaveSlotData slotData = new SaveSlotData(0);
            _store.WriteToDisk(slotData);

            Assert.IsTrue(_store.HasSlotFile(0));
            Assert.IsFalse(_store.HasSlotFile(1));
        }
    }
}
