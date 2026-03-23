namespace Game.Core
{
    /// <summary>
    /// ダメージ計算の静的ユーティリティ。
    /// 7属性別ダメージ計算、弱点倍率、クリティカル判定を提供する。
    /// </summary>
    public static class DamageCalculator
    {
        public const int k_MinDamage = 1;
        public const float k_WeaknessMult = 1.5f;
        public const float k_ResistMult = 0.5f;

        /// <summary>
        /// 単一属性の基本ダメージ計算。
        /// 式: (atk² × motionValue) / (atk + def)
        /// </summary>
        public static int CalculateBaseDamage(int attack, float motionValue, int defense)
        {
            if (attack <= 0)
            {
                return 0;
            }

            int atk = attack;
            int def = defense > 0 ? defense : 0;
            int denominator = atk + def;
            if (denominator <= 0)
            {
                return k_MinDamage;
            }
            int raw = (int)((float)(atk * atk) * motionValue / denominator);
            return raw < k_MinDamage ? k_MinDamage : raw;
        }

        /// <summary>
        /// 7属性それぞれについてダメージ計算し、合計を返す。
        /// 各属性: CalculateBaseDamage(atk, motionValue, def) × weakMult
        /// </summary>
        public static int CalculateTotalDamage(
            ElementalStatus attackStats,
            float motionValue,
            ElementalStatus defenseStats,
            Element weakElement)
        {
            int total = 0;
            total += CalculateChannelDamage(attackStats.slash, motionValue, defenseStats.slash, Element.Slash, weakElement);
            total += CalculateChannelDamage(attackStats.strike, motionValue, defenseStats.strike, Element.Strike, weakElement);
            total += CalculateChannelDamage(attackStats.pierce, motionValue, defenseStats.pierce, Element.Pierce, weakElement);
            total += CalculateChannelDamage(attackStats.fire, motionValue, defenseStats.fire, Element.Fire, weakElement);
            total += CalculateChannelDamage(attackStats.thunder, motionValue, defenseStats.thunder, Element.Thunder, weakElement);
            total += CalculateChannelDamage(attackStats.light, motionValue, defenseStats.light, Element.Light, weakElement);
            total += CalculateChannelDamage(attackStats.dark, motionValue, defenseStats.dark, Element.Dark, weakElement);
            return total < k_MinDamage ? k_MinDamage : total;
        }

        /// <summary>
        /// 単一属性チャネルのダメージ計算。attackが0なら0を返す。
        /// </summary>
        public static int CalculateChannelDamage(
            int attack, float motionValue, int defense,
            Element channel, Element weakElement)
        {
            if (attack <= 0)
            {
                return 0;
            }

            int baseDmg = CalculateBaseDamage(attack, motionValue, defense);
            float multiplier = GetWeaknessMultiplier(channel, weakElement);
            return (int)(baseDmg * multiplier);
        }

        /// <summary>
        /// 弱点倍率を取得。弱点ヒットならk_WeaknessMult、それ以外は1.0。
        /// Flags比較: weakElementに該当チャネルが含まれていればHit。
        /// </summary>
        public static float GetWeaknessMultiplier(Element channel, Element weakElement)
        {
            if (channel != Element.None && (weakElement & channel) != 0)
            {
                return k_WeaknessMult;
            }
            return 1.0f;
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
