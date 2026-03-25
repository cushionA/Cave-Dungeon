using System;
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
    /// 複数キャラクターが同時存在する場合のデータ整合性テスト。
    /// SoAコンテナの独立性、登録/解除の順序依存性を検証する。
    /// </summary>
    public class MultiCharacterCoexistenceTests
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
                    UnityEngine.Object.DestroyImmediate(obj);
                }
            }
            _spawnedObjects.Clear();
            TestSceneHelper.Cleanup();
            yield return null;
        }

        private GameObject SpawnCharacter(int maxHp, Vector3 position, string name = null)
        {
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(maxHp: maxHp);
            if (name != null)
            {
                info.name = name;
            }
            GameObject obj = TestSceneHelper.CreateBaseCharacterObject(info, position);
            _spawnedObjects.Add(obj);
            return obj;
        }

        [UnityTest]
        public IEnumerator MultiCharacter_TwoCharacters_IndependentHpValues()
        {
            GameObject char1 = SpawnCharacter(100, Vector3.zero);
            GameObject char2 = SpawnCharacter(200, new Vector3(3, 0, 0));

            yield return null; // Start

            BaseCharacter bc1 = char1.GetComponent<BaseCharacter>();
            BaseCharacter bc2 = char2.GetComponent<BaseCharacter>();

            CharacterVitals vitals1 = GameManager.Data.GetVitals(bc1.ObjectHash);
            CharacterVitals vitals2 = GameManager.Data.GetVitals(bc2.ObjectHash);

            Assert.AreEqual(100, vitals1.maxHp, "Character 1 should have maxHp 100");
            Assert.AreEqual(200, vitals2.maxHp, "Character 2 should have maxHp 200");
            Assert.AreEqual(100, vitals1.currentHp, "Character 1 should have currentHp 100");
            Assert.AreEqual(200, vitals2.currentHp, "Character 2 should have currentHp 200");
        }

        [UnityTest]
        public IEnumerator MultiCharacter_ThreeCharacters_AllRegisteredInSoA()
        {
            GameObject char1 = SpawnCharacter(50, Vector3.zero);
            GameObject char2 = SpawnCharacter(75, new Vector3(2, 0, 0));
            GameObject char3 = SpawnCharacter(100, new Vector3(4, 0, 0));

            yield return null; // Start

            BaseCharacter bc1 = char1.GetComponent<BaseCharacter>();
            BaseCharacter bc2 = char2.GetComponent<BaseCharacter>();
            BaseCharacter bc3 = char3.GetComponent<BaseCharacter>();

            Assert.IsTrue(GameManager.Data.TryGetValue(bc1.ObjectHash, out int _));
            Assert.IsTrue(GameManager.Data.TryGetValue(bc2.ObjectHash, out int _));
            Assert.IsTrue(GameManager.Data.TryGetValue(bc3.ObjectHash, out int _));

            // ハッシュがすべて異なることを確認
            Assert.AreNotEqual(bc1.ObjectHash, bc2.ObjectHash);
            Assert.AreNotEqual(bc2.ObjectHash, bc3.ObjectHash);
            Assert.AreNotEqual(bc1.ObjectHash, bc3.ObjectHash);
        }

        [UnityTest]
        public IEnumerator MultiCharacter_DestroyOne_OthersRemainRegistered()
        {
            GameObject char1 = SpawnCharacter(100, Vector3.zero);
            GameObject char2 = SpawnCharacter(150, new Vector3(2, 0, 0));
            GameObject char3 = SpawnCharacter(200, new Vector3(4, 0, 0));

            yield return null; // Start

            BaseCharacter bc1 = char1.GetComponent<BaseCharacter>();
            BaseCharacter bc2 = char2.GetComponent<BaseCharacter>();
            BaseCharacter bc3 = char3.GetComponent<BaseCharacter>();
            int hash1 = bc1.ObjectHash;
            int hash2 = bc2.ObjectHash;
            int hash3 = bc3.ObjectHash;

            // 中間のキャラクターを破棄
            UnityEngine.Object.DestroyImmediate(char2);
            _spawnedObjects.Remove(char2);
            yield return null;

            Assert.IsTrue(GameManager.Data.TryGetValue(hash1, out int _),
                "Character 1 should remain registered");
            Assert.IsFalse(GameManager.Data.TryGetValue(hash2, out int _),
                "Character 2 should be unregistered");
            Assert.IsTrue(GameManager.Data.TryGetValue(hash3, out int _),
                "Character 3 should remain registered");

            // 残存キャラクターのデータが破損していないことを確認
            CharacterVitals vitals1 = GameManager.Data.GetVitals(hash1);
            CharacterVitals vitals3 = GameManager.Data.GetVitals(hash3);
            Assert.AreEqual(100, vitals1.maxHp, "Character 1 HP should be intact");
            Assert.AreEqual(200, vitals3.maxHp, "Character 3 HP should be intact");
        }

        [UnityTest]
        public IEnumerator MultiCharacter_DestroyAll_SoAIsClean()
        {
            GameObject char1 = SpawnCharacter(100, Vector3.zero);
            GameObject char2 = SpawnCharacter(200, new Vector3(2, 0, 0));

            yield return null; // Start

            BaseCharacter bc1 = char1.GetComponent<BaseCharacter>();
            BaseCharacter bc2 = char2.GetComponent<BaseCharacter>();
            int hash1 = bc1.ObjectHash;
            int hash2 = bc2.ObjectHash;

            UnityEngine.Object.DestroyImmediate(char1);
            UnityEngine.Object.DestroyImmediate(char2);
            _spawnedObjects.Clear();
            yield return null;

            Assert.IsFalse(GameManager.Data.TryGetValue(hash1, out int _),
                "Character 1 should be unregistered");
            Assert.IsFalse(GameManager.Data.TryGetValue(hash2, out int _),
                "Character 2 should be unregistered");
        }

        [UnityTest]
        public IEnumerator MultiCharacter_RegisteredEvents_FireForEachCharacter()
        {
            List<int> registeredHashes = new List<int>();
            IDisposable subscription = GameManager.Events.OnCharacterRegistered
                .Subscribe(h => registeredHashes.Add(h));

            GameObject char1 = SpawnCharacter(100, Vector3.zero);
            GameObject char2 = SpawnCharacter(200, new Vector3(2, 0, 0));

            yield return null; // Start

            BaseCharacter bc1 = char1.GetComponent<BaseCharacter>();
            BaseCharacter bc2 = char2.GetComponent<BaseCharacter>();

            Assert.AreEqual(2, registeredHashes.Count,
                "OnCharacterRegistered should fire for each character");
            Assert.Contains(bc1.ObjectHash, registeredHashes);
            Assert.Contains(bc2.ObjectHash, registeredHashes);

            subscription.Dispose();
        }

        [UnityTest]
        public IEnumerator MultiCharacter_DamageOne_OtherHpUnaffected()
        {
            CharacterInfo info1 = TestSceneHelper.CreateTestCharacterInfo(
                belong: CharacterBelong.Enemy, feature: CharacterFeature.Minion, maxHp: 100);
            GameObject char1 = TestSceneHelper.CreateBaseCharacterObject(info1, Vector3.zero);
            DamageReceiver receiver1 = char1.AddComponent<DamageReceiver>();
            _spawnedObjects.Add(char1);

            CharacterInfo info2 = TestSceneHelper.CreateTestCharacterInfo(
                belong: CharacterBelong.Enemy, feature: CharacterFeature.Minion, maxHp: 100);
            GameObject char2 = TestSceneHelper.CreateBaseCharacterObject(info2, new Vector3(3, 0, 0));
            char2.AddComponent<DamageReceiver>();
            _spawnedObjects.Add(char2);

            yield return null; // Start

            BaseCharacter bc1 = char1.GetComponent<BaseCharacter>();
            BaseCharacter bc2 = char2.GetComponent<BaseCharacter>();

            // char1にのみダメージを適用
            DamageData data = new DamageData
            {
                attackerHash = 0,
                defenderHash = bc1.ObjectHash,
                damage = new ElementalStatus { slash = 30 },
                motionValue = 1.0f,
                attackElement = Element.Slash,
                feature = AttackFeature.Light
            };

            receiver1.ReceiveDamage(data);

            int hp1 = GameManager.Data.GetVitals(bc1.ObjectHash).currentHp;
            int hp2 = GameManager.Data.GetVitals(bc2.ObjectHash).currentHp;

            Assert.Less(hp1, 100, "Damaged character HP should be reduced");
            Assert.AreEqual(100, hp2, "Undamaged character HP should remain full");
        }

        [UnityTest]
        public IEnumerator MultiCharacter_SpawnAfterDestroy_NewCharacterRegistersCorrectly()
        {
            // 1体目を生成して破棄
            GameObject char1 = SpawnCharacter(100, Vector3.zero);
            yield return null; // Start

            BaseCharacter bc1 = char1.GetComponent<BaseCharacter>();
            int hash1 = bc1.ObjectHash;

            UnityEngine.Object.DestroyImmediate(char1);
            _spawnedObjects.Remove(char1);
            yield return null;

            Assert.IsFalse(GameManager.Data.TryGetValue(hash1, out int _),
                "First character should be unregistered");

            // 2体目を生成
            GameObject char2 = SpawnCharacter(200, Vector3.zero);
            yield return null; // Start

            BaseCharacter bc2 = char2.GetComponent<BaseCharacter>();
            int hash2 = bc2.ObjectHash;

            Assert.IsTrue(GameManager.Data.TryGetValue(hash2, out int _),
                "New character should be registered successfully");
            Assert.AreEqual(200, GameManager.Data.GetVitals(hash2).maxHp,
                "New character should have correct maxHp");
        }
    }
}
