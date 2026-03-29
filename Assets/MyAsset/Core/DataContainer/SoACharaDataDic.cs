using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// SoA (Structure of Arrays) character data container with hash-based O(1) access.
    /// Uses managed arrays (T[]) for each struct type, with swap-back removal.
    /// </summary>
    public class SoACharaDataDic : IDisposable
    {
        private const int k_DefaultCapacity = 16;

        // Hash -> dense index mapping
        private Dictionary<int, int> _hashToIndex;

        // Dense index -> hash (for swap-back)
        private int[] _indexToHash;

        // SoA arrays
        private CharacterVitals[] _vitals;
        private CombatStats[] _combatStats;
        private CharacterFlags[] _flags;
        private MoveParams[] _moveParams;
        private EquipmentStatus[] _equipmentStatus;
        private CharacterStatusEffects[] _statusEffects;
        private AnimationStateData[] _animationStates;

        private int _count;
        private int _capacity;
        private bool _disposed;

        public int Count => _count;

        public SoACharaDataDic() : this(k_DefaultCapacity)
        {
        }

        public SoACharaDataDic(int initialCapacity)
        {
            if (initialCapacity <= 0)
            {
                initialCapacity = k_DefaultCapacity;
            }

            _capacity = initialCapacity;
            _count = 0;
            _disposed = false;

            _hashToIndex = new Dictionary<int, int>(_capacity);
            _indexToHash = new int[_capacity];
            _vitals = new CharacterVitals[_capacity];
            _combatStats = new CombatStats[_capacity];
            _flags = new CharacterFlags[_capacity];
            _moveParams = new MoveParams[_capacity];
            _equipmentStatus = new EquipmentStatus[_capacity];
            _statusEffects = new CharacterStatusEffects[_capacity];
            _animationStates = new AnimationStateData[_capacity];
        }

        /// <summary>
        /// Registers a character entry. Returns the dense index.
        /// </summary>
        public int Add(int hash, CharacterVitals vitals, CombatStats combatStats,
            CharacterFlags flags, MoveParams moveParams,
            EquipmentStatus equipmentStatus = default, CharacterStatusEffects statusEffects = default,
            AnimationStateData animationState = default)
        {
            ThrowIfDisposed();

            if (_hashToIndex.ContainsKey(hash))
            {
                throw new ArgumentException($"Hash {hash} is already registered.", nameof(hash));
            }

            if (_count >= _capacity)
            {
                Grow();
            }

            int index = _count;
            _hashToIndex[hash] = index;
            _indexToHash[index] = hash;
            _vitals[index] = vitals;
            _combatStats[index] = combatStats;
            _flags[index] = flags;
            _moveParams[index] = moveParams;
            _equipmentStatus[index] = equipmentStatus;
            _statusEffects[index] = statusEffects;
            _animationStates[index] = animationState;

            _count++;
            return index;
        }

        /// <summary>
        /// Removes the entry for the given hash using swap-back.
        /// The last element is moved into the removed slot.
        /// </summary>
        public void Remove(int hash)
        {
            ThrowIfDisposed();

            if (!_hashToIndex.TryGetValue(hash, out int removeIndex))
            {
                throw new KeyNotFoundException($"Hash {hash} is not registered.");
            }

            int lastIndex = _count - 1;

            if (removeIndex != lastIndex)
            {
                // Swap the last element into the removed slot
                int lastHash = _indexToHash[lastIndex];

                _indexToHash[removeIndex] = lastHash;
                _vitals[removeIndex] = _vitals[lastIndex];
                _combatStats[removeIndex] = _combatStats[lastIndex];
                _flags[removeIndex] = _flags[lastIndex];
                _moveParams[removeIndex] = _moveParams[lastIndex];
                _equipmentStatus[removeIndex] = _equipmentStatus[lastIndex];
                _statusEffects[removeIndex] = _statusEffects[lastIndex];
                _animationStates[removeIndex] = _animationStates[lastIndex];

                // Update the moved element's hash mapping
                _hashToIndex[lastHash] = removeIndex;
            }

            // Clear the last slot
            _indexToHash[lastIndex] = 0;
            _vitals[lastIndex] = default;
            _combatStats[lastIndex] = default;
            _flags[lastIndex] = default;
            _moveParams[lastIndex] = default;
            _equipmentStatus[lastIndex] = default;
            _statusEffects[lastIndex] = default;
            _animationStates[lastIndex] = default;

            _hashToIndex.Remove(hash);
            _count--;
        }

        /// <summary>
        /// Returns a reference to the CharacterVitals for the given hash.
        /// </summary>
        public ref CharacterVitals GetVitals(int hash)
        {
            ThrowIfDisposed();
            int index = ResolveIndex(hash);
            return ref _vitals[index];
        }

        /// <summary>
        /// Returns a reference to the CombatStats for the given hash.
        /// </summary>
        public ref CombatStats GetCombatStats(int hash)
        {
            ThrowIfDisposed();
            int index = ResolveIndex(hash);
            return ref _combatStats[index];
        }

        /// <summary>
        /// Returns a reference to the CharacterFlags for the given hash.
        /// </summary>
        public ref CharacterFlags GetFlags(int hash)
        {
            ThrowIfDisposed();
            int index = ResolveIndex(hash);
            return ref _flags[index];
        }

        /// <summary>
        /// Returns a reference to the MoveParams for the given hash.
        /// </summary>
        public ref MoveParams GetMoveParams(int hash)
        {
            ThrowIfDisposed();
            int index = ResolveIndex(hash);
            return ref _moveParams[index];
        }

        /// <summary>
        /// Returns a reference to the EquipmentStatus for the given hash.
        /// </summary>
        public ref EquipmentStatus GetEquipmentStatus(int hash)
        {
            ThrowIfDisposed();
            int index = ResolveIndex(hash);
            return ref _equipmentStatus[index];
        }

        /// <summary>
        /// Returns a reference to the CharacterStatusEffects for the given hash.
        /// </summary>
        public ref CharacterStatusEffects GetStatusEffects(int hash)
        {
            ThrowIfDisposed();
            int index = ResolveIndex(hash);
            return ref _statusEffects[index];
        }

        /// <summary>
        /// Returns a reference to the AnimationStateData for the given hash.
        /// </summary>
        public ref AnimationStateData GetAnimationState(int hash)
        {
            ThrowIfDisposed();
            int index = ResolveIndex(hash);
            return ref _animationStates[index];
        }

        /// <summary>
        /// Tries to get the dense index for the given hash.
        /// Returns true if found.
        /// </summary>
        public bool TryGetValue(int hash, out int index)
        {
            ThrowIfDisposed();
            return _hashToIndex.TryGetValue(hash, out index);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _hashToIndex.Clear();
            _hashToIndex = null;
            _indexToHash = null;
            _vitals = null;
            _combatStats = null;
            _flags = null;
            _moveParams = null;
            _equipmentStatus = null;
            _statusEffects = null;
            _animationStates = null;
            _count = 0;
            _capacity = 0;
            _disposed = true;
        }

        private int ResolveIndex(int hash)
        {
            if (!_hashToIndex.TryGetValue(hash, out int index))
            {
                throw new KeyNotFoundException($"Hash {hash} is not registered.");
            }
            return index;
        }

        private void Grow()
        {
            int newCapacity = _capacity * 2;

            int[] newIndexToHash = new int[newCapacity];
            CharacterVitals[] newVitals = new CharacterVitals[newCapacity];
            CombatStats[] newCombatStats = new CombatStats[newCapacity];
            CharacterFlags[] newFlags = new CharacterFlags[newCapacity];
            MoveParams[] newMoveParams = new MoveParams[newCapacity];
            EquipmentStatus[] newEquipmentStatus = new EquipmentStatus[newCapacity];
            CharacterStatusEffects[] newStatusEffects = new CharacterStatusEffects[newCapacity];
            AnimationStateData[] newAnimationStates = new AnimationStateData[newCapacity];

            Array.Copy(_indexToHash, newIndexToHash, _count);
            Array.Copy(_vitals, newVitals, _count);
            Array.Copy(_combatStats, newCombatStats, _count);
            Array.Copy(_flags, newFlags, _count);
            Array.Copy(_moveParams, newMoveParams, _count);
            Array.Copy(_equipmentStatus, newEquipmentStatus, _count);
            Array.Copy(_statusEffects, newStatusEffects, _count);
            Array.Copy(_animationStates, newAnimationStates, _count);

            _indexToHash = newIndexToHash;
            _vitals = newVitals;
            _combatStats = newCombatStats;
            _flags = newFlags;
            _moveParams = newMoveParams;
            _equipmentStatus = newEquipmentStatus;
            _statusEffects = newStatusEffects;
            _animationStates = newAnimationStates;
            _capacity = newCapacity;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(SoACharaDataDic));
            }
        }
    }
}
