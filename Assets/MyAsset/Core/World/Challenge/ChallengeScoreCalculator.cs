using System;
using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// チャレンジモードのスコア計算とランク評価を行う静的クラス。
    /// MonoBehaviour非依存。
    /// </summary>
    public static class ChallengeScoreCalculator
    {
        private const int k_BaseScore = 10000;
        private const float k_HpBonusMultiplier = 0.1f;
        private const float k_MaxHpBonusRatio = 2.0f;
        private const float k_DeathPenaltyPerDeath = 0.1f;
        private const float k_MinDeathPenalty = 0.1f;

        /// <summary>
        /// スコアを計算する。
        /// score = (baseScore / clearTime) * (1 + hpBonusRatio) * deathPenalty
        /// </summary>
        /// <param name="result">チャレンジ結果データ</param>
        /// <param name="definition">チャレンジ定義（現在baseScoreは定数）</param>
        /// <returns>計算されたスコア（整数に切り捨て）</returns>
        public static int CalculateScore(ChallengeResult result, ChallengeDefinition definition)
        {
            float clearTime = Mathf.Max(result.clearTime, 0.001f);

            int safeDamageTaken = Mathf.Max(1, result.totalDamageTaken);
            float rawHpBonus = (float)result.totalDamageDealt / safeDamageTaken * k_HpBonusMultiplier;
            float hpBonusRatio = Mathf.Min(rawHpBonus, k_MaxHpBonusRatio);

            float rawPenalty = 1.0f - result.deathCount * k_DeathPenaltyPerDeath;
            float deathPenalty = Mathf.Max(k_MinDeathPenalty, rawPenalty);

            float rawScore = (k_BaseScore / clearTime) * (1f + hpBonusRatio) * deathPenalty;

            return (int)rawScore;
        }

        /// <summary>
        /// ランクを評価する。
        /// Platinum: score >= platinumThreshold AND clearTime <= goldTimeThreshold AND deathCount == 0
        /// Gold: score >= goldScoreThreshold OR clearTime <= goldTimeThreshold
        /// Silver: clearTime <= silverTimeThreshold
        /// Bronze: クリア済み (state == Completed)
        /// None: 未クリア
        /// </summary>
        public static ChallengeRank EvaluateRank(ChallengeResult result, ChallengeDefinition definition)
        {
            if (result.state != ChallengeState.Completed)
            {
                return ChallengeRank.None;
            }

            bool isPlatinumScore = result.score >= definition.PlatinumScoreThreshold;
            bool isGoldTime = result.clearTime <= definition.GoldTimeThreshold;
            bool isNoDeath = result.deathCount == 0;

            if (isPlatinumScore && isGoldTime && isNoDeath)
            {
                return ChallengeRank.Platinum;
            }

            bool isGoldScore = result.score >= definition.GoldScoreThreshold;

            if (isGoldScore || isGoldTime)
            {
                return ChallengeRank.Gold;
            }

            bool isSilverTime = result.clearTime <= definition.SilverTimeThreshold;

            if (isSilverTime)
            {
                return ChallengeRank.Silver;
            }

            return ChallengeRank.Bronze;
        }
    }
}
