namespace Game.Core
{
    /// <summary>
    /// ショップ購入/売却ロジック。通貨チェック+インベントリ追加/削除。
    /// </summary>
    public static class ShopLogic
    {
        public const float k_SellPriceRatio = 0.4f;

        /// <summary>
        /// 購入。通貨チェック→消費→インベントリ追加。
        /// 戻り値: 購入成功か。
        /// </summary>
        public static bool TryBuy(CurrencyManager currency, InventoryManager inventory,
            int itemId, ItemCategory category, int price, int maxStack)
        {
            if (!currency.TrySpend(price))
            {
                return false;
            }

            int added = inventory.Add(itemId, category, 1, maxStack);
            if (added <= 0)
            {
                // スタック上限等で追加できなかった場合、通貨を返却
                currency.Add(price);
                return false;
            }

            return true;
        }

        /// <summary>
        /// 売却。売却可否チェック→インベントリから削除→通貨追加。
        /// 戻り値: 売却成功か。
        /// </summary>
        public static bool TrySell(CurrencyManager currency, InventoryManager inventory,
            int itemId, ItemCategory category, int buyPrice)
        {
            if (!InventoryManager.CanSell(category))
            {
                return false;
            }

            int removed = inventory.Remove(itemId, 1);
            if (removed <= 0)
            {
                return false;
            }

            int sellPrice = CalculateSellPrice(buyPrice);
            currency.Add(sellPrice);
            return true;
        }

        /// <summary>売却価格計算。buyPrice * k_SellPriceRatio (切り捨て)。</summary>
        public static int CalculateSellPrice(int buyPrice)
        {
            return (int)(buyPrice * k_SellPriceRatio);
        }
    }
}
