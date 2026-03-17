namespace Game.Core
{
    /// <summary>
    /// 装備ステータス計算のピュアロジック。MonoBehaviour非依存。
    /// 攻撃力スケーリング、重量比率、性能倍率を算出する。
    /// </summary>
    public static class EquipmentStatCalculator
    {
        private const float k_WeightPenaltyThreshold = 0.7f;
        private const float k_MinPerformanceMultiplier = 0.5f;
        private const float k_PenaltyRange = 0.3f; // 1.0 - 0.7

        /// <summary>
        /// 攻撃力スケーリング。基礎攻撃力 * (1 + scalingFactor * statValue / 100)
        /// </summary>
        /// <param name="baseAttack">基礎攻撃力</param>
        /// <param name="scalingFactor">武器のスケーリング係数(0.0~1.0)</param>
        /// <param name="statValue">対応能力値(str/dex/intel)</param>
        /// <returns>スケーリング後の攻撃力（整数切り捨て）</returns>
        public static int CalculateScaledAttack(int baseAttack, float scalingFactor, int statValue)
        {
            float multiplier = 1.0f + scalingFactor * statValue / 100.0f;
            return (int)(baseAttack * multiplier);
        }

        /// <summary>
        /// 重量比率計算。totalWeight / maxCarryWeight。0未満は0、1超過は1にクランプ。
        /// maxCarryWeightが0以下の場合は1.0（過積載）を返す。
        /// </summary>
        /// <param name="totalWeight">装備合計重量</param>
        /// <param name="maxCarryWeight">最大搬送重量</param>
        /// <returns>0.0~1.0にクランプされた重量比率</returns>
        public static float CalculateWeightRatio(int totalWeight, int maxCarryWeight)
        {
            if (maxCarryWeight <= 0)
            {
                return 1.0f;
            }

            float ratio = (float)totalWeight / maxCarryWeight;

            if (ratio < 0.0f)
            {
                return 0.0f;
            }

            if (ratio > 1.0f)
            {
                return 1.0f;
            }

            return ratio;
        }

        /// <summary>
        /// 性能倍率計算。weightRatioに応じた性能低下。
        /// weightRatio 0.0~0.7 -> 倍率1.0（ペナルティなし）
        /// weightRatio 0.7~1.0 -> 線形補間で1.0から0.5へ低下
        /// </summary>
        /// <param name="weightRatio">重量比率(0.0~1.0)</param>
        /// <returns>性能倍率(0.5~1.0)</returns>
        public static float CalculatePerformanceMultiplier(float weightRatio)
        {
            if (weightRatio <= k_WeightPenaltyThreshold)
            {
                return 1.0f;
            }

            float t = (weightRatio - k_WeightPenaltyThreshold) / k_PenaltyRange;

            if (t > 1.0f)
            {
                t = 1.0f;
            }

            // Lerp(1.0, 0.5, t) = 1.0 + (0.5 - 1.0) * t = 1.0 - 0.5 * t
            return 1.0f + (k_MinPerformanceMultiplier - 1.0f) * t;
        }
    }
}
