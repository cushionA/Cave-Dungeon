using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Game.Runtime;

namespace Game.Tests.PlayMode
{
    /// <summary>
    /// CameraController (LateUpdate でターゲットを追従する MonoBehaviour ラッパー) の
    /// 基本動作を PlayMode で検証する。
    /// </summary>
    public class CameraFollowTests
    {
        private GameObject _cameraObj;
        private GameObject _targetObj;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            _cameraObj = new GameObject("TestCamera");
            _cameraObj.AddComponent<Camera>();
            _cameraObj.AddComponent<CameraController>();

            _targetObj = new GameObject("TestCameraTarget");
            _targetObj.transform.position = Vector3.zero;
            yield return null; // Awake
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_cameraObj != null)
            {
                Object.Destroy(_cameraObj);
            }
            if (_targetObj != null)
            {
                Object.Destroy(_targetObj);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator CameraFollow_WhenSetTargetCalled_SnapsToTargetPosition()
        {
            _targetObj.transform.position = new Vector3(5f, 2f, 0f);
            CameraController controller = _cameraObj.GetComponent<CameraController>();

            controller.SetTarget(_targetObj.transform);
            yield return null;

            // カメラは z=-10 でターゲットの (x,y) に吸着する
            Assert.AreEqual(5f, _cameraObj.transform.position.x, 0.001f);
            Assert.AreEqual(2f, _cameraObj.transform.position.y, 0.001f);
            Assert.AreEqual(-10f, _cameraObj.transform.position.z, 0.001f);
        }

        [UnityTest]
        public IEnumerator CameraFollow_WhenTargetMoves_CameraInterpolatesTowardsTarget()
        {
            CameraController controller = _cameraObj.GetComponent<CameraController>();
            // スタート地点は原点にスナップしておき、その後にターゲットを動かして LateUpdate で追従させる
            controller.SetTarget(_targetObj.transform);
            yield return null;

            Vector3 cameraStart = _cameraObj.transform.position;

            // ターゲットをデッドゾーンを超える距離へ動かす
            _targetObj.transform.position = new Vector3(10f, 0f, 0f);

            // 複数フレーム走らせて SmoothDamp で少しずつ近づくことを確認
            for (int i = 0; i < 30; i++)
            {
                yield return null;
            }

            Vector3 cameraAfter = _cameraObj.transform.position;
            Assert.Greater(cameraAfter.x, cameraStart.x,
                "カメラはターゲット方向（+X）へ移動する");
            Assert.LessOrEqual(cameraAfter.x, 10f + 0.001f,
                "ターゲット位置を通り越さない");
            Assert.AreEqual(-10f, cameraAfter.z, 0.001f,
                "z は常に -10 を維持");
        }
    }
}
