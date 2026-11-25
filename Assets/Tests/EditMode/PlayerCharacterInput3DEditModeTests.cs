using Game.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    public class PlayerCharacterInput3DEditModeTests
    {
        [TestCase(0d, true)]
        [TestCase(0.249d, true)]
        [TestCase(0.25d, false)]
        [TestCase(0.5d, false)]
        public void DodgeInput_OnlyShortPressesQualifyAsATap(double duration, bool expected)
        {
            Assert.AreEqual(expected, PlayerCharacterInput3D.IsDodgeTap(duration));
        }

        [TestCase(0f, 0f, 0f, 0f, 1f, 0f, 1f)]
        [TestCase(90f, 90f, 0f, 0f, 1f, 1f, 0f)]
        [TestCase(0f, 0f, 90f, 1f, 0f, 1f, 0f)]
        [TestCase(90f, 30f, 0f, 0.5f, 0f, 0f, -0.5f)]
        [TestCase(0f, 0f, 0f, 1f, 1f, 0.7071068f, 0.7071068f)]
        [TestCase(0f, 0f, 0f, 0.1f, 0f, 0f, 0f)]
        public void MoveDirection_CameraRotationAndStickInput_ProducesPlanarDirection(
            float yaw, float pitch, float roll, float stickX, float stickY, float expectedX, float expectedZ)
        {
            GameObject cameraObject = new GameObject("Projection camera");
            try
            {
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.transform.rotation = Quaternion.Euler(pitch, yaw, roll);
                Vector3 direction = PlayerCharacterInput3D.CameraRelativeDirection(new Vector2(stickX, stickY), camera);
                Assert.That(direction.x, Is.EqualTo(expectedX).Within(0.0001f));
                Assert.That(direction.y, Is.Zero);
                Assert.That(direction.z, Is.EqualTo(expectedZ).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
            }
        }
    }
}
