using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Game.Core
{
    public class InventoryManager : ISaveable
    {
        /// <summary>インベントリの最大スロット数（暫定）</summary>
        public const int k_MaxSlotCount = 99;

        private readonly List<ItemEntry> _items;

        // ===== ISaveable =====
        public string SaveId => "InventoryManager";

        object ISaveable.Serialize()
        {
            return new List<ItemEntry>(_items);
        }

        void ISaveable.Deserialize(object data)
        {
            _items.Clear();
            if (data is List<ItemEntry> items)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    _items.Add(items[i]);
                }
            }
            else if (data is JArray jArray)
            {
                List<ItemEntry> converted = jArray.ToObject<List<ItemEntry>>();
                if (converted != null)
                {
                    for (int i = 0; i < converted.Count; i++)
                    {
                        _items.Add(converted[i]);
                    }
                }
            }
        }

        public int ItemCount => _items.Count;

        public InventoryManager()
        {
            _items = new List<ItemEntry>();
        }

        /// <summary>
        /// アイテム追加。同一itemIdの全スタックを走査して余裕があれば順次充填し、
        /// 余剰分は新スロットとして追加する（k_MaxSlotCount まで）。
        /// 戻り値: 実際に追加された数量（上限到達で受け入れ切れなかった分は加算されない）。
        /// </summary>
        public int Add(int itemId, ItemCategory category, int count, int maxStack)
        {
            if (count <= 0)
            {
                return 0;
            }

            int remaining = count;
            int added = 0;

            // Pass 1: 既存の同一itemIdスタックに余地があれば順次詰める
            for (int i = 0; i < _items.Count && remaining > 0; i++)
            {
                if (_items[i].itemId != itemId)
                {
                    continue;
                }

                ItemEntry entry = _items[i];
                int space = entry.maxStack - entry.count;
                if (space <= 0)
                {
                    continue;
                }

                int toAdd = Math.Min(remaining, space);
                entry.count += toAdd;
                _items[i] = entry;
                added += toAdd;
                remaining -= toAdd;
            }

            // Pass 2: 余剰分は新スロットに配置（k_MaxSlotCount 上限まで）
            while (remaining > 0 && _items.Count < k_MaxSlotCount)
            {
                int toAdd = Math.Min(remaining, maxStack);
                ItemEntry newEntry = new ItemEntry
                {
                    itemId = itemId,
                    category = category,
                    count = toAdd,
                    maxStack = maxStack,
                };
                _items.Add(newEntry);
                added += toAdd;
                remaining -= toAdd;
            }

            return added;
        }

        /// <summary>
        /// アイテム削除。数量指定。
        /// 同一itemIdが複数スタックに分割されている場合、先頭から順に跨いで消費する。
        /// 空になったスタックはエントリごと除去する。
        /// 戻り値: 実際に削除された数量（総数不足時は削れた分だけ返る）。
        /// </summary>
        public int Remove(int itemId, int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            int remaining = count;
            int removed = 0;

            for (int i = 0; i < _items.Count && remaining > 0;)
            {
                if (_items[i].itemId != itemId)
                {
                    i++;
                    continue;
                }

                ItemEntry entry = _items[i];
                int toRemove = Math.Min(remaining, entry.count);
                entry.count -= toRemove;
                removed += toRemove;
                remaining -= toRemove;

                if (entry.count <= 0)
                {
                    _items.RemoveAt(i);
                    // RemoveAt で後続が詰まるので i は据え置き
                }
                else
                {
                    _items[i] = entry;
                    i++;
                }
            }

            return removed;
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
        /// アイテムID指定で数量取得（複数スタック分を合算）。なければ0。
        /// </summary>
        public int GetCount(int itemId)
        {
            int total = 0;
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].itemId == itemId)
                {
                    total += _items[i].count;
                }
            }
            return total;
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
