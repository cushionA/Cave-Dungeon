namespace Game.Core
{
    /// <summary>
    /// ボス報酬計算結果。
    /// </summary>
    public struct BossRewardResult
    {
        public int expReward;
        public int currencyReward;
    }

    /// <summary>
    /// ボス撃破報酬の計算・配布ロジック。
    /// DropTableは既存のEnemySystem_DropTableを使用するため、
    /// ここではEXP/通貨の固定報酬のみ管理する。
    /// </summary>
    public class BossRewardLogic
    {
        private readonly int _expReward;
        private readonly int _currencyReward;
        private bool _isDistributed;

        public bool IsDistributed => _isDistributed;

        public BossRewardLogic(int expReward, int currencyReward)
        {
            _expReward = expReward;
            _currencyReward = currencyReward;
            _isDistributed = false;
        }

        /// <summary>
        /// 報酬を計算する。
        /// </summary>
        public BossRewardResult CalculateReward()
        {
            return new BossRewardResult
            {
                expReward = _expReward,
                currencyReward = _currencyReward
            };
        }

        /// <summary>
        /// 配布済みとしてマークする（二重配布防止）。
        /// </summary>
        public void MarkDistributed()
        {
            _isDistributed = true;
        }
    }
}
