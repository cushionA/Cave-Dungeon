#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Game.Tests.PlayMode
{
    public sealed class GroupBattle3DScenePlayTests
    {
        private const string k_ScenePath = "Assets/Scenes/GroupBattle3DTestScene.unity";
        private const float k_Step = 0.02f;

        private Scene _scene;

        [UnityTearDown]
        public IEnumerator UnloadRoom()
        {
            if (_scene.isLoaded) { yield return SceneManager.UnloadSceneAsync(_scene); }
            TestSceneHelper.Cleanup();
            yield return null;
        }

        [UnityTest]
        public IEnumerator AuthoredRoom_ShieldsSplitStationsAndOnlyTheKnightCanReachTheObjective()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(k_ScenePath,
                new LoadSceneParameters(LoadSceneMode.Additive, LocalPhysicsMode.Physics3D));
            _scene = SceneManager.GetSceneByPath(k_ScenePath);

            CharacterActor3D knight = Find("[PLACEHOLDER] Knight").GetComponent<CharacterActor3D>();
            knight.GetComponent<PlayerCharacterInput3D>().enabled = false;
            CharacterActor3D shieldA = Find("Shield ally A").GetComponent<CharacterActor3D>();
            CharacterActor3D shieldB = Find("Shield ally B").GetComponent<CharacterActor3D>();
            Transform front = Find("Allied front").transform;
            Transform reserve = Find("Allied reserve").transform;
            EncounterCombat3D encounter = Find("Group battle").GetComponent<EncounterCombat3D>();
            EncounterGoal3D objective = encounter.GetComponent<EncounterGoal3D>();
            Assert.AreEqual(EncounterTraits.GroupBattle, objective.Traits);
            UIDocument hud = Find("[PLACEHOLDER] 3D warning HUD").GetComponent<UIDocument>();
            Label goalLabel = hud.rootVisualElement.Q<Label>("group-battle-goal");
            Assert.IsNotNull(goalLabel);

            for (int i = 0; i < 30 && encounter.GetAssignedStation(shieldA) == null; i++)
            {
                yield return new WaitForFixedUpdate();
            }
            Assert.IsTrue(objective.IsRunning);
            Assert.That(new[] { encounter.GetAssignedStation(shieldA), encounter.GetAssignedStation(shieldB) },
                Is.EquivalentTo(new[] { front, reserve }));

            CharacterActor3D bridgeDefender = encounter.GetAssignedStation(shieldA) == front ? shieldA : shieldB;
            CharacterActor3D sideDefender = bridgeDefender == shieldA ? shieldB : shieldA;
            BoxCollider bridge = Find("Narrow bridge").GetComponent<BoxCollider>();
            for (int i = 0; i < 150 && (bridgeDefender.Motor.Ground.Surface != bridge
                || Vector3.Distance(bridgeDefender.Motor.FeetPosition, front.position) > 0.55f
                || Vector3.Distance(sideDefender.Motor.FeetPosition, reserve.position) > 0.55f); i++)
            {
                yield return new WaitForFixedUpdate();
            }
            Assert.That(Vector3.Distance(bridgeDefender.Motor.FeetPosition, front.position), Is.LessThanOrEqualTo(0.55f));
            Assert.That(Vector3.Distance(sideDefender.Motor.FeetPosition, reserve.position), Is.LessThanOrEqualTo(0.55f));
            Assert.AreSame(bridge, bridgeDefender.Motor.Ground.Surface);

            Assert.IsTrue(encounter.TryIssue(knight, BroadcastAction.DefendPoint,
                position: front.position, surface: bridge));
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(BroadcastAction.DefendPoint, encounter.ActiveOrder);
            Assert.AreEqual(1, encounter.AssignedOrderCount);

            BoxCollider goal = Find("Knight goal bounds").GetComponent<BoxCollider>();
            CharacterActor3D sister = Find("[PLACEHOLDER] Sister").GetComponent<CharacterActor3D>();
            EncounterCombatant3D sisterCombatant = sister.GetComponent<EncounterCombatant3D>();
            sisterCombatant.enabled = false;
            Place(sister.Motor, goal.transform.position + Vector3.up * 0.08f);
            yield return new WaitForFixedUpdate();
            Assert.IsTrue(objective.IsRunning);
            Place(sister.Motor, Find("Sister post").transform.position + Vector3.up * 0.08f);
            sisterCombatant.enabled = true;

            Place(knight.Motor, knight.transform.position);
            for (int i = 0; i < 50 && knight.Motor.FeetPosition.x < goal.transform.position.x; i++)
            {
                knight.Motor.Simulate(Vector3.right, false, k_Step);
                Physics.SyncTransforms();
                yield return new WaitForFixedUpdate();
            }
            Assert.That(knight.Motor.FeetPosition.x, Is.EqualTo(goal.transform.position.x).Within(0.15f));
            for (int i = 0; i < 210 && objective.IsRunning && knight.Motor.FeetPosition.z < goal.transform.position.z; i++)
            {
                knight.Motor.Simulate(Vector3.forward, false, k_Step);
                Physics.SyncTransforms();
                yield return new WaitForFixedUpdate();
            }
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(ChallengeState.Completed, objective.Result.state,
                $"Knight feet {knight.Motor.FeetPosition}, body {knight.Body.bounds}, goal {goal.bounds}.");
            Assert.AreEqual("test-bridge-goal", objective.Result.challengeId);
            Assert.AreEqual("Room floor", knight.Motor.Ground.Surface.name);
            yield return new WaitForFixedUpdate();
            yield return null;
            Assert.IsNull(encounter.ActiveOrder);
            Assert.IsNull(encounter.GetAssignedStation(shieldA));
            Assert.AreEqual("目標地点に到達", goalLabel.text);
        }

        private static void Place(CharacterMotor3D motor, Vector3 position)
        {
            motor.enabled = false;
            motor.transform.position = position;
            Physics.SyncTransforms();
            motor.enabled = true;
            for (int i = 0; i < 12; i++) { motor.Simulate(Vector3.zero, false, k_Step); }
        }

        private GameObject Find(string name) => _scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Single(item => item.name == name).gameObject;
    }
}
#endif
