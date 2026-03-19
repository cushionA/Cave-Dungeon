using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class AICore_SensorSystemTests
    {
        [Test]
        public void SensorSystem_VisionDetection_DetectsInRange()
        {
            SensorSystem sensor = new SensorSystem(10f, 90f, 5f);
            SoACharaDataDic data = new SoACharaDataDic();
            int owner = 1;
            int target = 2;

            data.Add(owner, new CharacterVitals { position = Vector2.zero }, default, default, default);
            data.Add(target, new CharacterVitals { position = new Vector2(5f, 0f) }, default, default, default);

            sensor.UpdateDetection(owner, new List<int> { owner, target }, data, Vector2.right);

            Assert.AreEqual(1, sensor.DetectedHashes.Count);
            Assert.AreEqual(target, sensor.DetectedHashes[0]);

            data.Dispose();
        }

        [Test]
        public void SensorSystem_Vision_NotDetectedOutsideAngle()
        {
            SensorSystem sensor = new SensorSystem(10f, 90f, 0f);
            SoACharaDataDic data = new SoACharaDataDic();
            int owner = 1;
            int target = 2;

            data.Add(owner, new CharacterVitals { position = Vector2.zero }, default, default, default);
            data.Add(target, new CharacterVitals { position = new Vector2(-5f, 0f) }, default, default, default);

            sensor.UpdateDetection(owner, new List<int> { owner, target }, data, Vector2.right);

            Assert.AreEqual(0, sensor.DetectedHashes.Count);

            data.Dispose();
        }

        [Test]
        public void SensorSystem_Hearing_DetectsRegardlessOfAngle()
        {
            SensorSystem sensor = new SensorSystem(10f, 90f, 8f);
            SoACharaDataDic data = new SoACharaDataDic();
            int owner = 1;
            int target = 2;

            data.Add(owner, new CharacterVitals { position = Vector2.zero }, default, default, default);
            data.Add(target, new CharacterVitals { position = new Vector2(-5f, 0f) }, default, default, default);

            sensor.UpdateDetection(owner, new List<int> { owner, target }, data, Vector2.right);

            Assert.AreEqual(1, sensor.DetectedHashes.Count);

            data.Dispose();
        }

        [Test]
        public void SensorSystem_OutOfAllRanges_NotDetected()
        {
            SensorSystem sensor = new SensorSystem(5f, 90f, 5f);
            SoACharaDataDic data = new SoACharaDataDic();
            int owner = 1;
            int target = 2;

            data.Add(owner, new CharacterVitals { position = Vector2.zero }, default, default, default);
            data.Add(target, new CharacterVitals { position = new Vector2(20f, 0f) }, default, default, default);

            sensor.UpdateDetection(owner, new List<int> { owner, target }, data, Vector2.right);

            Assert.AreEqual(0, sensor.DetectedHashes.Count);

            data.Dispose();
        }
    }
}
