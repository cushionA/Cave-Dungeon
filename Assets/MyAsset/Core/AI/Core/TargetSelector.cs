using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>同じ観測版の候補をフィルタし、実際に取得できた値だけで選択する。</summary>
    public static partial class TargetSelector
    {
        public static int SelectTarget(AITargetSelect select, int ownerHash,
            AIObservationContext observation, IReadOnlyList<int> candidates = null)
        {
            if (observation == null || !observation.TryGetActor(ownerHash, out _)) { return 0; }
            IReadOnlyList<int> hashes = candidates ?? observation.ActorHashes;
            int best = 0;
            float bestValue = 0f;
            for (int i = 0; i < hashes.Count; i++)
            {
                int hash = hashes[i];
                if (!observation.TryGetActor(hash, out AIActorObservation candidate)
                    || !observation.Matches(select.filter, ownerHash, candidate)
                    || !observation.TryGetSortValue(select.sortKey, ownerHash, hash, out float value)) { continue; }
                if (best == 0 || (select.isDescending ? value > bestValue : value < bestValue))
                {
                    best = hash;
                    bestValue = value;
                }
            }
            return best;
        }
    }
}
