using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// HP reduction, armor break, and knockback force calculation.
    /// Pure static logic - no MonoBehaviour dependency.
    /// </summary>
    public static class HpArmorLogic
    {
        /// <summary>
        /// Bonus damage multiplier applied when armor is broken.
        /// </summary>
        public const float k_ArmorBreakBonusMult = 1.3f;

        /// <summary>
        /// Apply damage to a character with action armor priority.
        /// Action armor is consumed first, then base armor.
        /// When total armor (action + base) reaches 0, armorBroken bonus applies.
        /// HP is clamped to a minimum of 0.
        /// </summary>
        /// <param name="currentHp">Current HP (modified in place).</param>
        /// <param name="currentArmor">Current base armor (modified in place).</param>
        /// <param name="rawDamage">Base damage before armor break bonus.</param>
        /// <param name="armorBreakValue">Amount of armor to destroy.</param>
        /// <param name="actionArmor">Current action armor (modified in place). 0 if no action armor active.</param>
        /// <returns>(actualDamage, isKill, armorBroken)</returns>
        // TODO: ref引数が5個あり引数順序ミスのリスクがある。
        // DamageApplication構造体にまとめることを検討（破壊的変更のため次回リファクタリングで対応）。
        public static (int actualDamage, bool isKill, bool armorBroken) ApplyDamage(
            ref int currentHp, ref float currentArmor, int rawDamage, float armorBreakValue,
            ref float actionArmor)
        {
            bool armorBroken = false;
            int actualDamage = rawDamage;

            // Step 1: Apply armor break (action armor first, then base armor)
            if (armorBreakValue > 0f)
            {
                float remaining = armorBreakValue;

                // Step 1a: Consume action armor first
                if (actionArmor > 0f)
                {
                    float absorbed = Mathf.Min(actionArmor, remaining);
                    actionArmor -= absorbed;
                    remaining -= absorbed;
                }

                // Step 1b: Remaining break value goes to base armor
                if (remaining > 0f)
                {
                    currentArmor -= remaining;
                }

                // Step 2: Check if all armor is broken
                if (currentArmor <= 0f && actionArmor <= 0f)
                {
                    armorBroken = true;
                    actualDamage = Mathf.FloorToInt(rawDamage * k_ArmorBreakBonusMult);
                }
            }

            // Step 3: Reduce HP (clamp to 0)
            currentHp = Mathf.Max(0, currentHp - actualDamage);

            // Step 4: Determine kill
            bool isKill = currentHp <= 0;

            return (actualDamage, isKill, armorBroken);
        }

        /// <summary>
        /// Apply armor recovery over time. Recovery starts after delay has elapsed.
        /// </summary>
        /// <param name="currentArmor">Current armor (modified in place).</param>
        /// <param name="maxArmor">Maximum armor value.</param>
        /// <param name="recoveryRate">Recovery per second.</param>
        /// <param name="deltaTime">Time elapsed this frame.</param>
        public static void RecoverArmor(ref float currentArmor, float maxArmor,
            float recoveryRate, float deltaTime)
        {
            if (currentArmor >= maxArmor || recoveryRate <= 0f)
            {
                return;
            }

            currentArmor = Mathf.Min(maxArmor, currentArmor + recoveryRate * deltaTime);
        }

        /// <summary>
        /// Calculate knockback force after applying resistance.
        /// Result = baseForce * (1 - knockbackResistance), clamped so resistance cannot exceed 1.
        /// </summary>
        /// <param name="baseForce">Raw knockback direction and magnitude.</param>
        /// <param name="knockbackResistance">Resistance factor (0 = no resistance, 1 = full immunity).</param>
        /// <returns>Adjusted knockback force vector.</returns>
        public static Vector2 CalculateKnockback(Vector2 baseForce, float knockbackResistance)
        {
            float multiplier = Mathf.Max(0f, 1f - knockbackResistance);
            return baseForce * multiplier;
        }
    }
}
