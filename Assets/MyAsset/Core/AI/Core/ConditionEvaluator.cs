using UnityEngine;

namespace Game.Core
{
    /// <summary>確定した観測だけからAI条件を評価する。unknownは比較へ渡さない。</summary>
    public static partial class ConditionEvaluator
    {
        public static bool Evaluate(AICondition condition, int ownerHash, int targetHash, AIObservationContext observation)
        {
            return observation != null
                && observation.TryGetConditionValue(condition, ownerHash, targetHash, out float value)
                && Compare(value, condition.compareOp, condition.operandA, condition.operandB);
        }

        public static bool EvaluateAll(AICondition[] conditions, int ownerHash, int targetHash, AIObservationContext observation)
        {
            if (conditions == null || conditions.Length == 0) { return true; }
            for (int i = 0; i < conditions.Length; i++)
            {
                if (!Evaluate(conditions[i], ownerHash, targetHash, observation)) { return false; }
            }
            return true;
        }

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
    }
}
