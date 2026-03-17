namespace Game.Core
{
    /// <summary>
    /// スキル発動条件の判定ロジック。
    /// 空中攻撃可否、MP/スタミナによるスキル発動可否を判定する。
    /// </summary>
    public static class SkillConditionLogic
    {
        /// <summary>空中攻撃可否。isGrounded=false かつ isAlive=true で可能。</summary>
        public static bool CanAerialAttack(bool isGrounded, bool isAlive)
        {
            return !isGrounded && isAlive;
        }

        /// <summary>スキル発動可否。MP >= mpCost かつ スタミナ >= staminaCost で可能。</summary>
        public static bool CanUseSkill(float currentMp, float mpCost, float currentStamina, float staminaCost)
        {
            return currentMp >= mpCost && currentStamina >= staminaCost;
        }
    }
}
