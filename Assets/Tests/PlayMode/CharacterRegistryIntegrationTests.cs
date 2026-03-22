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
    /// CharacterRegistryとBaseCharacterの連携テスト。
    /// 名前登録→ハッシュ逆引き、生成/破棄連動を検証する。
    /// </summary>
    public class CharacterRegistryIntegrationTests
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
                    Object.DestroyImmediate(obj);
                }
            }
            _spawnedObjects.Clear();
            TestSceneHelper.Cleanup();
            yield return null;
        }

        [UnityTest]
        public IEnumerator CharacterRegistry_AfterBaseCharacterStart_NameToHashResolvable()
        {
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo();
            // CharacterInfoはScriptableObjectなのでnameプロパティを設定
            info.name = "TestHero";
            GameObject charObj = TestSceneHelper.CreateBaseCharacterObject(info, Vector3.zero);
            _spawnedObjects.Add(charObj);

            yield return null; // Start → RegisterName

            BaseCharacter bc = charObj.GetComponent<BaseCharacter>();
            bool found = CharacterRegistry.TryGetHashByName("TestHero", out int resolvedHash);

            Assert.IsTrue(found, "Name should be resolvable after BaseCharacter.Start");
            Assert.AreEqual(bc.ObjectHash, resolvedHash,
                "Resolved hash should match ObjectHash");
        }

        [UnityTest]
        public IEnumerator CharacterRegistry_ManualRegisterPlayer_PlayerHashIsSet()
        {
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(
                feature: CharacterFeature.Player);
            GameObject charObj = TestSceneHelper.CreateBaseCharacterObject(info, Vector3.zero);
            _spawnedObjects.Add(charObj);

            yield return null; // Start

            BaseCharacter bc = charObj.GetComponent<BaseCharacter>();
            int hash = bc.ObjectHash;

            // BaseCharacter.Startは RegisterName のみ。PlayerHash登録はPlayerCharacterが行う。
            // ここでは手動で登録してCharacterRegistryの動作を確認。
            CharacterRegistry.RegisterPlayer(hash);

            Assert.AreEqual(hash, CharacterRegistry.PlayerHash,
                "PlayerHash should be set after RegisterPlayer");
            Assert.IsTrue(CharacterRegistry.AllHashes.Contains(hash),
                "AllHashes should contain the player hash");
            Assert.IsTrue(CharacterRegistry.AllyHashes.Contains(hash),
                "AllyHashes should contain the player hash");
        }

        [UnityTest]
        public IEnumerator CharacterRegistry_ManualRegisterEnemy_EnemyHashesContainsHash()
        {
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(
                belong: CharacterBelong.Enemy, feature: CharacterFeature.Minion);
            GameObject charObj = TestSceneHelper.CreateBaseCharacterObject(info, Vector3.zero);
            _spawnedObjects.Add(charObj);

            yield return null; // Start

            BaseCharacter bc = charObj.GetComponent<BaseCharacter>();
            int hash = bc.ObjectHash;

            // EnemyCharacter.Startが行うことを手動で再現
            CharacterRegistry.RegisterEnemy(hash);

            Assert.IsTrue(CharacterRegistry.AllHashes.Contains(hash),
                "AllHashes should contain the enemy hash");
            Assert.IsTrue(CharacterRegistry.EnemyHashes.Contains(hash),
                "EnemyHashes should contain the enemy hash");
        }

        [UnityTest]
        public IEnumerator CharacterRegistry_Unregister_RemovesFromAllLists()
        {
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo();
            info.name = "UnregisterTarget";
            GameObject charObj = TestSceneHelper.CreateBaseCharacterObject(info, Vector3.zero);
            _spawnedObjects.Add(charObj);

            yield return null; // Start

            BaseCharacter bc = charObj.GetComponent<BaseCharacter>();
            int hash = bc.ObjectHash;

            // 手動登録してから解除
            CharacterRegistry.RegisterEnemy(hash);
            Assert.IsTrue(CharacterRegistry.EnemyHashes.Contains(hash));

            CharacterRegistry.Unregister(hash);

            Assert.IsFalse(CharacterRegistry.AllHashes.Contains(hash),
                "AllHashes should not contain unregistered hash");
            Assert.IsFalse(CharacterRegistry.EnemyHashes.Contains(hash),
                "EnemyHashes should not contain unregistered hash");

            // 名前マッピングも削除される
            bool found = CharacterRegistry.TryGetHashByName("UnregisterTarget", out int _);
            Assert.IsFalse(found, "Name mapping should be removed after Unregister");
        }

        [UnityTest]
        public IEnumerator CharacterRegistry_Clear_RemovesAllEntries()
        {
            CharacterInfo info1 = TestSceneHelper.CreateTestCharacterInfo();
            info1.name = "Char1";
            GameObject charObj1 = TestSceneHelper.CreateBaseCharacterObject(info1, Vector3.zero);
            _spawnedObjects.Add(charObj1);

            CharacterInfo info2 = TestSceneHelper.CreateTestCharacterInfo();
            info2.name = "Char2";
            GameObject charObj2 = TestSceneHelper.CreateBaseCharacterObject(info2, new Vector3(2, 0, 0));
            _spawnedObjects.Add(charObj2);

            yield return null; // Start for both

            BaseCharacter bc1 = charObj1.GetComponent<BaseCharacter>();
            BaseCharacter bc2 = charObj2.GetComponent<BaseCharacter>();
            CharacterRegistry.RegisterPlayer(bc1.ObjectHash);
            CharacterRegistry.RegisterEnemy(bc2.ObjectHash);

            CharacterRegistry.Clear();

            Assert.AreEqual(0, CharacterRegistry.AllHashes.Count,
                "AllHashes should be empty after Clear");
            Assert.AreEqual(0, CharacterRegistry.AllyHashes.Count,
                "AllyHashes should be empty after Clear");
            Assert.AreEqual(0, CharacterRegistry.EnemyHashes.Count,
                "EnemyHashes should be empty after Clear");
            Assert.AreEqual(0, CharacterRegistry.PlayerHash,
                "PlayerHash should be 0 after Clear");
        }
    }
}
