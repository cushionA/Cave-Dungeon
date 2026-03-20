using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// チャレンジモードの定義データ。
    /// ScriptableObjectとしてアセット管理する。
    /// テスト用にSetForTestメソッドを提供する。
    /// </summary>
    [CreateAssetMenu(fileName = "NewChallenge", menuName = "Game/Challenge/ChallengeDefinition")]
    public class ChallengeDefinition : ScriptableObject
    {
        [Header("基本情報")]
        [SerializeField] private string challengeId;
        [SerializeField] private string challengeName;
        [SerializeField] private string description;
        [SerializeField] private ChallengeType challengeType;

        [Header("制限")]
        [SerializeField] private float timeLimit;
        [SerializeField] private int maxDeathCount;

        [Header("コンテンツ")]
        [SerializeField] private string[] bossIds;
        [SerializeField] private int waveCount;
        [SerializeField] private int enemiesPerWave;
        [SerializeField] private int levelCap;

        [Header("ランク閾値")]
        [SerializeField] private float silverTimeThreshold;
        [SerializeField] private float goldTimeThreshold;
        [SerializeField] private int goldScoreThreshold;
        [SerializeField] private int platinumScoreThreshold;

        [Header("報酬")]
        [SerializeField] private int currencyReward;
        [SerializeField] private string itemRewardId;

        // === Properties ===

        public string ChallengeId => challengeId;
        public string ChallengeName => challengeName;
        public string Description => description;
        public ChallengeType ChallengeTypeValue => challengeType;
        public float TimeLimit => timeLimit;
        public int MaxDeathCount => maxDeathCount;
        public string[] BossIds => bossIds;
        public int WaveCount => waveCount;
        public int EnemiesPerWave => enemiesPerWave;
        public int LevelCap => levelCap;
        public float SilverTimeThreshold => silverTimeThreshold;
        public float GoldTimeThreshold => goldTimeThreshold;
        public int GoldScoreThreshold => goldScoreThreshold;
        public int PlatinumScoreThreshold => platinumScoreThreshold;
        public int CurrencyReward => currencyReward;
        public string ItemRewardId => itemRewardId;

        /// <summary>
        /// テスト用セットアップメソッド。
        /// ScriptableObject.CreateInstance後に呼び出して値を設定する。
        /// </summary>
        public void SetForTest(
            string challengeId,
            string challengeName,
            ChallengeType challengeType,
            float timeLimit = 0f,
            int maxDeathCount = 0,
            string[] bossIds = null,
            int waveCount = 0,
            int enemiesPerWave = 0,
            int levelCap = 0,
            float silverTimeThreshold = 0f,
            float goldTimeThreshold = 0f,
            int goldScoreThreshold = 0,
            int platinumScoreThreshold = 0,
            int currencyReward = 0,
            string itemRewardId = "")
        {
            this.challengeId = challengeId;
            this.challengeName = challengeName;
            this.challengeType = challengeType;
            this.timeLimit = timeLimit;
            this.maxDeathCount = maxDeathCount;
            this.bossIds = bossIds ?? new string[0];
            this.waveCount = waveCount;
            this.enemiesPerWave = enemiesPerWave;
            this.levelCap = levelCap;
            this.silverTimeThreshold = silverTimeThreshold;
            this.goldTimeThreshold = goldTimeThreshold;
            this.goldScoreThreshold = goldScoreThreshold;
            this.platinumScoreThreshold = platinumScoreThreshold;
            this.currencyReward = currencyReward;
            this.itemRewardId = itemRewardId;
        }
    }
}
