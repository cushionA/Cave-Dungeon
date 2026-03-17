using NUnit.Framework;
using Game.Core;
using UnityEngine;

namespace Game.Tests.EditMode
{
    public class MapSystemCameraTests
    {
        private const float k_SmoothTime = 0.3f;
        private const float k_DeadZoneRadius = 0.5f;
        private const float k_DeltaTime = 0.016f;
        private const float k_FloatDelta = 0.01f;

        private static readonly Rect k_DefaultBounds = new Rect(-50f, -50f, 100f, 100f);

        private CameraFollowLogic CreateDefaultLogic()
        {
            return new CameraFollowLogic(k_SmoothTime, k_DeadZoneRadius, k_DefaultBounds);
        }

        [Test]
        public void CameraFollowLogic_CalculatePosition_MovesTowardTarget()
        {
            CameraFollowLogic logic = CreateDefaultLogic();
            Vector2 currentPos = new Vector2(0f, 0f);
            Vector2 targetPos = new Vector2(10f, 5f);

            Vector2 result = logic.CalculatePosition(currentPos, targetPos, k_DeltaTime);

            // 結果は現在位置とターゲットの間にあるはず
            Assert.IsTrue(result.x > currentPos.x, "カメラがターゲット方向（X）に移動していない");
            Assert.IsTrue(result.x < targetPos.x, "カメラがターゲットを超えている（X）");
            Assert.IsTrue(result.y > currentPos.y, "カメラがターゲット方向（Y）に移動していない");
            Assert.IsTrue(result.y < targetPos.y, "カメラがターゲットを超えている（Y）");
        }

        [Test]
        public void CameraFollowLogic_CalculatePosition_WhenInDeadZone_DoesNotMove()
        {
            CameraFollowLogic logic = CreateDefaultLogic();
            Vector2 currentPos = new Vector2(5f, 5f);
            // デッドゾーン半径(0.5f)以内のターゲット
            Vector2 targetPos = new Vector2(5.3f, 5.2f);

            Vector2 result = logic.CalculatePosition(currentPos, targetPos, k_DeltaTime);

            Assert.AreEqual(currentPos.x, result.x, k_FloatDelta, "デッドゾーン内でX方向に移動した");
            Assert.AreEqual(currentPos.y, result.y, k_FloatDelta, "デッドゾーン内でY方向に移動した");
        }

        [Test]
        public void CameraFollowLogic_ClampToBounds_ClampsPosition()
        {
            Rect bounds = new Rect(-10f, -10f, 20f, 20f); // xMin=-10, xMax=10, yMin=-10, yMax=10
            CameraFollowLogic logic = new CameraFollowLogic(k_SmoothTime, k_DeadZoneRadius, bounds);

            Vector2 outsidePos = new Vector2(15f, -20f);
            Vector2 result = logic.ClampToBounds(outsidePos);

            Assert.AreEqual(10f, result.x, k_FloatDelta, "X座標が境界内にクランプされていない");
            Assert.AreEqual(-10f, result.y, k_FloatDelta, "Y座標が境界内にクランプされていない");
        }

        [Test]
        public void CameraFollowLogic_SnapToTarget_SetsPositionImmediately()
        {
            Rect bounds = new Rect(-10f, -10f, 20f, 20f);
            CameraFollowLogic logic = new CameraFollowLogic(k_SmoothTime, k_DeadZoneRadius, bounds);
            Vector2 targetPos = new Vector2(5f, -3f);

            Vector2 result = logic.SnapToTarget(targetPos);

            Assert.AreEqual(targetPos.x, result.x, k_FloatDelta, "SnapToTargetでX座標が即座に設定されていない");
            Assert.AreEqual(targetPos.y, result.y, k_FloatDelta, "SnapToTargetでY座標が即座に設定されていない");
        }

        [Test]
        public void CameraFollowLogic_SnapToTarget_ClampsTooBounds()
        {
            Rect bounds = new Rect(-10f, -10f, 20f, 20f);
            CameraFollowLogic logic = new CameraFollowLogic(k_SmoothTime, k_DeadZoneRadius, bounds);
            Vector2 targetPos = new Vector2(100f, 100f);

            Vector2 result = logic.SnapToTarget(targetPos);

            Assert.AreEqual(10f, result.x, k_FloatDelta, "SnapToTargetで境界クランプが適用されていない（X）");
            Assert.AreEqual(10f, result.y, k_FloatDelta, "SnapToTargetで境界クランプが適用されていない（Y）");
        }

        [Test]
        public void CameraFollowLogic_CalculatePosition_RespectsClampBounds()
        {
            Rect bounds = new Rect(-5f, -5f, 10f, 10f); // xMin=-5, xMax=5, yMin=-5, yMax=5
            CameraFollowLogic logic = new CameraFollowLogic(k_SmoothTime, k_DeadZoneRadius, bounds);
            Vector2 currentPos = new Vector2(4.5f, 4.5f);
            Vector2 targetPos = new Vector2(100f, 100f);

            Vector2 result = logic.CalculatePosition(currentPos, targetPos, k_DeltaTime);

            Assert.IsTrue(result.x <= 5f, "境界クランプが適用されていない（X）");
            Assert.IsTrue(result.y <= 5f, "境界クランプが適用されていない（Y）");
        }
    }
}
