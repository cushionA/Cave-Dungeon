using Game.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    public class ProjectileWarningDirectionTests
    {
        [TestCase(0f, 0f, 1f, 0f)]
        [TestCase(90f, 0f, 0f, 1f)]
        [TestCase(180f, 0f, -1f, 0f)]
        [TestCase(270f, 0f, 0f, -1f)]
        [TestCase(0f, 90f, 0f, -1f)]
        [TestCase(0f, 270f, 0f, 1f)]
        public void ProjectileWarningDirection_WhenCameraRotates_MovesWithTheView(float roll, float yaw, float x, float y)
        {
            GameObject camera = new GameObject("Warning camera orientation");
            try
            {
                camera.transform.rotation = Quaternion.Euler(0f, yaw, roll);
                Vector2 direction = ProjectileWarningOverlay.ProjectWarningDirection(camera.transform, Vector2.right);
                Assert.That(direction.x, Is.EqualTo(x).Within(0.001f));
                Assert.That(direction.y, Is.EqualTo(y).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(camera);
            }
        }
    }
}
