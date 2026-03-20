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
            SoACharaDataDic data, float currentTime, DamageScoreTracker scoreTracker = null)
        {
            float value = GetConditionValue(condition, ownerHash, targetHash, data, currentTime, scoreTracker);
            return Compare(value, condition.compareOp, condition.operandA, condition.operandB);
        }

        /// <summary>
        /// Evaluates all conditions with AND logic. Returns true if all pass.
        /// Null or empty array returns true (no conditions = always valid).
        /// </summary>
        public static bool EvaluateAll(AICondition[] conditions, int ownerHash, int targetHash,
            SoACharaDataDic data, float currentTime, DamageScoreTracker scoreTracker = null)
        {
            if (conditions == null || conditions.Length == 0)
            {
                return true;
            }

            for (int i = 0; i < conditions.Length; i++)
            {
                if (!Evaluate(conditions[i], ownerHash, targetHash, data, currentTime, scoreTracker))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Extracts the numeric value for a condition from the data container.
        /// TargetFilter in the condition determines which characters to evaluate.
        /// </summary>
        public static float GetConditionValue(AICondition condition, int ownerHash, int targetHash,
            SoACharaDataDic data, float currentTime, DamageScoreTracker scoreTracker = null)
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
                    // Count of characters matching the filter
                    // Requires runtime candidate list - return operandA as placeholder
                    return condition.operandA;
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

                // NearbyFaction, ProjectileNear, ObjectNearby, EventFired
                // require runtime systems not yet fully implemented.
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
