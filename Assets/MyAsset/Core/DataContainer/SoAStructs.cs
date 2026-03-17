using System;
using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// HP/MP/stamina/armor/position - per-character vitals stored in SoA container.
    /// </summary>
    [Serializable]
    public struct CharacterVitals
    {
        public int currentHp;
        public int maxHp;
        public int currentMp;
        public int maxMp;
        public float currentStamina;
        public float maxStamina;
        public float currentArmor;
        public float maxArmor;
        public Vector2 position;
        public int level;
    }

    /// <summary>
    /// Offensive and defensive combat parameters.
    /// </summary>
    [Serializable]
    public struct CombatStats
    {
        public int physicalAttack;
        public int magicAttack;
        public int physicalDefense;
        public int magicDefense;
        public float criticalRate;
        public float criticalMultiplier;
        public GuardStats guardStats;
    }

    /// <summary>
    /// All character state flags bit-packed into a single ulong.
    /// Belong: bits 0-2, Feature: bits 8-23, AbilityFlags: bits 24-31.
    /// </summary>
    public struct CharacterFlags
    {
        public ulong flags;

        // Belong: bits 0-2
        public CharacterBelong Belong
        {
            get => (CharacterBelong)(flags & 0x7UL);
            set => flags = (flags & ~0x7UL) | ((ulong)value & 0x7UL);
        }

        // Feature: bits 8-23
        public CharacterFeature Feature
        {
            get => (CharacterFeature)((flags >> 8) & 0xFFFFUL);
            set => flags = (flags & ~(0xFFFFUL << 8)) | (((ulong)value & 0xFFFFUL) << 8);
        }

        // AbilityFlags: bits 24-31
        public AbilityFlag AbilityFlags
        {
            get => (AbilityFlag)((flags >> 24) & 0xFFUL);
            set => flags = (flags & ~(0xFFUL << 24)) | (((ulong)value & 0xFFUL) << 24);
        }

        public static CharacterFlags Pack(CharacterBelong belong, CharacterFeature feature, AbilityFlag ability)
        {
            CharacterFlags f = default;
            f.Belong = belong;
            f.Feature = feature;
            f.AbilityFlags = ability;
            return f;
        }
    }

    /// <summary>
    /// Movement parameters for a character.
    /// </summary>
    [Serializable]
    public struct MoveParams
    {
        public float moveSpeed;
        public float jumpForce;
        public float dashSpeed;
        public float dashDuration;
        public float gravityScale;
        public float weightRatio;
    }
}
