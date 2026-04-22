using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Game.Core;
using Game.Runtime;

namespace Game.Tests.PlayMode
{
    /// <summary>
    /// HudController の基本動作（コンポーネント初期化・公開 API の例外安全性）を検証する。
    /// UXML を伴わない最小構成で安全に初期化できること、ボス表示 / 通貨更新 API が
    /// UI が無くても例外を投げないことを確認する。
    /// </summary>
    public class HudBasicTests
    {
        private GameObject _hudObj;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            _hudObj = new GameObject("TestHud");
            _hudObj.AddComponent<UIDocument>();
            _hudObj.AddComponent<HudController>();
            yield return null; // Awake + OnEnable
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_hudObj != null)
            {
                Object.Destroy(_hudObj);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator Hud_WhenInstantiated_InitializesWithoutError()
        {
            HudController hud = _hudObj.GetComponent<HudController>();
            Assert.IsNotNull(hud, "HudController コンポーネントが付与されている");

            // Awake + OnEnable 完了後に数フレーム走らせて Update が例外を投げないことを確認
            // GameManager.Data が null でも早期 return するガードがあるはず
            for (int i = 0; i < 3; i++)
            {
                yield return null;
            }

            Assert.IsTrue(hud.isActiveAndEnabled, "HudController は有効状態");
        }

        [UnityTest]
        public IEnumerator Hud_ShowAndHideBoss_DoesNotThrow()
        {
            HudController hud = _hudObj.GetComponent<HudController>();

            // UIDocument の rootVisualElement が null でも API は安全に呼べる
            hud.ShowBossHp(bossHash: 42, bossName: "テストボス");
            yield return null;

            hud.HideBossHp();
            yield return null;

            Assert.Pass("ShowBossHp / HideBossHp が UXML 不在の環境でも例外を投げない");
        }

        [UnityTest]
        public IEnumerator Hud_UpdateCurrency_DoesNotThrow()
        {
            HudController hud = _hudObj.GetComponent<HudController>();

            // _currencyText が null（UXML 未バインド）でも API は安全
            hud.UpdateCurrency(1234);
            yield return null;
            hud.UpdateCurrency(0);
            yield return null;

            Assert.Pass("UpdateCurrency が UXML 不在の環境でも例外を投げない");
        }
    }
}
