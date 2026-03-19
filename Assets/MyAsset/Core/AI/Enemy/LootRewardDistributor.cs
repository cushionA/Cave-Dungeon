using System;

namespace Game.Core
{
    public class LootRewardDistributor
    {
        public event Action<int> OnExpRewarded;
        public event Action<int> OnCurrencyRewarded;
        public event Action<int, int> OnItemDropped;

        public void Distribute(DropTableEvaluator.DropResult result)
        {
            if (result.exp > 0)
            {
                OnExpRewarded?.Invoke(result.exp);
            }

            if (result.currency > 0)
            {
                OnCurrencyRewarded?.Invoke(result.currency);
            }

            for (int i = 0; i < result.droppedItemCount; i++)
            {
                OnItemDropped?.Invoke(result.droppedItemIds[i], result.droppedItemCounts[i]);
            }
        }
    }
}
