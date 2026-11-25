using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.Runtime
{
    internal static class ProjectileSpatialIndex
    {
        internal const float DefaultQueryRadius = AIObservationContext.NearbyHostileProjectileRadius;
    }

    internal readonly struct ProjectileSpatialSample<TWorld>
    {
        internal readonly TWorld World;
        internal readonly Vector3 Position;
        internal readonly bool IsPresent;

        internal ProjectileSpatialSample(TWorld world, Vector3 position)
        {
            World = world;
            Position = position;
            IsPresent = true;
        }
    }

    /// <summary>弾が多く複数の照会がある固定更新だけ、物理ワールド別の疎なグリッドを構築する。</summary>
    internal sealed class ProjectileSpatialIndex<T, TWorld>
    {
        private const float k_InverseCellSize = 1f / ProjectileSpatialIndex.DefaultQueryRadius;
        private const int k_MinimumIndexedShots = 1024;
        private const int k_MinimumQueriesForIndexing = 48;

        private readonly bool _isPlanar;
        private readonly Func<T, ProjectileSpatialSample<TWorld>> _sample;
        private readonly Dictionary<(TWorld World, Vector3Int Cell), List<Entry>> _cells =
            new Dictionary<(TWorld, Vector3Int), List<Entry>>();
        private readonly Stack<List<Entry>> _emptyCells = new Stack<List<Entry>>();
        private int _queryCount;
        private bool _hasFrequentQueries;
        private bool _isBuilt;

        internal ProjectileSpatialIndex(Func<T, ProjectileSpatialSample<TWorld>> sample, bool isPlanar = false)
        {
            _sample = sample;
            _isPlanar = isPlanar;
        }

        internal void BeginTick()
        {
            _hasFrequentQueries = _queryCount >= k_MinimumQueriesForIndexing;
            _queryCount = 0;
            Invalidate();
        }

        internal void Invalidate()
        {
            if (!_isBuilt) { return; }

            foreach (List<Entry> entries in _cells.Values)
            {
                entries.Clear();
                _emptyCells.Push(entries);
            }

            _cells.Clear();
            _isBuilt = false;
        }

        internal void Collect(List<T> source, TWorld world, Vector3 center, float radius, List<T> results,
            bool canCache = true)
        {
            results.Clear();
            if (_isPlanar) { center.z = 0f; }
            float radiusSquared = radius * radius;
            if (source == null || source.Count == 0 || radius < 0f || !float.IsFinite(radiusSquared) || !IsFinite(center))
            {
                return;
            }

            bool canIndex = canCache && source.Count >= k_MinimumIndexedShots;
            if (canIndex) { _queryCount++; }
            // 前回の照会数が多ければ、最初の照会からグリッドを使い回す。
            // 移動途中のコールバックには、その時点の実データを返す。
            if (!canIndex || !_hasFrequentQueries)
            {
                CollectDirectly(source, world, center, radiusSquared, results);
                return;
            }

            if (!_isBuilt) { Build(source); }
            CollectFromCells(world, center, radius, radiusSquared, results);
        }

        private void CollectDirectly(List<T> source, TWorld world, Vector3 center, float radiusSquared, List<T> results)
        {
            foreach (T item in source)
            {
                ProjectileSpatialSample<TWorld> sample = _sample(item);
                if (!sample.IsPresent || !EqualityComparer<TWorld>.Default.Equals(sample.World, world)) { continue; }
                Vector3 position = sample.Position;
                if (_isPlanar) { position.z = 0f; }
                if ((position - center).sqrMagnitude <= radiusSquared) { results.Add(item); }
            }
        }

        private void Build(List<T> source)
        {
            foreach (T item in source)
            {
                ProjectileSpatialSample<TWorld> sample = _sample(item);
                Vector3 position = sample.Position;
                if (_isPlanar) { position.z = 0f; }
                if (!sample.IsPresent || !IsFinite(position)) { continue; }

                (TWorld, Vector3Int) key = (sample.World, GetCell(position));
                if (!_cells.TryGetValue(key, out List<Entry> entries))
                {
                    entries = _emptyCells.Count > 0 ? _emptyCells.Pop() : new List<Entry>(4);
                    _cells.Add(key, entries);
                }

                entries.Add(new Entry(position, item));
            }

            _isBuilt = true;
        }

        private void CollectFromCells(TWorld world, Vector3 center, float radius, float radiusSquared, List<T> results)
        {
            Vector3 extent = new Vector3(radius, radius, _isPlanar ? 0f : radius);
            Vector3Int min = GetCell(center - extent);
            Vector3Int max = GetCell(center + extent);
            double cellCount = ((double)max.x - min.x + 1) * ((double)max.y - min.y + 1)
                * ((double)max.z - min.z + 1);

            // 探索範囲がセルの総数より広い時は、存在するセルだけを調べる。
            if (cellCount > _cells.Count)
            {
                foreach (KeyValuePair<(TWorld World, Vector3Int Cell), List<Entry>> cellEntries in _cells)
                {
                    Vector3Int cell = cellEntries.Key.Cell;
                    if (cell.x >= min.x && cell.x <= max.x && cell.y >= min.y && cell.y <= max.y
                        && cell.z >= min.z && cell.z <= max.z
                        && EqualityComparer<TWorld>.Default.Equals(cellEntries.Key.World, world))
                    {
                        CollectEntries(cellEntries.Value, center, radiusSquared, results);
                    }
                }

                return;
            }

            for (long z = min.z; z <= max.z; z++)
            {
                for (long y = min.y; y <= max.y; y++)
                {
                    for (long x = min.x; x <= max.x; x++)
                    {
                        Vector3Int cell = new Vector3Int((int)x, (int)y, (int)z);
                        if (_cells.TryGetValue((world, cell), out List<Entry> entries))
                        {
                            CollectEntries(entries, center, radiusSquared, results);
                        }
                    }
                }
            }
        }

        private static Vector3Int GetCell(Vector3 position) => new Vector3Int(
            GetCellCoordinate(position.x), GetCellCoordinate(position.y), GetCellCoordinate(position.z));

        private static int GetCellCoordinate(float value)
        {
            float coordinate = value * k_InverseCellSize;
            if (coordinate >= int.MaxValue) { return int.MaxValue; }
            if (coordinate <= int.MinValue) { return int.MinValue; }
            return Mathf.FloorToInt(coordinate);
        }

        private static bool IsFinite(Vector3 position) =>
            float.IsFinite(position.x) && float.IsFinite(position.y) && float.IsFinite(position.z);

        private static void CollectEntries(List<Entry> entries, Vector3 center, float radiusSquared, List<T> results)
        {
            foreach (Entry entry in entries)
            {
                if ((entry.Position - center).sqrMagnitude <= radiusSquared)
                {
                    results.Add(entry.Item);
                }
            }
        }

        private readonly struct Entry
        {
            internal readonly Vector3 Position;
            internal readonly T Item;

            internal Entry(Vector3 position, T item)
            {
                Position = position;
                Item = item;
            }
        }
    }
}
