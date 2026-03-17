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
        /// Apply damage to a character. If armorBreakValue > 0, armor is reduced first.
        /// When armor reaches 0, armorBroken is flagged and rawDamage receives a bonus multiplier.
        /// HP is clamped to a minimum of 0.
        /// </summary>
        /// <param name="currentHp">Current HP (modified in place).</param>
        /// <param name="currentArmor">Current armor (modified in place).</param>
        /// <param name="rawDamage">Base damage before armor break bonus.</param>
        /// <param name="armorBreakValue">Amount of armor to destroy.</param>
        /// <returns>(actualDamage, isKill, armorBroken)</returns>
        public static (int actualDamage, bool isKill, bool armorBroken) ApplyDamage(
            ref int currentHp, ref float currentArmor, int rawDamage, float armorBreakValue)
        {
            bool armorBroken = false;
            int actualDamage = rawDamage;

            // Step 1: Apply armor break
            if (armorBreakValue > 0f)
            {
                currentArmor -= armorBreakValue;

                // Step 2: Check if armor is broken
                if (currentArmor <= 0f)
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
