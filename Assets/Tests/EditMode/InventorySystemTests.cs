using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class InventorySystemTests
    {
        private InventoryManager _inventory;

        [SetUp]
        public void SetUp()
        {
            _inventory = new InventoryManager();
        }

        [Test]
        public void InventoryManager_Add_IncreasesItemCount()
        {
            int added = _inventory.Add(1, ItemCategory.Consumable, 3, 10);

            Assert.AreEqual(3, added);
            Assert.AreEqual(3, _inventory.GetCount(1));
            Assert.AreEqual(1, _inventory.ItemCount);
        }

        [Test]
        public void InventoryManager_Remove_DecreasesCount()
        {
            _inventory.Add(1, ItemCategory.Consumable, 5, 10);

            int removed = _inventory.Remove(1, 3);

            Assert.AreEqual(3, removed);
            Assert.AreEqual(2, _inventory.GetCount(1));

            // Remove remaining - entry should be deleted
            int removedAll = _inventory.Remove(1, 5);

            Assert.AreEqual(2, removedAll);
            Assert.AreEqual(0, _inventory.GetCount(1));
            Assert.AreEqual(0, _inventory.ItemCount);
        }

        [Test]
        public void InventoryManager_GetByCategory_FiltersCorrectly()
        {
            _inventory.Add(1, ItemCategory.Consumable, 2, 10);
            _inventory.Add(2, ItemCategory.Weapon, 1, 1);
            _inventory.Add(3, ItemCategory.Consumable, 1, 10);
            _inventory.Add(4, ItemCategory.Material, 5, 99);

            System.Collections.Generic.List<ItemEntry> consumables =
                _inventory.GetByCategory(ItemCategory.Consumable);

            Assert.AreEqual(2, consumables.Count);
            Assert.IsTrue(consumables.TrueForAll(e => e.category == ItemCategory.Consumable));
        }

        [Test]
        public void InventoryManager_CanSell_ReturnsFalseForNonSellable()
        {
            // Non-sellable categories
            Assert.IsFalse(InventoryManager.CanSell(ItemCategory.Weapon));
            Assert.IsFalse(InventoryManager.CanSell(ItemCategory.Shield));
            Assert.IsFalse(InventoryManager.CanSell(ItemCategory.Core));
            Assert.IsFalse(InventoryManager.CanSell(ItemCategory.KeyItem));
            Assert.IsFalse(InventoryManager.CanSell(ItemCategory.PlayerMagic));
            Assert.IsFalse(InventoryManager.CanSell(ItemCategory.CompanionMagic));
            Assert.IsFalse(InventoryManager.CanSell(ItemCategory.Flavor));

            // Sellable categories
            Assert.IsTrue(InventoryManager.CanSell(ItemCategory.Consumable));
            Assert.IsTrue(InventoryManager.CanSell(ItemCategory.Material));
        }

        [Test]
        public void InventoryManager_Add_RespectsMaxStack()
        {
            int added = _inventory.Add(1, ItemCategory.Consumable, 8, 5);

            Assert.AreEqual(5, added);
            Assert.AreEqual(5, _inventory.GetCount(1));

            // Adding more to an already-full stack
            int addedMore = _inventory.Add(1, ItemCategory.Consumable, 3, 5);

            Assert.AreEqual(0, addedMore);
            Assert.AreEqual(5, _inventory.GetCount(1));
        }
    }
}
