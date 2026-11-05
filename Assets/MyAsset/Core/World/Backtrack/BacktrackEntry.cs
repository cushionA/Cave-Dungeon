using System;

namespace Game.Core
{

    /// <summary>
    /// バックトラック報酬エントリ。回収状態を含む。
    /// </summary>
    [Serializable]
    public struct BacktrackEntry
    {
        public string rewardId;
        public BacktrackRewardType rewardType;
        public AbilityFlag requiredAbility;    // 必要な能力フラグ
        public string locationHint;            // マップ上のヒント文
        public bool collected;                 // 回収済みフラグ
    }
}
