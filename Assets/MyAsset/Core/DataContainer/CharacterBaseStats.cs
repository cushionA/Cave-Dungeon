using System;
using UnityEngine;

namespace Game.Core
{

    [Serializable]
    public struct CharacterBaseStats
    {
        public int maxHp;
        public int maxMp;
        public float maxStamina;
        public float maxArmor;
        public float staminaRecoveryRate;
        public float staminaRecoveryDelay;
        public StatModifier attributes;
        public CombatStats combat;
        public MoveParams movement;
        public AbilityFlag abilityFlags;

        public static CharacterBaseStats FromValues(in CharacterVitals vitals, in CombatStats combat,
            in CharacterFlags flags, in MoveParams movement)
        {
            return new CharacterBaseStats
            {
                maxHp = vitals.maxHp,
                maxMp = vitals.maxMp,
                maxStamina = vitals.maxStamina,
                maxArmor = vitals.maxArmor,
                staminaRecoveryRate = vitals.staminaRecoveryRate,
                staminaRecoveryDelay = vitals.staminaRecoveryDelay,
                combat = combat,
                movement = movement,
                abilityFlags = flags.AbilityFlags,
            };
        }
    }
}
