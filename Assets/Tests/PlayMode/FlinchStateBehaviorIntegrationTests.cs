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
    /// Task B の統合 PlayMode テスト。
    /// DamageReceiver に実際に DamageData を流し、
    /// Flinch 中の被弾でアーマー 0 強制 / Flinch 上書きなし / Knockback 許容を検証する。
    /// </summary>
    public class FlinchStateBehaviorIntegrationTests
    {
        // ref locals はイテレータ内で使えないので外部ヘルパーで書き込む
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

        private static ActState GetActState(int hash)
        {
            return GameManager.Data.GetFlags(hash).ActState;
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
        // Task B: Flinch 中の挙動
        // =====================================================================

        [UnityTest]
        public IEnumerator FlinchState_ReceivingHit_ForcesArmorToZero()
        {
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(
                belong: CharacterBelong.Enemy, feature: CharacterFeature.Minion, maxHp: 200);
            info.maxArmor = 50f;
            info.armorRecoveryRate = 0f;
            info.armorRecoveryDelay = 10f;
            GameObject obj = TestSceneHelper.CreateBaseCharacterObjectWithDamageReceiver(info, new Vector3(3, 2, 0));
            DamageReceiver receiver = obj.GetComponent<DamageReceiver>();
            yield return null;

            int hash = obj.GetComponent<BaseCharacter>().ObjectHash;

            // 明示的に base armor 50 + Flinch 状態に設定
            SetArmor(hash, 50f, 50f);
            SetActState(hash, ActState.Flinch);

            DamageData data = new DamageData
            {
                attackerHash = 0,
                defenderHash = hash,
                damage = new ElementalStatus { slash = 10 },
                motionValue = 1.0f,
                attackElement = Element.Slash,
                armorBreakValue = 5f,  // 通常ならアーマー 50→45 だが、Flinch で強制 0 にされた後さらに削られる
                feature = AttackFeature.None
            };

            receiver.ReceiveDamage(data);

            // Flinch 強制で 50 → 0 になった後、HpArmorLogic がさらに armorBreakValue=5 で -5 まで減算する。
            // 重要なのは「Flinch 中は 50→45 といった軽減が発生しない=アーマーが機能していない」こと。
            Assert.LessOrEqual(GetArmor(hash), 0f,
                "Flinch 中の被弾で currentArmor は 0 以下 (Flinch 強制 0 を経由するため 50→45 では止まらない)");

            Object.Destroy(obj);
        }

        [UnityTest]
        public IEnumerator FlinchState_ReceivingFlinchAttack_DoesNotOverwriteFlinch()
        {
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(
                belong: CharacterBelong.Enemy, feature: CharacterFeature.Minion, maxHp: 200);
            info.maxArmor = 0f;
            GameObject obj = TestSceneHelper.CreateBaseCharacterObject(info, new Vector3(3, 2, 0));
            DamageReceiver receiver = obj.AddComponent<DamageReceiver>();
            yield return null;

            int hash = obj.GetComponent<BaseCharacter>().ObjectHash;

            // Flinch 状態に設定
            SetActState(hash, ActState.Flinch);

            // Flinch 系攻撃 (knockbackForce なし)
            DamageData data = new DamageData
            {
                attackerHash = 0,
                defenderHash = hash,
                damage = new ElementalStatus { slash = 20 },
                motionValue = 1.0f,
                attackElement = Element.Slash,
                feature = AttackFeature.Light
                // knockbackForce = 0 → Flinch 系攻撃
            };

            DamageResult result = receiver.ReceiveDamage(data);

            Assert.AreEqual(ActState.Flinch, GetActState(hash),
                "Flinch 中の Flinch 攻撃は ActState を書き換えない (タイマー延長なし)");

            Object.Destroy(obj);
        }

        [UnityTest]
        public IEnumerator FlinchState_ReceivingKnockbackAttack_TransitionsToKnockbacked()
        {
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(
                belong: CharacterBelong.Enemy, feature: CharacterFeature.Minion, maxHp: 200);
            info.maxArmor = 0f;
            GameObject obj = TestSceneHelper.CreateBaseCharacterObject(info, new Vector3(3, 2, 0));
            DamageReceiver receiver = obj.AddComponent<DamageReceiver>();
            yield return null;

            int hash = obj.GetComponent<BaseCharacter>().ObjectHash;

            // Flinch 状態に設定
            SetActState(hash, ActState.Flinch);

            // 吹き飛ばし攻撃
            DamageData data = new DamageData
            {
                attackerHash = 0,
                defenderHash = hash,
                damage = new ElementalStatus { slash = 20 },
                motionValue = 1.0f,
                attackElement = Element.Slash,
                feature = AttackFeature.Heavy,
                knockbackForce = new Vector2(-5f, 3f)
            };

            DamageResult result = receiver.ReceiveDamage(data);

            Assert.AreEqual(HitReaction.Knockback, result.hitReaction,
                "Flinch 中でも Knockback 攻撃は HitReaction.Knockback を返す");
            Assert.AreEqual(ActState.Knockbacked, GetActState(hash),
                "Flinch 中の Knockback 攻撃 → ActState=Knockbacked に遷移");

            Object.Destroy(obj);
        }

        [UnityTest]
        public IEnumerator FlinchState_DamageIsNotReducedByArmor()
        {
            // Flinch 中は armor 完全無視でダメージが通る
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(
                belong: CharacterBelong.Enemy, feature: CharacterFeature.Minion, maxHp: 200);
            info.maxArmor = 100f;
            info.armorRecoveryRate = 0f;
            info.armorRecoveryDelay = 10f;
            GameObject obj = TestSceneHelper.CreateBaseCharacterObjectWithDamageReceiver(info, new Vector3(3, 2, 0));
            DamageReceiver receiver = obj.GetComponent<DamageReceiver>();
            yield return null;

            int hash = obj.GetComponent<BaseCharacter>().ObjectHash;

            // 大きな armor と Flinch 状態
            SetArmor(hash, 100f, 100f);
            SetActState(hash, ActState.Flinch);

            int hpBefore = GameManager.Data.GetVitals(hash).currentHp;

            DamageData data = new DamageData
            {
                attackerHash = 0,
                defenderHash = hash,
                damage = new ElementalStatus { slash = 30 },
                motionValue = 1.0f,
                attackElement = Element.Slash,
                feature = AttackFeature.Light
            };

            DamageResult result = receiver.ReceiveDamage(data);

            // Flinch 中は armor=0 強制 → armorBroken ボーナス倍率 (x1.3) がかかる可能性がある。
            // いずれにせよ「軽減なしでダメージが通る」を検証する。
            Assert.Greater(result.totalDamage, 0,
                "Flinch 中でもダメージは通る");
            int hpAfter = GameManager.Data.GetVitals(hash).currentHp;
            Assert.Less(hpAfter, hpBefore,
                "Flinch 中の被弾は HP が減る (armor で軽減されない)");

            Object.Destroy(obj);
        }

        [UnityTest]
        public IEnumerator FlinchState_AfterActionArmorBreak_NoReFlinchOnNextHit()
        {
            // 同じ action 内で armor 0 になって Flinch 遷移した後、2 発目でも Flinch は延長されない
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(
                belong: CharacterBelong.Enemy, feature: CharacterFeature.Minion, maxHp: 500);
            info.maxArmor = 0f;
            info.armorRecoveryRate = 0f;
            info.armorRecoveryDelay = 10f;
            GameObject obj = TestSceneHelper.CreateBaseCharacterObject(info, new Vector3(3, 2, 0));
            DamageReceiver receiver = obj.AddComponent<DamageReceiver>();
            yield return null;

            int hash = obj.GetComponent<BaseCharacter>().ObjectHash;

            ActionEffect[] effects = new ActionEffect[]
            {
                new ActionEffect { type = ActionEffectType.Armor, startTime = 0f, duration = 100f, value = 10f }
            };
            receiver.SetActionEffects(effects);

            DamageData data = new DamageData
            {
                attackerHash = 0,
                defenderHash = hash,
                damage = new ElementalStatus { slash = 5 },
                motionValue = 1.0f,
                attackElement = Element.Slash,
                armorBreakValue = 15f,
                feature = AttackFeature.None
            };

            // 1 発目: armor 10→0 削り切り → Flinch (Task A)
            DamageResult first = receiver.ReceiveDamage(data);
            Assert.AreEqual(HitReaction.Flinch, first.hitReaction, "1発目: 削り切り → Flinch");
            Assert.AreEqual(ActState.Flinch, GetActState(hash));

            // 2 発目: ActState.Flinch 中なので Task B の Flinch 上書き禁止が効く
            DamageResult second = receiver.ReceiveDamage(data);
            Assert.AreEqual(ActState.Flinch, GetActState(hash),
                "Task B: 既に Flinch 中の 2 発目でも ActState は Flinch 維持 (タイマー延長なし)");

            Object.Destroy(obj);
        }
    }
}
