using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Game.Core;
using Game.Runtime;

namespace Game.Tests.PlayMode
{
    /// <summary>
    /// ProCamera2DRoomsAdapter のテスト。
    /// GameEvents.OnAreaTransition を購読し、ProCamera2DRooms.EnterRoom(toAreaId) に転送する。
    /// 配置先 ProCamera2DRooms 不在 / Room ID 未登録 / 空 ID は no-op で安全動作することを検証する。
    /// </summary>
    public class ProCamera2DRoomsAdapterTests
    {
        private GameObject _gmObj;
        private GameObject _adapterObj;
        private ProCamera2DRoomsAdapter _adapter;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            _gmObj = TestSceneHelper.CreateGameManager();
            yield return null;

            _adapterObj = new GameObject("TestProCameraRoomsAdapter");
            _adapter = _adapterObj.AddComponent<ProCamera2DRoomsAdapter>();
            yield return null;
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
        public IEnumerator ProCamera2DRoomsAdapter_WhenAreaTransitionFiredAndRoomsNotAssigned_DoesNotThrowAndFlagsNoop()
        {
            // ProCamera2DRooms 未割当の環境では Adapter は no-op で動く必要がある
            Assert.DoesNotThrow(() =>
                GameManager.Events.FireAreaTransition("Persistent", "Stage1_1"),
                "ProCamera2DRooms 未割当時でも Fire が例外を出さない");

            yield return null;

            Assert.IsTrue(_adapter.LastAreaTransitionWasNoop,
                "ProCamera2DRooms 未割当時は LastAreaTransitionWasNoop=true で no-op 扱い");
        }

        [UnityTest]
        public IEnumerator ProCamera2DRoomsAdapter_WhenToAreaIdEmpty_FlagsNoop()
        {
            // toAreaId 空文字も no-op (FlagManager と同じ防御)
            GameManager.Events.FireAreaTransition("Persistent", "");
            yield return null;

            Assert.IsTrue(_adapter.LastAreaTransitionWasNoop,
                "toAreaId 空のときは no-op");
        }

        [UnityTest]
        public IEnumerator ProCamera2DRoomsAdapter_WhenDisabledAndReenabled_SubscriptionToggles()
        {
            // OnEnable / OnDisable で購読/解除が対称であることを、無効化中の Fire が adapter に届かないことで確認する
            _adapterObj.SetActive(true);
            yield return null;
            GameManager.Events.FireAreaTransition("Persistent", "Stage1_1");
            yield return null;
            Assert.IsTrue(_adapter.LastAreaTransitionWasNoop,
                "enable 中の発火は受信される (no-op だが受信)");

            _adapterObj.SetActive(false);
            yield return null;
            _adapter.ResetLastTransitionFlagForTest();
            GameManager.Events.FireAreaTransition("Persistent", "Stage1_2");
            yield return null;
            Assert.IsFalse(_adapter.LastAreaTransitionWasNoop,
                "OnDisable 後の Fire は購読解除済みなので flag は false のまま");
        }
    }
}
