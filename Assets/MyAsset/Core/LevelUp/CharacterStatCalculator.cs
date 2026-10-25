using System;

namespace Game.Core
{
    public static class CharacterStatCalculator
    {
        public static CharacterVitals CreateVitals(in CharacterBaseStats basis, in CharacterGrowth growth)
        {
            CharacterVitals result = RecalculateVitals(basis, growth, default);
            result.currentHp = result.maxHp;
            result.currentMp = result.maxMp;
            result.currentStamina = result.maxStamina;
            result.currentArmor = result.maxArmor;
            UpdateRatios(ref result);
            return result;
        }

        public static CharacterVitals RecalculateVitals(in CharacterBaseStats basis,
            in CharacterGrowth growth, in CharacterVitals current)
        {
            CharacterVitals result = current;
            result.maxHp = basis.maxHp + growth.allocatedStats.vit * LevelUpLogic.k_HpPerVit;
            result.maxMp = basis.maxMp + growth.allocatedStats.mnd * LevelUpLogic.k_MpPerMnd;
            result.maxStamina = basis.maxStamina;
            result.maxArmor = basis.maxArmor;
            result.staminaRecoveryRate = basis.staminaRecoveryRate;
            result.staminaRecoveryDelay = basis.staminaRecoveryDelay;
            result.level = growth.level;
            result.currentHp = Math.Min(result.currentHp, result.maxHp);
            result.currentMp = Math.Min(result.currentMp, result.maxMp);
            result.currentStamina = Math.Min(result.currentStamina, result.maxStamina);
            result.currentArmor = Math.Min(result.currentArmor, result.maxArmor);
            UpdateRatios(ref result);
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

        private static void UpdateRatios(ref CharacterVitals vitals)
        {
            vitals.hpRatio = vitals.maxHp > 0 ? (byte)(100L * vitals.currentHp / vitals.maxHp) : (byte)0;
            vitals.mpRatio = vitals.maxMp > 0 ? (byte)(100f * vitals.currentMp / vitals.maxMp) : (byte)0;
        }
    }
}
