using System;
using System.Collections.Generic;

namespace Game.Core
{
    public partial class SoACharaDataDic
    {
        private void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(SoACharaDataDic));
            }
        }

        public ref CharacterVitals GetVitals(int hash)
        {
            ThrowIfDisposed();
            if (!TryGetIndexByHash(hash, out int idx))
            {
                throw new KeyNotFoundException($"Hash {hash} is not registered.");
            }
            return ref GetCharacterVitalsByIndex(idx);
        }

        public ref CombatStats GetCombatStats(int hash)
        {
            ThrowIfDisposed();
            if (!TryGetIndexByHash(hash, out int idx))
            {
                throw new KeyNotFoundException($"Hash {hash} is not registered.");
            }
            return ref GetCombatStatsByIndex(idx);
        }

        public ref CharacterFlags GetFlags(int hash)
        {
            ThrowIfDisposed();
            if (!TryGetIndexByHash(hash, out int idx))
            {
                throw new KeyNotFoundException($"Hash {hash} is not registered.");
            }
            return ref GetCharacterFlagsByIndex(idx);
        }

        public ref MoveParams GetMoveParams(int hash)
        {
            ThrowIfDisposed();
            if (!TryGetIndexByHash(hash, out int idx))
            {
                throw new KeyNotFoundException($"Hash {hash} is not registered.");
            }
            return ref GetMoveParamsByIndex(idx);
        }

        public ref EquipmentStatus GetEquipmentStatus(int hash)
        {
            ThrowIfDisposed();
            if (!TryGetIndexByHash(hash, out int idx))
            {
                throw new KeyNotFoundException($"Hash {hash} is not registered.");
            }
            return ref GetEquipmentStatusByIndex(idx);
        }

        public ref CharacterStatusEffects GetStatusEffects(int hash)
        {
            ThrowIfDisposed();
            if (!TryGetIndexByHash(hash, out int idx))
            {
                throw new KeyNotFoundException($"Hash {hash} is not registered.");
            }
            return ref GetCharacterStatusEffectsByIndex(idx);
        }

        public ref AnimationStateData GetAnimationState(int hash)
        {
            ThrowIfDisposed();
            if (!TryGetIndexByHash(hash, out int idx))
            {
                throw new KeyNotFoundException($"Hash {hash} is not registered.");
            }
            return ref GetAnimationStateDataByIndex(idx);
        }

        public bool TryGetValue(int hash, out int index)
        {
            ThrowIfDisposed();
            return TryGetIndexByHash(hash, out index);
        }

        public void GetAllHashes(List<int> output)
        {
            ThrowIfDisposed();
            for (int i = 0; i < _count; i++)
            {
                output.Add(_hashCodes[i]);
            }
        }

        public ManagedCharacter GetManaged(int hash)
        {
            ThrowIfDisposed();
            if (TryGetIndexByHash(hash, out int idx))
            {
                return GetManagedCharacterByIndex(idx);
            }
            return null;
        }

        public bool TryGetManaged(int hash, out ManagedCharacter managed)
        {
            ThrowIfDisposed();
            return TryGetManagedCharacterByHash(hash, out managed);
        }

        public int Add(int hash, CharacterVitals vitals, CombatStats combatStats,
            CharacterFlags flags, MoveParams moveParams,
            EquipmentStatus equipmentStatus = default, CharacterStatusEffects statusEffects = default,
            AnimationStateData animationState = default, ManagedCharacter managed = null)
        {
            ThrowIfDisposed();
            return AddByHash(hash, vitals, combatStats, flags, moveParams,
                equipmentStatus, statusEffects, animationState, managed);
        }

        public void Remove(int hash)
        {
            ThrowIfDisposed();
            if (!RemoveByHash(hash))
            {
                throw new KeyNotFoundException($"Hash {hash} is not registered.");
            }
        }
    }
}
