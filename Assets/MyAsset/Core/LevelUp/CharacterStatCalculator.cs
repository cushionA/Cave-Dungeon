using System;

namespace Game.Core
{
    public static class CharacterStatCalculator
    {
        /// <summary>
        /// 素体・成長・全装備から利用値を再導出する。前回の導出結果には加算しない。
        /// 状態効果倍率、持ち方速度、重量による性能補正はここでは適用しない。
        /// </summary>
        public static EquipmentDerivedStats CalculateDerived(in CharacterBaseStats basis, in CharacterGrowth growth,
            in CharacterVitals current, in EquipmentStatModifiers weapon, in EquipmentStatModifiers shield,
            in EquipmentStatModifiers core)
        {
            StatModifier modifiers = AddAttributes(weapon.attributes, shield.attributes, core.attributes);
            StatModifier attributes = AddAttributes(
                CharacterStatCalculator.CalculateAttributes(basis, growth), modifiers, default);

            // 基礎VIT/MNDはbasisの最大値に含まれている。追加配分と装備の分だけ導出する。
            long unequippedMaxHp = (long)basis.maxHp + (long)growth.allocatedStats.vit * LevelUpLogic.k_HpPerVit;
            int maxHp = ClampStat(unequippedMaxHp + (long)modifiers.vit * LevelUpLogic.k_HpPerVit
                + weapon.bonusHp + (long)shield.bonusHp + core.bonusHp);
            // 装備の付け替えだけでは死亡経路を発火しない。HPを持つ個体の上限は1を保つ。
            // 未設定のHP=0と死亡中のcurrentHp=0は、この処理で復活させない。
            if (maxHp == 0 && (unequippedMaxHp > 0 || current.currentHp > 0))
            {
                maxHp = 1;
            }
            int maxMp = ClampStat((long)basis.maxMp
                + ((long)growth.allocatedStats.mnd + modifiers.mnd) * LevelUpLogic.k_MpPerMnd
                + weapon.bonusMp + (long)shield.bonusMp + core.bonusMp);
            float maxStamina = Math.Max(0f, basis.maxStamina + weapon.bonusStamina + shield.bonusStamina + core.bonusStamina);
            float maxArmor = Math.Max(0f, basis.maxArmor + weapon.bonusArmor + shield.bonusArmor + core.bonusArmor);

            CombatStats combat = new CombatStats
            {
                attack = AddChannels(basis.combat.attack,
                    ScaleAttackBonus(weapon, attributes), ScaleAttackBonus(shield, attributes), ScaleAttackBonus(core, attributes)),
                defense = AddChannels(basis.combat.defense, weapon.defenseBonus, shield.defenseBonus, core.defenseBonus),
                statusDefense = StatusEffectDefenseProfile.Combine(basis.combat.statusDefense,
                    weapon.statusDefense, shield.statusDefense, core.statusDefense),
                guardStats = shield.overridesGuard ? shield.guardStats : basis.combat.guardStats,
                knockbackResistance = basis.combat.knockbackResistance,
            };
            return new EquipmentDerivedStats
            {
                vitals = ApplyVitalsLimits(current, growth.level, maxHp, maxMp, maxStamina, maxArmor,
                    basis.staminaRecoveryRate, basis.staminaRecoveryDelay),
                combat = combat,
                attributeModifiers = modifiers,
            };
        }

        private static StatModifier AddAttributes(in StatModifier first, in StatModifier second, in StatModifier third)
        {
            return new StatModifier
            {
                str = first.str + second.str + third.str,
                dex = first.dex + second.dex + third.dex,
                intel = first.intel + second.intel + third.intel,
                vit = first.vit + second.vit + third.vit,
                mnd = first.mnd + second.mnd + third.mnd,
                end = first.end + second.end + third.end,
            };
        }

        private static ElementalStatus AddChannels(in ElementalStatus basis, in ElementalStatus weapon,
            in ElementalStatus shield, in ElementalStatus core)
        {
            return new ElementalStatus
            {
                slash = ClampStat((long)basis.slash + weapon.slash + shield.slash + core.slash),
                strike = ClampStat((long)basis.strike + weapon.strike + shield.strike + core.strike),
                pierce = ClampStat((long)basis.pierce + weapon.pierce + shield.pierce + core.pierce),
                fire = ClampStat((long)basis.fire + weapon.fire + shield.fire + core.fire),
                thunder = ClampStat((long)basis.thunder + weapon.thunder + shield.thunder + core.thunder),
                light = ClampStat((long)basis.light + weapon.light + shield.light + core.light),
                dark = ClampStat((long)basis.dark + weapon.dark + shield.dark + core.dark),
            };
        }

        private static int ClampStat(long value) => value <= 0 ? 0 : value >= int.MaxValue ? int.MaxValue : (int)value;

        private static ElementalStatus ScaleAttackBonus(in EquipmentStatModifiers item, in StatModifier attributes)
        {
            return new ElementalStatus
            {
                slash = ScaleAttackChannel(item.attackBonus.slash, item, attributes),
                strike = ScaleAttackChannel(item.attackBonus.strike, item, attributes),
                pierce = ScaleAttackChannel(item.attackBonus.pierce, item, attributes),
                fire = ScaleAttackChannel(item.attackBonus.fire, item, attributes),
                thunder = ScaleAttackChannel(item.attackBonus.thunder, item, attributes),
                light = ScaleAttackChannel(item.attackBonus.light, item, attributes),
                dark = ScaleAttackChannel(item.attackBonus.dark, item, attributes),
            };
        }

        private static int ScaleAttackChannel(int bonus, in EquipmentStatModifiers item, in StatModifier attributes)
        {
            if (item.strengthScaling == 0f && item.dexterityScaling == 0f && item.intelligenceScaling == 0f)
            {
                return bonus;
            }
            // 単一能力の設定は既存APIへ渡し、演算順・端数処理も維持する。
            if (item.dexterityScaling == 0f && item.intelligenceScaling == 0f)
            {
                return EquipmentStatCalculator.CalculateScaledAttack(bonus, item.strengthScaling, attributes.str);
            }
            if (item.strengthScaling == 0f && item.intelligenceScaling == 0f)
            {
                return EquipmentStatCalculator.CalculateScaledAttack(bonus, item.dexterityScaling, attributes.dex);
            }
            if (item.strengthScaling == 0f && item.dexterityScaling == 0f)
            {
                return EquipmentStatCalculator.CalculateScaledAttack(bonus, item.intelligenceScaling, attributes.intel);
            }
            float scaling = item.strengthScaling * attributes.str
                + item.dexterityScaling * attributes.dex + item.intelligenceScaling * attributes.intel;
            float multiplier = 1.0f + scaling / 100.0f;
            return (int)(bonus * multiplier);
        }

        public static CharacterVitals CreateVitals(in CharacterBaseStats basis, in CharacterGrowth growth)
        {
            CharacterVitals result = RecalculateVitals(basis, growth, default);
            result.currentHp = result.maxHp;
            result.currentMp = result.maxMp;
            result.currentStamina = result.maxStamina;
            result.currentArmor = result.maxArmor;
            return result;
        }

        public static CharacterVitals RecalculateVitals(in CharacterBaseStats basis,
            in CharacterGrowth growth, in CharacterVitals current)
        {
            return ApplyVitalsLimits(current, growth.level,
                basis.maxHp + growth.allocatedStats.vit * LevelUpLogic.k_HpPerVit,
                basis.maxMp + growth.allocatedStats.mnd * LevelUpLogic.k_MpPerMnd,
                basis.maxStamina, basis.maxArmor, basis.staminaRecoveryRate, basis.staminaRecoveryDelay);
        }

        private static CharacterVitals ApplyVitalsLimits(in CharacterVitals current, int level,
            int maxHp, int maxMp, float maxStamina, float maxArmor,
            float staminaRecoveryRate, float staminaRecoveryDelay)
        {
            CharacterVitals result = current;
            result.maxHp = maxHp;
            result.maxMp = maxMp;
            result.maxStamina = maxStamina;
            result.maxArmor = maxArmor;
            result.staminaRecoveryRate = staminaRecoveryRate;
            result.staminaRecoveryDelay = staminaRecoveryDelay;
            result.level = level;
            result.currentHp = Math.Min(result.currentHp, result.maxHp);
            result.currentMp = Math.Min(result.currentMp, result.maxMp);
            result.currentStamina = Math.Min(result.currentStamina, result.maxStamina);
            result.currentArmor = Math.Min(result.currentArmor, result.maxArmor);
            return result;
        }

        public static StatModifier CalculateAttributes(in CharacterBaseStats basis, in CharacterGrowth growth)
        {
            return new StatModifier
            {
                str = basis.attributes.str + growth.allocatedStats.str,
                dex = basis.attributes.dex + growth.allocatedStats.dex,
                intel = basis.attributes.intel + growth.allocatedStats.intel,
                vit = basis.attributes.vit + growth.allocatedStats.vit,
                mnd = basis.attributes.mnd + growth.allocatedStats.mnd,
                end = basis.attributes.end + growth.allocatedStats.end,
            };
        }


    }
}
