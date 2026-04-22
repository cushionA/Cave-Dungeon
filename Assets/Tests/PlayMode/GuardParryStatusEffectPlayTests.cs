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
    /// ガード / ジャストガード / 状態異常の PlayMode 結合テスト。
    /// DamageReceiver → GuardJudgmentLogic → StatusEffectManager の呼び出し経路を
    /// 実コンポーネント構成で通し、結果 (GuardResult / ダメージ量 / スタミナ・アーマー) が
    /// 既存ロジックを正しく経由していることを検証する。
    /// </summary>
    public class GuardParryStatusEffectPlayTests
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

        // =========================================================================
        // ガード成立時のダメージ軽減
        // =========================================================================

        [UnityTest]
        public IEnumerator GuardParryStatus_NormalGuard_ReducesDamageComparedToUnguarded()
        {
            CharacterInfo targetInfo = TestSceneHelper.CreateTestCharacterInfo(
                belong: CharacterBelong.Ally, feature: CharacterFeature.Player, maxHp: 1000);
            targetInfo.maxStamina = 1000f;
            GameObject targetObj = TestSceneHelper.CreateBaseCharacterObject(targetInfo, new Vector3(3, 2, 0));
            DamageReceiver receiver = targetObj.AddComponent<DamageReceiver>();
            yield return null;

            int targetHash = targetObj.GetComponent<BaseCharacter>().ObjectHash;

            // CombatStats.guardStats を直接設定（CharacterInfo からの自動反映経路ではないため）
            // - guardStrength を十分大きくしてスタミナ削りを 0 に
            // - slashCut = 0.5 で斬属性ダメージを半減
            // - guardDirection = Both で前後どちらからの攻撃もガード成立
            ref CombatStats combat = ref GameManager.Data.GetCombatStats(targetHash);
            combat.guardStats = new GuardStats
            {
                guardStrength = 9999f,
                slashCut = 0.5f,
                guardDirection = GuardDirection.Both,
            };

            // JustGuard 窓を外した状態（連続JG窓も無し）になるよう、十分な時間ガードを続ける。
            receiver.SetGuarding(true);
            for (int i = 0; i < 30; i++)
            {
                yield return null;  // k_JustGuardWindow=0.1f を越えた時間だけ経過させる
            }

            DamageData data = new DamageData
            {
                attackerHash = 0,
                defenderHash = targetHash,
                damage = new ElementalStatus { slash = 100 },
                motionValue = 1.0f,
                attackElement = Element.Slash,
                knockbackForce = new Vector2(-5f, 0f),
                feature = AttackFeature.None,
                armorBreakValue = 0f,
            };

            int hpBefore = GameManager.Data.GetVitals(targetHash).currentHp;
            DamageResult result = receiver.ReceiveDamage(data);
            int hpAfter = GameManager.Data.GetVitals(targetHash).currentHp;

            Assert.AreEqual(GuardResult.Guarded, result.guardResult,
                "JustGuard 窓外でのガードは通常 Guarded になる");
            Assert.Greater(result.totalDamage, 0, "通常ガードはダメージ 0 ではない");
            Assert.Less(hpBefore - hpAfter, 100,
                "属性別カットによりダメージ軽減されている（< 100）");

            Object.Destroy(targetObj);
        }

        // =========================================================================
        // JustGuard 成立時: ダメージ 0 + スタミナ/アーマー回復
        // =========================================================================

        [UnityTest]
        public IEnumerator GuardParryStatus_JustGuard_ZeroDamageAndRecoversStaminaAndArmor()
        {
            CharacterInfo targetInfo = TestSceneHelper.CreateTestCharacterInfo(
                belong: CharacterBelong.Ally, feature: CharacterFeature.Player, maxHp: 500);
            targetInfo.maxStamina = 100f;
            targetInfo.maxArmor = 100f;
            targetInfo.armorRecoveryRate = 0f;
            targetInfo.armorRecoveryDelay = 999f;  // 自然回復で値が動かないようにする
            GameObject targetObj = TestSceneHelper.CreateBaseCharacterObject(targetInfo, new Vector3(3, 2, 0));
            DamageReceiver receiver = targetObj.AddComponent<DamageReceiver>();
            yield return null;

            int targetHash = targetObj.GetComponent<BaseCharacter>().ObjectHash;

            // スタミナ・アーマーを中間値に設定して回復余地を確保
            ref CharacterVitals v = ref GameManager.Data.GetVitals(targetHash);
            v.currentStamina = 50f;
            v.maxStamina = 100f;
            v.currentArmor = 50f;
            v.maxArmor = 100f;

            int hpBefore = v.currentHp;

            // ガード開始直後 (guardTimeSinceStart=0) → JustGuard 窓内
            receiver.SetGuarding(true);

            DamageData data = new DamageData
            {
                attackerHash = 0,
                defenderHash = targetHash,
                damage = new ElementalStatus { slash = 999 },
                motionValue = 1.0f,
                attackElement = Element.Slash,
                knockbackForce = new Vector2(-5f, 0f),
                feature = AttackFeature.None,
            };

            DamageResult result = receiver.ReceiveDamage(data);

            Assert.AreEqual(GuardResult.JustGuard, result.guardResult, "JustGuard 成立");
            Assert.AreEqual(0, result.totalDamage, "JustGuard はダメージ 0");

            ref CharacterVitals vAfter = ref GameManager.Data.GetVitals(targetHash);
            Assert.AreEqual(hpBefore, vAfter.currentHp, "HP は減らない");
            Assert.Greater(vAfter.currentStamina, 50f,
                "ジャストガード時のスタミナ回復（+k_JustGuardStaminaRecovery）");
            Assert.Greater(vAfter.currentArmor, 50f,
                "ジャストガード時のアーマー回復（+k_JustGuardArmorRecovery）");

            Object.Destroy(targetObj);
        }

        // =========================================================================
        // Unparriable 攻撃はガード不能 → NoGuard
        // =========================================================================

        [UnityTest]
        public IEnumerator GuardParryStatus_UnparriableAttack_ReturnsNoGuardEvenIfGuarding()
        {
            CharacterInfo targetInfo = TestSceneHelper.CreateTestCharacterInfo(
                belong: CharacterBelong.Ally, feature: CharacterFeature.Player, maxHp: 1000);
            GameObject targetObj = TestSceneHelper.CreateBaseCharacterObject(targetInfo, new Vector3(3, 2, 0));
            DamageReceiver receiver = targetObj.AddComponent<DamageReceiver>();
            yield return null;

            int targetHash = targetObj.GetComponent<BaseCharacter>().ObjectHash;

            // ガード押下中 + JustGuard 窓内 でも Unparriable 属性なら NoGuard になる
            receiver.SetGuarding(true);

            DamageData data = new DamageData
            {
                attackerHash = 0,
                defenderHash = targetHash,
                damage = new ElementalStatus { slash = 100 },
                motionValue = 1.0f,
                attackElement = Element.Slash,
                knockbackForce = new Vector2(-5f, 0f),
                feature = AttackFeature.Unparriable,
            };

            DamageResult result = receiver.ReceiveDamage(data);

            Assert.AreEqual(GuardResult.NoGuard, result.guardResult,
                "Unparriable はガード押下中でも NoGuard");
            Assert.Greater(result.totalDamage, 0, "NoGuard なのでダメージが通る");

            Object.Destroy(targetObj);
        }

        // =========================================================================
        // 状態異常蓄積 → 発動 (DamageReceiver.ReceiveDamage → StatusEffectManager.Accumulate 経路)
        // =========================================================================

        [UnityTest]
        public IEnumerator GuardParryStatus_StatusEffectAccumulation_ReachesThresholdAndActivates()
        {
            CharacterInfo targetInfo = TestSceneHelper.CreateTestCharacterInfo(
                belong: CharacterBelong.Enemy, feature: CharacterFeature.Minion, maxHp: 1000);
            GameObject targetObj = TestSceneHelper.CreateBaseCharacterObject(targetInfo, new Vector3(3, 2, 0));
            DamageReceiver receiver = targetObj.AddComponent<DamageReceiver>();

            StatusEffectManager manager = new StatusEffectManager();
            receiver.SetStatusEffectManager(manager);
            yield return null;

            int targetHash = targetObj.GetComponent<BaseCharacter>().ObjectHash;

            DamageData data = new DamageData
            {
                attackerHash = 0,
                defenderHash = targetHash,
                damage = new ElementalStatus { slash = 1 },
                motionValue = 1.0f,
                attackElement = Element.Slash,
                feature = AttackFeature.None,
                statusEffectInfo = new StatusEffectInfo
                {
                    effect = StatusEffectId.Poison,
                    accumulateValue = StatusEffectManager.k_DefaultThreshold,  // 閾値一撃
                    duration = 3f,
                    tickDamage = 1f,
                    tickInterval = 1f,
                },
            };

            DamageResult result = receiver.ReceiveDamage(data);

            Assert.IsTrue(manager.IsActive(StatusEffectId.Poison),
                "蓄積値が閾値に達した瞬間に毒状態が発動する");
            Assert.AreEqual(StatusEffectId.Poison, result.appliedEffect,
                "DamageResult.appliedEffect に発動した効果 ID が反映される");

            Object.Destroy(targetObj);
        }

        // =========================================================================
        // 状態異常の効果時間経過で解除
        // =========================================================================

        [UnityTest]
        public IEnumerator GuardParryStatus_StatusEffectExpires_AfterDurationPasses()
        {
            CharacterInfo targetInfo = TestSceneHelper.CreateTestCharacterInfo(
                belong: CharacterBelong.Enemy, feature: CharacterFeature.Minion, maxHp: 1000);
            GameObject targetObj = TestSceneHelper.CreateBaseCharacterObject(targetInfo, new Vector3(3, 2, 0));
            DamageReceiver receiver = targetObj.AddComponent<DamageReceiver>();

            StatusEffectManager manager = new StatusEffectManager();
            receiver.SetStatusEffectManager(manager);
            yield return null;

            int targetHash = targetObj.GetComponent<BaseCharacter>().ObjectHash;

            DamageData data = new DamageData
            {
                attackerHash = 0,
                defenderHash = targetHash,
                damage = new ElementalStatus { slash = 1 },
                motionValue = 1.0f,
                attackElement = Element.Slash,
                feature = AttackFeature.None,
                statusEffectInfo = new StatusEffectInfo
                {
                    effect = StatusEffectId.Burn,
                    accumulateValue = StatusEffectManager.k_DefaultThreshold,
                    duration = 1.0f,
                    tickDamage = 0f,
                    tickInterval = 2f,
                },
            };

            receiver.ReceiveDamage(data);
            Assert.IsTrue(manager.IsActive(StatusEffectId.Burn), "発症直後は有効");

            // 効果時間(1秒)を超えるまで Tick して解除を促す
            manager.Tick(0.5f);
            Assert.IsTrue(manager.IsActive(StatusEffectId.Burn),
                "半分経過時点ではまだ有効");

            manager.Tick(0.6f);
            Assert.IsFalse(manager.IsActive(StatusEffectId.Burn),
                "効果時間経過後は自然解除される");

            Object.Destroy(targetObj);
        }
    }
}
