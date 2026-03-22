using System.Collections.Generic;
using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// 各クラスのISaveable実装テスト。
    /// SaveId一意性、Serialize/Deserialize往復を検証。
    /// </summary>
    public class ISaveableImplementationTests
    {
        // ===== CurrencyManager =====

        [Test]
        public void CurrencyManager_ImplementsISaveable()
        {
            CurrencyManager manager = new CurrencyManager();
            Assert.IsInstanceOf<ISaveable>(manager);
        }

        [Test]
        public void CurrencyManager_SaveId_ReturnsCurrencyManager()
        {
            CurrencyManager manager = new CurrencyManager();
            ISaveable saveable = manager;
            Assert.AreEqual("CurrencyManager", saveable.SaveId);
        }

        [Test]
        public void CurrencyManager_SerializeDeserialize_RoundTrip()
        {
            CurrencyManager original = new CurrencyManager(500);
            original.Add(200);
            ISaveable saveable = original;

            object data = saveable.Serialize();

            CurrencyManager restored = new CurrencyManager();
            ISaveable restoredSaveable = restored;
            restoredSaveable.Deserialize(data);

            Assert.AreEqual(700, restored.Balance);
        }

        [Test]
        public void CurrencyManager_Deserialize_NegativeBalance_ClampsToZero()
        {
            CurrencyManager manager = new CurrencyManager();
            ISaveable saveable = manager;
            saveable.Deserialize(-100);

            Assert.AreEqual(0, manager.Balance);
        }

        // ===== InventoryManager =====

        [Test]
        public void InventoryManager_ImplementsISaveable()
        {
            InventoryManager manager = new InventoryManager();
            Assert.IsInstanceOf<ISaveable>(manager);
        }

        [Test]
        public void InventoryManager_SaveId_ReturnsInventoryManager()
        {
            InventoryManager manager = new InventoryManager();
            ISaveable saveable = manager;
            Assert.AreEqual("InventoryManager", saveable.SaveId);
        }

        [Test]
        public void InventoryManager_SerializeDeserialize_RoundTrip()
        {
            InventoryManager original = new InventoryManager();
            original.Add(1, ItemCategory.Consumable, 3, 10);
            original.Add(2, ItemCategory.Material, 5, 99);
            ISaveable saveable = original;

            object data = saveable.Serialize();

            InventoryManager restored = new InventoryManager();
            ISaveable restoredSaveable = restored;
            restoredSaveable.Deserialize(data);

            Assert.AreEqual(2, restored.ItemCount);
            Assert.AreEqual(3, restored.GetCount(1));
            Assert.AreEqual(5, restored.GetCount(2));
        }

        [Test]
        public void InventoryManager_Deserialize_EmptyList_ClearsInventory()
        {
            InventoryManager manager = new InventoryManager();
            manager.Add(1, ItemCategory.Consumable, 5, 10);
            ISaveable saveable = manager;

            saveable.Deserialize(new List<ItemEntry>());

            Assert.AreEqual(0, manager.ItemCount);
        }

        // ===== LevelUpLogic =====

        [Test]
        public void LevelUpLogic_ImplementsISaveable()
        {
            LevelUpLogic logic = new LevelUpLogic();
            Assert.IsInstanceOf<ISaveable>(logic);
        }

        [Test]
        public void LevelUpLogic_SaveId_ReturnsLevelUpLogic()
        {
            LevelUpLogic logic = new LevelUpLogic();
            ISaveable saveable = logic;
            Assert.AreEqual("LevelUpLogic", saveable.SaveId);
        }

        [Test]
        public void LevelUpLogic_SerializeDeserialize_RoundTrip()
        {
            LevelUpLogic original = new LevelUpLogic(1);
            original.AddExp(250); // level 1→3 (100+200=300, remaining 250-100-200=-50? Let's check: level*100)
            // level1: need 100. 250-100=150, level2. level2: need 200. 150<200. So level=2, exp=150
            original.AllocatePoint(StatType.Str);
            original.AllocatePoint(StatType.Vit);
            ISaveable saveable = original;

            object data = saveable.Serialize();

            LevelUpLogic restored = new LevelUpLogic();
            ISaveable restoredSaveable = restored;
            restoredSaveable.Deserialize(data);

            Assert.AreEqual(2, restored.Level);
            Assert.AreEqual(150, restored.CurrentExp);
            Assert.AreEqual(1, restored.AvailablePoints); // 3 gained - 2 allocated = 1
            Assert.AreEqual(1, restored.AllocatedStats.str);
            Assert.AreEqual(1, restored.AllocatedStats.vit);
            Assert.AreEqual(0, restored.AllocatedStats.dex);
        }

        // ===== GateRegistry =====

        [Test]
        public void GateRegistry_ImplementsISaveable()
        {
            GateRegistry registry = new GateRegistry();
            Assert.IsInstanceOf<ISaveable>(registry);
        }

        [Test]
        public void GateRegistry_SaveId_ReturnsGateRegistry()
        {
            GateRegistry registry = new GateRegistry();
            ISaveable saveable = registry;
            Assert.AreEqual("GateRegistry", saveable.SaveId);
        }

        [Test]
        public void GateRegistry_SerializeDeserialize_RoundTrip()
        {
            GateRegistry original = new GateRegistry();
            original.Register("gate_a", false);
            original.Register("gate_b", true);
            original.Open("gate_a");
            ISaveable saveable = original;

            object data = saveable.Serialize();

            GateRegistry restored = new GateRegistry();
            ISaveable restoredSaveable = restored;
            restoredSaveable.Deserialize(data);

            Assert.IsTrue(restored.IsOpen("gate_a"));
            Assert.IsTrue(restored.IsOpen("gate_b"));
            Assert.AreEqual(2, restored.Count);
        }

        // ===== BacktrackRewardManager =====

        [Test]
        public void BacktrackRewardManager_ImplementsISaveable()
        {
            BacktrackRewardManager manager = new BacktrackRewardManager();
            Assert.IsInstanceOf<ISaveable>(manager);
        }

        [Test]
        public void BacktrackRewardManager_SaveId_ReturnsBacktrackRewardManager()
        {
            BacktrackRewardManager manager = new BacktrackRewardManager();
            ISaveable saveable = manager;
            Assert.AreEqual("BacktrackRewardManager", saveable.SaveId);
        }

        [Test]
        public void BacktrackRewardManager_SerializeDeserialize_RoundTrip()
        {
            BacktrackRewardManager original = new BacktrackRewardManager();
            original.MarkCollected("reward_1");
            original.MarkCollected("reward_2");
            ISaveable saveable = original;

            object data = saveable.Serialize();

            BacktrackRewardManager restored = new BacktrackRewardManager();
            ISaveable restoredSaveable = restored;
            restoredSaveable.Deserialize(data);

            Assert.IsTrue(restored.IsCollected("reward_1"));
            Assert.IsTrue(restored.IsCollected("reward_2"));
            Assert.IsFalse(restored.IsCollected("reward_3"));
        }

        // ===== SaveId一意性テスト =====

        [Test]
        public void AllISaveables_HaveUniqueSaveIds()
        {
            HashSet<string> ids = new HashSet<string>();

            ISaveable[] saveables = new ISaveable[]
            {
                new CurrencyManager(),
                new InventoryManager(),
                new LevelUpLogic(),
                new GateRegistry(),
                new BacktrackRewardManager(),
            };

            foreach (ISaveable saveable in saveables)
            {
                Assert.IsTrue(ids.Add(saveable.SaveId),
                    $"Duplicate SaveId detected: {saveable.SaveId}");
            }
        }
    }
}
