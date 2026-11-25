using System.Collections;
using System.Collections.Generic;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests.PlayMode
{
    public sealed class DamageReceiver3DPlayTests
    {
        private readonly List<Game.Core.CharacterInfo> _assets = new List<Game.Core.CharacterInfo>();
        private Scene _room;
        private Scene _separateRoom;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            TestSceneHelper.Cleanup();
            yield return null;

            TestSceneHelper.CreateGameManager().GetComponent<GameManager>().enabled = false;
            CreateSceneParameters physics = new CreateSceneParameters(LocalPhysicsMode.Physics3D);
            _room = SceneManager.CreateScene("DamageReceiver3D", physics);
            _separateRoom = SceneManager.CreateScene("DamageReceiver3DSeparate", physics);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_room.isLoaded) { yield return SceneManager.UnloadSceneAsync(_room); }
            if (_separateRoom.isLoaded) { yield return SceneManager.UnloadSceneAsync(_separateRoom); }
            foreach (Game.Core.CharacterInfo asset in _assets)
            {
                Object.Destroy(asset);
            }
            _assets.Clear();
            TestSceneHelper.Cleanup();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Damage3D_FrontOnlyGuard_UsesFacingOnTheWorldFloorPlane()
        {
            CharacterActor3D defender = CreateActor(_room, CharacterBelong.Ally, Vector3.zero, 90f);
            CharacterActor3D front = CreateActor(_room, CharacterBelong.Enemy, new Vector3(2f, 2f, 0f));
            CharacterActor3D rear = CreateActor(_room, CharacterBelong.Enemy, new Vector3(-2f, 0f, 0f));
            yield return null;

            int hp = GameManager.Data.GetVitals(defender.ObjectHash).currentHp;
            GameManager.Data.GetCombatStats(defender.ObjectHash).guardStats.guardDirection = GuardDirection.Front;
            DamageReceiver receiver = defender.GetComponent<DamageReceiver>();
            Assert.IsTrue(receiver.IsAlive);
            receiver.SetGuarding(true);

            DamageResult frontHit = receiver.ReceiveDamage3D(Capture(front, defender), front.transform.position);
            Assert.AreEqual(GuardResult.JustGuard, frontHit.guardResult);
            Assert.AreEqual(hp, GameManager.Data.GetVitals(defender.ObjectHash).currentHp);

            DamageResult rearHit = receiver.ReceiveDamage3D(Capture(rear, defender), rear.transform.position);
            Assert.AreEqual(GuardResult.NoGuard, rearHit.guardResult);
            Assert.That(rearHit.totalDamage, Is.Positive);
            Assert.AreEqual(hp - rearHit.totalDamage, GameManager.Data.GetVitals(defender.ObjectHash).currentHp);
        }

        [UnityTest]
        public IEnumerator Damage3D_ForeignSceneFriendlyActorOrReusedGeneration_RejectsTheHit()
        {
            CharacterActor3D defender = CreateActor(_room, CharacterBelong.Ally, Vector3.zero);
            CharacterActor3D source = CreateActor(_room, CharacterBelong.Enemy, Vector3.forward * 2f);
            CharacterActor3D friend = CreateActor(_room, CharacterBelong.Ally, Vector3.back * 2f);
            CharacterActor3D foreign = CreateActor(_separateRoom, CharacterBelong.Enemy, Vector3.forward * 2f);
            yield return null;

            int hp = GameManager.Data.GetVitals(defender.ObjectHash).currentHp;
            DamageReceiver receiver = defender.GetComponent<DamageReceiver>();
            Assert.That(receiver.ReceiveDamage3D(Capture(friend, defender), friend.transform.position).totalDamage, Is.Zero);
            Assert.That(receiver.ReceiveDamage3D(Capture(foreign, defender), foreign.transform.position).totalDamage, Is.Zero);

            DamageData stale = Capture(source, defender);
            source.gameObject.SetActive(false);
            source.gameObject.SetActive(true);
            yield return null;

            Assert.AreNotEqual(stale.attackerRef, source.Actor);
            Assert.That(receiver.ReceiveDamage3D(stale, source.transform.position).totalDamage, Is.Zero);

            DamageData oldTarget = Capture(source, defender);
            defender.gameObject.SetActive(false);
            defender.gameObject.SetActive(true);
            yield return null;

            Assert.AreNotEqual(oldTarget.defenderRef, defender.Actor);
            Assert.That(receiver.ReceiveDamage3D(oldTarget, source.transform.position).totalDamage, Is.Zero);
            Assert.That(receiver.ReceiveDamage(Capture(source, defender)).totalDamage, Is.Zero);
            Assert.AreEqual(hp, GameManager.Data.GetVitals(defender.ObjectHash).currentHp);
            Assert.That(receiver.ReceiveDamage3D(Capture(source, defender), source.transform.position).totalDamage, Is.Positive);
        }

        private CharacterActor3D CreateActor(Scene scene, CharacterBelong belong, Vector3 position, float yaw = 0f)
        {
            Game.Core.CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(belong, CharacterFeature.Minion);
            _assets.Add(info);
            GameObject body = new GameObject("Damage3D Actor");
            body.SetActive(false);
            SceneManager.MoveGameObjectToScene(body, scene);
            body.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            CharacterActor3D actor = body.AddComponent<CharacterActor3D>();
            actor.SetCharacterInfoForTest(info);
            body.SetActive(true);
            return actor;
        }

        private static DamageData Capture(CharacterActor3D source, CharacterActor3D defender)
        {
            AttackMotionData motion = new AttackMotionData { motionValue = 1f, attackElement = Element.Slash };
            Assert.IsTrue(CombatDataHelper.TryCaptureAttack(source.ObjectHash, motion,
                GameManager.Data, GameManager.Actors, out AttackContext context));
            DamageData attack = CombatDataHelper.BuildDamageData(
                source.ObjectHash, defender.ObjectHash, motion, context.AttackStats);
            context.ApplyTo(ref attack, defender.Actor);
            return attack;
        }
    }
}
