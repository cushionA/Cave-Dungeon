using System;
using System.Collections;
using System.Collections.Generic;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using R3;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.PlayMode
{
    public class HitResolutionFlowPlayTests
    {
        private readonly List<UnityEngine.Object> _created = new List<UnityEngine.Object>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            TestSceneHelper.CreateGameManager();
            _created.Add(TestSceneHelper.CreateGround());
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
            {
                if (_created[i] != null)
                {
                    UnityEngine.Object.Destroy(_created[i]);
                }
            }
            _created.Clear();
            yield return null;
            TestSceneHelper.Cleanup();
            yield return null;
        }

        private Game.Core.CharacterInfo CreateInfo(int maxHp)
        {
            Game.Core.CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(maxHp: maxHp);
            _created.Add(info);
            return info;
        }

        private GameObject CreateActor(Game.Core.CharacterInfo info, bool includeReceiver = false)
        {
            GameObject actor = includeReceiver
                ? TestSceneHelper.CreateBaseCharacterObjectWithDamageReceiver(info, new Vector3(0f, 2f, 0f))
                : TestSceneHelper.CreateBaseCharacterObject(info, new Vector3(0f, 2f, 0f));
            _created.Add(actor);
            return actor;
        }

        [UnityTest]
        public IEnumerator HitResolution_PoolAcquireBeforeFirstStart_InitializesSharedRecoverySettings()
        {
            Game.Core.CharacterInfo info = CreateInfo(1000);
            info.armorRecoveryRate = 6f;
            info.armorRecoveryDelay = 2f;
            GameObject actor = CreateActor(info, true);
            BaseCharacter character = actor.GetComponent<BaseCharacter>();
            bool registrationObserved = false;
            using IDisposable subscription = GameManager.Events.OnCharacterRegistered.Subscribe(hash =>
            {
                if (hash != character.ObjectHash)
                {
                    return;
                }
                registrationObserved = true;
                Assert.AreEqual(6f, GameManager.Data.GetDefenseState(hash).armorRecoveryRate);
                Assert.AreEqual(2f, GameManager.Data.GetDefenseState(hash).armorRecoveryDelay);
            });
            character.OnPoolAcquire();
            yield return null;

            CharacterDefenseState state = GameManager.Data.GetDefenseState(character.ObjectHash);
            Assert.AreEqual(6f, state.armorRecoveryRate);
            Assert.AreEqual(2f, state.armorRecoveryDelay);
            Assert.IsTrue(registrationObserved);
        }

        [UnityTest]
        public IEnumerator HitResolution_GuardedHit_PublishesUpdatedSharedResourcesAndOneGuardEvent()
        {
            Game.Core.CharacterInfo info = CreateInfo(1000);
            info.maxArmor = 100f;
            info.maxStamina = 50f;
            GameObject actor = CreateActor(info);
            DamageReceiver receiver = actor.AddComponent<DamageReceiver>();
            yield return null;
            int hash = actor.GetComponent<BaseCharacter>().ObjectHash;
            GameManager.Data.GetCombatStats(hash).guardStats = new GuardStats { guardDirection = GuardDirection.Both };
            GameManager.Data.GetVitals(hash).currentStamina = 50f;
            GameManager.Data.GetVitals(hash).currentArmor = 100f;
            receiver.SetGuarding(true);
            GameManager.Data.GetDefenseState(hash).guardElapsedTime = 1f;
            int damageEvents = 0;
            int guardEvents = 0;
            using IDisposable damageSubscription = GameManager.Events.OnDamageDealt.Subscribe(hit =>
            {
                damageEvents++;
                Assert.AreEqual(hash, hit.defenderHash);
                Assert.AreEqual(20f, GameManager.Data.GetVitals(hash).currentStamina);
                Assert.AreEqual(95f, GameManager.Data.GetVitals(hash).currentArmor);
            });
            using IDisposable guardSubscription = GameManager.Events.OnGuardEvent.Subscribe(hit =>
            {
                guardEvents++;
                Assert.AreEqual(GuardResult.Guarded, hit.result);
            });

            DamageResult result = receiver.ReceiveDamage(new DamageData
            {
                defenderHash = hash, damage = new ElementalStatus { slash = 20 }, motionValue = 1f,
                guardDamage = 30f, armorBreakValue = 5f,
            });

            Assert.AreEqual(GuardResult.Guarded, result.guardResult);
            Assert.AreEqual(30f, result.guardDamage);
            Assert.AreEqual(5f, result.armorDamage);
            Assert.AreEqual(0f, result.actionArmorDamage);
            Assert.AreEqual(1, damageEvents);
            Assert.AreEqual(1, guardEvents);
        }

        [UnityTest]
        public IEnumerator HitResolution_ActionArmorConsumption_IsVisibleThroughSharedDataAndResetsOnActionChange()
        {
            Game.Core.CharacterInfo info = CreateInfo(1000);
            info.maxArmor = 0f;
            GameObject actor = CreateActor(info);
            DamageReceiver receiver = actor.AddComponent<DamageReceiver>();
            yield return null;
            int hash = actor.GetComponent<BaseCharacter>().ObjectHash;
            ActionEffect[] effects = { new ActionEffect { type = ActionEffectType.Armor, value = 30f, startTime = 0f, duration = 10f } };
            receiver.SetActionEffects(effects);
            DamageData hit = new DamageData
            {
                defenderHash = hash, damage = new ElementalStatus { slash = 1 }, motionValue = 1f,
                armorBreakValue = 10f,
            };

            DamageResult first = receiver.ReceiveDamage(hit);
            DamageResult second = receiver.ReceiveDamage(hit);

            Assert.AreEqual(10f, first.actionArmorDamage);
            Assert.AreEqual(10f, second.actionArmorDamage);
            Assert.AreEqual(20f, GameManager.Data.GetDefenseState(hash).actionArmorConsumed);
            receiver.SetActionEffects(effects);
            Assert.AreEqual(0f, GameManager.Data.GetDefenseState(hash).actionArmorConsumed);
        }

        [UnityTest]
        public IEnumerator HitResolution_DeadTarget_IgnoresFurtherHitsWithoutRepeatedDeathEvents()
        {
            Game.Core.CharacterInfo info = CreateInfo(10);
            GameObject actor = CreateActor(info);
            DamageReceiver receiver = actor.AddComponent<DamageReceiver>();
            yield return null;
            int hash = actor.GetComponent<BaseCharacter>().ObjectHash;
            int deaths = 0;
            using IDisposable subscription = GameManager.Events.OnCharacterDeath.Subscribe(_ => deaths++);
            DamageData hit = new DamageData
            {
                defenderHash = hash, damage = new ElementalStatus { slash = 1000 }, motionValue = 1f,
            };

            DamageResult first = receiver.ReceiveDamage(hit);
            DamageResult second = receiver.ReceiveDamage(hit);

            Assert.IsTrue(first.isKill);
            Assert.AreEqual(0, second.totalDamage);
            Assert.IsFalse(second.isKill);
            Assert.AreEqual(1, deaths);
            Assert.AreEqual(0, GameManager.Data.GetVitals(hash).currentHp);
        }
    }
}
