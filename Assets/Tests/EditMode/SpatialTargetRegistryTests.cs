using System;
using NUnit.Framework;
using UnityEngine;
using Game.Core;

namespace Game.Tests.EditMode
{
    [TestFixture]
    public class SpatialTargetRegistryTests
    {
        private SpatialTargetRegistry _registry;
        private SoACharaDataDic _data;

        [SetUp]
        public void SetUp()
        {
            // cellSize=10, maxCapacity=32
            _registry = new SpatialTargetRegistry(10f, 32);
            _data = new SoACharaDataDic(8);
        }

        [TearDown]
        public void TearDown()
        {
            _registry.Dispose();
            _data.Dispose();
        }

        [Test]
        public void SpatialTargetRegistry_Register_IncreasesCount()
        {
            _registry.Register(1, new Vector2(0f, 0f));
            _registry.Register(2, new Vector2(5f, 0f));

            Assert.AreEqual(2, _registry.Count);
        }

        [Test]
        public void SpatialTargetRegistry_Unregister_DecreasesCount()
        {
            _registry.Register(1, new Vector2(0f, 0f));
            _registry.Register(2, new Vector2(5f, 0f));

            _registry.Unregister(1);

            Assert.AreEqual(1, _registry.Count);
        }

        [Test]
        public void SpatialTargetRegistry_QueryNeighbors_FindsNearbyTargets()
        {
            _registry.Register(1, new Vector2(0f, 0f));
            _registry.Register(2, new Vector2(3f, 0f));
            _registry.Register(3, new Vector2(50f, 0f)); // far away

            int[] resultArray = new int[16];
            Span<int> results = resultArray;
            int count = _registry.QueryNeighbors(new Vector2(0f, 0f), 5f, results);

            // hash 1 (distance 0) and hash 2 (distance 3) should be found
            Assert.AreEqual(2, count);
        }

        [Test]
        public void SpatialTargetRegistry_QueryNeighbors_ExcludesFarTargets()
        {
            _registry.Register(1, new Vector2(0f, 0f));
            _registry.Register(2, new Vector2(100f, 0f));

            int[] resultArray = new int[16];
            Span<int> results = resultArray;
            int count = _registry.QueryNeighbors(new Vector2(0f, 0f), 5f, results);

            // Only hash 1 (at origin) within radius 5
            Assert.AreEqual(1, count);
            Assert.AreEqual(1, results[0]);
        }

        [Test]
        public void SpatialTargetRegistry_SyncPositions_UpdatesFromSoAData()
        {
            // Register at initial position
            _data.Add(1, new CharacterVitals { position = new Vector2(0f, 0f) }, default, default, default);
            _data.Add(2, new CharacterVitals { position = new Vector2(3f, 0f) }, default, default, default);

            _registry.Register(1, new Vector2(0f, 0f));
            _registry.Register(2, new Vector2(3f, 0f));

            // Move hash 2 far away in SoA data
            ref CharacterVitals v2 = ref _data.GetVitals(2);
            v2.position = new Vector2(100f, 0f);

            // Sync positions from SoA
            _registry.SyncPositions(_data);

            // Now hash 2 should be out of range
            int[] resultArray = new int[16];
            Span<int> results = resultArray;
            int count = _registry.QueryNeighbors(new Vector2(0f, 0f), 5f, results);

            Assert.AreEqual(1, count);
            Assert.AreEqual(1, results[0]);
        }

        [Test]
        public void SpatialTargetRegistry_UnregisterThenQuery_DoesNotReturnRemoved()
        {
            _registry.Register(1, new Vector2(0f, 0f));
            _registry.Register(2, new Vector2(1f, 0f));

            _registry.Unregister(2);

            int[] resultArray = new int[16];
            Span<int> results = resultArray;
            int count = _registry.QueryNeighbors(new Vector2(0f, 0f), 5f, results);

            Assert.AreEqual(1, count);
            Assert.AreEqual(1, results[0]);
        }

        // === SensorSystem + SpatialTargetRegistry Integration ===

        [Test]
        public void SensorSystem_WithRegistry_DetectsInVision()
        {
            SensorSystem sensor = new SensorSystem(10f, 90f, 5f);

            _data.Add(1, new CharacterVitals { position = Vector2.zero }, default, default, default);
            _data.Add(2, new CharacterVitals { position = new Vector2(5f, 0f) }, default, default, default);

            _registry.Register(1, Vector2.zero);
            _registry.Register(2, new Vector2(5f, 0f));

            sensor.UpdateDetection(1, _registry, _data, Vector2.right);

            Assert.AreEqual(1, sensor.DetectedHashes.Count);
            Assert.AreEqual(2, sensor.DetectedHashes[0]);
        }

        [Test]
        public void SensorSystem_WithRegistry_NotDetectedOutsideAngle()
        {
            SensorSystem sensor = new SensorSystem(10f, 90f, 0f);

            _data.Add(1, new CharacterVitals { position = Vector2.zero }, default, default, default);
            _data.Add(2, new CharacterVitals { position = new Vector2(-5f, 0f) }, default, default, default);

            _registry.Register(1, Vector2.zero);
            _registry.Register(2, new Vector2(-5f, 0f));

            sensor.UpdateDetection(1, _registry, _data, Vector2.right);

            Assert.AreEqual(0, sensor.DetectedHashes.Count);
        }

        [Test]
        public void SensorSystem_WithRegistry_HearingDetectsRegardlessOfAngle()
        {
            SensorSystem sensor = new SensorSystem(10f, 90f, 8f);

            _data.Add(1, new CharacterVitals { position = Vector2.zero }, default, default, default);
            _data.Add(2, new CharacterVitals { position = new Vector2(-5f, 0f) }, default, default, default);

            _registry.Register(1, Vector2.zero);
            _registry.Register(2, new Vector2(-5f, 0f));

            sensor.UpdateDetection(1, _registry, _data, Vector2.right);

            Assert.AreEqual(1, sensor.DetectedHashes.Count);
        }

        [Test]
        public void SensorSystem_WithRegistry_FarTargetsNotDetected()
        {
            SensorSystem sensor = new SensorSystem(5f, 90f, 5f);

            _data.Add(1, new CharacterVitals { position = Vector2.zero }, default, default, default);
            _data.Add(2, new CharacterVitals { position = new Vector2(20f, 0f) }, default, default, default);

            _registry.Register(1, Vector2.zero);
            _registry.Register(2, new Vector2(20f, 0f));

            sensor.UpdateDetection(1, _registry, _data, Vector2.right);

            Assert.AreEqual(0, sensor.DetectedHashes.Count);
        }

        [Test]
        public void SensorSystem_WithRegistry_AfterPositionSync_DetectsMovedTarget()
        {
            SensorSystem sensor = new SensorSystem(5f, 180f, 0f);

            _data.Add(1, new CharacterVitals { position = Vector2.zero }, default, default, default);
            _data.Add(2, new CharacterVitals { position = new Vector2(100f, 0f) }, default, default, default);

            _registry.Register(1, Vector2.zero);
            _registry.Register(2, new Vector2(100f, 0f));

            // Target 2 is far, not detected
            sensor.UpdateDetection(1, _registry, _data, Vector2.right);
            Assert.AreEqual(0, sensor.DetectedHashes.Count);

            // Move target 2 close in SoA, sync, then detect
            ref CharacterVitals v2 = ref _data.GetVitals(2);
            v2.position = new Vector2(3f, 0f);
            _registry.SyncPositions(_data);

            sensor.UpdateDetection(1, _registry, _data, Vector2.right);
            Assert.AreEqual(1, sensor.DetectedHashes.Count);
            Assert.AreEqual(2, sensor.DetectedHashes[0]);
        }
    }
}
