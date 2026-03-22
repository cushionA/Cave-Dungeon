using System.Collections.Generic;
using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// Evaluates AICondition structs against SoA character data.
    /// Pure static utility - no allocations, no state.
    /// </summary>
    public static class ConditionEvaluator
    {
        /// <summary>
        /// Evaluates a single AICondition against the data container.
        /// </summary>
        public static bool Evaluate(AICondition condition, int ownerHash, int targetHash,
            SoACharaDataDic data, float currentTime, DamageScoreTracker scoreTracker = null,
            List<int> allHashes = null, List<int> allyHashes = null)
        {
            float value = GetConditionValue(condition, ownerHash, targetHash, data, currentTime, scoreTracker, allHashes, allyHashes);
            return Compare(value, condition.compareOp, condition.operandA, condition.operandB);
        }

        /// <summary>
        /// Evaluates all conditions with AND logic. Returns true if all pass.
        /// Null or empty array returns true (no conditions = always valid).
        /// </summary>
        public static bool EvaluateAll(AICondition[] conditions, int ownerHash, int targetHash,
            SoACharaDataDic data, float currentTime, DamageScoreTracker scoreTracker = null,
            List<int> allHashes = null, List<int> allyHashes = null)
        {
            if (conditions == null || conditions.Length == 0)
            {
                return true;
            }

            for (int i = 0; i < conditions.Length; i++)
            {
                if (!Evaluate(conditions[i], ownerHash, targetHash, data, currentTime, scoreTracker, allHashes, allyHashes))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Extracts the numeric value for a condition from the data container.
        /// TargetFilter in the condition determines which characters to evaluate.
        /// allHashes/allyHashes: CharacterRegistryから渡されるキャラクターリスト（Count/NearbyFaction用）。
        /// </summary>
        public static float GetConditionValue(AICondition condition, int ownerHash, int targetHash,
            SoACharaDataDic data, float currentTime, DamageScoreTracker scoreTracker = null,
            List<int> allHashes = null, List<int> allyHashes = null)
        {
            switch (condition.conditionType)
            {
                case AIConditionType.HpRatio:
                {
                    int hash = ResolveTargetHash(condition.filter, ownerHash, targetHash);
                    if (!data.TryGetValue(hash, out int _))
                    {
                        return 0f;
                    }
                    ref CharacterVitals vitals = ref data.GetVitals(hash);
                    return vitals.maxHp > 0 ? (float)vitals.currentHp / vitals.maxHp * 100f : 0f;
                }

                case AIConditionType.MpRatio:
                {
                    int hash = ResolveTargetHash(condition.filter, ownerHash, targetHash);
                    if (!data.TryGetValue(hash, out int _))
                    {
                        return 0f;
                    }
                    ref CharacterVitals vitals = ref data.GetVitals(hash);
                    return vitals.maxMp > 0 ? (float)vitals.currentMp / vitals.maxMp * 100f : 0f;
                }

                case AIConditionType.StaminaRatio:
                {
                    int hash = ResolveTargetHash(condition.filter, ownerHash, targetHash);
                    if (!data.TryGetValue(hash, out int _))
                    {
                        return 0f;
                    }
                    ref CharacterVitals vitals = ref data.GetVitals(hash);
                    return vitals.maxStamina > 0f ? vitals.currentStamina / vitals.maxStamina * 100f : 0f;
                }

                case AIConditionType.ArmorRatio:
                {
                    int hash = ResolveTargetHash(condition.filter, ownerHash, targetHash);
                    if (!data.TryGetValue(hash, out int _))
                    {
                        return 0f;
                    }
                    ref CharacterVitals vitals = ref data.GetVitals(hash);
                    return vitals.maxArmor > 0f ? vitals.currentArmor / vitals.maxArmor * 100f : 0f;
                }

                case AIConditionType.Distance:
                {
                    if (!data.TryGetValue(ownerHash, out int _) ||
                        !data.TryGetValue(targetHash, out int _))
                    {
                        return float.MaxValue;
                    }
                    ref CharacterVitals ownerVitals = ref data.GetVitals(ownerHash);
                    ref CharacterVitals targetVitals = ref data.GetVitals(targetHash);
                    return Vector2.Distance(ownerVitals.position, targetVitals.position);
                }

                case AIConditionType.DamageScore:
                {
                    if (scoreTracker == null)
                    {
                        return 0f;
                    }
                    int scoreTargetHash = ResolveTargetHash(condition.filter, ownerHash, targetHash);
                    return scoreTracker.GetScore(scoreTargetHash, currentTime);
                }

                case AIConditionType.Count:
                {
                    // フィルタのbelong条件に一致するキャラクター数を返す
                    if (allHashes == null)
                    {
                        return 0f;
                    }
                    int count = 0;
                    for (int i = 0; i < allHashes.Count; i++)
                    {
                        int h = allHashes[i];
                        if (h == ownerHash && !condition.filter.includeSelf)
                        {
                            continue;
                        }
                        if (!data.TryGetValue(h, out int _))
                        {
                            continue;
                        }
                        ref CharacterFlags f = ref data.GetFlags(h);
                        if (condition.filter.belong != 0 && (f.Belong & condition.filter.belong) == 0)
                        {
                            continue;
                        }
                        count++;
                    }
                    return count;
                }

                case AIConditionType.NearbyFaction:
                {
                    // 指定距離内の同陣営キャラ数を返す（operandAを距離閾値として使用）
                    if (allHashes == null)
                    {
                        return 0f;
                    }
                    if (!data.TryGetValue(ownerHash, out int _))
                    {
                        return 0f;
                    }
                    ref CharacterVitals ownerV = ref data.GetVitals(ownerHash);
                    float threshold = condition.operandA > 0 ? condition.operandA : 10f;
                    float thresholdSq = threshold * threshold;
                    int factionCount = 0;
                    List<int> allies = allyHashes ?? allHashes;
                    for (int i = 0; i < allies.Count; i++)
                    {
                        int h = allies[i];
                        if (h == ownerHash)
                        {
                            continue;
                        }
                        if (!data.TryGetValue(h, out int _))
                        {
                            continue;
                        }
                        ref CharacterVitals v = ref data.GetVitals(h);
                        float dx = ownerV.position.x - v.position.x;
                        float dy = ownerV.position.y - v.position.y;
                        if (dx * dx + dy * dy <= thresholdSq)
                        {
                            factionCount++;
                        }
                    }
                    return factionCount;
                }

                case AIConditionType.ProjectileNear:
                {
                    // 弾丸検出はランタイム物理システム依存のため、SoAからは取得不可
                    // operandAを閾値距離として、将来的にProjectileManager連携予定
                    return 0f;
                }

                case AIConditionType.ObjectNearby:
                {
                    // オブジェクト検出はランタイム物理システム依存
                    // 将来的にインタラクタブルオブジェクトレジストリ連携予定
                    return 0f;
                }

                case AIConditionType.EventFired:
                {
                    // イベントフラグ判定: BrainEventFlagsからoperandAのビットが立っているか
                    if (!data.TryGetValue(ownerHash, out int _))
                    {
                        return 0f;
                    }
                    ref CharacterFlags flags = ref data.GetFlags(ownerHash);
                    return (flags.BrainEventFlags & (byte)condition.operandA) != 0 ? 1f : 0f;
                }

                case AIConditionType.SelfActState:
                {
                    if (!data.TryGetValue(ownerHash, out int _))
                    {
                        return 0f;
                    }
                    ref CharacterFlags flags = ref data.GetFlags(ownerHash);
                    return (float)(int)flags.ActState;
                }

                default:
                    return 0f;
            }
        }

        /// <summary>
        /// Compares a value against operands using the specified operator.
        /// </summary>
        public static bool Compare(float value, CompareOp op, int operandA, int operandB)
        {
            switch (op)
            {
                case CompareOp.Less:         return value < operandA;
                case CompareOp.LessEqual:    return value <= operandA;
                case CompareOp.Equal:        return Mathf.Approximately(value, operandA);
                case CompareOp.GreaterEqual: return value >= operandA;
                case CompareOp.Greater:      return value > operandA;
                case CompareOp.NotEqual:     return !Mathf.Approximately(value, operandA);
                case CompareOp.InRange:      return value >= operandA && value <= operandB;
                case CompareOp.HasFlag:      return ((int)value & operandA) == operandA;
                case CompareOp.HasAny:       return ((int)value & operandA) != 0;
                default:                     return false;
            }
        }

        /// <summary>
        /// Resolves which hash to use based on the filter's includeSelf flag.
        /// </summary>
        private static int ResolveTargetHash(TargetFilter filter, int ownerHash, int targetHash)
        {
            return filter.includeSelf ? ownerHash : targetHash;
        }

        /// <summary>
        /// Gets the maximum damage score from the score entries, applying time decay.
        /// </summary>
        private static float GetMaxDamageScore(DamageScoreEntry[] scores, float currentTime)
        {
            if (scores == null || scores.Length == 0)
            {
                return 0f;
            }

            float maxScore = 0f;
            for (int i = 0; i < scores.Length; i++)
            {
                if (scores[i].attackerHash == 0)
                {
                    continue;
                }

                float elapsed = currentTime - scores[i].lastUpdateTime;
                float decayed = scores[i].score * Mathf.Pow(0.95f, elapsed);
                if (decayed > maxScore)
                {
                    maxScore = decayed;
                }
            }
            return maxScore;
        }
    }
}
