using System.Collections;
using System.Collections.Generic;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using CharacterInfo = Game.Core.CharacterInfo;

namespace Game.Tests.PlayMode
{
    public class CharacterStatRegistrationFlowTests
    {
        private readonly List<Object> _created = new List<Object>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            TestSceneHelper.CreateGameManager();
            GameManager.Instance.enabled = false;
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

        private BaseCharacter CreateActor(CharacterBelong belong, CharacterFeature feature)
        {
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(belong, feature);
            info.initialLevel = 7;
            info.baseAttributes = new StatModifier { str = 5, intel = 8, vit = 4, mnd = 6 };
            _created.Add(info);
            GameObject actor = TestSceneHelper.CreateBaseCharacterObject(info, Vector3.zero);
            _created.Add(actor);
            return actor.GetComponent<BaseCharacter>();
        }

        [UnityTest]
        public IEnumerator CharacterStats_RuntimeFactions_UseTheSameRegistrationAndGrowthPath()
        {
            BaseCharacter player = CreateActor(CharacterBelong.Ally, CharacterFeature.Player);
            BaseCharacter enemy = CreateActor(CharacterBelong.Enemy, CharacterFeature.Minion);
            BaseCharacter companion = CreateActor(CharacterBelong.Ally, CharacterFeature.Companion);
            yield return null;
            BaseCharacter[] actors = { player, enemy, companion };
            LevelUpLogic growth = new LevelUpLogic(7);
            growth.AddExp(700);
            Assert.IsTrue(growth.AllocatePoint(StatType.Vit));
            Assert.IsTrue(growth.AllocatePoint(StatType.Mnd));

            foreach (BaseCharacter actor in actors)
            {
                actor.enabled = false;
                int hash = actor.ObjectHash;
                Assert.AreEqual(7, GameManager.Data.GetVitals(hash).level);
                Assert.AreEqual(100, GameManager.Data.GetVitals(hash).maxHp);
                Assert.AreEqual(8, GameManager.Data.GetAttributes(hash).intel);
                GameManager.Data.GetVitals(hash).currentMp = 13.5f;

                GameManager.Instance.ApplyCharacterGrowth(hash, growth.Growth);

                Assert.AreEqual(8, GameManager.Data.GetGrowth(hash).level);
                Assert.AreEqual(110, GameManager.Data.GetVitals(hash).maxHp);
                Assert.AreEqual(55, GameManager.Data.GetVitals(hash).maxMp);
                Assert.AreEqual(13.5f, GameManager.Data.GetVitals(hash).currentMp);
                Assert.AreEqual(5, GameManager.Data.GetAttributes(hash).vit);
            }
        }

        [UnityTest]
        public IEnumerator CharacterStats_RuntimePoolReuse_RestoresAuthoredBasisAndInitialGrowth()
        {
            BaseCharacter actor = CreateActor(CharacterBelong.Enemy, CharacterFeature.Minion);
            yield return null;
            actor.enabled = false;
            int hash = actor.ObjectHash;
            GameManager.Instance.ApplyCharacterGrowth(hash, new CharacterGrowth
            {
                level = 10, allocatedStats = new StatModifier { vit = 3, mnd = 2 },
            });
            GameManager.Data.GetVitals(hash).currentHp = 1;
            GameManager.Data.GetVitals(hash).currentMp = 0.5f;

            actor.OnPoolReturn();
            Assert.IsFalse(GameManager.IsCharacterValid(hash));
            actor.OnPoolAcquire();

            Assert.IsTrue(GameManager.IsCharacterValid(hash));
            Assert.AreEqual(7, GameManager.Data.GetGrowth(hash).level);
            Assert.AreEqual(0, GameManager.Data.GetGrowth(hash).allocatedStats.vit);
            Assert.AreEqual(100, GameManager.Data.GetBaseStats(hash).maxHp);
            Assert.AreEqual(100, GameManager.Data.GetVitals(hash).maxHp);
            Assert.AreEqual(100, GameManager.Data.GetVitals(hash).currentHp);
            Assert.AreEqual(50f, GameManager.Data.GetVitals(hash).currentMp);
            Assert.AreEqual(4, GameManager.Data.GetAttributes(hash).vit);
            yield break;
        }

        [UnityTest]
        public IEnumerator CharacterStats3D_WhenGrowthIsApplied_UpdatesLiveVitalsAndResetsOnNewGeneration()
        {
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo();
            info.initialLevel = 3;
            _created.Add(info);
            GameObject root = new GameObject("3D growth actor");
            root.SetActive(false);
            _created.Add(root);
            CharacterActor3D actor = root.AddComponent<CharacterActor3D>();
            actor.SetCharacterInfoForTest(info);
            root.SetActive(true);
            yield return null;

            ActorRef owner = actor.Actor;
            LevelUpLogic levelUp = new LevelUpLogic(GameManager.Data.GetGrowth(owner.Hash).level);
            Assert.AreEqual(3, levelUp.Level);
            Assert.AreEqual(1, levelUp.AddExp(levelUp.GetExpForNextLevel()));
            Assert.IsTrue(levelUp.AllocatePoint(StatType.Vit));
            Assert.IsTrue(levelUp.AllocatePoint(StatType.Mnd));
            GameManager.Data.GetVitals(owner.Hash).currentHp = 64;
            GameManager.Data.GetVitals(owner.Hash).currentMp = 12.5f;
            GameManager.Instance.ApplyCharacterGrowth(owner.Hash, levelUp.Growth);

            Assert.AreEqual(owner, actor.Actor);
            Assert.AreEqual(4, GameManager.Data.GetVitals(owner.Hash).level);
            Assert.AreEqual(110, GameManager.Data.GetVitals(owner.Hash).maxHp);
            Assert.AreEqual(55, GameManager.Data.GetVitals(owner.Hash).maxMp);
            Assert.AreEqual(64, GameManager.Data.GetVitals(owner.Hash).currentHp);
            Assert.AreEqual(12.5f, GameManager.Data.GetVitals(owner.Hash).currentMp);

            actor.enabled = false;
            actor.enabled = true;
            Assert.AreNotEqual(owner, actor.Actor);
            Assert.AreEqual(3, GameManager.Data.GetGrowth(owner.Hash).level);
            Assert.AreEqual(0, GameManager.Data.GetGrowth(owner.Hash).allocatedStats.vit);
            Assert.AreEqual(100, GameManager.Data.GetVitals(owner.Hash).currentHp);
            Assert.AreEqual(50f, GameManager.Data.GetVitals(owner.Hash).currentMp);
        }
    }
}
