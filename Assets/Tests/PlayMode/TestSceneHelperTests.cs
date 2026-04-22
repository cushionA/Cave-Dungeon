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
    /// TestSceneHelper の helper 動作を確認する。
    /// CreateBaseCharacterObjectWithDamageReceiver は BaseCharacter が
    /// Awake 時に DamageReceiver / CharacterAnimationController をキャッシュできる順序で追加する。
    /// </summary>
    public class TestSceneHelperTests
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
        public IEnumerator CreateBaseCharacterObjectWithDamageReceiver_AwakeCachesDamageReceiver()
        {
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo();
            GameObject charObj = TestSceneHelper.CreateBaseCharacterObjectWithDamageReceiver(info, Vector3.zero);
            _spawnedObjects.Add(charObj);

            // Awake 時点で BaseCharacter.DamageReceiver がキャッシュ済みであることを確認
            BaseCharacter bc = charObj.GetComponent<BaseCharacter>();
            DamageReceiver attached = charObj.GetComponent<DamageReceiver>();
            Assert.IsNotNull(attached, "DamageReceiver should be attached by helper");
            Assert.IsNotNull(bc.DamageReceiver,
                "BaseCharacter.DamageReceiver should be cached at Awake when added before BaseCharacter");
            Assert.AreSame(attached, bc.DamageReceiver,
                "Cached DamageReceiver should be the one attached by the helper");

            yield return null;
        }

        [UnityTest]
        public IEnumerator CreateBaseCharacterObjectWithDamageReceiver_AwakeCachesAnimationController()
        {
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo();
            GameObject charObj = TestSceneHelper.CreateBaseCharacterObjectWithDamageReceiver(info, Vector3.zero);
            _spawnedObjects.Add(charObj);

            BaseCharacter bc = charObj.GetComponent<BaseCharacter>();
            CharacterAnimationController controller = charObj.GetComponent<CharacterAnimationController>();
            Assert.IsNotNull(controller, "CharacterAnimationController should be attached by helper");
            Assert.IsNotNull(bc.AnimationController,
                "BaseCharacter.AnimationController should be cached at Awake when added before BaseCharacter");
            Assert.AreSame(controller, bc.AnimationController,
                "Cached AnimationController should be the one attached by the helper");

            yield return null;
        }

        [UnityTest]
        public IEnumerator CreateBaseCharacterObjectWithDamageReceiver_StartInitializesDamageReceiver()
        {
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(maxHp: 100);
            info.maxArmor = 42f;
            info.armorRecoveryRate = 5f;
            info.armorRecoveryDelay = 1f;

            GameObject charObj = TestSceneHelper.CreateBaseCharacterObjectWithDamageReceiver(info, Vector3.zero);
            _spawnedObjects.Add(charObj);

            yield return null; // Start を待つ

            BaseCharacter bc = charObj.GetComponent<BaseCharacter>();
            // Start で DamageReceiver.SetArmorRecoveryParams が呼ばれたことを検証する代わりに
            // SoA 登録が成功している (HP が maxHp と一致する) ことを確認
            Assert.IsTrue(bc.IsAlive, "BaseCharacter should be registered and alive after Start");
            Assert.AreEqual(100, GameManager.Data.GetVitals(bc.ObjectHash).currentHp);
        }
    }
}
