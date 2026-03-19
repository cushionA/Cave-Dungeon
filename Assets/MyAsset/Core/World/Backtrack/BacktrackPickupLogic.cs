namespace Game.Core
{
    /// <summary>
    /// バックトラック報酬のインタラクト（回収）ロジック。
    /// MonoBehaviour版BacktrackRewardPickupがこのクラスをコンポジションで保持する。
    /// </summary>
    public class BacktrackPickupLogic
    {
        private readonly BacktrackRewardManager _manager;

        public BacktrackPickupLogic(BacktrackRewardManager manager)
        {
            _manager = manager;
        }

        /// <summary>
        /// 報酬の回収を試行する。
        /// 能力チェック + 回収済みチェックを行い、成功したら MarkCollected する。
        /// </summary>
        public bool TryCollect(string rewardId, AbilityFlag currentAbilities)
        {
            if (_manager.IsCollected(rewardId))
            {
                return false;
            }

            BacktrackRewardData reward = _manager.FindReward(rewardId);
            if (reward.rewardId == null)
            {
                return false;
            }

            if (!BacktrackRewardChecker.CanCollect(reward, currentAbilities))
            {
                return false;
            }

            _manager.MarkCollected(rewardId);
            return true;
        }
    }
}
