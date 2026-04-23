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
    /// Task A の統合 PlayMode テスト。
    /// DamageReceiver に実際に DamageData を流し、
    /// 行動アーマーを削り切った瞬間に HitReaction.Flinch に上書きされ ActState.Flinch に遷移すること、
    /// SuperArmor 中は保護されて ActState が書き換わらないことを検証する。
    /// </summary>
    public class ActionArmorBreakFlinchIntegrationTests
    {
        // ref locals はイテレータ内で使えないので外部ヘルパーで書き込む
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
        // Task A: 行動アーマー削り切り → Flinch 遷移 + 行動中断
        // =====================================================================

        [UnityTest]
        public IEnumerator ActionArmor10_DamageBreaks_ActStateBecomesFlinch()
        {
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(
                belong: CharacterBelong.Enemy, feature: CharacterFeature.Minion, maxHp: 200);
            info.maxArmor = 0f;
            info.armorRecoveryRate = 0f;
            info.armorRecoveryDelay = 10f;
            GameObject obj = TestSceneHelper.CreateBaseCharacterObject(info, new Vector3(3, 2, 0));
            DamageReceiver receiver = obj.AddComponent<DamageReceiver>();
            yield return null;

            int hash = obj.GetComponent<BaseCharacter>().ObjectHash;

            // actionArmor 10 付与
            ActionEffect[] effects = new ActionEffect[]
            {
                new ActionEffect { type = ActionEffectType.Armor, startTime = 0f, duration = 100f, value = 10f }
            };
            receiver.SetActionEffects(effects);

            // ActState 初期値は Neutral
            Assert.AreEqual(ActState.Neutral, GetActState(hash));

            // armorBreakValue=15 でヒット → actionArmor 10 → 0 に削られる
            DamageData data = new DamageData
            {
                attackerHash = 0,
                defenderHash = hash,
                damage = new ElementalStatus { slash = 10 },
                motionValue = 1.0f,
                attackElement = Element.Slash,
                armorBreakValue = 15f,
                feature = AttackFeature.None
            };

            DamageResult result = receiver.ReceiveDamage(data);

            Assert.AreEqual(HitReaction.Flinch, result.hitReaction,
                "アーマー削り切り瞬間は HitReaction が None から Flinch に上書きされる");
            Assert.AreEqual(ActState.Flinch, GetActState(hash),
                "アーマー削り切り → ActState=Flinch に遷移");

            Object.Destroy(obj);
        }

        [UnityTest]
        public IEnumerator ActionArmor10_WithSuperArmor_DamageBreaks_ActStateStaysNeutral()
        {
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(
                belong: CharacterBelong.Enemy, feature: CharacterFeature.Minion, maxHp: 200);
            info.maxArmor = 0f;
            info.armorRecoveryRate = 0f;
            info.armorRecoveryDelay = 10f;
            GameObject obj = TestSceneHelper.CreateBaseCharacterObject(info, new Vector3(3, 2, 0));
            DamageReceiver receiver = obj.AddComponent<DamageReceiver>();
            yield return null;

            int hash = obj.GetComponent<BaseCharacter>().ObjectHash;

            // actionArmor 10 + SuperArmor を同時に付与
            ActionEffect[] effects = new ActionEffect[]
            {
                new ActionEffect { type = ActionEffectType.Armor, startTime = 0f, duration = 100f, value = 10f },
                new ActionEffect { type = ActionEffectType.SuperArmor, startTime = 0f, duration = 100f, value = 0f }
            };
            receiver.SetActionEffects(effects);

            SetActState(hash, ActState.Attacking);  // 攻撃中を想定

            DamageData data = new DamageData
            {
                attackerHash = 0,
                defenderHash = hash,
                damage = new ElementalStatus { slash = 10 },
                motionValue = 1.0f,
                attackElement = Element.Slash,
                armorBreakValue = 15f,
                feature = AttackFeature.None
            };

            DamageResult result = receiver.ReceiveDamage(data);

            // SuperArmor 中は Flinch しない = HitReaction.None
            Assert.AreEqual(HitReaction.None, result.hitReaction,
                "SuperArmor 中は armor が削られても HitReaction=None");
            Assert.AreEqual(ActState.Attacking, GetActState(hash),
                "SuperArmor 中は ActState が上書きされず Attacking を維持");

            Object.Destroy(obj);
        }
    }
}
