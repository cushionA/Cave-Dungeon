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

            Object.Destroy(targetObj);
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

            Object.Destroy(targetObj);
        }

        // ===== 新仕様: JustGuard完全0 =====

        [UnityTest]
        public IEnumerator DamageReceiver_JustGuard_DamageIsZero()
        {
            CharacterInfo targetInfo = TestSceneHelper.CreateTestCharacterInfo(
                belong: CharacterBelong.Ally, feature: CharacterFeature.Player, maxHp: 100);
            GameObject targetObj = TestSceneHelper.CreateBaseCharacterObject(targetInfo, new Vector3(3, 2, 0));
            DamageReceiver receiver = targetObj.AddComponent<DamageReceiver>();
            yield return null;

            int targetHash = targetObj.GetComponent<BaseCharacter>().ObjectHash;
            int hpBefore = GameManager.Data.GetVitals(targetHash).currentHp;

            // ガード開始直後(guardTime=0) → JustGuard窓内
            receiver.SetGuarding(true);

            DamageData data = new DamageData
            {
                attackerHash = 0,
                defenderHash = targetHash,
                damage = new ElementalStatus { slash = 50 },
                motionValue = 1.0f,
                attackElement = Element.Slash,
                knockbackForce = new Vector2(-5f, 0f),  // 前方からの攻撃扱い
                feature = AttackFeature.None
            };

            DamageResult result = receiver.ReceiveDamage(data);

            Assert.AreEqual(GuardResult.JustGuard, result.guardResult, "JustGuard成立");
            Assert.AreEqual(0, result.totalDamage, "JustGuardはダメージ完全0");
            int hpAfter = GameManager.Data.GetVitals(targetHash).currentHp;
            Assert.AreEqual(hpBefore, hpAfter, "HPが1も減っていない");

            Object.Destroy(targetObj);
        }

        // ===== 新仕様: 飛翔体JustGuard時アーマー削り0 =====

        /// <summary>ref locals はイテレータ内で使えないので外部ヘルパーで書き込む。</summary>
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

        [UnityTest]
        public IEnumerator DamageReceiver_ProjectileJustGuard_DoesNotBreakArmor()
        {
            CharacterInfo targetInfo = TestSceneHelper.CreateTestCharacterInfo(
                belong: CharacterBelong.Ally, feature: CharacterFeature.Player, maxHp: 100);
            targetInfo.maxArmor = 50f;
            targetInfo.armorRecoveryRate = 0f;
            targetInfo.armorRecoveryDelay = 10f;
            GameObject targetObj = TestSceneHelper.CreateBaseCharacterObject(targetInfo, new Vector3(3, 2, 0));
            DamageReceiver receiver = targetObj.AddComponent<DamageReceiver>();
            yield return null;

            int targetHash = targetObj.GetComponent<BaseCharacter>().ObjectHash;
            // アーマー値を明示的に初期化(プール外の素直なMaxArmor開始)
            SetArmor(targetHash, 50f, 50f);
            float armorBefore = GetArmor(targetHash);

            receiver.SetGuarding(true);  // JustGuard成立

            DamageData data = new DamageData
            {
                attackerHash = 0,
                defenderHash = targetHash,
                damage = new ElementalStatus { slash = 20 },
                motionValue = 1.0f,
                attackElement = Element.Slash,
                knockbackForce = new Vector2(-1f, 0f),
                feature = AttackFeature.None,
                armorBreakValue = 999f,        // 本来ならアーマーを吹き飛ばすほどの値
                justGuardResistance = 0f,      // 近接なら全アーマー削りされる
                isProjectile = true            // 飛翔体フラグ → アーマー削り0に分岐
            };

            DamageResult result = receiver.ReceiveDamage(data);

            Assert.AreEqual(GuardResult.JustGuard, result.guardResult);
            // JustGuard回復で +10 される可能性があるので「少なくとも減ってない」を検証
            float armorAfter = GetArmor(targetHash);
            Assert.GreaterOrEqual(armorAfter, armorBefore,
                "飛翔体JustGuard時は armorBreakValue 999 でもアーマーが削れない(回復で増える可能性あり)");

            Object.Destroy(targetObj);
        }

        // ===== 新仕様: プール復帰時の連続JG窓リセット =====

        [UnityTest]
        public IEnumerator DamageReceiver_OnPoolAcquire_ResetsContinuousJustGuardState()
        {
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(
                belong: CharacterBelong.Enemy, feature: CharacterFeature.Minion, maxHp: 100);
            GameObject obj = TestSceneHelper.CreateBaseCharacterObject(info, new Vector3(3, 2, 0));
            DamageReceiver receiver = obj.AddComponent<DamageReceiver>();
            yield return null;

            int hash = obj.GetComponent<BaseCharacter>().ObjectHash;

            // Step 1: JustGuard成立で連続JG窓を開く
            receiver.SetGuarding(true);
            DamageData hit = new DamageData
            {
                attackerHash = 0,
                defenderHash = hash,
                damage = new ElementalStatus { slash = 10 },
                motionValue = 1.0f,
                attackElement = Element.Slash,
                knockbackForce = new Vector2(-1f, 0f)
            };
            DamageResult first = receiver.ReceiveDamage(hit);
            Assert.AreEqual(GuardResult.JustGuard, first.guardResult, "初手JustGuard成立で窓を開く");

            // Step 2: プール返却→取得サイクルを模倣 (ResetInternalStateを介する)
            BaseCharacter bc = obj.GetComponent<BaseCharacter>();
            bc.OnPoolReturn();
            bc.OnPoolAcquire();
            yield return null;

            // Step 3: 連続JG窓がリセットされていれば、ガード非押下状態から通常判定になる
            // ガード未押下 → NoGuard が返れば窓は無効(JustGuardが返ればバグ)
            DamageData next = new DamageData
            {
                attackerHash = 0,
                defenderHash = hash,
                damage = new ElementalStatus { slash = 10 },
                motionValue = 1.0f,
                attackElement = Element.Slash,
                knockbackForce = new Vector2(-1f, 0f)
            };
            DamageResult second = receiver.ReceiveDamage(next);

            Assert.AreEqual(GuardResult.NoGuard, second.guardResult,
                "プール復帰後はガード非押下でJustGuard成立しない(ResetInternalStateで_isGuarding/_continuousJustGuardExpireTimeがクリア)");

            Object.Destroy(obj);
        }
    }
}
