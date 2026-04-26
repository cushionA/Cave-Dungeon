using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Game.Core;
using Game.Runtime;
using CharacterInfo = Game.Core.CharacterInfo;

namespace Game.Tests.PlayMode
{
    /// <summary>
    /// Flinch 解除時のアーマー満タンリセット結合テスト (FUTURE_TASKS L528)。
    /// DamageReceiver.Update() が ActState の Flinch → 非 Flinch 遷移を検出し、
    /// SoA の currentArmor を maxArmor へ復帰させることを検証する。
    /// </summary>
    public class FlinchArmorResetIntegrationTests
    {
        private static void SetArmor(int hash, float current, float max)
        {
            ref CharacterVitals v = ref GameManager.Data.GetVitals(hash);
            v.currentArmor = current;
            v.maxArmor = max;
        }

        private static float GetArmor(int hash)
        {
            return GameManager.Data.GetVitals(hash).currentArmor;
        }

        private static void SetActState(int hash, ActState state)
        {
            ref CharacterFlags f = ref GameManager.Data.GetFlags(hash);
            f.ActState = state;
        }

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

        // =====================================================================
        // Flinch 解除瞬間の満タンリセット
        // =====================================================================

        [UnityTest]
        public IEnumerator FlinchExit_FromFlinchToNeutral_ResetsArmorToMax()
        {
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(
                belong: CharacterBelong.Enemy, feature: CharacterFeature.Minion, maxHp: 200);
            info.maxArmor = 50f;
            info.armorRecoveryRate = 0f;  // 自然回復で結果を汚さない
            info.armorRecoveryDelay = 100f;
            GameObject obj = TestSceneHelper.CreateBaseCharacterObjectWithDamageReceiver(info, new Vector3(3, 2, 0));
            yield return null;

            int hash = obj.GetComponent<BaseCharacter>().ObjectHash;

            // Flinch 突入: armor=0 強制 + ActState=Flinch
            SetArmor(hash, 0f, 50f);
            SetActState(hash, ActState.Flinch);
            yield return null;  // Update が走り _previousActState = Flinch にする

            Assert.AreEqual(0f, GetArmor(hash), "Flinch 中は armor=0");

            // Flinch 解除
            SetActState(hash, ActState.Neutral);
            yield return null;  // Update が遷移を検出して armor をリセット

            Assert.AreEqual(50f, GetArmor(hash),
                "Flinch → Neutral 遷移で currentArmor が maxArmor に満タン復帰");

            Object.Destroy(obj);
        }

        [UnityTest]
        public IEnumerator FlinchExit_StayInFlinch_ArmorNotReset()
        {
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(
                belong: CharacterBelong.Enemy, feature: CharacterFeature.Minion, maxHp: 200);
            info.maxArmor = 50f;
            info.armorRecoveryRate = 0f;
            info.armorRecoveryDelay = 100f;
            GameObject obj = TestSceneHelper.CreateBaseCharacterObjectWithDamageReceiver(info, new Vector3(3, 2, 0));
            yield return null;

            int hash = obj.GetComponent<BaseCharacter>().ObjectHash;

            SetArmor(hash, 0f, 50f);
            SetActState(hash, ActState.Flinch);
            yield return null;
            yield return null;  // Flinch 継続フレーム

            Assert.AreEqual(0f, GetArmor(hash),
                "Flinch 継続中はリセットされない");

            Object.Destroy(obj);
        }

        [UnityTest]
        public IEnumerator FlinchExit_FromFlinchToKnockback_AlsoResetsArmor()
        {
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(
                belong: CharacterBelong.Enemy, feature: CharacterFeature.Minion, maxHp: 200);
            info.maxArmor = 30f;
            info.armorRecoveryRate = 0f;
            info.armorRecoveryDelay = 100f;
            GameObject obj = TestSceneHelper.CreateBaseCharacterObjectWithDamageReceiver(info, new Vector3(3, 2, 0));
            yield return null;

            int hash = obj.GetComponent<BaseCharacter>().ObjectHash;

            SetArmor(hash, 0f, 30f);
            SetActState(hash, ActState.Flinch);
            yield return null;

            // Flinch → Knockbacked (Flinch 中の追撃で吹き飛ばし)
            SetActState(hash, ActState.Knockbacked);
            yield return null;

            Assert.AreEqual(30f, GetArmor(hash),
                "Flinch → Knockbacked への遷移でも armor がリセットされる (Flinch 解除条件は state 不問)");

            Object.Destroy(obj);
        }

        [UnityTest]
        public IEnumerator FlinchExit_NeverEnteredFlinch_NoArmorChange()
        {
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(
                belong: CharacterBelong.Enemy, feature: CharacterFeature.Minion, maxHp: 200);
            info.maxArmor = 50f;
            info.armorRecoveryRate = 0f;
            info.armorRecoveryDelay = 100f;
            GameObject obj = TestSceneHelper.CreateBaseCharacterObjectWithDamageReceiver(info, new Vector3(3, 2, 0));
            yield return null;

            int hash = obj.GetComponent<BaseCharacter>().ObjectHash;

            // Flinch を経由せず、armor を 25 に手動設定 (途中ダメージを想定)
            SetArmor(hash, 25f, 50f);
            SetActState(hash, ActState.Neutral);
            yield return null;
            yield return null;

            Assert.AreEqual(25f, GetArmor(hash),
                "Flinch を経由しないなら armor は変更されない (回復は別経路)");

            Object.Destroy(obj);
        }
    }
}
