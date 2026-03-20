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

    /// <summary>
    /// AIテンプレートデータ。既存 CompanionAIConfig を内包。
    /// </summary>
    [Serializable]
    public struct AITemplateData
    {
        public string templateId;
        public string templateName;
        public string description;
        public string authorName;
        public AITemplateCategory category;
        public CompanionAIConfig config;
        public string[] tags;
    }
}
