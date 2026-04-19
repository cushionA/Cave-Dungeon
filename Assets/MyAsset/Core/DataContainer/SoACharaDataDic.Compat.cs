using System.Collections.Generic;

namespace Game.Core
{
    public partial class SoACharaDataDic
    {
        public ref CharacterVitals GetVitals(int hash)
        {
            if (!TryGetIndexByHash(hash, out int idx))
            {
                throw new KeyNotFoundException($"Hash {hash} is not registered.");
            }
            return ref GetCharacterVitalsByIndex(idx);
        }

        public ref CombatStats GetCombatStats(int hash)
        {
            if (!TryGetIndexByHash(hash, out int idx))
            {
                throw new KeyNotFoundException($"Hash {hash} is not registered.");
            }
            return ref GetCombatStatsByIndex(idx);
        }

        public ref CharacterFlags GetFlags(int hash)
        {
            if (!TryGetIndexByHash(hash, out int idx))
            {
                throw new KeyNotFoundException($"Hash {hash} is not registered.");
            }
            return ref GetCharacterFlagsByIndex(idx);
        }

        public ref MoveParams GetMoveParams(int hash)
        {
            if (!TryGetIndexByHash(hash, out int idx))
            {
                throw new KeyNotFoundException($"Hash {hash} is not registered.");
            }
            return ref GetMoveParamsByIndex(idx);
        }

        public ref EquipmentStatus GetEquipmentStatus(int hash)
        {
            if (!TryGetIndexByHash(hash, out int idx))
            {
                throw new KeyNotFoundException($"Hash {hash} is not registered.");
            }
            return ref GetEquipmentStatusByIndex(idx);
        }

        public ref CharacterStatusEffects GetStatusEffects(int hash)
        {
            if (!TryGetIndexByHash(hash, out int idx))
            {
                throw new KeyNotFoundException($"Hash {hash} is not registered.");
            }
            return ref GetCharacterStatusEffectsByIndex(idx);
        }

        public ref AnimationStateData GetAnimationState(int hash)
        {
            if (!TryGetIndexByHash(hash, out int idx))
            {
                throw new KeyNotFoundException($"Hash {hash} is not registered.");
            }
            return ref GetAnimationStateDataByIndex(idx);
        }

        public bool TryGetValue(int hash, out int index)
        {
            return TryGetIndexByHash(hash, out index);
        }

        public void GetAllHashes(List<int> output)
        {
            for (int i = 0; i < _count; i++)
            {
                output.Add(_hashCodes[i]);
            }
        }

        public ManagedCharacter GetManaged(int hash)
        {
            if (TryGetIndexByHash(hash, out int idx))
            {
                return GetManagedCharacterByIndex(idx);
            }
            return null;
        }

        public bool TryGetManaged(int hash, out ManagedCharacter managed)
        {
            return TryGetManagedCharacterByHash(hash, out managed);
        }

        public int Add(int hash, CharacterVitals vitals, CombatStats combatStats,
            CharacterFlags flags, MoveParams moveParams,
            EquipmentStatus equipmentStatus = default, CharacterStatusEffects statusEffects = default,
            AnimationStateData animationState = default, ManagedCharacter managed = null)
        {
            return AddByHash(hash, vitals, combatStats, flags, moveParams,
                equipmentStatus, statusEffects, animationState, managed);
        }

        public void Remove(int hash)
        {
            if (!RemoveByHash(hash))
            {
                throw new KeyNotFoundException($"Hash {hash} is not registered.");
            }
        }
    }
}
