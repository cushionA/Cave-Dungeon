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
        }

        /// <summary>
        /// Registers a character entry. Returns the dense index.
        /// </summary>
        public int Add(int hash, CharacterVitals vitals, CombatStats combatStats,
            CharacterFlags flags, MoveParams moveParams)
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

                // Update the moved element's hash mapping
                _hashToIndex[lastHash] = removeIndex;
            }

            // Clear the last slot
            _indexToHash[lastIndex] = 0;
            _vitals[lastIndex] = default;
            _combatStats[lastIndex] = default;
            _flags[lastIndex] = default;
            _moveParams[lastIndex] = default;

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

            Array.Copy(_indexToHash, newIndexToHash, _count);
            Array.Copy(_vitals, newVitals, _count);
            Array.Copy(_combatStats, newCombatStats, _count);
            Array.Copy(_flags, newFlags, _count);
            Array.Copy(_moveParams, newMoveParams, _count);

            _indexToHash = newIndexToHash;
            _vitals = newVitals;
            _combatStats = newCombatStats;
            _flags = newFlags;
            _moveParams = newMoveParams;
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
