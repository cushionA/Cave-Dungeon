using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class ShopSystemCoreTests
    {
        [Test]
        public void ShopLogic_TryBuy_Success_DeductsCurrencyAndAddsItem()
        {
            CurrencyManager currency = new CurrencyManager(100);
            InventoryManager inventory = new InventoryManager();
            int itemId = 1;
            ItemCategory category = ItemCategory.Consumable;
            int price = 50;
            int maxStack = 10;

            bool result = ShopLogic.TryBuy(currency, inventory, itemId, category, price, maxStack);

            Assert.IsTrue(result);
            Assert.AreEqual(50, currency.Balance);
            Assert.AreEqual(1, inventory.GetCount(itemId));
        }

        [Test]
        public void ShopLogic_TryBuy_InsufficientFunds_ReturnsFalse()
        {
            CurrencyManager currency = new CurrencyManager(10);
            InventoryManager inventory = new InventoryManager();
            int itemId = 1;
            ItemCategory category = ItemCategory.Consumable;
            int price = 50;
            int maxStack = 10;

            bool result = ShopLogic.TryBuy(currency, inventory, itemId, category, price, maxStack);

            Assert.IsFalse(result);
            Assert.AreEqual(10, currency.Balance);
            Assert.AreEqual(0, inventory.GetCount(itemId));
        }

        [Test]
        public void ShopLogic_TrySell_AddsCurrencyAndRemovesItem()
        {
            CurrencyManager currency = new CurrencyManager(0);
            InventoryManager inventory = new InventoryManager();
            int itemId = 1;
            ItemCategory category = ItemCategory.Consumable;
            int buyPrice = 100;
            int maxStack = 10;
            inventory.Add(itemId, category, 1, maxStack);

            bool result = ShopLogic.TrySell(currency, inventory, itemId, category, buyPrice);

            Assert.IsTrue(result);
            int expectedSellPrice = ShopLogic.CalculateSellPrice(buyPrice);
            Assert.AreEqual(40, expectedSellPrice);
            Assert.AreEqual(40, currency.Balance);
            Assert.AreEqual(0, inventory.GetCount(itemId));
        }

        [Test]
        public void ShopLogic_TrySell_NonSellableItem_ReturnsFalse()
        {
            CurrencyManager currency = new CurrencyManager(0);
            InventoryManager inventory = new InventoryManager();
            int itemId = 1;
            ItemCategory category = ItemCategory.KeyItem;
            int buyPrice = 100;
            int maxStack = 1;
            inventory.Add(itemId, category, 1, maxStack);

            bool result = ShopLogic.TrySell(currency, inventory, itemId, category, buyPrice);

            Assert.IsFalse(result);
            Assert.AreEqual(0, currency.Balance);
            Assert.AreEqual(1, inventory.GetCount(itemId));
        }
    }
}
