using System;
using System.Collections;
using System.Collections.Generic;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using R3;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Game.Tests.PlayMode
{
    public sealed class MissionAttackSlots3DPlayTests
    {
        private readonly List<ScriptableObject> _assets = new();
        private Scene _room;
        private Scene _otherRoom;
        private MissionAttackSlots3D _slots;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            TestSceneHelper.CreateGameManager().GetComponent<GameManager>().enabled = false;
            _room = SceneManager.CreateScene("Mission attack room", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            _otherRoom = SceneManager.CreateScene("Other mission room", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            _slots = new MissionAttackSlots3D(GameManager.Actions, GameManager.Events);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _slots?.Dispose();
            if (_otherRoom.isLoaded) { yield return SceneManager.UnloadSceneAsync(_otherRoom); }
            if (_room.isLoaded) { yield return SceneManager.UnloadSceneAsync(_room); }
            foreach (ScriptableObject asset in _assets) { Object.Destroy(asset); }
            _assets.Clear();
            TestSceneHelper.Cleanup();
            yield return null;
        }

        [UnityTest]
        public IEnumerator AttackSlot_TwoAlliesRequestSameTargetTogether_OnlyAcceptedActionHoldsThroughRecovery()
        {
            CharacterActor3D first = CreateActor(_room, CharacterBelong.Ally, Vector3.zero);
            CharacterActor3D other = CreateActor(_room, CharacterBelong.Ally, Vector3.right * 2f);
            CharacterActor3D target = CreateActor(_room, CharacterBelong.Enemy, Vector3.forward * 1.5f);
            AttackInfo attack = CreateAttack();
            yield return null;
            Physics.SyncTransforms();

            Assert.IsTrue(_slots.TryExecute(first, target.Actor, attack, out long id));
            Assert.IsFalse(_slots.TryExecute(other, target.Actor, attack, out long denied));
            Assert.AreEqual(0, denied);
            Assert.AreEqual(1, _slots.ActiveCount);
            Assert.IsTrue(GameManager.Actions.Execution.TryGetInstance(id, out ActionExecution action));
            Assert.AreEqual(first.Actor, action.ownerRef);
            Assert.AreEqual(target.Actor, action.targetRef);
            Assert.AreEqual(95, GameManager.Data.GetVitals(first.ObjectHash).currentStamina);
            Assert.AreEqual(100, GameManager.Data.GetVitals(other.ObjectHash).currentStamina);

            GameManager.Actions.Execution.Tick(0.43f);
            Assert.IsTrue(GameManager.Actions.Execution.TryGetInstance(id, out action));
            Assert.AreEqual(AnimationPhase.Recovery, action.phase);
            Assert.Less(GameManager.Data.GetVitals(target.ObjectHash).currentHp, 100);
            Assert.IsFalse(_slots.TryExecute(other, target.Actor, attack, out _));

            GameManager.Actions.Execution.Tick(0.3f);
            Assert.AreEqual(0, _slots.ActiveCount);
            Assert.IsTrue(_slots.TryExecute(other, target.Actor, attack, out _));
        }

        [UnityTest]
        public IEnumerator AttackSlot_RejectedOrInterruptedExecution_FreesReservationForTheNextAlly()
        {
            CharacterActor3D first = CreateActor(_room, CharacterBelong.Ally, Vector3.zero);
            CharacterActor3D other = CreateActor(_room, CharacterBelong.Ally, Vector3.right * 2f);
            CharacterActor3D target = CreateActor(_room, CharacterBelong.Enemy, Vector3.forward * 1.5f);
            AttackInfo attack = CreateAttack();
            attack.mpCost = 3;
            yield return null;
            GameManager.Data.GetVitals(first.ObjectHash).currentMp = 0;

            Assert.IsFalse(_slots.TryExecute(first, target.Actor, attack, out _));
            Assert.AreEqual(0, _slots.ActiveCount);
            Assert.IsTrue(_slots.TryExecute(other, target.Actor, attack, out long accepted));
            Assert.IsTrue(GameManager.Actions.Execution.Cancel(accepted, force: true));
            Assert.AreEqual(0, _slots.ActiveCount);
            GameManager.Data.GetVitals(first.ObjectHash).currentMp = 3;
            Assert.IsTrue(_slots.TryExecute(first, target.Actor, attack, out _));
        }

        [UnityTest]
        public IEnumerator AttackSlot_ActorGenerationsOrPhysicsWorldChange_CannotKeepOrReuseReservation()
        {
            CharacterActor3D first = CreateActor(_room, CharacterBelong.Ally, Vector3.zero);
            CharacterActor3D other = CreateActor(_room, CharacterBelong.Ally, Vector3.right * 2f);
            CharacterActor3D target = CreateActor(_room, CharacterBelong.Enemy, Vector3.forward * 1.5f);
            CharacterActor3D remote = CreateActor(_otherRoom, CharacterBelong.Enemy, Vector3.forward * 1.5f);
            AttackInfo attack = CreateAttack();
            yield return null;

            Assert.IsFalse(_slots.TryExecute(first, other.Actor, attack, out _));
            Assert.IsFalse(_slots.TryExecute(first, remote.Actor, attack, out _));
            Assert.AreEqual(0, _slots.ActiveCount);

            ActorRef original = target.Actor;
            Assert.IsTrue(_slots.TryExecute(first, original, attack, out long originalAttack));
            Assert.IsTrue(GameManager.Actions.Execution.TryGetInstance(originalAttack, out ActionExecution execution));
            target.gameObject.SetActive(false);
            Assert.AreEqual(0, _slots.ActiveCount);
            Assert.IsFalse(GameManager.Actions.Execution.IsGroupActive(execution.groupId));

            target.gameObject.SetActive(true);
            Assert.AreNotEqual(original, target.Actor);
            Assert.IsFalse(_slots.TryExecute(other, original, attack, out _));
            Assert.IsTrue(_slots.TryExecute(other, target.Actor, attack, out _));

            other.gameObject.SetActive(false);
            Assert.AreEqual(0, _slots.ActiveCount);
            Assert.IsTrue(_slots.TryExecute(first, target.Actor, attack, out _));
            GameManager.Data.GetVitals(target.ObjectHash).currentHp = 0;
            GameManager.Events.FireCharacterDeath(target.Actor, first.Actor);
            Assert.AreEqual(0, _slots.ActiveCount);
        }

        [UnityTest]
        public IEnumerator AttackSlot_StartNotificationReentersAndRemovesTarget_ProvisionalReservationCancelsAfterAcceptance()
        {
            CharacterActor3D first = CreateActor(_room, CharacterBelong.Ally, Vector3.zero);
            CharacterActor3D other = CreateActor(_room, CharacterBelong.Ally, Vector3.right * 2f);
            CharacterActor3D target = CreateActor(_room, CharacterBelong.Enemy, Vector3.forward * 1.5f);
            AttackInfo attack = CreateAttack();
            yield return null;

            ActorRef original = target.Actor;
            bool rivalAccepted = true;
            int reservedDuringStart = 0;
            using IDisposable listener = GameManager.Events.OnActionExecution.Subscribe(notification =>
            {
                if (notification.kind != ActionSignalKind.Started || notification.execution.ownerRef != first.Actor) { return; }
                reservedDuringStart = _slots.ActiveCount;
                rivalAccepted = _slots.TryExecute(other, original, attack, out _);
                target.gameObject.SetActive(false);
            });
            Assert.IsTrue(_slots.TryExecute(first, original, attack, out long accepted));
            Assert.AreEqual(1, reservedDuringStart);
            Assert.IsFalse(rivalAccepted);
            Assert.AreEqual(0, _slots.ActiveCount);
            Assert.IsFalse(GameManager.Actions.Execution.TryGetInstance(accepted, out _));
        }

        private CharacterActor3D CreateActor(Scene scene, CharacterBelong belong, Vector3 position)
        {
            Game.Core.CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(belong, CharacterFeature.Minion);
            _assets.Add(info);
            GameObject body = new GameObject("Mission actor");
            body.SetActive(false);
            body.transform.position = position;
            body.layer = LayerMask.NameToLayer(belong == CharacterBelong.Ally ? "Player" : "Enemy");
            SceneManager.MoveGameObjectToScene(body, scene);
            CharacterController capsule = body.AddComponent<CharacterController>();
            capsule.height = 1.8f;
            capsule.center = Vector3.up * 0.9f;
            capsule.radius = 0.3f;
            CharacterActor3D actor = body.AddComponent<CharacterActor3D>();
            actor.SetCharacterInfoForTest(info);
            body.SetActive(true);
            return actor;
        }

        private AttackInfo CreateAttack()
        {
            MotionDefinition motion = ScriptableObject.CreateInstance<MotionDefinition>();
            motion.info = new MotionInfo { preMotionDuration = 0.2f, activeMotionDuration = 0.2f, recoveryDuration = 0.2f };
            _assets.Add(motion);
            AttackInfo attack = ScriptableObject.CreateInstance<AttackInfo>();
            attack.motion = motion;
            attack.damageMultiplier = 1f;
            attack.staminaCost = 5f;
            attack.attackElement = Element.Slash;
            attack.attack3D = new AttackProfile3D
            {
                shape = AttackShape3D.Melee,
                localCenter = new Vector3(0f, 0.9f, 1f),
                halfExtents = new Vector3(0.65f, 0.6f, 0.9f),
                targetLayers = LayerMask.GetMask("Player", "Enemy"),
                obstructionLayers = LayerMask.GetMask("Ground"),
            };
            _assets.Add(attack);
            return attack;
        }
    }
}
