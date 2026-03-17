using System;
using System.Collections.Generic;

namespace Game.Core
{
    public class InventoryManager
    {
        private readonly List<ItemEntry> _items;

        public int ItemCount => _items.Count;

        public InventoryManager()
        {
            _items = new List<ItemEntry>();
        }

        /// <summary>
        /// アイテム追加。既存スタック可能ならスタック、上限超過分は追加しない。
        /// 戻り値: 実際に追加された数量。
        /// </summary>
        public int Add(int itemId, ItemCategory category, int count, int maxStack)
        {
            if (count <= 0)
            {
                return 0;
            }

            // Search for existing entry with same itemId
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].itemId == itemId)
                {
                    ItemEntry entry = _items[i];
                    int space = entry.maxStack - entry.count;
                    int toAdd = Math.Min(count, space);
                    if (toAdd <= 0)
                    {
                        return 0;
                    }
                    entry.count += toAdd;
                    _items[i] = entry;
                    return toAdd;
                }
            }

            // New entry
            int actualCount = Math.Min(count, maxStack);
            ItemEntry newEntry = new ItemEntry
            {
                itemId = itemId,
                category = category,
                count = actualCount,
                maxStack = maxStack,
            };
            _items.Add(newEntry);
            return actualCount;
        }

        /// <summary>
        /// アイテム削除。数量指定。
        /// 戻り値: 実際に削除された数量。
        /// </summary>
        public int Remove(int itemId, int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].itemId == itemId)
                {
                    ItemEntry entry = _items[i];
                    int toRemove = Math.Min(count, entry.count);
                    entry.count -= toRemove;

                    if (entry.count <= 0)
                    {
                        _items.RemoveAt(i);
                    }
                    else
                    {
                        _items[i] = entry;
                    }

                    return toRemove;
                }
            }

            return 0;
        }

        /// <summary>
        /// カテゴリでフィルタ取得。
        /// </summary>
        public List<ItemEntry> GetByCategory(ItemCategory category)
        {
            List<ItemEntry> result = new List<ItemEntry>();
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].category == category)
                {
                    result.Add(_items[i]);
                }
            }
            return result;
        }

        /// <summary>
        /// アイテムID指定で数量取得。なければ0。
        /// </summary>
        public int GetCount(int itemId)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].itemId == itemId)
                {
                    return _items[i].count;
                }
            }
            return 0;
        }

        /// <summary>
        /// 売却可否。Consumable/Material のみ true。
        /// Weapon/Shield/Core/KeyItem/PlayerMagic/CompanionMagic/Flavor は false。
        /// </summary>
        public static bool CanSell(ItemCategory category)
        {
            return category == ItemCategory.Consumable
                || category == ItemCategory.Material;
        }

        /// <summary>
        /// スタック可否。Consumable/Material のみ true。
        /// </summary>
        public static bool CanStack(ItemCategory category)
        {
            return category == ItemCategory.Consumable
                || category == ItemCategory.Material;
        }
    }
}
