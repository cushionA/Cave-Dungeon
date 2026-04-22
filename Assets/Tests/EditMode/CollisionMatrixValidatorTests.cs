using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Game.Editor;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// CollisionMatrixSetup.ValidateCollisionMatrix の動作検証。
    /// 期待値と実設定が一致していれば 0 件、違えば LogError が出て不一致件数が返ることを確認する。
    /// </summary>
    [TestFixture]
    public class CollisionMatrixValidatorTests
    {
        private List<(int a, int b, bool ignore)> _savedState;

        [SetUp]
        public void SetUp()
        {
            // 期待値テーブルに関わるペアの現在値をバックアップし、テスト後に復元する
            _savedState = new List<(int, int, bool)>();
            foreach (CollisionMatrixSetup.ExpectedPair p in CollisionMatrixSetup.GetExpectedPairs())
            {
                int a = Mathf.Min(p.layerA, p.layerB);
                int b = Mathf.Max(p.layerA, p.layerB);
                bool current = Physics2D.GetIgnoreLayerCollision(a, b);
                _savedState.Add((a, b, current));
            }
        }

        [TearDown]
        public void TearDown()
        {
            // バックアップから完全復元
            foreach ((int a, int b, bool ignore) in _savedState)
            {
                Physics2D.IgnoreLayerCollision(a, b, ignore);
            }
        }

        [Test]
        public void Validate_WhenMatrixIsCorrect_ReturnsZeroMismatches()
        {
            // 期待値通りに設定してから検証すると 0 件になる
            CollisionMatrixSetup.SetupCollisionMatrix();

            int mismatches = CollisionMatrixSetup.ValidateCollisionMatrix();

            Assert.AreEqual(0, mismatches,
                "Apply 直後は期待値と一致するので不一致 0 件になるべき");
        }

        [Test]
        public void Validate_WhenPairIsBroken_LogsErrorAndCountsMismatch()
        {
            // 一旦期待値通りに整える
            CollisionMatrixSetup.SetupCollisionMatrix();

            // 期待値テーブルから 1 組を意図的に反転させる
            // CharaPassThrough (12) <-> Ground (6) は期待値 shouldIgnore=false のため true に反転
            const int k_CharaPassThrough = 12;
            const int k_Ground = 6;
            bool originalExpected = Physics2D.GetIgnoreLayerCollision(k_CharaPassThrough, k_Ground);
            Physics2D.IgnoreLayerCollision(k_CharaPassThrough, k_Ground, !originalExpected);

            // LogError が最低 1 件出ることを宣言
            LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex(@"\[CollisionMatrixSetup\].+コリジョン設定が期待値と異なります"));

            int mismatches = CollisionMatrixSetup.ValidateCollisionMatrix();

            Assert.GreaterOrEqual(mismatches, 1,
                "期待値テーブルと矛盾する組があれば 1 件以上報告されるべき");
        }
    }
}
