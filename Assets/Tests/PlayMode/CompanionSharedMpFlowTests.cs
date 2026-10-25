using System.Collections;
using System.Collections.Generic;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Game.Tests.PlayMode
{
    public class CompanionSharedMpFlowTests
    {
        private readonly List<Object> _created = new List<Object>();
        private CompanionCharacter _companion;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            TestSceneHelper.CreateGameManager();
            GameManager.Instance.enabled = false;
            Game.Core.CharacterInfo playerInfo = TestSceneHelper.CreateTestCharacterInfo();
            _created.Add(playerInfo);
            GameObject player = TestSceneHelper.CreateBaseCharacterObject(playerInfo, Vector3.zero);
            _created.Add(player);
            yield return null;
            CharacterRegistry.RegisterPlayer(player.GetComponent<BaseCharacter>().ObjectHash);

            Game.Core.CharacterInfo companionInfo = TestSceneHelper.CreateTestCharacterInfo();
            companionInfo.name = "SharedMpCompanion";
            _created.Add(companionInfo);
            GameObject actor = new GameObject("SharedMpCompanion");
            _created.Add(actor);
            actor.AddComponent<Rigidbody2D>();
            actor.AddComponent<BoxCollider2D>();
            actor.AddComponent<SpriteRenderer>();
            _companion = actor.AddComponent<CompanionCharacter>();
            _companion.SetCharacterInfoForTest(companionInfo);
            _companion.SetMpSettings(new CompanionMpSettings
            {
                baseRecoveryRate = 5f, mpRecoverActionRate = 10f, vanishRecoveryMultiplier = 1.3f,
                returnThresholdRatio = 0.5f, maxReserveMp = 100,
            });
            yield return null;
            _companion.enabled = false;
            Assert.IsNotNull(_companion.AIController);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
            {
                if (_created[i] != null) { Object.Destroy(_created[i]); }
            }
            _created.Clear();
            yield return null;
            TestSceneHelper.Cleanup();
            yield return null;
        }

        [UnityTest]
        public IEnumerator CompanionSharedMp_RuntimeActionAndRecovery_ExposeOneBalance()
        {
            int owner = _companion.ObjectHash;
            ActionDefinition cast = ScriptableObject.CreateInstance<ActionDefinition>();
            _created.Add(cast);
            cast.mpCost = 20;
            cast.gameplay = ActionGameplayFlags.Cast;
            cast.durationMode = ActionDurationMode.Fixed;
            cast.duration = 2f;
            Assert.IsTrue(GameManager.Actions.Execute(owner, cast, 0, Vector2.right, out _));
            Assert.AreEqual(30f, _companion.AIController.MpManager.CurrentMp);

            _companion.AIController.Tick(1f, new List<int>(), 0f);

            Assert.AreEqual(35f, GameManager.Data.GetVitals(owner).currentMp);
            Assert.AreEqual(35f, _companion.AIController.MpManager.CurrentMp);
            Assert.AreEqual(95, _companion.AIController.MpManager.ReserveMp);
            AICondition ratio = new AICondition { conditionType = AIConditionType.MpRatio, filter = new TargetFilter { includeSelf = true } };
            Assert.AreEqual(70f, ConditionEvaluator.GetConditionValue(ratio, owner, 0, GameManager.Data, 0f));
            yield break;
        }

        [UnityTest]
        public IEnumerator CompanionSharedMp_PoolReuse_DetachesPreviousManagerFromNewResource()
        {
            int owner = _companion.ObjectHash;
            CompanionMpManager previous = _companion.AIController.MpManager;
            previous.ApplyBarrierDamage(previous.CurrentMp);
            Assert.IsTrue(previous.IsVanished);
            Assert.AreEqual(0.3f, _companion.GetComponent<SpriteRenderer>().color.a, 0.001f);
            _companion.OnPoolReturn();
            Assert.IsFalse(GameManager.IsCharacterValid(owner));
            _companion.OnPoolAcquire();
            Assert.IsTrue(GameManager.IsCharacterValid(owner));
            Assert.AreNotSame(previous, _companion.AIController.MpManager);
            Assert.AreEqual(1f, _companion.GetComponent<SpriteRenderer>().color.a, 0.001f);
            Assert.AreEqual(50f, GameManager.Data.GetVitals(owner).currentMp);
            Assert.AreEqual(0f, previous.ConsumeMp(10f));
            previous.Tick(10f);
            Assert.AreEqual(50f, GameManager.Data.GetVitals(owner).currentMp);
            _companion.AIController.MpManager.ConsumeMp(0.5f);
            Assert.AreEqual(49.5f, GameManager.Data.GetVitals(owner).currentMp);
            yield break;
        }
    }
}
