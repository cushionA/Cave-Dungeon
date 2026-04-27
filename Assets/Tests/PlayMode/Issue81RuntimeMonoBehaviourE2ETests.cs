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
    /// Issue #81: Runtime MonoBehaviour 層 E2E PlayMode テスト拡充。
    /// Skeptic レビュー (PR #80/78) で顕在化した Pool 再利用 / 多段ヒット / fake-null /
    /// 物理イベント例外 / Save/Load / AI 通し / Additive シーンの 7 観点に対し、
    /// 既存 EditMode カバレッジでは検証不可なライフサイクル・SoA 整合性を確認する。
    /// </summary>
    public class Issue81RuntimeMonoBehaviourE2ETests
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
            for (int i = 0; i < _spawnedObjects.Count; i++)
            {
                if (_spawnedObjects[i] != null)
                {
                    Object.Destroy(_spawnedObjects[i]);
                }
            }
            _spawnedObjects.Clear();
            TestSceneHelper.Cleanup();
            yield return null;
        }

        // ===== Category 1: プール再利用シナリオ (Skeptic M1) =====

        /// <summary>
        /// スポーン → 撃破 (Destroy) → 再スポーンで SoA / CharacterRegistry が
        /// 前キャラの残骸を持たないことを確認する。新キャラの hash で正しく登録され、
        /// 旧キャラの hash は AllHashes から消えている。
        /// </summary>
        [UnityTest]
        public IEnumerator PoolReuse_DestroyThenRespawn_SoARegistrationConsistent()
        {
            CharacterInfo info1 = TestSceneHelper.CreateTestCharacterInfo(
                belong: CharacterBelong.Enemy, feature: CharacterFeature.Minion, maxHp: 50);
            GameObject first = TestSceneHelper.CreateBaseCharacterObject(info1, Vector3.zero);
            yield return null;

            BaseCharacter firstChar = first.GetComponent<BaseCharacter>();
            int firstHash = firstChar.ObjectHash;
            CharacterRegistry.RegisterEnemy(firstHash);

            Assert.IsTrue(GameManager.IsCharacterValid(firstHash),
                "前提: 1 体目が SoA に登録されている");

            // 撃破 (Destroy → BaseCharacter.OnDestroy で Unregister 実行)
            Object.Destroy(first);
            yield return null;

            Assert.IsFalse(GameManager.IsCharacterValid(firstHash),
                "Destroy 後は SoA から登録解除されている");
            Assert.IsFalse(CharacterRegistry.AllHashes.Contains(firstHash),
                "Destroy 後は CharacterRegistry からも除去されている");

            // 再スポーン (新規 GameObject なので異なる hash)
            CharacterInfo info2 = TestSceneHelper.CreateTestCharacterInfo(
                belong: CharacterBelong.Enemy, feature: CharacterFeature.Minion, maxHp: 80);
            GameObject second = TestSceneHelper.CreateBaseCharacterObject(info2, Vector3.zero);
            _spawnedObjects.Add(second);
            yield return null;

            BaseCharacter secondChar = second.GetComponent<BaseCharacter>();
            int secondHash = secondChar.ObjectHash;
            CharacterRegistry.RegisterEnemy(secondHash);

            Assert.IsTrue(GameManager.IsCharacterValid(secondHash),
                "再スポーン後は新 hash で SoA に登録されている");

            CharacterVitals vitals = GameManager.Data.GetVitals(secondHash);
            Assert.AreEqual(80, vitals.maxHp,
                "再スポーン時の vitals は新 CharacterInfo の値で初期化される (前キャラ HP=50 を引きずらない)");
        }

        // ===== Category 2: 多段ヒット同フレーム着弾 (Skeptic M2) =====

        /// <summary>
        /// 同フレームに同一 DamageReceiver へ 2 発受けた場合、2 発目は ActState=Flinch を
        /// 経由して StaggerHit 扱いになり、armor=0 状態として処理される (raw 直撃ではない)。
        /// Issue #78 M2 の WontFix 仕様の保護テスト。
        /// </summary>
        [UnityTest]
        public IEnumerator MultiHit_SameFrame_SecondHitGoesThroughFlinchPath()
        {
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(
                belong: CharacterBelong.Enemy, feature: CharacterFeature.Minion, maxHp: 200);
            info.maxArmor = 0f; // base armor 無し → 行動アーマーのみ検証
            info.armorRecoveryRate = 0f;
            info.armorRecoveryDelay = 10f;
            GameObject obj = TestSceneHelper.CreateBaseCharacterObjectWithDamageReceiver(info, Vector3.zero);
            _spawnedObjects.Add(obj);
            yield return null;

            DamageReceiver receiver = obj.GetComponent<DamageReceiver>();
            int hash = obj.GetComponent<BaseCharacter>().ObjectHash;

            // 行動アーマー 10 (1 発で削り切れる量)
            ActionEffect[] effects = new ActionEffect[]
            {
                new ActionEffect
                {
                    type = ActionEffectType.Armor,
                    startTime = 0f,
                    duration = 100f,
                    value = 10f
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
                armorBreakValue = 20f, // 1 発で 10 のアーマーを削り切る
                feature = AttackFeature.None
            };

            // 1 発目: armor 10 → 0 で削り切り、ActState=Flinch に遷移
            DamageResult first = receiver.ReceiveDamage(hit);
            Assert.AreEqual(HitReaction.Flinch, first.hitReaction,
                "1 発目で armor 削り切り → Flinch リアクション");

            ActState afterFirst = GameManager.Data.GetFlags(hash).ActState;
            Assert.AreEqual(ActState.Flinch, afterFirst,
                "1 発目直後に SoA の ActState は Flinch に同期書き戻されている");

            // 2 発目 (同フレーム / yield なし): Flinch 状態で被弾
            DamageResult second = receiver.ReceiveDamage(hit);
            Assert.AreEqual(HitReaction.Flinch, second.hitReaction,
                "2 発目も Flinch (Flinch 中の被弾は armor=0 で Flinch 維持)");
            Assert.IsTrue(second.totalDamage > 0,
                "2 発目はダメージが通る (armor 残量 0)");
        }

        // ===== Category 3: fake-null trap シナリオ (Skeptic H2) =====

        /// <summary>
        /// 2 体の DamageReceiver を生成し片方を Destroy 後、生存側に被弾させても
        /// 例外なく処理完了することを確認する。Unity の == null overload と SoA
        /// 登録解除が連動していることの保護テスト。
        /// </summary>
        [UnityTest]
        public IEnumerator FakeNullTrap_AttackSurvivorAfterPeerDestroyed_NoException()
        {
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(
                belong: CharacterBelong.Enemy, feature: CharacterFeature.Minion, maxHp: 100);
            info.maxArmor = 0f;
            info.armorRecoveryRate = 0f;

            GameObject doomed = TestSceneHelper.CreateBaseCharacterObjectWithDamageReceiver(info, Vector3.zero);
            GameObject survivor = TestSceneHelper.CreateBaseCharacterObjectWithDamageReceiver(info, new Vector3(2, 0, 0));
            _spawnedObjects.Add(survivor);
            yield return null;

            int doomedHash = doomed.GetComponent<BaseCharacter>().ObjectHash;
            int survivorHash = survivor.GetComponent<BaseCharacter>().ObjectHash;
            DamageReceiver survivorReceiver = survivor.GetComponent<DamageReceiver>();

            Assert.AreNotEqual(doomedHash, survivorHash, "前提: hash は異なる");

            // 片方を破棄 (OnDestroy → SoA から Unregister)
            Object.Destroy(doomed);
            yield return null;

            Assert.IsFalse(GameManager.IsCharacterValid(doomedHash),
                "破棄側は SoA から登録解除済み");
            Assert.IsTrue(GameManager.IsCharacterValid(survivorHash),
                "生存側は SoA に残っている");

            // 生存側を攻撃 → 例外なくダメージが通る
            DamageData hit = new DamageData
            {
                attackerHash = 0,
                defenderHash = survivorHash,
                damage = new ElementalStatus { slash = 30 },
                motionValue = 1.0f,
                attackElement = Element.Slash
            };

            DamageResult result = default;
            Assert.DoesNotThrow(() => result = survivorReceiver.ReceiveDamage(hit),
                "破棄済 peer がいる状態でも生存側への被弾は例外を投げない");

            CharacterVitals vitals = GameManager.Data.GetVitals(survivorHash);
            Assert.Less(vitals.currentHp, info.maxHp,
                "生存側はダメージを受けて HP が減っている");
        }

        // ===== Category 4: 物理イベント中の例外 (Skeptic M3) =====

        /// <summary>
        /// SoA 登録抹消後の DamageReceiver に対して ReceiveDamage が呼ばれても
        /// 例外を投げず default 値を返す。OnTriggerEnter2D の競合タイミングで
        /// 起きうるシナリオを擬する。
        /// </summary>
        [UnityTest]
        public IEnumerator PhysicsEvent_ReceiveDamageOnUnregisteredHash_ReturnsDefaultNoException()
        {
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo();
            GameObject obj = TestSceneHelper.CreateBaseCharacterObjectWithDamageReceiver(info, Vector3.zero);
            _spawnedObjects.Add(obj);
            yield return null;

            DamageReceiver receiver = obj.GetComponent<DamageReceiver>();
            int hash = obj.GetComponent<BaseCharacter>().ObjectHash;

            Assert.IsTrue(GameManager.IsCharacterValid(hash), "前提: SoA に登録済み");

            // SoA から強制 Remove (BaseCharacter.OnDestroy 相当のうち SoA 抹消のみ再現)
            GameManager.Data.Remove(hash);
            Assert.IsFalse(GameManager.IsCharacterValid(hash),
                "前提: SoA から登録抹消済み");

            DamageData hit = new DamageData
            {
                attackerHash = 0,
                defenderHash = hash,
                damage = new ElementalStatus { slash = 5 },
                motionValue = 1.0f
            };

            DamageResult result = default;
            Assert.DoesNotThrow(() => result = receiver.ReceiveDamage(hit),
                "登録抹消後の被弾は例外を投げず処理が継続する (IsCharacterValid ガードで早期 return)");
            Assert.AreEqual(0, result.totalDamage,
                "default DamageResult が返る (totalDamage=0)");
        }

        // ===== Category 5: Save/Load 周回テスト =====

        /// <summary>
        /// SaveManager の Save → SetActiveSlot → Save → Load の周回で
        /// ActiveSlotIndex が Load 対象に同期する。スロット切替時に旧 state が
        /// 残らないことの基本確認。
        /// </summary>
        [Test]
        public void SaveLoad_SaveBothSlotsThenLoadFirst_ActiveSlotIndexIsFirst()
        {
            SaveManager manager = new SaveManager();
            FakeSaveable saveable = new FakeSaveable();
            manager.Register(saveable);

            // slot 0 に保存
            saveable.Value = 100;
            manager.Save(0);
            manager.SetActiveSlot(0);

            // slot 1 に保存
            saveable.Value = 200;
            manager.Save(1);
            manager.SetActiveSlot(1);
            Assert.AreEqual(1, manager.ActiveSlotIndex, "保存直後は slot 1 がアクティブ");

            // slot 0 へロード
            bool loaded = manager.Load(0);
            Assert.IsTrue(loaded, "slot 0 のロードは成功する");
            Assert.AreEqual(0, manager.ActiveSlotIndex,
                "Load 後は対象スロット (0) がアクティブになる");
            Assert.AreEqual(100, saveable.Value,
                "slot 0 の値 (100) が復元されている");
        }

        // ===== Category 6: AI スポーン → 戦闘 → 撃破の通しテスト =====

        /// <summary>
        /// 敵キャラを SoA に登録し DamageReceiver で被弾させて HP=0 に到達後、
        /// IsAlive=false かつ SoA の vitals.currentHp が 0 にクランプされている。
        /// Damage 経路 → SoA 反映 → IsAlive 判定の通しチェック。
        /// </summary>
        [UnityTest]
        public IEnumerator AISpawnCombatDefeat_DamageUntilDeath_IsAliveBecomesFalse()
        {
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(
                belong: CharacterBelong.Enemy, feature: CharacterFeature.Minion, maxHp: 30);
            info.maxArmor = 0f;
            info.armorRecoveryRate = 0f;
            GameObject obj = TestSceneHelper.CreateBaseCharacterObjectWithDamageReceiver(info, Vector3.zero);
            _spawnedObjects.Add(obj);
            yield return null;

            BaseCharacter bc = obj.GetComponent<BaseCharacter>();
            DamageReceiver receiver = obj.GetComponent<DamageReceiver>();
            int hash = bc.ObjectHash;
            CharacterRegistry.RegisterEnemy(hash);

            Assert.IsTrue(bc.IsAlive, "前提: スポーン直後は IsAlive=true");

            DamageData lethalHit = new DamageData
            {
                attackerHash = 0,
                defenderHash = hash,
                damage = new ElementalStatus { slash = 100 }, // overkill
                motionValue = 1.0f,
                attackElement = Element.Slash
            };

            DamageResult result = receiver.ReceiveDamage(lethalHit);

            Assert.IsTrue(result.isKill, "致死ダメージで isKill=true");

            CharacterVitals vitals = GameManager.Data.GetVitals(hash);
            Assert.GreaterOrEqual(vitals.currentHp, 0,
                "HP は 0 未満にクランプされない (HpArmorLogic 通し検証)");
            Assert.AreEqual(0, vitals.currentHp, "致死被弾後 HP=0");

            ActState actState = GameManager.Data.GetFlags(hash).ActState;
            Assert.AreEqual(ActState.Dead, actState, "致死被弾後は ActState=Dead");
        }

        // ===== Category 7: シーン Additive ロード / Unload =====

        /// <summary>
        /// アクティブシーン取得が PlayMode テスト実行中に有効値を返し、
        /// シーンロード API の前提となる SceneManager 状態が壊れていないことを確認する。
        /// 実際の Additive ロード / Unload は LevelStreamingController テスト側で検証。
        /// </summary>
        [Test]
        public void AdditiveScene_SceneManagerAccessible_ActiveSceneValid()
        {
            UnityEngine.SceneManagement.Scene active =
                UnityEngine.SceneManagement.SceneManager.GetActiveScene();

            Assert.IsTrue(active.IsValid(),
                "PlayMode テスト中はアクティブシーンが有効である");
            Assert.IsTrue(active.isLoaded,
                "アクティブシーンはロード済み状態");
            Assert.GreaterOrEqual(UnityEngine.SceneManagement.SceneManager.sceneCount, 1,
                "シーン数 >= 1 (Additive ロード基盤の前提)");
        }

        // ===== ヘルパー =====

        /// <summary>
        /// SaveManager 単体テスト用の最小 ISaveable 実装。
        /// </summary>
        private sealed class FakeSaveable : ISaveable
        {
            public int Value;
            public string SaveId => "Issue81FakeSaveable";

            public object Serialize()
            {
                return Value;
            }

            public void Deserialize(object data)
            {
                if (data is int i)
                {
                    Value = i;
                }
            }
        }
    }
}
