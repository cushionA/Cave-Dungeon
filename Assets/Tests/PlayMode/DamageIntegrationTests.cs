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

        /// <summary>ref locals はイテレータ内で使えないので外部ヘルパーで書き込む。</summary>
        private static void SetActState(int hash, ActState state)
        {
            ref CharacterFlags f = ref GameManager.Data.GetFlags(hash);
            f.ActState = state;
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

        // ===== B2: ActionArmor 消費累積の書き戻し =====

        [UnityTest]
        public IEnumerator DamageReceiver_ActionArmor_ConsumedAcrossMultipleHits()
        {
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(
                belong: CharacterBelong.Enemy, feature: CharacterFeature.Minion, maxHp: 200);
            info.maxArmor = 0f; // 基本アーマーは無し → 行動アーマーの消費経路を純粋検証
            info.armorRecoveryRate = 0f;
            info.armorRecoveryDelay = 10f;
            GameObject obj = TestSceneHelper.CreateBaseCharacterObject(info, new Vector3(3, 2, 0));
            DamageReceiver receiver = obj.AddComponent<DamageReceiver>();
            yield return null;

            int hash = obj.GetComponent<BaseCharacter>().ObjectHash;

            // 行動アーマー 30 を長時間有効にする
            ActionEffect[] effects = new ActionEffect[]
            {
                new ActionEffect
                {
                    type = ActionEffectType.Armor,
                    startTime = 0f,
                    duration = 100f,
                    value = 30f
                }
            };
            receiver.SetActionEffects(effects);

            DamageData hit = new DamageData
            {
                attackerHash = 0,
                defenderHash = hash,
                damage = new ElementalStatus { slash = 10 },
                motionValue = 1.0f,
                attackElement = Element.Slash,
                armorBreakValue = 20f,  // 1発で 20 削る
                feature = AttackFeature.None
            };

            // 1発目: 行動アーマー 30 → 10 に減る (armorBroken なし)
            DamageResult first = receiver.ReceiveDamage(hit);
            Assert.IsFalse(first.hitReaction == HitReaction.Flinch,
                "アーマー残量10でFlinchせず (SuperArmor相当) — totalArmorBefore=30でFlinch抑制");

            // 2発目: 累積 40 削りで行動アーマーを完全消費 → armorBroken 扱いになりボーナス適用
            DamageResult second = receiver.ReceiveDamage(hit);
            // ActionArmor残量は 10 → 0 に完全消費
            // 3発目でアーマーゼロ → Flinch 発生を期待
            DamageResult third = receiver.ReceiveDamage(hit);
            Assert.AreEqual(HitReaction.Flinch, third.hitReaction,
                "行動アーマー完全消費後の被弾はFlinchに遷移 (消費量書き戻し正常)");

            Object.Destroy(obj);
        }

        [UnityTest]
        public IEnumerator DamageReceiver_SetActionEffects_ResetsConsumedArmor()
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

            ActionEffect[] effects = new ActionEffect[]
            {
                new ActionEffect { type = ActionEffectType.Armor, startTime = 0f, duration = 100f, value = 30f }
            };
            receiver.SetActionEffects(effects);

            DamageData hit = new DamageData
            {
                attackerHash = 0,
                defenderHash = hash,
                damage = new ElementalStatus { slash = 10 },
                motionValue = 1.0f,
                attackElement = Element.Slash,
                armorBreakValue = 20f,
                feature = AttackFeature.None
            };

            // 1発目で armor 30 → 10 に消費
            receiver.ReceiveDamage(hit);

            // 行動切替 → 消費量リセット
            receiver.SetActionEffects(effects);

            // リセット後の1発目は 20 削り、残 10 → Flinchしないはず
            DamageResult afterReset = receiver.ReceiveDamage(hit);
            Assert.AreNotEqual(HitReaction.Flinch, afterReset.hitReaction,
                "SetActionEffects後は累積消費がリセットされ、アーマー残量が戻る");

            Object.Destroy(obj);
        }

        // ===== B3: HitReaction → ActState 書き戻し =====

        [UnityTest]
        public IEnumerator DamageReceiver_FlinchHit_WritesFlinchToActState()
        {
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(
                belong: CharacterBelong.Enemy, feature: CharacterFeature.Minion, maxHp: 200);
            info.maxArmor = 0f;
            GameObject obj = TestSceneHelper.CreateBaseCharacterObject(info, new Vector3(3, 2, 0));
            DamageReceiver receiver = obj.AddComponent<DamageReceiver>();
            yield return null;

            int hash = obj.GetComponent<BaseCharacter>().ObjectHash;

            // 初期状態は Neutral
            Assert.AreEqual(ActState.Neutral, GameManager.Data.GetFlags(hash).ActState);

            DamageData data = new DamageData
            {
                attackerHash = 0,
                defenderHash = hash,
                damage = new ElementalStatus { slash = 20 },
                motionValue = 1.0f,
                attackElement = Element.Slash,
                feature = AttackFeature.Light  // knockbackForceなし → Flinch
            };

            DamageResult result = receiver.ReceiveDamage(data);

            Assert.AreEqual(HitReaction.Flinch, result.hitReaction);
            Assert.AreEqual(ActState.Flinch, GameManager.Data.GetFlags(hash).ActState,
                "HitReaction.Flinch → SoA ActState.Flinch に書き戻されている");

            Object.Destroy(obj);
        }

        [UnityTest]
        public IEnumerator DamageReceiver_KnockbackHit_WritesKnockbackedToActState()
        {
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(
                belong: CharacterBelong.Enemy, feature: CharacterFeature.Minion, maxHp: 200);
            info.maxArmor = 0f;
            GameObject obj = TestSceneHelper.CreateBaseCharacterObject(info, new Vector3(3, 2, 0));
            DamageReceiver receiver = obj.AddComponent<DamageReceiver>();
            yield return null;

            int hash = obj.GetComponent<BaseCharacter>().ObjectHash;

            DamageData data = new DamageData
            {
                attackerHash = 0,
                defenderHash = hash,
                damage = new ElementalStatus { slash = 20 },
                motionValue = 1.0f,
                attackElement = Element.Slash,
                feature = AttackFeature.Heavy,
                knockbackForce = new Vector2(-5f, 3f)  // 吹き飛ばし力あり
            };

            DamageResult result = receiver.ReceiveDamage(data);

            Assert.AreEqual(HitReaction.Knockback, result.hitReaction);
            Assert.AreEqual(ActState.Knockbacked, GameManager.Data.GetFlags(hash).ActState,
                "HitReaction.Knockback → SoA ActState.Knockbacked に書き戻されている");

            Object.Destroy(obj);
        }

        [UnityTest]
        public IEnumerator DamageReceiver_LethalHit_WritesDeadToActState()
        {
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(
                belong: CharacterBelong.Enemy, feature: CharacterFeature.Minion, maxHp: 1);
            info.maxArmor = 0f;
            GameObject obj = TestSceneHelper.CreateBaseCharacterObject(info, new Vector3(3, 2, 0));
            DamageReceiver receiver = obj.AddComponent<DamageReceiver>();
            yield return null;

            int hash = obj.GetComponent<BaseCharacter>().ObjectHash;

            DamageData data = new DamageData
            {
                attackerHash = 0,
                defenderHash = hash,
                damage = new ElementalStatus { slash = 999 },
                motionValue = 1.0f,
                attackElement = Element.Slash,
                feature = AttackFeature.Heavy
            };

            DamageResult result = receiver.ReceiveDamage(data);

            Assert.IsTrue(result.isKill);
            Assert.AreEqual(ActState.Dead, GameManager.Data.GetFlags(hash).ActState,
                "致死ダメージ時は ActState.Dead が優先される");

            Object.Destroy(obj);
        }

        [UnityTest]
        public IEnumerator DamageReceiver_GuardedHit_KeepsPreviousActState()
        {
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(
                belong: CharacterBelong.Ally, feature: CharacterFeature.Player, maxHp: 200);
            info.maxArmor = 0f;
            GameObject obj = TestSceneHelper.CreateBaseCharacterObject(info, new Vector3(3, 2, 0));
            DamageReceiver receiver = obj.AddComponent<DamageReceiver>();
            yield return null;

            int hash = obj.GetComponent<BaseCharacter>().ObjectHash;

            // Guardingモードかつジャストガード窓外にする
            receiver.SetGuarding(true);
            // 1フレーム進めて guardTimeSinceStart を加算
            for (int i = 0; i < 30; i++) { yield return null; }

            // 予め ActState を Guarding に明示設定 (ref locals はイテレータ内で使えないのでヘルパー経由)
            SetActState(hash, ActState.Guarding);

            DamageData data = new DamageData
            {
                attackerHash = 0,
                defenderHash = hash,
                damage = new ElementalStatus { slash = 10 },
                motionValue = 1.0f,
                attackElement = Element.Slash,
                knockbackForce = new Vector2(-1f, 0f),
                feature = AttackFeature.None
            };

            DamageResult result = receiver.ReceiveDamage(data);

            // ガード成功(Guarded/JustGuard) → ActState は上書きしない
            Assert.IsTrue(result.guardResult == GuardResult.Guarded || result.guardResult == GuardResult.JustGuard,
                "ガード成功すること");
            Assert.AreEqual(HitReaction.None, result.hitReaction,
                "ガード成功時は HitReaction.None");
            Assert.AreEqual(ActState.Guarding, GameManager.Data.GetFlags(hash).ActState,
                "HitReaction.None の場合 ActState は維持される");

            Object.Destroy(obj);
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
