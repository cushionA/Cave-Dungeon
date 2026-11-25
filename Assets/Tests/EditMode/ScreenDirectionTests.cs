using Game.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    public class ScreenDirectionTests
    {
        [TestCase(1f, 0f, 0)]
        [TestCase(1f, 1f, 1)]
        [TestCase(0f, 1f, 2)]
        [TestCase(-1f, 1f, 3)]
        [TestCase(-1f, 0f, 4)]
        [TestCase(-1f, -1f, 5)]
        [TestCase(0f, -1f, 6)]
        [TestCase(1f, -1f, 7)]
        [TestCase(1f, 0.4f, 0)]
        [TestCase(1f, 0.42f, 1)]
        [TestCase(1f, -0.42f, 7)]
        [TestCase(1f, -0.4f, 0)]
        public void MultiLockDirection_QuantizesScreenVectorIntoEightSectors(float x, float y, int expected)
        {
            Assert.AreEqual(expected, PlayerInputHandler.ScreenDirection(new Vector2(x, y)));
        }
    }
}
