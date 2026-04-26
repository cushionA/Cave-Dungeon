using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Game.Core;
using Game.Runtime;

namespace Game.Tests.PlayMode
{
    /// <summary>
    /// ProCamera2DShakeAdapter のテスト。
    /// GameEvents.OnCameraShakeRequested を購読し、ProCamera2DShake.Instance.Shake(presetName) に転送する。
    /// ProCamera2DShake が未配置の環境でも no-op で動くこと、購読の対称性、空 preset 名での安全動作を検証する。
    /// </summary>
    public class ProCamera2DShakeAdapterTests
    {
        private GameObject _gmObj;
        private GameObject _adapterObj;
        private ProCamera2DShakeAdapter _adapter;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            _gmObj = TestSceneHelper.CreateGameManager();
            yield return null; // GameManager.Awake で _core を初期化させる

            _adapterObj = new GameObject("TestProCameraShakeAdapter");
            _adapter = _adapterObj.AddComponent<ProCamera2DShakeAdapter>();
            yield return null; // OnEnable で購読を完了させる
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_adapterObj != null)
            {
                Object.Destroy(_adapterObj);
            }
            if (_gmObj != null)
            {
                Object.Destroy(_gmObj);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator ProCamera2DShakeAdapter_WhenShakeFiredAndNoProCamera_DoesNotThrowAndFlagsNoop()
        {
            // ProCamera2DShake 未配置の環境では Adapter は no-op で動く必要がある (例外で落ちると製品コードで困る)
            CameraShakeParams shakeParams = new CameraShakeParams("Hit");

            Assert.DoesNotThrow(() => GameManager.Events.FireCameraShakeRequested(shakeParams),
                "ProCamera2DShake 不在時でも Fire が例外を出さない");

            yield return null;

            Assert.IsTrue(_adapter.LastShakeWasNoop,
                "ProCamera2DShake 不在時は LastShakeWasNoop=true で no-op 扱い");
        }

        [UnityTest]
        public IEnumerator ProCamera2DShakeAdapter_WhenPresetNameEmpty_FlagsNoop()
        {
            // preset name 未指定 (空文字) は no-op 扱い
            CameraShakeParams shakeParams = new CameraShakeParams("");

            GameManager.Events.FireCameraShakeRequested(shakeParams);
            yield return null;

            Assert.IsTrue(_adapter.LastShakeWasNoop,
                "preset name 空のときは no-op");
        }

        [UnityTest]
        public IEnumerator ProCamera2DShakeAdapter_WhenDisabledAndReenabled_SubscriptionToggles()
        {
            // OnEnable / OnDisable で購読/解除が対称であることを、無効化中の Fire が adapter に届かないことで確認する
            _adapterObj.SetActive(false);
            yield return null;

            // 無効化中に Fire しても LastShakeWasNoop は変わらない (購読解除済み = no callback)
            // 初期値は false なので、まず一度 enable で true にしてから比較する
            _adapterObj.SetActive(true);
            yield return null;
            GameManager.Events.FireCameraShakeRequested(new CameraShakeParams("Hit"));
            yield return null;
            Assert.IsTrue(_adapter.LastShakeWasNoop, "enable 中の発火は受信される (no-op だが受信)");

            // 再 disable してから発火 → 受信しないので LastShakeWasNoop が変化しないことを確認
            _adapterObj.SetActive(false);
            yield return null;
            // 一旦 false にリセット
            _adapter.ResetLastShakeFlagForTest();
            GameManager.Events.FireCameraShakeRequested(new CameraShakeParams("Hit"));
            yield return null;
            Assert.IsFalse(_adapter.LastShakeWasNoop,
                "OnDisable 後の Fire は購読解除済みなので flag は false のまま");
        }
    }
}
