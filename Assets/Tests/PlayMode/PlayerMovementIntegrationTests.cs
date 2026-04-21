using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Game.Core;
using Game.Runtime;
using CharacterInfo = Game.Core.CharacterInfo;

namespace Game.Tests.PlayMode
{
    public class PlayerMovementIntegrationTests
    {
        private GameObject _gmObject;
        private GameObject _groundObject;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            _gmObject = TestSceneHelper.CreateGameManager();
            _groundObject = TestSceneHelper.CreateGround();
            yield return null; // Awake
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            TestSceneHelper.Cleanup();
            if (_groundObject != null)
            {
                Object.Destroy(_groundObject);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator BaseCharacter_Start_RegistersInSoA()
        {
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(maxHp: 150);
            GameObject charObj = TestSceneHelper.CreateBaseCharacterObject(info, new Vector3(0, 2, 0));

            yield return null; // Start

            BaseCharacter bc = charObj.GetComponent<BaseCharacter>();
            Assert.IsTrue(GameManager.Data.TryGetValue(bc.ObjectHash, out int _));
            int currentHp = GameManager.Data.GetVitals(bc.ObjectHash).currentHp;
            Assert.AreEqual(150, currentHp);

            Object.Destroy(charObj);
        }

        [UnityTest]
        public IEnumerator BaseCharacter_GroundCheck_DetectsGround()
        {
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo();
            // 地面(y=-1, height=2 → top=0) のすぐ上から落とす
            GameObject charObj = TestSceneHelper.CreateBaseCharacterObject(info, new Vector3(0, 0.6f, 0));

            // 物理演算で落下させる。Physics2D 設定や FixedDeltaTime の環境差に耐えるため、
            // 固定回数 FixedUpdate を回して確実に接地完了させる。
            yield return null; // Start
            for (int i = 0; i < 120; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            BaseCharacter bc = charObj.GetComponent<BaseCharacter>();
            bc.UpdateGroundCheck();

            Assert.IsTrue(bc.IsGrounded,
                $"Character should be grounded after falling. position.y={charObj.transform.position.y}");

            Object.Destroy(charObj);
        }

        [UnityTest]
        public IEnumerator BaseCharacter_OnDestroy_UnregistersFromSoA()
        {
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo();
            GameObject charObj = TestSceneHelper.CreateBaseCharacterObject(info, new Vector3(0, 2, 0));
            yield return null; // Start

            BaseCharacter bc = charObj.GetComponent<BaseCharacter>();
            int hash = bc.ObjectHash;

            Object.Destroy(charObj);
            yield return null;

            Assert.IsFalse(GameManager.Data.TryGetValue(hash, out int _));
        }
    }
}
