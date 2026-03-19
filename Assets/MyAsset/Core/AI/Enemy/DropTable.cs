using System;

namespace Game.Core
{
    [Serializable]
    public struct DropEntry
    {
        public int itemId;
        public float dropRate;
        public int minCount;
        public int maxCount;
    }

    [Serializable]
    public struct DropTableData
    {
        public int expReward;
        public int currencyMin;
        public int currencyMax;
        public DropEntry[] entries;
    }

    public static class DropTableEvaluator
    {
        public struct DropResult
        {
            public int exp;
            public int currency;
            public int[] droppedItemIds;
            public int[] droppedItemCounts;
            public int droppedItemCount;
        }

        public static DropResult Evaluate(DropTableData table, float[] randomValues)
        {
            DropResult result = new DropResult
            {
                exp = table.expReward,
                currency = table.currencyMin + (table.currencyMax > table.currencyMin
                    ? (int)(randomValues[0] * (table.currencyMax - table.currencyMin))
                    : 0)
            };

            if (table.entries == null || table.entries.Length == 0)
            {
                result.droppedItemIds = Array.Empty<int>();
                result.droppedItemCounts = Array.Empty<int>();
                return result;
            }

            int[] tempIds = new int[table.entries.Length];
            int[] tempCounts = new int[table.entries.Length];
            int count = 0;

            for (int i = 0; i < table.entries.Length; i++)
            {
                int rvIndex = i + 1;
                float rv = rvIndex < randomValues.Length ? randomValues[rvIndex] : 0f;
                if (rv < table.entries[i].dropRate)
                {
                    tempIds[count] = table.entries[i].itemId;
                    int range = table.entries[i].maxCount - table.entries[i].minCount;
                    tempCounts[count] = table.entries[i].minCount + (range > 0 ? (int)(rv / table.entries[i].dropRate * range) : 0);
                    if (tempCounts[count] < table.entries[i].minCount)
                    {
                        tempCounts[count] = table.entries[i].minCount;
                    }
                    count++;
                }
            }

            result.droppedItemIds = new int[count];
            result.droppedItemCounts = new int[count];
            Array.Copy(tempIds, result.droppedItemIds, count);
            Array.Copy(tempCounts, result.droppedItemCounts, count);
            result.droppedItemCount = count;

            return result;
        }
    }
}
