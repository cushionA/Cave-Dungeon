using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class MapSystemMinimapTests
    {
        private WorldMapData CreateTestWorldMap()
        {
            WorldMapData mapData = new WorldMapData();

            AreaDefinition area1 = new AreaDefinition
            {
                areaId = "forest",
                bounds = new Rect(0f, 0f, 100f, 100f),
                displayName = "Forest"
            };

            AreaDefinition area2 = new AreaDefinition
            {
                areaId = "cave",
                bounds = new Rect(100f, 0f, 80f, 60f),
                displayName = "Cave"
            };

            AreaDefinition area3 = new AreaDefinition
            {
                areaId = "village",
                bounds = new Rect(0f, 100f, 120f, 80f),
                displayName = "Village"
            };

            mapData.AddArea(area1);
            mapData.AddArea(area2);
            mapData.AddArea(area3);

            return mapData;
        }

        [Test]
        public void WorldMapData_MarkVisited_AddsToVisitedSet()
        {
            WorldMapData mapData = CreateTestWorldMap();

            Assert.IsFalse(mapData.IsVisited("forest"));

            bool firstMark = mapData.MarkVisited("forest");

            Assert.IsTrue(firstMark);
            Assert.IsTrue(mapData.IsVisited("forest"));
            Assert.AreEqual(1, mapData.VisitedAreaCount);

            bool secondMark = mapData.MarkVisited("forest");

            Assert.IsFalse(secondMark);
            Assert.AreEqual(1, mapData.VisitedAreaCount);
        }

        [Test]
        public void WorldMapData_GetExplorationRate_CalculatesCorrectly()
        {
            WorldMapData mapData = CreateTestWorldMap();

            Assert.AreEqual(0f, mapData.GetExplorationRate(), 0.001f);

            mapData.MarkVisited("forest");
            mapData.MarkVisited("cave");

            float rate = mapData.GetExplorationRate();

            Assert.AreEqual(2f / 3f, rate, 0.001f);

            mapData.MarkVisited("village");

            Assert.AreEqual(1f, mapData.GetExplorationRate(), 0.001f);

            WorldMapData emptyMap = new WorldMapData();

            Assert.AreEqual(0f, emptyMap.GetExplorationRate(), 0.001f);
        }

        [Test]
        public void WorldMapData_GetAreaAtPosition_ReturnsCorrectArea()
        {
            WorldMapData mapData = CreateTestWorldMap();

            string forestResult = mapData.GetAreaAtPosition(new Vector2(50f, 50f));
            Assert.AreEqual("forest", forestResult);

            string caveResult = mapData.GetAreaAtPosition(new Vector2(130f, 30f));
            Assert.AreEqual("cave", caveResult);

            string villageResult = mapData.GetAreaAtPosition(new Vector2(60f, 140f));
            Assert.AreEqual("village", villageResult);

            string nullResult = mapData.GetAreaAtPosition(new Vector2(-50f, -50f));
            Assert.IsNull(nullResult);
        }

        [Test]
        public void WorldMapData_RestoreVisitedAreas_RestoresState()
        {
            WorldMapData mapData = CreateTestWorldMap();
            mapData.MarkVisited("forest");
            mapData.MarkVisited("village");

            List<string> savedIds = mapData.GetVisitedAreaIds();

            Assert.AreEqual(2, savedIds.Count);
            Assert.Contains("forest", savedIds);
            Assert.Contains("village", savedIds);

            WorldMapData restoredMap = CreateTestWorldMap();

            Assert.AreEqual(0, restoredMap.VisitedAreaCount);

            restoredMap.RestoreVisitedAreas(savedIds);

            Assert.AreEqual(2, restoredMap.VisitedAreaCount);
            Assert.IsTrue(restoredMap.IsVisited("forest"));
            Assert.IsTrue(restoredMap.IsVisited("village"));
            Assert.IsFalse(restoredMap.IsVisited("cave"));
        }
    }
}
