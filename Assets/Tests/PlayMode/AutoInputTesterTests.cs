using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Game.Runtime;

namespace Game.Tests.PlayMode
{
    /// <summary>
    /// AutoInputTester (開発用自動入力) の基本動作を PlayMode で検証する。
    /// 実テストシナリオそのものではなく、コンポーネントの生成・有効/無効切替・
    /// プロパティ設定の最小動作のみを確認する。
    /// </summary>
    public class AutoInputTesterTests
    {
        private GameObject _testerObj;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_testerObj != null)
            {
                Object.Destroy(_testerObj);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator AutoInputTester_WhenEnableOnStartIsFalse_DoesNotInvokeSequence()
        {
            _testerObj = new GameObject("AutoInputTester_Disabled");
            AutoInputTester tester = _testerObj.AddComponent<AutoInputTester>();
#if UNITY_EDITOR
            tester.EnableOnStart = false;
#endif

            // Awake + Start
            yield return null;

            // Start が呼ばれるが _enableOnStart=false なので DelayedStart は動かない
            // コルーチン開始待ちを含めて数フレーム経過しても破綻しないことを確認
            for (int i = 0; i < 5; i++)
            {
                yield return null;
            }

            Assert.IsTrue(tester.isActiveAndEnabled, "テスター自体は有効状態のまま");
        }

        [UnityTest]
        public IEnumerator AutoInputTester_WhenEnableOnStartIsTrueButNoPlayer_LogsErrorAndStops()
        {
            // PlayerCharacter が存在しない場合、DelayedStart は Debug.LogError を出して終了する
            // PlayMode テストとしてエラーログが出ることを期待する
            LogAssert.Expect(LogType.Error, new Regex("PlayerCharacter が見つかりません"));

            _testerObj = new GameObject("AutoInputTester_Enabled");
            AutoInputTester tester = _testerObj.AddComponent<AutoInputTester>();
#if UNITY_EDITOR
            tester.EnableOnStart = true;
            tester.LoopCount = 1;
#endif

            // DelayedStart は WaitForSeconds(0.5f) 後にプレイヤー検索 → LogError
            yield return new WaitForSeconds(0.6f);

            Assert.IsTrue(tester.isActiveAndEnabled, "エラー後もコンポーネント自体は破棄されない");
        }

        [Test]
        public void AutoInputTester_TestTogglesAreIndependentlySettable()
        {
#if UNITY_EDITOR
            GameObject go = new GameObject("AutoInputTester_Toggles");
            try
            {
                AutoInputTester tester = go.AddComponent<AutoInputTester>();

                // EditMode 属性のプロパティを個別に制御できることを確認
                tester.TestMove = false;
                tester.TestJump = true;
                tester.TestLightAttack = false;
                tester.TestHeavyAttack = true;

                Assert.IsFalse(tester.TestMove);
                Assert.IsTrue(tester.TestJump);
                Assert.IsFalse(tester.TestLightAttack);
                Assert.IsTrue(tester.TestHeavyAttack);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
#else
            Assert.Pass("UNITY_EDITOR 限定 API のためスキップ");
#endif
        }
    }
}
