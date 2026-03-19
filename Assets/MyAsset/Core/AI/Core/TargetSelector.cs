using System.Collections.Generic;
using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// Filters and sorts candidate targets based on AITargetSelect criteria.
    /// Pure static utility - returns the best target hash or 0 if none found.
    /// </summary>
    public static class TargetSelector
    {
        /// <summary>
        /// Selects the best target from candidates based on filter and sort criteria.
        /// Returns 0 if no valid target found.
        /// </summary>
        public static int SelectTarget(AITargetSelect select, int ownerHash,
            List<int> candidateHashes, SoACharaDataDic data, float currentTime)
        {
            if (candidateHashes == null || candidateHashes.Count == 0)
            {
                return 0;
            }

            List<int> filtered = FilterCandidates(select.filter, ownerHash, candidateHashes, data);
            if (filtered.Count == 0)
            {
                return 0;
            }

            return SortAndPick(select.sortKey, select.isDescending, ownerHash, filtered, data, currentTime);
        }

        /// <summary>
        /// Filters candidates by TargetFilter criteria.
        /// Checks belong, feature, weakPoint, distance range.
        /// </summary>
        public static List<int> FilterCandidates(TargetFilter filter, int ownerHash,
            List<int> candidates, SoACharaDataDic data)
        {
            List<int> result = new List<int>();

            for (int i = 0; i < candidates.Count; i++)
            {
                int hash = candidates[i];

                // Self exclusion (unless includeSelf is set)
                if (hash == ownerHash && !filter.includeSelf)
                {
                    continue;
                }

                if (!data.TryGetValue(hash, out int _))
                {
                    continue;
                }

                ref CharacterFlags flags = ref data.GetFlags(hash);

                // Belong filter
                if (filter.belong != 0)
                {
                    bool isAnd = (filter.filterFlags & FilterBitFlag.BelongAnd) != 0;
                    if (isAnd)
                    {
                        if ((flags.Belong & filter.belong) != filter.belong)
                        {
                            continue;
                        }
                    }
                    else
                    {
                        if ((flags.Belong & filter.belong) == 0)
                        {
                            continue;
                        }
                    }
                }

                // Feature filter
                if (filter.feature != 0)
                {
                    bool isAnd = (filter.filterFlags & FilterBitFlag.FeatureAnd) != 0;
                    if (isAnd)
                    {
                        if ((flags.Feature & filter.feature) != filter.feature)
                        {
                            continue;
                        }
                    }
                    else
                    {
                        if ((flags.Feature & filter.feature) == 0)
                        {
                            continue;
                        }
                    }
                }

                // WeakPoint filter
                if (filter.weakPoint != 0)
                {
                    ref CombatStats combat = ref data.GetCombatStats(hash);
                    // Check if target has weakness to specified element
                    // (simplified: check if defense for that element is low)
                    // Full implementation depends on weakness system
                }

                // Distance range filter
                if (filter.distanceRange.x > 0f || filter.distanceRange.y > 0f)
                {
                    if (!data.TryGetValue(ownerHash, out int _))
                    {
                        continue;
                    }
                    ref CharacterVitals ownerV = ref data.GetVitals(ownerHash);
                    ref CharacterVitals targetV = ref data.GetVitals(hash);
                    float dist = Vector2.Distance(ownerV.position, targetV.position);

                    if (filter.distanceRange.x > 0f && dist < filter.distanceRange.x)
                    {
                        continue;
                    }
                    if (filter.distanceRange.y > 0f && dist > filter.distanceRange.y)
                    {
                        continue;
                    }
                }

                result.Add(hash);
            }

            return result;
        }

        /// <summary>
        /// Picks the best candidate by sorting on the specified key.
        /// isDescending=true selects the highest value, false selects the lowest.
        /// </summary>
        public static int SortAndPick(TargetSortKey sortKey, bool isDescending,
            int ownerHash, List<int> candidates, SoACharaDataDic data, float currentTime)
        {
            if (candidates.Count == 1)
            {
                return candidates[0];
            }

            int bestIndex = 0;
            float bestValue = GetSortValue(sortKey, ownerHash, candidates[0], data, currentTime);

            for (int i = 1; i < candidates.Count; i++)
            {
                float value = GetSortValue(sortKey, ownerHash, candidates[i], data, currentTime);
                bool isBetter = isDescending ? value > bestValue : value < bestValue;
                if (isBetter)
                {
                    bestValue = value;
                    bestIndex = i;
                }
            }

            return candidates[bestIndex];
        }

        private static float GetSortValue(TargetSortKey key, int ownerHash, int targetHash,
            SoACharaDataDic data, float currentTime)
        {
            switch (key)
            {
                case TargetSortKey.Distance:
                {
                    ref CharacterVitals ov = ref data.GetVitals(ownerHash);
                    ref CharacterVitals tv = ref data.GetVitals(targetHash);
                    return Vector2.Distance(ov.position, tv.position);
                }
                case TargetSortKey.HpRatio:
                {
                    ref CharacterVitals v = ref data.GetVitals(targetHash);
                    return v.maxHp > 0 ? (float)v.currentHp / v.maxHp : 0f;
                }
                case TargetSortKey.HpValue:
                {
                    ref CharacterVitals v = ref data.GetVitals(targetHash);
                    return v.currentHp;
                }
                case TargetSortKey.AttackPower:
                {
                    ref CombatStats c = ref data.GetCombatStats(targetHash);
                    return c.attack.Total;
                }
                case TargetSortKey.DefensePower:
                {
                    ref CombatStats c = ref data.GetCombatStats(targetHash);
                    return c.defense.Total;
                }
                case TargetSortKey.TargetingCount:
                {
                    // 狙われている数（ランタイムで集計が必要、暫定0）
                    return 0f;
                }
                case TargetSortKey.LastAttacker:
                {
                    // DamageScoreEntry[]はDamageScoreTracker側で管理。
                    // SoAコンテナ統合はSection 2 AI統合時に実施。
                    return 0f;
                }
                case TargetSortKey.DamageScore:
                {
                    // DamageScoreEntry[]はDamageScoreTracker側で管理。
                    // SoAコンテナ統合はSection 2 AI統合時に実施。
                    return 0f;
                }
                case TargetSortKey.Self:
                {
                    // 自分自身なら最高値（SelectTargetで自分を選ぶ用）
                    return targetHash == ownerHash ? 1f : 0f;
                }
                case TargetSortKey.Player:
                {
                    // プレイヤーなら最高値
                    ref CharacterFlags f = ref data.GetFlags(targetHash);
                    return (f.Feature & CharacterFeature.Player) != 0 ? 1f : 0f;
                }
                case TargetSortKey.Sister:
                {
                    // シスター（常駐仲間）なら最高値
                    ref CharacterFlags f = ref data.GetFlags(targetHash);
                    return (f.Feature & CharacterFeature.Companion) != 0 ? 1f : 0f;
                }
                default:
                    return 0f;
            }
        }
    }
}
