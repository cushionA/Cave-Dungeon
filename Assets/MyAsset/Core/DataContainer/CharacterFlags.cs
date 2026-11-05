using System;
using UnityEngine;

namespace Game.Core
{

    /// <summary>
    /// All character state flags bit-packed into a single ulong.
    /// Layout:
    ///   [0-2]   CharacterBelong (3 bits)
    ///   [3-8]   ActState (6 bits)
    ///   [9-17]  CharacterFeature (9 bits)
    ///   [18-23] Reserved/SpecialEffect (6 bits)
    ///   [24-35] RecognizeObjectType (12 bits)
    ///   [36-43] BrainEventFlagType (8 bits)
    ///   [44-59] AbilityFlag (16 bits)
    ///   [60]    Vanished (1 bit)
    ///   [61-63] Reserved (3 bits)
    /// </summary>
    public struct CharacterFlags
    {
        public ulong flags;

        public bool IsVanished
        {
            readonly get => (flags & (1UL << 60)) != 0;
            set => flags = value ? flags | (1UL << 60) : flags & ~(1UL << 60);
        }

        // Belong: bits 0-2 (3 bits)
        public CharacterBelong Belong
        {
            readonly get => (CharacterBelong)(flags & 0x7UL);
            set => flags = (flags & ~0x7UL) | ((ulong)value & 0x7UL);
        }

        // ActState: bits 3-8 (6 bits)
        public ActState ActState
        {
            readonly get => (ActState)((flags >> 3) & 0x3FUL);
            set => flags = (flags & ~(0x3FUL << 3)) | (((ulong)value & 0x3FUL) << 3);
        }

        // Feature: bits 9-17 (9 bits)
        public CharacterFeature Feature
        {
            readonly get => (CharacterFeature)((flags >> 9) & 0x1FFUL);
            set => flags = (flags & ~(0x1FFUL << 9)) | (((ulong)value & 0x1FFUL) << 9);
        }

        // RecognizeObjectType: bits 24-35 (12 bits)
        public ushort RecognizeObjectType
        {
            readonly get => (ushort)((flags >> 24) & 0xFFFUL);
            set => flags = (flags & ~(0xFFFUL << 24)) | (((ulong)value & 0xFFFUL) << 24);
        }

        // BrainEventFlagType: bits 36-43 (8 bits)
        public byte BrainEventFlags
        {
            readonly get => (byte)((flags >> 36) & 0xFFUL);
            set => flags = (flags & ~(0xFFUL << 36)) | (((ulong)value & 0xFFUL) << 36);
        }

        // AbilityFlags: bits 44-59 (16 bits)
        public AbilityFlag AbilityFlags
        {
            readonly get => (AbilityFlag)((flags >> 44) & 0xFFFFUL);
            set => flags = (flags & ~(0xFFFFUL << 44)) | (((ulong)value & 0xFFFFUL) << 44);
        }

        public static CharacterFlags Pack(CharacterBelong belong, CharacterFeature feature, AbilityFlag ability)
        {
            CharacterFlags f = default;
            f.Belong = belong;
            f.Feature = feature;
            f.AbilityFlags = ability;
            return f;
        }

        public static CharacterFlags Pack(CharacterBelong belong, CharacterFeature feature,
            ActState actState, AbilityFlag ability)
        {
            CharacterFlags f = default;
            f.Belong = belong;
            f.Feature = feature;
            f.ActState = actState;
            f.AbilityFlags = ability;
            return f;
        }
    }
}
