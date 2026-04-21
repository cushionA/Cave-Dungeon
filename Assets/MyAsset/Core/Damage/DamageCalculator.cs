namespace Game.Core
{
    /// <summary>
    /// ダメージ計算の静的ユーティリティ。
    /// 7属性別ダメージ計算 + ガードカット率適用を提供する。
    /// 弱点倍率・クリティカル機構は仕様外のため非搭載
    /// (弱点は defense[channel] を属性別に低く設定することで表現)。
    /// </summary>
    public static class DamageCalculator
    {
        public const int k_MinDamage = 1;

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
        /// 各属性: CalculateBaseDamage(atk, motionValue, def)
        /// </summary>
        public static int CalculateTotalDamage(
            ElementalStatus attackStats,
            float motionValue,
            ElementalStatus defenseStats)
        {
            int total = 0;
            total += CalculateChannelDamage(attackStats.slash, motionValue, defenseStats.slash);
            total += CalculateChannelDamage(attackStats.strike, motionValue, defenseStats.strike);
            total += CalculateChannelDamage(attackStats.pierce, motionValue, defenseStats.pierce);
            total += CalculateChannelDamage(attackStats.fire, motionValue, defenseStats.fire);
            total += CalculateChannelDamage(attackStats.thunder, motionValue, defenseStats.thunder);
            total += CalculateChannelDamage(attackStats.light, motionValue, defenseStats.light);
            total += CalculateChannelDamage(attackStats.dark, motionValue, defenseStats.dark);
            return total < k_MinDamage ? k_MinDamage : total;
        }

        /// <summary>
        /// 単一属性チャネルのダメージ計算。attackが0なら0を返す。
        /// </summary>
        public static int CalculateChannelDamage(
            int attack, float motionValue, int defense)
        {
            if (attack <= 0)
            {
                return 0;
            }

            return CalculateBaseDamage(attack, motionValue, defense);
        }

        /// <summary>
        /// 7属性それぞれについてダメージ計算し、属性別ガードカット率を適用して合計を返す。
        /// applyCuts=false なら CalculateTotalDamage と等価（回帰互換）。
        /// 各チャネル: CalculateBaseDamage × (1 - xxxCut) を floor して合算。
        /// </summary>
        public static int CalculateTotalDamageWithElementalCut(
            ElementalStatus attackStats,
            float motionValue,
            ElementalStatus defenseStats,
            GuardStats guardCuts,
            bool applyCuts)
        {
            int total = 0;
            total += ApplyChannelWithCut(attackStats.slash,   motionValue, defenseStats.slash,   applyCuts ? guardCuts.slashCut   : 0f);
            total += ApplyChannelWithCut(attackStats.strike,  motionValue, defenseStats.strike,  applyCuts ? guardCuts.strikeCut  : 0f);
            total += ApplyChannelWithCut(attackStats.pierce,  motionValue, defenseStats.pierce,  applyCuts ? guardCuts.pierceCut  : 0f);
            total += ApplyChannelWithCut(attackStats.fire,    motionValue, defenseStats.fire,    applyCuts ? guardCuts.fireCut    : 0f);
            total += ApplyChannelWithCut(attackStats.thunder, motionValue, defenseStats.thunder, applyCuts ? guardCuts.thunderCut : 0f);
            total += ApplyChannelWithCut(attackStats.light,   motionValue, defenseStats.light,   applyCuts ? guardCuts.lightCut   : 0f);
            total += ApplyChannelWithCut(attackStats.dark,    motionValue, defenseStats.dark,    applyCuts ? guardCuts.darkCut    : 0f);
            return total < k_MinDamage ? k_MinDamage : total;
        }

        /// <summary>
        /// 単一属性チャネルに属性別カット率を適用する。cut=0ならそのままのチャネルダメージを返す。
        /// </summary>
        private static int ApplyChannelWithCut(
            int attack, float motionValue, int defense, float cut)
        {
            if (attack <= 0)
            {
                return 0;
            }

            int baseDmg = CalculateBaseDamage(attack, motionValue, defense);
            float channelDmg = baseDmg;
            if (cut > 0f)
            {
                float clampedCut = cut > 1f ? 1f : cut;
                channelDmg *= (1f - clampedCut);
            }
            // channelDmg は非負 (baseDmg>=0, (1-cut)>=0 のため)。明示の意図でFloorToInt。
            return UnityEngine.Mathf.FloorToInt(channelDmg);
        }
    }
}
