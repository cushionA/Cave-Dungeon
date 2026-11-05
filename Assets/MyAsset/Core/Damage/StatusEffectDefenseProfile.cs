using System;

namespace Game.Core
{
    /// <summary>一つの効果に対する蓄積軽減・発症閾値・完全免疫。すべて値で保持する。</summary>
    [Serializable]
    public struct StatusEffectDefense
    {
        public float accumulationReduction;
        /// <summary>素体の 0 は既定閾値。装備入力では発症閾値への加算補正。</summary>
        public float threshold;
        public bool immune;
    }

    /// <summary>全効果共通と個別の防御入力。配列や参照を持たず、SoAの値として格納できる。</summary>
    [Serializable]
    public struct StatusEffectDefenseProfile
    {
        public const float DefaultThreshold = 100f;
        public float commonReduction;
        /// <summary>共通の発症閾値倍率への加算値。0.5 は閾値1.5倍、装備間は加算。</summary>
        public float thresholdMultiplierBonus;
        public StatusEffectDefense poison;
        public StatusEffectDefense burn;
        public StatusEffectDefense bleed;
        public StatusEffectDefense stun;
        public StatusEffectDefense freeze;
        public StatusEffectDefense paralyze;
        public StatusEffectDefense slow;
        public StatusEffectDefense blind;
        public StatusEffectDefense silence;
        public StatusEffectDefense weakness;
        public StatusEffectDefense curse;
        public StatusEffectDefense confusion;
        public StatusEffectDefense attackUp;
        public StatusEffectDefense defenseUp;
        public StatusEffectDefense speedUp;

        /// <summary>個別の未解決設定を取得する。None/範囲外は空の設定。</summary>
        public readonly StatusEffectDefense Get(StatusEffectId id)
        {
            switch (id)
            {
                case StatusEffectId.Poison: return poison;
                case StatusEffectId.Burn: return burn;
                case StatusEffectId.Bleed: return bleed;
                case StatusEffectId.Stun: return stun;
                case StatusEffectId.Freeze: return freeze;
                case StatusEffectId.Paralyze: return paralyze;
                case StatusEffectId.Slow: return slow;
                case StatusEffectId.Blind: return blind;
                case StatusEffectId.Silence: return silence;
                case StatusEffectId.Weakness: return weakness;
                case StatusEffectId.Curse: return curse;
                case StatusEffectId.Confusion: return confusion;
                case StatusEffectId.AttackUp: return attackUp;
                case StatusEffectId.DefenseUp: return defenseUp;
                case StatusEffectId.SpeedUp: return speedUp;
                default: return default;
            }
        }

        /// <summary>個別・共通軽減を合算し、従来のカット率は残存蓄積へ乗算する。</summary>
        public readonly StatusEffectDefense Resolve(StatusEffectId id, float statusCut = 0f)
        {
            Validate();
            RequireFinite(statusCut, nameof(statusCut));
            StatusEffectDefense selected = Get(id);
            double reduction = Clamp01((double)commonReduction + selected.accumulationReduction);
            double legacyCut = Clamp01(statusCut);
            selected.accumulationReduction = (float)(1d - (1d - reduction) * (1d - legacyCut));
            double threshold = selected.threshold == 0f ? DefaultThreshold : selected.threshold;
            selected.threshold = ClampThreshold(threshold * (1d + thresholdMultiplierBonus));
            return selected;
        }

        /// <summary>有限値だけを許容する。装備の軽減率/閾値には負の補正も指定できる。</summary>
        public readonly void Validate()
        {
            RequireFinite(commonReduction, nameof(commonReduction));
            RequireFinite(thresholdMultiplierBonus, nameof(thresholdMultiplierBonus));
            for (int i = (int)StatusEffectId.None + 1; i < (int)StatusEffectId.Count; i++)
            {
                StatusEffectDefense defense = Get((StatusEffectId)i);
                RequireFinite(defense.accumulationReduction, nameof(defense.accumulationReduction));
                RequireFinite(defense.threshold, nameof(defense.threshold));
            }
        }

        /// <summary>素体から毎回再導出する。装備の閾値は加算後1以上にし、倍率はResolveで適用する。</summary>
        public static StatusEffectDefenseProfile Combine(in StatusEffectDefenseProfile basis,
            in StatusEffectDefenseProfile weapon, in StatusEffectDefenseProfile shield,
            in StatusEffectDefenseProfile core)
        {
            basis.Validate();
            weapon.Validate();
            shield.Validate();
            core.Validate();
            return new StatusEffectDefenseProfile
            {
                commonReduction = AddFinite(basis.commonReduction, weapon.commonReduction, shield.commonReduction, core.commonReduction),
                thresholdMultiplierBonus = AddFinite(basis.thresholdMultiplierBonus, weapon.thresholdMultiplierBonus,
                    shield.thresholdMultiplierBonus, core.thresholdMultiplierBonus),
                poison = CombineEffect(basis.poison, weapon.poison, shield.poison, core.poison),
                burn = CombineEffect(basis.burn, weapon.burn, shield.burn, core.burn),
                bleed = CombineEffect(basis.bleed, weapon.bleed, shield.bleed, core.bleed),
                stun = CombineEffect(basis.stun, weapon.stun, shield.stun, core.stun),
                freeze = CombineEffect(basis.freeze, weapon.freeze, shield.freeze, core.freeze),
                paralyze = CombineEffect(basis.paralyze, weapon.paralyze, shield.paralyze, core.paralyze),
                slow = CombineEffect(basis.slow, weapon.slow, shield.slow, core.slow),
                blind = CombineEffect(basis.blind, weapon.blind, shield.blind, core.blind),
                silence = CombineEffect(basis.silence, weapon.silence, shield.silence, core.silence),
                weakness = CombineEffect(basis.weakness, weapon.weakness, shield.weakness, core.weakness),
                curse = CombineEffect(basis.curse, weapon.curse, shield.curse, core.curse),
                confusion = CombineEffect(basis.confusion, weapon.confusion, shield.confusion, core.confusion),
                attackUp = CombineEffect(basis.attackUp, weapon.attackUp, shield.attackUp, core.attackUp),
                defenseUp = CombineEffect(basis.defenseUp, weapon.defenseUp, shield.defenseUp, core.defenseUp),
                speedUp = CombineEffect(basis.speedUp, weapon.speedUp, shield.speedUp, core.speedUp),
            };
        }

        private static StatusEffectDefense CombineEffect(in StatusEffectDefense basis,
            in StatusEffectDefense weapon, in StatusEffectDefense shield, in StatusEffectDefense core)
        {
            // 補正合計0は素体の未設定値0を維持する。補正ありでは平坦閾値を先に1以上へ制限する。
            double thresholdAdjustment = (double)weapon.threshold + shield.threshold + core.threshold;
            float threshold = basis.threshold;
            if (thresholdAdjustment != 0d)
            {
                double baseThreshold = threshold == 0f ? DefaultThreshold : threshold;
                threshold = ClampThreshold(baseThreshold + thresholdAdjustment);
            }
            return new StatusEffectDefense
            {
                accumulationReduction = AddFinite(basis.accumulationReduction, weapon.accumulationReduction,
                    shield.accumulationReduction, core.accumulationReduction),
                threshold = threshold,
                immune = basis.immune || weapon.immune || shield.immune || core.immune,
            };
        }

        private static float AddFinite(float first, float second, float third, float fourth)
        {
            double sum = (double)first + second + third + fourth;
            return (float)Math.Min(float.MaxValue, Math.Max(-float.MaxValue, sum));
        }

        private static double Clamp01(double value) => Math.Min(1d, Math.Max(0d, value));

        private static float ClampThreshold(double value) => (float)Math.Min(float.MaxValue, Math.Max(1d, value));

        private static void RequireFinite(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new ArgumentException("Status effect defense values must be finite.", name);
            }
        }

    }
}

