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
