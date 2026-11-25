using System.Collections;
using System.Collections.Generic;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using CharacterInfo = Game.Core.CharacterInfo;

namespace Game.Tests.PlayMode
{
    public sealed class EncounterGoal3DPlayTests
    {
        private readonly List<CharacterInfo> _characterInfos = new List<CharacterInfo>();
        private Scene _room;
        private ChallengeDefinition _challenge;

        [UnitySetUp]
        public IEnumerator CreateRoom()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            TestSceneHelper.CreateGameManager();
            _room = SceneManager.CreateScene(nameof(EncounterGoal3DPlayTests),
                new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            _challenge = ScriptableObject.CreateInstance<ChallengeDefinition>();
            _challenge.SetForTest("bridge-goal", "Bridge goal", ChallengeType.TimeAttack, timeLimit: 15f);
        }

        [UnityTearDown]
        public IEnumerator UnloadRoom()
        {
            if (_room.isLoaded) { yield return SceneManager.UnloadSceneAsync(_room); }
            foreach (CharacterInfo info in _characterInfos) { Object.Destroy(info); }
            _characterInfos.Clear();
            Object.Destroy(_challenge);
            TestSceneHelper.Cleanup();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Goal_KnightBelowTheVolumeOrOtherActorsInside_OnlyCompletesForDesignatedKnight()
        {
            Vector3 floorPosition = new Vector3(1f, 3f, 4f);
            CharacterActor3D knight = CreateActor(CharacterFeature.Player, new Vector3(1f, 0f, 4f));
            CreateActor(CharacterFeature.Player, floorPosition);
            CreateActor(CharacterFeature.Minion, floorPosition + Vector3.right);
            EncounterGoal3D encounter = CreateEncounter(knight, floorPosition + Vector3.up);
            List<ChallengeState> ends = new List<ChallengeState>();
            encounter.Ended += ends.Add;
            Assert.IsTrue(encounter.TryStartEncounter());
            Assert.IsFalse(encounter.TryStartEncounter());
            encounter.CompleteEncounter();
            Assert.IsTrue(encounter.IsRunning, "An explicit completion request still requires the knight to reach the goal.");

            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            Assert.IsTrue(encounter.IsRunning);
            Assert.IsEmpty(ends);

            knight.transform.position = floorPosition;
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(ChallengeState.Completed, encounter.Result.state);
            Assert.AreEqual("bridge-goal", encounter.Result.challengeId);
            CollectionAssert.AreEqual(new[] { ChallengeState.Completed }, ends);
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(1, ends.Count);
        }

        [UnityTest]
        public IEnumerator Goal_AbortAndActorGenerationChange_PreventsCompletionUntilNewAttempt()
        {
            CharacterActor3D knight = CreateActor(CharacterFeature.Player, Vector3.zero);
            EncounterGoal3D encounter = CreateEncounter(knight, new Vector3(0f, 1f, 4f));
            List<ChallengeState> ends = new List<ChallengeState>();
            encounter.Ended += ends.Add;
            Assert.IsTrue(encounter.TryStartEncounter());
            encounter.AbortEncounter();
            encounter.AbortEncounter();
            Assert.IsFalse(encounter.IsRunning);
            CollectionAssert.AreEqual(new[] { ChallengeState.Failed }, ends);

            Assert.IsTrue(encounter.TryStartEncounter());
            ActorRef first = knight.Actor;
            knight.gameObject.SetActive(false);
            knight.transform.position = new Vector3(0f, 0f, 4f);
            knight.gameObject.SetActive(true);
            Assert.AreNotEqual(first, knight.Actor);
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            Assert.IsFalse(encounter.IsRunning);
            CollectionAssert.AreEqual(new[] { ChallengeState.Failed, ChallengeState.Failed }, ends);

            Assert.IsTrue(encounter.TryStartEncounter());
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(ChallengeState.Completed, encounter.Result.state);
            CollectionAssert.AreEqual(new[] { ChallengeState.Failed, ChallengeState.Failed, ChallengeState.Completed }, ends);
        }

        [UnityTest]
        public IEnumerator Goal_SceneUnload_EndsTheActiveEncounterOnce()
        {
            CharacterActor3D knight = CreateActor(CharacterFeature.Player, Vector3.zero);
            EncounterGoal3D encounter = CreateEncounter(knight, new Vector3(0f, 1f, 4f));
            List<ChallengeState> ends = new List<ChallengeState>();
            encounter.Ended += ends.Add;
            Assert.IsTrue(encounter.TryStartEncounter());

            yield return SceneManager.UnloadSceneAsync(_room);

            CollectionAssert.AreEqual(new[] { ChallengeState.Failed }, ends);
        }

        private CharacterActor3D CreateActor(CharacterFeature feature, Vector3 position)
        {
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(CharacterBelong.Ally, feature);
            _characterInfos.Add(info);
            GameObject actor = new GameObject(feature.ToString());
            actor.SetActive(false);
            SceneManager.MoveGameObjectToScene(actor, _room);
            actor.transform.position = position;
            CharacterController body = actor.AddComponent<CharacterController>();
            body.height = 1.6f;
            body.center = Vector3.up * 0.8f;
            body.radius = 0.3f;
            CharacterActor3D character = actor.AddComponent<CharacterActor3D>();
            character.SetCharacterInfoForTest(info);
            actor.SetActive(true);
            return character;
        }

        private EncounterGoal3D CreateEncounter(CharacterActor3D knight, Vector3 center)
        {
            GameObject destination = new GameObject("Bridge goal");
            SceneManager.MoveGameObjectToScene(destination, _room);
            destination.transform.position = center;
            BoxCollider objective = destination.AddComponent<BoxCollider>();
            objective.isTrigger = true;
            objective.size = new Vector3(3f, 2f, 2f);
            EncounterGoal3D encounter = destination.AddComponent<EncounterGoal3D>();
            encounter.ConfigureForTest(knight, _challenge, objective);
            return encounter;
        }
    }
}
