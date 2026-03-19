using System.Collections;
using NUnit.Framework;
using R3;
using UnityEngine;
using UnityEngine.TestTools;
using Game.Core;
using Game.Runtime;
using CharacterInfo = Game.Core.CharacterInfo;

namespace Game.Tests.PlayMode
{
    public class DamageIntegrationTests
    {
        [UnitySetUp]
        public IEnumerator Setup()
        {
            TestSceneHelper.CreateGameManager();
            TestSceneHelper.CreateGround();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            TestSceneHelper.Cleanup();
            yield return null;
        }

        [UnityTest]
        public IEnumerator DamageReceiver_ReceiveDamage_ReducesHp()
        {
            // ターゲットキャラ作成
            CharacterInfo targetInfo = TestSceneHelper.CreateTestCharacterInfo(
                belong: CharacterBelong.Enemy, feature: CharacterFeature.Minion, maxHp: 100);
            GameObject targetObj = TestSceneHelper.CreateBaseCharacterObject(targetInfo, new Vector3(3, 2, 0));
            DamageReceiver receiver = targetObj.AddComponent<DamageReceiver>();
            yield return null; // Start

            BaseCharacter targetChar = targetObj.GetComponent<BaseCharacter>();
            int targetHash = targetChar.ObjectHash;

            // ダメージを直接適用
            DamageData data = new DamageData
            {
                attackerHash = 0,
                defenderHash = targetHash,
                damage = new ElementalStatus { slash = 20 },
                motionValue = 1.0f,
                attackElement = Element.Slash,
                feature = AttackFeature.Light
            };

            DamageResult result = receiver.ReceiveDamage(data);

            Assert.IsTrue(result.totalDamage > 0, "Should deal damage");
            int currentHp = GameManager.Data.GetVitals(targetHash).currentHp;
            Assert.Less(currentHp, 100, "HP should be reduced");

            Object.DestroyImmediate(targetObj);
        }

        [UnityTest]
        public IEnumerator DamageReceiver_LethalDamage_FiresDeathEvent()
        {
            CharacterInfo targetInfo = TestSceneHelper.CreateTestCharacterInfo(
                belong: CharacterBelong.Enemy, feature: CharacterFeature.Minion, maxHp: 1);
            GameObject targetObj = TestSceneHelper.CreateBaseCharacterObject(targetInfo, new Vector3(3, 2, 0));
            DamageReceiver receiver = targetObj.AddComponent<DamageReceiver>();
            yield return null;

            bool deathFired = false;
            GameManager.Events.OnCharacterDeath.Subscribe(e => deathFired = true);

            DamageData data = new DamageData
            {
                attackerHash = 0,
                defenderHash = targetObj.GetComponent<BaseCharacter>().ObjectHash,
                damage = new ElementalStatus { slash = 999 },
                motionValue = 1.0f,
                attackElement = Element.Slash
            };

            DamageResult result = receiver.ReceiveDamage(data);

            Assert.IsTrue(result.isKill, "Should be a killing blow");
            Assert.IsTrue(deathFired, "Death event should fire");

            Object.DestroyImmediate(targetObj);
        }
    }
}
