namespace Game.Core
{
    /// <summary>
    /// バックトラック報酬のマップマーカー状態。
    /// </summary>
    public enum BacktrackMarkerState : byte
    {
        Hidden,      // 能力未取得：非表示
        Available,   // 能力取得済み＋未回収：表示
        Collected    // 回収済み：完了マーク
    }

    /// <summary>
    /// バックトラック報酬のマップマーカー表示判定ロジック。
    /// MapSystemのマーカー管理と連携する。
    /// </summary>
    public class BacktrackMapMarker
    {
        private readonly string _rewardId;
        private readonly AbilityFlag _requiredAbility;

        public string RewardId => _rewardId;

        public BacktrackMapMarker(string rewardId, AbilityFlag requiredAbility)
        {
            _rewardId = rewardId;
            _requiredAbility = requiredAbility;
        }

        /// <summary>
        /// 現在の能力と回収状態からマーカー状態を判定する。
        /// </summary>
        public BacktrackMarkerState GetState(AbilityFlag currentAbilities, bool isCollected)
        {
            if (isCollected)
            {
                return BacktrackMarkerState.Collected;
            }

            if (_requiredAbility == AbilityFlag.None ||
                (currentAbilities & _requiredAbility) == _requiredAbility)
            {
                return BacktrackMarkerState.Available;
            }

            return BacktrackMarkerState.Hidden;
        }
    }
}
