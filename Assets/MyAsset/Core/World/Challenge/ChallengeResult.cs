using System;

namespace Game.Core
{
    /// <summary>
    /// チャレンジ1回分の結果データ。
    /// </summary>
    [Serializable]
    public struct ChallengeResult
    {
        public string challengeId;
        public ChallengeRank rank;
        public ChallengeState state;
        public float clearTime;
        public int score;
        public int deathCount;
        public int totalDamageDealt;
        public int totalDamageTaken;
        public bool isNewRecord;
    }
}
