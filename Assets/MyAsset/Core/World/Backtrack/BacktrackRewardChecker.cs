namespace Game.Core
{
    /// <summary>
    /// 個別報酬の回収可能判定。AbilityFlag + 位置条件で判定する。
    /// </summary>
    public static class BacktrackRewardChecker
    {
        /// <summary>
        /// 報酬が回収可能か判定する。
        /// requiredAbility が None なら無条件で回収可能。
        /// それ以外は currentAbilities に全て含まれている場合のみ true。
        /// </summary>
        public static bool CanCollect(BacktrackRewardData reward, AbilityFlag currentAbilities)
        {
            if (reward.requiredAbility == AbilityFlag.None)
            {
                return true;
            }

            return (currentAbilities & reward.requiredAbility) == reward.requiredAbility;
        }
    }
}
