namespace Game.Core
{
    /// <summary>
    /// アイテム使用ロジック。消耗品使用・魔法使用の判定と消費処理。
    /// </summary>
    public static class ItemUsageLogic
    {
        /// <summary>
        /// 消耗品使用。inventoryから1個消費。在庫0なら失敗。
        /// 戻り値: 使用成功か。
        /// </summary>
        public static bool TryUseConsumable(InventoryManager inventory, int itemId)
        {
            int currentCount = inventory.GetCount(itemId);
            if (currentCount <= 0)
            {
                return false;
            }

            inventory.Remove(itemId, 1);
            return true;
        }

        /// <summary>
        /// 魔法使用。MP消費。MP不足なら失敗。
        /// 戻り値: 使用成功か。
        /// </summary>
        public static bool TryUseMagic(ref float currentMp, float mpCost)
        {
            if (currentMp < mpCost)
            {
                return false;
            }

            currentMp -= mpCost;
            return true;
        }
    }
}
