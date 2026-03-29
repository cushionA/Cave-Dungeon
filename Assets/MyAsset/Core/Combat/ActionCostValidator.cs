namespace Game.Core
{
    /// <summary>
    /// 行動実行のスタミナ・MPコスト検証。MonoBehaviour非依存。
    /// </summary>
    public static class ActionCostValidator
    {
        /// <summary>
        /// 現在のリソースで行動を実行可能か判定する。
        /// </summary>
        public static bool CanAfford(float currentStamina, int currentMp,
            float staminaCost, float mpCost)
        {
            if (staminaCost > 0f && currentStamina < staminaCost)
            {
                return false;
            }
            if (mpCost > 0f && currentMp < (int)mpCost)
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// スタミナ・MPを消費する。CanAffordで確認済みの前提で呼ぶ。
        /// </summary>
        public static void DeductCost(ref float currentStamina, ref int currentMp,
            float staminaCost, float mpCost)
        {
            currentStamina -= staminaCost;
            currentMp -= (int)mpCost;
        }
    }
}
