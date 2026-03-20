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
        // ホットパスでの毎回アロケーション回避用の再利用バッファ
        // NOTE: メインスレッド専用。Job System等から呼ばないこと
        private static readonly List<int> s_FilterBuffer = new List<int>(32);

        public static int SelectTarget(AITargetSelect select, int ownerHash,
            List<int> candidateHashes, SoACharaDataDic data, float currentTime,
            DamageScoreTracker scoreTracker = null)
        {
            if (candidateHashes == null || candidateHashes.Count == 0)
            {
                return 0;
            }

            s_FilterBuffer.Clear();
            FilterCandidates(select.filter, ownerHash, candidateHashes, data, s_FilterBuffer);
            if (s_FilterBuffer.Count == 0)
            {
                return 0;
            }

            return SortAndPick(select.sortKey, select.isDescending, ownerHash, s_FilterBuffer, data, currentTime, scoreTracker);
        }

        /// <summary>
        /// Filters candidates by TargetFilter criteria.
        /// Checks belong, feature, weakPoint, distance range.
        /// </summary>
        /// <summary>
        /// Filters candidates into the provided output list (zero-alloc).
        /// </summary>
        public static void FilterCandidates(TargetFilter filter, int ownerHash,
            List<int> candidates, SoACharaDataDic data, List<int> output)
        {
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

                // WeakPoint filter: 指定属性の防御値が0以下なら弱点とみなす
                if (filter.weakPoint != 0)
                {
                    ref CombatStats combat = ref data.GetCombatStats(hash);
                    bool hasWeakness = false;
                    if ((filter.weakPoint & Element.Slash) != 0 && combat.defense.slash <= 0) { hasWeakness = true; }
                    if ((filter.weakPoint & Element.Strike) != 0 && combat.defense.strike <= 0) { hasWeakness = true; }
                    if ((filter.weakPoint & Element.Pierce) != 0 && combat.defense.pierce <= 0) { hasWeakness = true; }
                    if ((filter.weakPoint & Element.Fire) != 0 && combat.defense.fire <= 0) { hasWeakness = true; }
                    if ((filter.weakPoint & Element.Thunder) != 0 && combat.defense.thunder <= 0) { hasWeakness = true; }
                    if ((filter.weakPoint & Element.Light) != 0 && combat.defense.light <= 0) { hasWeakness = true; }
                    if ((filter.weakPoint & Element.Dark) != 0 && combat.defense.dark <= 0) { hasWeakness = true; }
                    if (!hasWeakness)
                    {
                        continue;
                    }
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

                output.Add(hash);
            }
        }

        /// <summary>
        /// Filters candidates by TargetFilter criteria (allocating version for backward compat).
        /// </summary>
        public static List<int> FilterCandidates(TargetFilter filter, int ownerHash,
            List<int> candidates, SoACharaDataDic data)
        {
            List<int> result = new List<int>();
            FilterCandidates(filter, ownerHash, candidates, data, result);
            return result;
        }

        /// <summary>
        /// Picks the best candidate by sorting on the specified key.
        /// isDescending=true selects the highest value, false selects the lowest.
        /// </summary>
        public static int SortAndPick(TargetSortKey sortKey, bool isDescending,
            int ownerHash, List<int> candidates, SoACharaDataDic data, float currentTime,
            DamageScoreTracker scoreTracker = null)
        {
            if (candidates.Count == 1)
            {
                return candidates[0];
            }

            int bestIndex = 0;
            float bestValue = GetSortValue(sortKey, ownerHash, candidates[0], data, currentTime, scoreTracker);

            for (int i = 1; i < candidates.Count; i++)
            {
                float value = GetSortValue(sortKey, ownerHash, candidates[i], data, currentTime, scoreTracker);
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
            SoACharaDataDic data, float currentTime, DamageScoreTracker scoreTracker = null)
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
                    // 対象のRecognizeObjectTypeビットが立っている数をカウント
                    ref CharacterFlags tf = ref data.GetFlags(targetHash);
                    int bits = tf.RecognizeObjectType;
                    int popCount = 0;
                    while (bits != 0)
                    {
                        popCount += bits & 1;
                        bits >>= 1;
                    }
                    return popCount;
                }
                case TargetSortKey.LastAttacker:
                {
                    // DamageScoreTrackerから最高スコアの攻撃者ハッシュと一致するか
                    if (scoreTracker == null)
                    {
                        return 0f;
                    }
                    int topAttacker = scoreTracker.GetHighestScoreAttacker(currentTime);
                    return targetHash == topAttacker ? 1f : 0f;
                }
                case TargetSortKey.DamageScore:
                {
                    // DamageScoreTrackerから対象の累積ダメージスコアを取得
                    if (scoreTracker == null)
                    {
                        return 0f;
                    }
                    return scoreTracker.GetScore(targetHash, currentTime);
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
