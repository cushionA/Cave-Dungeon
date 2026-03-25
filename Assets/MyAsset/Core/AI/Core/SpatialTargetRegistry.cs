using System;
using UnityEngine;
using ODC.Runtime;

namespace Game.Core
{
    /// <summary>
    /// Wraps SpatialHashContainer2D to provide int-hash spatial queries for the SoA architecture.
    /// Manages target registration, position synchronization, and neighbor queries.
    /// </summary>
    public class SpatialTargetRegistry : IDisposable
    {
        private class SpatialEntry
        {
            public int Hash;
        }

        private SpatialHashContainer2D<SpatialEntry> _spatialHash;
        private SpatialEntry[] _entryPool;
        private int[] _registeredHashes;
        private int _count;
        private SpatialEntry[] _queryBuffer;

        public int Count => _count;

        public SpatialTargetRegistry(float cellSize, int maxCapacity)
        {
            _spatialHash = new SpatialHashContainer2D<SpatialEntry>(
                cellSize, maxCapacity, SpatialPlane2D.XY);
            _entryPool = new SpatialEntry[maxCapacity];
            for (int i = 0; i < maxCapacity; i++)
            {
                _entryPool[i] = new SpatialEntry();
            }
            _registeredHashes = new int[maxCapacity];
            _count = 0;
            _queryBuffer = new SpatialEntry[maxCapacity];
        }

        /// <summary>
        /// Registers a target at the given position.
        /// </summary>
        public void Register(int hash, Vector2 position)
        {
            if (_count >= _entryPool.Length)
            {
                throw new InvalidOperationException("SpatialTargetRegistry is full.");
            }

            SpatialEntry entry = _entryPool[_count];
            entry.Hash = hash;
            _registeredHashes[_count] = hash;
            _count++;
            _spatialHash.Add(hash, entry, new Vector3(position.x, position.y, 0f));
        }

        /// <summary>
        /// Unregisters a target.
        /// </summary>
        public void Unregister(int hash)
        {
            _spatialHash.Remove(hash);
            for (int i = 0; i < _count; i++)
            {
                if (_registeredHashes[i] == hash)
                {
                    _count--;
                    _registeredHashes[i] = _registeredHashes[_count];
                    SpatialEntry temp = _entryPool[i];
                    _entryPool[i] = _entryPool[_count];
                    _entryPool[_count] = temp;
                    break;
                }
            }
        }

        /// <summary>
        /// Synchronizes all registered target positions from SoA data.
        /// Call once per AI tick before querying.
        /// </summary>
        public void SyncPositions(SoACharaDataDic data)
        {
            for (int i = 0; i < _count; i++)
            {
                int hash = _registeredHashes[i];
                if (data.TryGetValue(hash, out int _))
                {
                    ref CharacterVitals vitals = ref data.GetVitals(hash);
                    _spatialHash.UpdatePosition(
                        hash, new Vector3(vitals.position.x, vitals.position.y, 0f));
                }
            }
        }

        /// <summary>
        /// Queries neighbors within radius. Returns count of results written to the span.
        /// </summary>
        public int QueryNeighbors(Vector2 center, float radius, Span<int> results)
        {
            Vector3 center3D = new Vector3(center.x, center.y, 0f);
            int count = _spatialHash.QueryNeighbors(
                center3D, radius, _queryBuffer.AsSpan(0, _queryBuffer.Length));
            int resultCount = Math.Min(count, results.Length);
            for (int i = 0; i < resultCount; i++)
            {
                results[i] = _queryBuffer[i].Hash;
            }
            return resultCount;
        }

        public void Dispose()
        {
            _spatialHash.Dispose();
        }
    }
}
