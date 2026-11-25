using System;
using System.IO;
using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class InventoryUsageBeltTests
    {
        [Test]
        public void ItemUsageLogic_TryUseConsumable_RemovesFromInventory()
        {
            InventoryManager inventory = new InventoryManager();
            int itemId = 10;
            inventory.Add(itemId, ItemCategory.Consumable, 3, 10);

            // Use one - should succeed and reduce count
            bool result = ItemUsageLogic.TryUseConsumable(inventory, itemId);

            Assert.IsTrue(result);
            Assert.AreEqual(2, inventory.GetCount(itemId));

            // Use remaining two
            ItemUsageLogic.TryUseConsumable(inventory, itemId);
            ItemUsageLogic.TryUseConsumable(inventory, itemId);

            Assert.AreEqual(0, inventory.GetCount(itemId));

            // No stock left - should fail
            bool failResult = ItemUsageLogic.TryUseConsumable(inventory, itemId);

            Assert.IsFalse(failResult);
            Assert.AreEqual(0, inventory.GetCount(itemId));
        }

        [Test]
        public void ItemUsageLogic_TryUseMagic_ConsumesMp()
        {
            float currentMp = 50f;
            float mpCost = 20f;

            // Enough MP - should succeed
            bool result = ItemUsageLogic.TryUseMagic(ref currentMp, mpCost);

            Assert.IsTrue(result);
            Assert.AreEqual(30f, currentMp, 0.001f);

            // Use again - still enough
            bool result2 = ItemUsageLogic.TryUseMagic(ref currentMp, mpCost);

            Assert.IsTrue(result2);
            Assert.AreEqual(10f, currentMp, 0.001f);

            // Not enough MP - should fail
            bool failResult = ItemUsageLogic.TryUseMagic(ref currentMp, mpCost);

            Assert.IsFalse(failResult);
            Assert.AreEqual(10f, currentMp, 0.001f);
        }

        [Test]
        public void ItemUsageLogic_TryUseConsumable_DoesNotConsumeSameIdInAnotherCategory()
        {
            InventoryManager inventory = new InventoryManager();
            inventory.Add(10, ItemCategory.Material, 3, 10);
            Assert.IsFalse(ItemUsageLogic.TryUseConsumable(inventory, 10));
            Assert.AreEqual(0, inventory.GetConsumableCount(10));

            Assert.AreEqual(1, inventory.Add(10, ItemCategory.Consumable, 1, 10));
            Assert.AreEqual(1, inventory.GetConsumableCount(10));
            Assert.IsTrue(ItemUsageLogic.TryUseConsumable(inventory, 10));
            Assert.IsFalse(ItemUsageLogic.TryUseConsumable(inventory, 10));
            Assert.AreEqual(3, inventory.GetByCategory(ItemCategory.Material)[0].count);
            Assert.AreEqual(3, inventory.GetCount(10));
        }

        [Test]
        public void ConsumableBelt_AssignmentRequiresStockAndRemainsWhileDepleted()
        {
            InventoryManager inventory = new InventoryManager();
            ConsumableBelt belt = new ConsumableBelt(inventory);
            inventory.Add(0, ItemCategory.Consumable, 1, 1);
            inventory.Add(1, ItemCategory.Weapon, 1, 1);

            Assert.AreEqual(4, belt.SlotCount);
            Assert.AreEqual(-1, belt.GetItemId(-1));
            Assert.IsFalse(belt.TryAssign(-1, 0));
            Assert.IsFalse(belt.TryAssign(3, 1));
            Assert.IsFalse(belt.TryAssign(3, 2));
            Assert.IsTrue(belt.TryAssign(3, 0));
            belt.Prev();
            Assert.AreEqual(3, belt.ActiveIndex);
            Assert.AreEqual(0, belt.ActiveItemId);
            Assert.IsTrue(ItemUsageLogic.TryUseConsumable(inventory, belt.ActiveItemId));
            Assert.IsFalse(ItemUsageLogic.TryUseConsumable(inventory, belt.ActiveItemId));
            Assert.AreEqual(0, belt.ActiveItemId);

            inventory.Add(0, ItemCategory.Consumable, 1, 1);
            Assert.IsTrue(ItemUsageLogic.TryUseConsumable(inventory, belt.ActiveItemId));
            Assert.IsTrue(belt.TryClear(3));
            Assert.AreEqual(-1, belt.ActiveItemId);
            belt.Next();
            Assert.AreEqual(0, belt.ActiveIndex);
            Assert.IsFalse(belt.TrySelect(4));
        }

        [Test]
        public void ConsumableBelt_AndInventory_RestoreSeparateSlotsAndOlderMissingEntries()
        {
            string path = Path.Combine(Path.GetTempPath(), "ConsumableBelt-" + Guid.NewGuid().ToString("N"));
            try
            {
                SaveDataStore disk = new SaveDataStore(path);
                using GameManagerCore original = new GameManagerCore();
                original.Inventory.Add(5, ItemCategory.Consumable, 1, 3);
                original.Inventory.Add(6, ItemCategory.Consumable, 2, 3);
                Assert.IsTrue(original.ConsumableBelt.TryAssign(2, 5));
                Assert.IsTrue(original.ConsumableBelt.TrySelect(2));
                Assert.IsTrue(ItemUsageLogic.TryUseConsumable(original.Inventory, original.ConsumableBelt.ActiveItemId));
                Assert.IsTrue(disk.TryWriteToDisk(original.Saves.Save(0)));

                Assert.IsTrue(original.ConsumableBelt.TryClear(2));
                Assert.IsTrue(original.ConsumableBelt.TryAssign(1, 6));
                Assert.IsTrue(original.ConsumableBelt.TrySelect(1));
                Assert.IsTrue(ItemUsageLogic.TryUseConsumable(original.Inventory, original.ConsumableBelt.ActiveItemId));
                Assert.IsTrue(disk.TryWriteToDisk(original.Saves.Save(1)));

                SaveSlotData legacy = new SaveSlotData(2);
                legacy.entries[original.Inventory.SaveId] = ((ISaveable)original.Inventory).Serialize();
                Assert.IsTrue(disk.TryWriteToDisk(legacy));

                using GameManagerCore restored = new GameManagerCore();
                for (int slot = 0; slot < SaveManager.k_MaxSlots; slot++)
                {
                    SaveSlotData saved = disk.ReadFromDisk(slot);
                    Assert.IsNotNull(saved);
                    restored.Saves.SetSlotData(slot, saved);
                }

                Assert.IsTrue(restored.Saves.Load(1));
                Assert.AreEqual(1, restored.Inventory.GetConsumableCount(6));
                Assert.AreEqual(1, restored.ConsumableBelt.ActiveIndex);
                Assert.AreEqual(6, restored.ConsumableBelt.ActiveItemId);

                Assert.IsTrue(restored.Saves.Load(0));
                Assert.AreEqual(2, restored.Inventory.GetConsumableCount(6));
                Assert.AreEqual(0, restored.Inventory.GetConsumableCount(5));
                Assert.AreEqual(2, restored.ConsumableBelt.ActiveIndex);
                Assert.AreEqual(5, restored.ConsumableBelt.ActiveItemId);
                Assert.AreEqual(-1, restored.ConsumableBelt.GetItemId(1));

                Assert.IsTrue(restored.Saves.Load(2));
                Assert.AreEqual(1, restored.Inventory.GetConsumableCount(6));
                Assert.AreEqual(0, restored.ConsumableBelt.ActiveIndex);
                Assert.AreEqual(-1, restored.ConsumableBelt.GetItemId(2));

                restored.Saves.SetSlotData(2, new SaveSlotData(2));
                Assert.IsTrue(restored.Saves.Load(2));
                Assert.AreEqual(0, restored.Inventory.ItemCount);

                Assert.IsTrue(restored.Saves.Load(0));
                restored.NextPlaythrough.MarkCleared();
                Assert.IsTrue(restored.NextPlaythrough.TryBeginNextPlaythrough(
                    0, restored.Saves.LoadRevision, restored.Playthrough.CycleNumber, "start", disk));
                Assert.AreEqual(2, restored.Playthrough.CycleNumber);
                Assert.AreEqual(2, restored.Inventory.GetConsumableCount(6));
                Assert.AreEqual(2, restored.ConsumableBelt.ActiveIndex);
                Assert.AreEqual(5, restored.ConsumableBelt.ActiveItemId);
            }
            finally
            {
                if (Directory.Exists(path)) { Directory.Delete(path, true); }
            }
        }

        [Test]
        public void BeltShortcut_NextPrev_LoopsAround()
        {
            BeltShortcut belt = new BeltShortcut(4);

            Assert.AreEqual(0, belt.ActiveIndex);

            // Next 3 times -> index 3
            belt.Next();
            belt.Next();
            belt.Next();
            Assert.AreEqual(3, belt.ActiveIndex);

            // Next once more -> loop to index 0
            belt.Next();
            Assert.AreEqual(0, belt.ActiveIndex);

            // Prev -> loop to index 3
            belt.Prev();
            Assert.AreEqual(3, belt.ActiveIndex);
        }

        [Test]
        public void BeltShortcut_SetClearSlot_WorksCorrectly()
        {
            BeltShortcut belt = new BeltShortcut(4);

            // Initial state - all empty (-1)
            Assert.AreEqual(-1, belt.ActiveItemId);

            // Set slot 0
            belt.SetSlot(0, 42);
            Assert.AreEqual(42, belt.ActiveItemId);

            // Set slot 2 and navigate to it
            belt.SetSlot(2, 99);
            belt.Next();
            belt.Next();
            Assert.AreEqual(2, belt.ActiveIndex);
            Assert.AreEqual(99, belt.ActiveItemId);

            // Clear slot 2
            belt.ClearSlot(2);
            Assert.AreEqual(-1, belt.ActiveItemId);
        }
    }
}
