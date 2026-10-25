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
    }
}
