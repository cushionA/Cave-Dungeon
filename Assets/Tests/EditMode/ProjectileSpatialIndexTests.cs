using System.Collections.Generic;
using Game.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    public sealed class ProjectileSpatialIndexTests
    {
        [Test]
        public void Collect_PlanarQueryAcrossNegativeCellBoundary_UsesWorldXYAndExactRadius()
        {
            ProjectileSpatialIndex<(int, Vector3), int> index = NewIndex(isPlanar: true);
            List<(int, Vector3)> shots = new List<(int, Vector3)>
            {
                (1, new Vector3(-1f, 0f, 90f)),
                (1, new Vector3(1f, 1f, 0f)),
                (2, new Vector3(-1f, 0f, 0f)),
            };
            for (int i = 3; i < 1200; i++)
            {
                shots.Add((1, new Vector3(i * 10f, 0f, 0f)));
            }

            List<(int, Vector3)> results = new List<(int, Vector3)>();
            for (int query = 0; query < 64; query++)
            {
                index.Collect(shots, 1, Vector3.zero, 1f, results);
                CollectionAssert.AreEqual(new[] { shots[0] }, results);
            }

            index.BeginTick();
            index.Collect(shots, 1, Vector3.zero, 1f, results);
            CollectionAssert.AreEqual(new[] { shots[0] }, results);
        }

        [Test]
        public void Collect_SpatialQueryWithFewOccupiedCells_UsesHeightAndWorld()
        {
            ProjectileSpatialIndex<(int, Vector3), int> index = NewIndex();
            List<(int, Vector3)> shots = new List<(int, Vector3)>
            {
                (1, new Vector3(0f, 0f, -2f)),
                (1, new Vector3(0f, 10f, 0f)),
                (2, new Vector3(0f, 0f, -2f)),
            };
            for (int i = 3; i < 1200; i++)
            {
                shots.Add((1, new Vector3(0f, 16f, 0f)));
            }

            List<(int, Vector3)> results = new List<(int, Vector3)>();
            for (int query = 0; query < 64; query++)
            {
                index.Collect(shots, 1, Vector3.zero, 8f, results);
                CollectionAssert.AreEqual(new[] { shots[0] }, results);
            }

            index.BeginTick();
            index.Collect(shots, 1, Vector3.zero, 8f, results);
            CollectionAssert.AreEqual(new[] { shots[0] }, results);
        }

        [Test]
        public void Collect_SnapshotInvalidatedAfterMovingAndRemoving_UsesCurrentPositions()
        {
            ProjectileSpatialIndex<(int, Vector3), int> index = NewIndex();
            List<(int, Vector3)> shots = new List<(int, Vector3)> { (1, Vector3.zero) };
            for (int i = 1; i < 1200; i++) { shots.Add((1, new Vector3(i * 10f, 0f, 0f))); }
            List<(int, Vector3)> results = new List<(int, Vector3)>();
            for (int query = 0; query < 64; query++) { index.Collect(shots, 1, Vector3.zero, 1f, results); }
            index.BeginTick();
            index.Collect(shots, 1, Vector3.zero, 1f, results);
            CollectionAssert.AreEqual(new[] { shots[0] }, results);

            shots[0] = (1, new Vector3(5f, 0f, 0f));
            index.Collect(shots, 1, Vector3.zero, 1f, results, canCache: false);
            Assert.IsEmpty(results);

            index.Invalidate();
            for (int query = 0; query < 64; query++) { index.Collect(shots, 1, shots[0].Item2, 1f, results); }
            CollectionAssert.AreEqual(new[] { shots[0] }, results);

            Vector3 previous = shots[0].Item2;
            shots.RemoveAt(0);
            index.Invalidate();
            index.Collect(shots, 1, previous, 1f, results);
            Assert.IsEmpty(results);
        }

        private static ProjectileSpatialIndex<(int, Vector3), int> NewIndex(bool isPlanar = false) =>
            new ProjectileSpatialIndex<(int, Vector3), int>(
                shot => new ProjectileSpatialSample<int>(shot.Item1, shot.Item2), isPlanar);
    }
}
