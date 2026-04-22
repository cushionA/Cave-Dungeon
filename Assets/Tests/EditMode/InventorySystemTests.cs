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
        public void InventoryManager_Add_OverflowsToNewStack()
        {
            // maxStack=5, count=8 → 1枠目満杯(5) + 2枠目(3) で全8個格納
            int added = _inventory.Add(1, ItemCategory.Consumable, 8, 5);

            Assert.AreEqual(8, added);
            Assert.AreEqual(8, _inventory.GetCount(1));
            Assert.AreEqual(2, _inventory.ItemCount,
                "maxStack超過分は新スロットへ");
        }

        [Test]
        public void InventoryManager_Add_FillsExistingStacksBeforeCreatingNew()
        {
            // 1枠目を4まで追加（余裕1）、2枠目を3まで追加（余裕2）を事前に作る
            _inventory.Add(1, ItemCategory.Consumable, 4, 5);
            _inventory.Add(1, ItemCategory.Consumable, 3, 5);
            // この時点で 1 entry (count=5) + 1 entry (count=2) を想定

            // 5個追加: 1枠目満杯(+1)、2枠目満杯(+3)、残り1は新枠
            int added = _inventory.Add(1, ItemCategory.Consumable, 5, 5);

            Assert.AreEqual(5, added);
            Assert.AreEqual(12, _inventory.GetCount(1));
        }

        [Test]
        public void InventoryManager_Add_WhenMaxSlotReached_ReturnsPartialAdded()
        {
            // k_MaxSlotCount 個の別アイテムを事前に満載
            for (int i = 0; i < InventoryManager.k_MaxSlotCount; i++)
            {
                _inventory.Add(100 + i, ItemCategory.Consumable, 1, 1);
            }

            Assert.AreEqual(InventoryManager.k_MaxSlotCount, _inventory.ItemCount);

            // 新アイテム追加 → k_MaxSlotCount に達しているため追加不可
            int added = _inventory.Add(9999, ItemCategory.Consumable, 10, 5);

            Assert.AreEqual(0, added,
                "スロット上限到達で新規アイテム追加不可");
        }

        [Test]
        public void InventoryManager_Remove_SpansMultipleStacks()
        {
            // Entry1(5) + Entry2(3) の2スタック状態を作る
            _inventory.Add(1, ItemCategory.Consumable, 8, 5);
            Assert.AreEqual(2, _inventory.ItemCount);

            // 7個削除 → 先頭スタック(5)が空になり削除、次スタック(3)から2個減って残1
            int removed = _inventory.Remove(1, 7);

            Assert.AreEqual(7, removed);
            Assert.AreEqual(1, _inventory.GetCount(1));
            Assert.AreEqual(1, _inventory.ItemCount,
                "空になったスタックはエントリごと除去される");
        }

        [Test]
        public void InventoryManager_Remove_WhenCountExceedsTotal_ReturnsRemovedAmount()
        {
            // 複数スタック合計8個の状態
            _inventory.Add(1, ItemCategory.Consumable, 8, 5);

            // 総数を超える10個削除要求 → 削れた分だけ返る
            int removed = _inventory.Remove(1, 10);

            Assert.AreEqual(8, removed,
                "総数を超えて削除要求された場合、実際に削除できた分のみ返す");
            Assert.AreEqual(0, _inventory.GetCount(1));
            Assert.AreEqual(0, _inventory.ItemCount,
                "全スタックが空になればエントリ全消滅");
        }
    }
}
