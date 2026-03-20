using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using R3;
using UnityEngine;
using UnityEngine.TestTools;
using Game.Core;
using Game.Runtime;
using CharacterInfo = Game.Core.CharacterInfo;

namespace Game.Tests.PlayMode
{
    /// <summary>
    /// BaseCharacterのライフサイクル（Awake→Start→OnDestroy）に関するPlayModeテスト。
    /// GameManager登録/解除、CharacterRegistry連携、イベント発火を検証する。
    /// </summary>
    public class BaseCharacterLifecycleTests
    {
        private List<GameObject> _spawnedObjects;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            _spawnedObjects = new List<GameObject>();
            TestSceneHelper.CreateGameManager();
            yield return null; // Awake完了を待つ
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (GameObject obj in _spawnedObjects)
            {
                if (obj != null)
                {
                    Object.DestroyImmediate(obj);
                }
            }
            _spawnedObjects.Clear();
            TestSceneHelper.Cleanup();
            yield return null;
        }

        // ===== BaseCharacter ライフサイクル =====

        [UnityTest]
        public IEnumerator BaseCharacter_Start_RegistersInSoAWithCorrectVitals()
        {
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(maxHp: 200);
            GameObject charObj = TestSceneHelper.CreateBaseCharacterObject(info, Vector3.zero);
            _spawnedObjects.Add(charObj);

            yield return null; // Start

            BaseCharacter bc = charObj.GetComponent<BaseCharacter>();
            int hash = bc.ObjectHash;

            Assert.IsTrue(GameManager.Data.TryGetValue(hash, out int _),
                "BaseCharacter should be registered in SoA after Start");

            CharacterVitals vitals = GameManager.Data.GetVitals(hash);
            Assert.AreEqual(200, vitals.currentHp, "currentHp should match CharacterInfo.maxHp");
            Assert.AreEqual(200, vitals.maxHp, "maxHp should match CharacterInfo.maxHp");
            Assert.AreEqual(50, vitals.currentMp, "currentMp should match CharacterInfo.maxMp");
            Assert.AreEqual(50, vitals.maxMp, "maxMp should match CharacterInfo.maxMp");
        }

        [UnityTest]
        public IEnumerator BaseCharacter_Start_FiresCharacterRegisteredEvent()
        {
            int eventHash = 0;
            IDisposable subscription = GameManager.Events.OnCharacterRegistered
                .Subscribe(h => eventHash = h);

            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo();
            GameObject charObj = TestSceneHelper.CreateBaseCharacterObject(info, Vector3.zero);
            _spawnedObjects.Add(charObj);

            yield return null; // Start → RegisterCharacter → FireCharacterRegistered

            BaseCharacter bc = charObj.GetComponent<BaseCharacter>();
            Assert.AreEqual(bc.ObjectHash, eventHash,
                "OnCharacterRegistered should fire with the character's ObjectHash");

            subscription.Dispose();
        }

        [UnityTest]
        public IEnumerator BaseCharacter_OnDestroy_UnregistersFromSoA()
        {
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo();
            GameObject charObj = TestSceneHelper.CreateBaseCharacterObject(info, Vector3.zero);

            yield return null; // Start

            BaseCharacter bc = charObj.GetComponent<BaseCharacter>();
            int hash = bc.ObjectHash;

            Assert.IsTrue(GameManager.Data.TryGetValue(hash, out int _),
                "Precondition: character should be registered");

            Object.DestroyImmediate(charObj);
            yield return null;

            Assert.IsFalse(GameManager.Data.TryGetValue(hash, out int _),
                "Character should be unregistered from SoA after OnDestroy");
        }


        [UnityTest]
        public IEnumerator BaseCharacter_OnDestroy_FiresCharacterRemovedEvent()
        {
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo();
            GameObject charObj = TestSceneHelper.CreateBaseCharacterObject(info, Vector3.zero);

            yield return null; // Start

            BaseCharacter bc = charObj.GetComponent<BaseCharacter>();
            int hash = bc.ObjectHash;

            int removedHash = 0;
            IDisposable subscription = GameManager.Events.OnCharacterRemoved
                .Subscribe(h => removedHash = h);

            Object.DestroyImmediate(charObj);
            yield return null;

            Assert.AreEqual(hash, removedHash,
                "OnCharacterRemoved should fire with the destroyed character's hash");

            subscription.Dispose();
            // charObj is already destroyed — no cleanup needed
        }

        [UnityTest]
        public IEnumerator BaseCharacter_Awake_ObjectHashIsInstanceId()
        {
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo();
            GameObject charObj = TestSceneHelper.CreateBaseCharacterObject(info, Vector3.zero);
            _spawnedObjects.Add(charObj);

            // ObjectHashはAwakeで設定されるので、同フレームでアクセス可能
            BaseCharacter bc = charObj.GetComponent<BaseCharacter>();
            Assert.AreEqual(charObj.GetInstanceID(), bc.ObjectHash,
                "ObjectHash should equal the GameObject's InstanceID");

            yield return null;
        }

        [UnityTest]
        public IEnumerator BaseCharacter_IsAlive_TrueAfterRegistration()
        {
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(maxHp: 100);
            GameObject charObj = TestSceneHelper.CreateBaseCharacterObject(info, Vector3.zero);
            _spawnedObjects.Add(charObj);

            yield return null; // Start

            BaseCharacter bc = charObj.GetComponent<BaseCharacter>();
            Assert.IsTrue(bc.IsAlive, "IsAlive should be true when HP > 0");
        }

        [UnityTest]
        public IEnumerator BaseCharacter_WithoutCharacterInfo_DoesNotRegister()
        {
            // CharacterInfoがnullの場合、エラーログが出ることを許容する
            LogAssert.ignoreFailingMessages = true;

            // CharacterInfoをnullのまま作成
            GameObject go = new GameObject("TestCharNoInfo");
            go.AddComponent<Rigidbody2D>();
            go.AddComponent<BoxCollider2D>();
            BaseCharacter bc = go.AddComponent<BaseCharacter>();
            _spawnedObjects.Add(go);
            // _characterInfoを設定しない

            yield return null; // Start — should log error but not crash

            int hash = bc.ObjectHash;
            Assert.IsFalse(GameManager.Data.TryGetValue(hash, out int _),
                "Character without CharacterInfo should not be registered in SoA");

            LogAssert.ignoreFailingMessages = false;
        }
    }
}
