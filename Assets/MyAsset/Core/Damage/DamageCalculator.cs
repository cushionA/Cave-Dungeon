namespace Game.Core
{
    /// <summary>
    /// ダメージ計算の静的ユーティリティ。
    /// 基本ダメージ、属性倍率、クリティカル判定を提供する。
    /// </summary>
    public static class DamageCalculator
    {
        public const int k_MinDamage = 1;
        public const float k_WeaknessMult = 1.5f;
        public const float k_ResistMult = 0.5f;

        /// <summary>
        /// 基本ダメージ計算。attack * motionValue - defense。最小k_MinDamage。
        /// </summary>
        public static int CalculateBaseDamage(int attack, float motionValue, int defense)
        {
            int raw = (int)(attack * motionValue) - defense;
            return raw < k_MinDamage ? k_MinDamage : raw;
        }

        /// <summary>
        /// 属性ダメージ計算。elementAttack * elementMult。
        /// weakElement→k_WeaknessMult、resistElement→k_ResistMult、それ以外→1.0。
        /// </summary>
        public static int CalculateElementalDamage(
            int elementAttack,
            Element attackElement,
            Element weakElement,
            Element resistElement)
        {
            float multiplier = 1.0f;

            if (attackElement != Element.None && attackElement == weakElement)
            {
                multiplier = k_WeaknessMult;
            }
            else if (attackElement != Element.None && attackElement == resistElement)
            {
                multiplier = k_ResistMult;
            }

            return (int)(elementAttack * multiplier);
        }

        /// <summary>
        /// クリティカル判定。critRate(0.0~1.0)を超えるか。
        /// randomValueを使った決定論的判定（テスト用）。
        /// </summary>
        public static bool IsCritical(float critRate, float randomValue)
        {
            return randomValue < critRate;
        }

        /// <summary>
        /// クリティカル倍率適用。damage * critMultiplier。
        /// </summary>
        public static int ApplyCritical(int damage, float critMultiplier, bool isCritical)
        {
            if (!isCritical)
            {
                return damage;
            }

            return (int)(damage * critMultiplier);
        }
    }
}
