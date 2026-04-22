using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Game.Runtime;

namespace Game.Tests.PlayMode
{
    /// <summary>
    /// CameraShakeController (Perlin ノイズ駆動の画面シェイク MonoBehaviour) の基本動作を検証する。
    /// GameManager を介さず直接 Shake API を呼ぶ形で、コントローラ単体の挙動をテストする。
    /// </summary>
    public class CameraShakeControllerTests
    {
        private GameObject _cameraObj;
        private CameraShakeController _controller;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            _cameraObj = new GameObject("TestCameraShake");
            _cameraObj.AddComponent<Camera>();
            _controller = _cameraObj.AddComponent<CameraShakeController>();
            // 明示的に localPosition を 0 にしておく (Camera デフォルトは 0 だが念のため)
            _cameraObj.transform.localPosition = Vector3.zero;
            yield return null; // Awake / OnEnable
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_cameraObj != null)
            {
                Object.Destroy(_cameraObj);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator CameraShake_WhenShakeCalled_LocalPositionBecomesNonZero()
        {
            // Shake 呼び出し直後は localPosition=0 (Update がまだ走っていない) なので、
            // 1 フレーム進めてから Update による加算を検証する。
            _controller.Shake(magnitude: 1f, duration: 1f);
            Assert.IsTrue(_controller.IsShaking, "Shake 呼び出し後は IsShaking=true");

            // 数フレーム進めると Perlin ノイズのサンプリングで localPosition が 0 から離れる。
            // Perlin は seed 次第で一時的に 0 付近を通ることがあるため、10 フレーム中のどこかで
            // 非ゼロになることを検証する。
            bool becameNonZero = false;
            for (int i = 0; i < 10; i++)
            {
                yield return null;
                if (_cameraObj.transform.localPosition.sqrMagnitude > 0.000001f)
                {
                    becameNonZero = true;
                    break;
                }
            }

            Assert.IsTrue(becameNonZero,
                "Shake 中は Perlin ノイズにより localPosition が 0 以外になるはず");
        }

        [UnityTest]
        public IEnumerator CameraShake_WhenDurationElapsed_LocalPositionResetsToZero()
        {
            float duration = 0.2f;
            _controller.Shake(magnitude: 0.5f, duration: duration);

            // duration を超える時間を待つ。念のため少し余分に待機。
            float waited = 0f;
            float timeout = duration + 0.3f;
            while (waited < timeout)
            {
                yield return null;
                waited += Time.deltaTime;
            }

            Assert.IsFalse(_controller.IsShaking,
                "duration 経過後は IsShaking=false");
            Assert.AreEqual(0f, _cameraObj.transform.localPosition.x, 0.0001f,
                "duration 経過後は localPosition.x が 0 に戻る");
            Assert.AreEqual(0f, _cameraObj.transform.localPosition.y, 0.0001f,
                "duration 経過後は localPosition.y が 0 に戻る");
        }

        [UnityTest]
        public IEnumerator CameraShake_WhenCalledMultipleTimes_UsesMaximumMagnitude()
        {
            // まず小さい magnitude で開始
            _controller.Shake(magnitude: 0.1f, duration: 10f);
            yield return null;

            // 大きい magnitude で上書き
            _controller.Shake(magnitude: 5f, duration: 10f);
            yield return null;

            // その後さらに小さい magnitude を呼んでも上書きされないことを検証するため
            // 挙動としては「最大 magnitude 5 が保持されている」ことを確認する。
            // 小さい magnitude (例: 0.01f) を渡しても、最大 5 が維持される。
            _controller.Shake(magnitude: 0.01f, duration: 10f);

            // 複数フレーム走らせて localPosition の最大振幅を計測。
            // magnitude 5 に対応する大きな振幅 (例: |x| > 0.5) が観測されるはず。
            // (0.01 だけが採用されていた場合、振幅は 0.01 程度にしかならず 0.5 には届かない)
            float observedMax = 0f;
            for (int i = 0; i < 30; i++)
            {
                yield return null;
                float absX = Mathf.Abs(_cameraObj.transform.localPosition.x);
                float absY = Mathf.Abs(_cameraObj.transform.localPosition.y);
                float peak = Mathf.Max(absX, absY);
                if (peak > observedMax)
                {
                    observedMax = peak;
                }
            }

            Assert.Greater(observedMax, 0.5f,
                $"最大 magnitude=5 が採用されていれば |offset| は 0.5 を超えるはず (observed={observedMax})。" +
                "小さい magnitude で上書きされていた場合ここで fail する。");
        }
    }
}
