using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>同じ観測版の候補をフィルタし、実際に取得できた値だけで選択する。</summary>
    public static partial class TargetSelector
    {
        public static int SelectTarget(in AITargetSelect select, int ownerHash,
            AIObservationContext observation, IReadOnlyList<int> candidates = null)
        {
            if (observation == null) { return 0; }
            AIManagedWorld world = new AIManagedWorld { observation = observation };
            AIManagedCandidates sampled = new AIManagedCandidates { hashes = candidates ?? observation.ActorHashes };
            return AIQueryKernel.SelectTarget(select, ownerHash, ref world, ref sampled);
        }
    }
}
