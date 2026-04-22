using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Game.Core;
using Game.Runtime;
using CharacterInfo = Game.Core.CharacterInfo;

namespace Game.Tests.PlayMode
{
    /// <summary>
    /// AutoInputTester の vitals スナップショット共通ヘルパー (GetVitalsSnapshot) の検証。
    /// ヘルパー抽出後も TakeSnapshot / LogResetState が同じ値を読めることを保証する。
    /// </summary>
    public class AutoInputTesterHelperTests
    {
        private List<GameObject> _spawnedObjects;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            _spawnedObjects = new List<GameObject>();
            TestSceneHelper.CreateGameManager();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (GameObject obj in _spawnedObjects)
            {
                if (obj != null)
                {
                    Object.Destroy(obj);
                }
            }
            _spawnedObjects.Clear();
            TestSceneHelper.Cleanup();
            yield return null;
        }

        [UnityTest]
        public IEnumerator GetVitalsSnapshot_InvalidHash_ReturnsIsValidFalse()
        {
            // 未登録ハッシュ (9999) は isValid=false の default を返す
            AutoInputTester.VitalsSnapshot snap = AutoInputTester.GetVitalsSnapshotForTest(9999);

            Assert.IsFalse(snap.isValid, "未登録ハッシュでは isValid=false を返すべき");
            Assert.AreEqual(0, snap.currentHp);
            Assert.AreEqual(0, snap.maxHp);
            Assert.AreEqual(0f, snap.currentStamina);
            Assert.AreEqual(0f, snap.maxStamina);
            yield return null;
        }

        [UnityTest]
        public IEnumerator GetVitalsSnapshot_ValidHash_MatchesGameManagerData()
        {
            // 登録済みキャラの vitals を GetVitalsSnapshot と GameManager.Data.GetVitals で読み比べて一致を確認
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(maxHp: 123);
            GameObject charObj = TestSceneHelper.CreateBaseCharacterObject(info, Vector3.zero);
            _spawnedObjects.Add(charObj);
            yield return null; // Start を待つ

            BaseCharacter bc = charObj.GetComponent<BaseCharacter>();
            int hash = bc.ObjectHash;

            // 元データを直接取得した場合の値
            ref CharacterVitals expected = ref GameManager.Data.GetVitals(hash);
            int expectedHp = expected.currentHp;
            int expectedMaxHp = expected.maxHp;
            float expectedStamina = expected.currentStamina;
            float expectedMaxStamina = expected.maxStamina;

            // ヘルパーを経由した場合
            AutoInputTester.VitalsSnapshot snap = AutoInputTester.GetVitalsSnapshotForTest(hash);

            Assert.IsTrue(snap.isValid, "登録済みハッシュでは isValid=true を返すべき");
            Assert.AreEqual(expectedHp, snap.currentHp, "currentHp が一致する");
            Assert.AreEqual(expectedMaxHp, snap.maxHp, "maxHp が一致する");
            Assert.AreEqual(expectedStamina, snap.currentStamina, "currentStamina が一致する");
            Assert.AreEqual(expectedMaxStamina, snap.maxStamina, "maxStamina が一致する");
        }

        [UnityTest]
        public IEnumerator GetVitalsSnapshot_ReflectsSoaUpdates()
        {
            // vitals を書き換えた後、ヘルパー経由でも新しい値が読めることを確認
            // (TakeSnapshot / LogResetState が同じヘルパーを呼ぶので、両者の出力も追随する)
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(maxHp: 100);
            GameObject charObj = TestSceneHelper.CreateBaseCharacterObject(info, Vector3.zero);
            _spawnedObjects.Add(charObj);
            yield return null;

            int hash = charObj.GetComponent<BaseCharacter>().ObjectHash;

            // vitals を書き換え
            ref CharacterVitals vitals = ref GameManager.Data.GetVitals(hash);
            vitals.currentHp = 42;
            vitals.currentStamina = 37.5f;

            AutoInputTester.VitalsSnapshot snap = AutoInputTester.GetVitalsSnapshotForTest(hash);

            Assert.IsTrue(snap.isValid);
            Assert.AreEqual(42, snap.currentHp, "SoA 更新後の HP が反映される");
            Assert.AreEqual(37.5f, snap.currentStamina, "SoA 更新後のスタミナが反映される");
        }
    }
}
