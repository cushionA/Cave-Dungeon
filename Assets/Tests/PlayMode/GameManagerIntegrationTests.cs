using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Game.Core;
using Game.Runtime;
using CharacterInfo = Game.Core.CharacterInfo;

namespace Game.Tests.PlayMode
{
    public class GameManagerIntegrationTests
    {
        [UnitySetUp]
        public IEnumerator Setup()
        {
            TestSceneHelper.CreateGameManager();
            yield return null; // Awakeを待つ
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            TestSceneHelper.Cleanup();
            yield return null;
        }

        [UnityTest]
        public IEnumerator GameManager_Awake_SingletonIsSet()
        {
            yield return null;
            Assert.IsNotNull(GameManager.Instance);
            Assert.IsNotNull(GameManager.Data);
            Assert.IsNotNull(GameManager.Events);
        }

        [UnityTest]
        public IEnumerator GameManager_RegisterCharacter_DataAccessible()
        {
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(maxHp: 200);
            int hash = 12345;

            // テスト専用オーバーロードのためObsolete警告を抑制
#pragma warning disable CS0618
            GameManager.Instance.RegisterCharacter(hash, info);
#pragma warning restore CS0618
            yield return null;

            Assert.IsTrue(GameManager.Data.TryGetValue(hash, out int _));
            int currentHp = GameManager.Data.GetVitals(hash).currentHp;
            int maxHp = GameManager.Data.GetVitals(hash).maxHp;
            Assert.AreEqual(200, currentHp);
            Assert.AreEqual(200, maxHp);
        }

        [UnityTest]
        public IEnumerator GameManager_UnregisterCharacter_DataRemoved()
        {
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo();
            int hash = 99999;

#pragma warning disable CS0618
            GameManager.Instance.RegisterCharacter(hash, info);
#pragma warning restore CS0618
            GameManager.Instance.UnregisterCharacter(hash);
            yield return null;

            Assert.IsFalse(GameManager.Data.TryGetValue(hash, out int _));
        }
    }
}
