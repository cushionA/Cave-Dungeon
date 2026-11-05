using System;

namespace Game.Core
{

    /// <summary>
    /// リーダーボードの1チャレンジ分の記録。
    /// </summary>
    [Serializable]
    public struct LeaderboardEntry
    {
        public string challengeId;
        public ChallengeRank rank;
        public float bestTime;
        public int bestScore;
        public int attemptCount;
        public int clearCount;
        public string dateAchieved;
    }
}
