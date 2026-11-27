#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
    public sealed class ExpeditionQuest3DScenePlayTests
    {
        private const string k_ScenePath = "Assets/Scenes/ExpeditionQuest3DTestScene.unity";
        private const string k_SpeciesId = "goblin-engineer";
        private Scene _scene;
        private Type _dialogueManager;
        private Type _lua;
        private float _previousTimeScale;

        [UnityTest]
        public IEnumerator AuthoredNPCQuest_ReportsOnlyAfterReachingThe3DObjective_DeploysAidThatFollowsKnight()
        {
            _previousTimeScale = Time.timeScale;
            Time.timeScale = 1f;
            TestSceneHelper.Cleanup();
            yield return null;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(k_ScenePath,
                new LoadSceneParameters(LoadSceneMode.Additive, LocalPhysicsMode.Physics3D));
            _scene = SceneManager.GetSceneByPath(k_ScenePath);
            SceneManager.SetActiveScene(_scene);
            yield return null;
            yield return ExpeditionEvent3DScenePlayTests.WaitForJournalRestore();

            // Pixel Crushers and its adapters are in predefined assemblies that the test asmdef cannot reference.
            _dialogueManager = FindIntegrationType("PixelCrushers.DialogueSystem.DialogueManager");
            _lua = FindIntegrationType("PixelCrushers.DialogueSystem.Lua");
            Type questGiverType = FindIntegrationType("Game.Runtime.ExpeditionQuestGiver");
            Component giver = _scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren(questGiverType, true)).Single();
            CharacterActor3D knight = (CharacterActor3D)ReadField(giver, "knight", BindingFlags.NonPublic);
            BoxCollider objective = (BoxCollider)ReadField(giver, "objective", BindingFlags.NonPublic);
            ExpeditionSummonDeployment3D deployment = FindInRoom<ExpeditionSummonDeployment3D>();
            NpcInteraction3D sister = deployment.GetComponent<NpcInteraction3D>();
            Assert.IsNotNull(sister);
            NpcInteraction3D questNpc = _scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<NpcInteraction3D>(true)).Single(npc => npc != sister);
            Assert.IsTrue(knight.Actor.IsValid);
            knight.GetComponent<PlayerCharacterInput3D>().enabled = false;
            ExpeditionSquadPanel panel = FindInRoom<ExpeditionSquadPanel>();
            ExpeditionSquad squad = GameManager.GetSubManager<ExpeditionSquadManager>()?.Squad;
            Assert.IsNotNull(squad);

            Assert.That(questGiverType.GetProperty("Stage").GetValue(giver), Is.EqualTo(0),
                "The authored scene's journal must open with the quest available.");
            Assert.AreEqual(0, ReadStage());
            Assert.IsEmpty(squad.GetUnlockedSpecies());
            Assert.IsFalse((bool)RunLua("return ExpeditionReportQuest()", "asBool"));

            Talk(knight, questNpc);
            yield return ChooseResponse("ExpeditionAcceptQuest");
            Assert.AreEqual(1, ReadStage());
            Assert.IsFalse((bool)RunLua("return ExpeditionAcceptQuest()", "asBool"));
            yield return CloseConversation();

            Vector3 center = objective.transform.TransformPoint(objective.center);
            Vector3 bodyOffset = knight.Body.transform.TransformVector(knight.Body.center);
            Place(knight, center - bodyOffset + Vector3.up * (objective.bounds.size.y + knight.Body.height));
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(1, ReadStage(), "An identical XZ position above the goal must not progress the quest.");
            Assert.IsFalse((bool)RunLua("return ExpeditionReportQuest()", "asBool"));
            Assert.IsEmpty(squad.GetUnlockedSpecies());

            Place(knight, center - bodyOffset);
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(2, ReadStage());
            Assert.IsEmpty(squad.GetUnlockedSpecies());

            Talk(knight, questNpc);
            yield return ChooseResponse("ExpeditionReportQuest");
            Assert.AreEqual(3, ReadStage());
            Assert.IsFalse((bool)RunLua("return ExpeditionReportQuest()", "asBool"));
            Assert.AreEqual(k_SpeciesId, squad.GetUnlockedSpecies().Single().SpeciesId);
            Assert.IsTrue(GameManager.ExpeditionAid.IsUnlocked(k_SpeciesId));
            yield return CloseConversation();

            Assert.IsFalse(panel.gameObject.activeInHierarchy);
            Talk(knight, sister);
            yield return ChooseResponse("ExpeditionOpenSquadPanel");
            yield return CloseConversation();
            Assert.IsTrue(panel.isActiveAndEnabled);
            VisualElement squadRoot = panel.GetComponent<UIDocument>().rootVisualElement;
            VisualElement speciesRow = squadRoot.Q("expedition-species-row");
            Assert.AreEqual(k_SpeciesId, speciesRow.Q<Label>("expedition-species-name").text);
            Submit(squadRoot.Q<Button>("expedition-increase"));
            Button loadPreset = squadRoot.Q<Button>("expedition-preset-load");
            Assert.IsFalse(loadPreset.enabledInHierarchy);
            Submit(squadRoot.Q<Button>("expedition-preset-save"));
            Assert.AreEqual("枠 1", squadRoot.Q<DropdownField>("expedition-preset-slot").value);
            Submit(squadRoot.Q<Button>("expedition-decrease"));
            Assert.AreEqual("0", squadRoot.Q<Label>("expedition-quantity").text);
            Submit(loadPreset);
            Assert.AreEqual("1", squadRoot.Q<Label>("expedition-quantity").text);
            Assert.IsEmpty(squad.Confirmed);
            Submit(squadRoot.Q<Button>("expedition-confirm"));
            Assert.AreEqual(k_SpeciesId, squad.Confirmed.Single().SpeciesId);
            Assert.AreEqual(1, squad.ConfirmedCount);
            Submit(squadRoot.Q<Button>("expedition-close"));
            Assert.IsFalse(panel.isActiveAndEnabled);

            Talk(knight, sister);
            yield return ChooseResponse("ExpeditionTryDeployConfirmed");
            yield return CloseConversation();
            var summons = new List<ExpeditionSummonDeployment3D.DeployedSummon>();
            deployment.CopyActiveSummons(summons);
            Assert.That(summons.Count, Is.EqualTo(1));
            Assert.AreEqual(k_SpeciesId, summons[0].SpeciesId);
            ActorRef deployedActor = summons[0].Actor;
            Assert.IsTrue(GameManager.Actors.IsCurrent(deployedActor));
            CharacterActor3D summon = GameManager.Data.GetManaged(deployedActor.Hash) as CharacterActor3D;
            Assert.IsNotNull(summon);

            yield return VerifySummonFollowsKnight(knight, summon);

            deployment.CopyActiveSummons(summons);
            Assert.That(summons.Single().Actor, Is.EqualTo(deployedActor));
            Assert.IsTrue(GameManager.Actors.IsCurrent(deployedActor));
            Assert.That(summon.gameObject.scene, Is.EqualTo(_scene));
        }

        [UnityTearDown]
        public IEnumerator UnloadRoom()
        {
            if (IsConversationActive()) { _dialogueManager.GetMethod("StopConversation").Invoke(null, null); }
            if (_scene.isLoaded) { yield return SceneManager.UnloadSceneAsync(_scene); }
            Time.timeScale = _previousTimeScale;
            TestSceneHelper.Cleanup();
            yield return null;
        }

        private static void Talk(CharacterActor3D knight, NpcInteraction3D npc)
        {
            Vector3 targetFeet = npc.transform.position + Vector3.back;
            Place(knight, targetFeet - (knight.Motor.FeetPosition - knight.transform.position));
            Assert.IsTrue(npc.CanInteract(knight.Actor.Hash));
            npc.Interact(knight.Actor.Hash);
        }

        private static void Place(CharacterActor3D knight, Vector3 position)
        {
            knight.Body.enabled = false;
            knight.transform.position = position;
            knight.Body.enabled = true;
            Physics.SyncTransforms();
        }

        private static IEnumerator VerifySummonFollowsKnight(CharacterActor3D knight, CharacterActor3D summon)
        {
            const float arrivalTolerance = 0.15f;
            const float followDistanceTolerance = 2.5f;
            Vector3 summonStart = summon.Motor.FeetPosition;
            // The southwest side of the mine is open floor behind the Sister interaction point.
            Vector3 destination = new Vector3(1.4f, 0f, -6.4f);
            for (int step = 0; step < 120; step++)
            {
                Vector3 offset = destination - knight.Motor.FeetPosition;
                offset.y = 0f;
                if (offset.sqrMagnitude <= arrivalTolerance * arrivalTolerance) { break; }
                knight.Motor.Simulate(offset.normalized, false, Time.fixedDeltaTime);
                yield return new WaitForFixedUpdate();
            }
            Assert.That(Vector3.Distance(knight.Motor.FeetPosition, destination), Is.LessThan(0.25f));

            for (int step = 0; step < 90 && Vector3.Distance(knight.Motor.FeetPosition, summon.Motor.FeetPosition) > followDistanceTolerance; step++)
            {
                yield return new WaitForFixedUpdate();
            }
            Assert.That(Vector3.Distance(summon.Motor.FeetPosition, summonStart), Is.GreaterThan(2f));
            Assert.That(Vector3.Distance(knight.Motor.FeetPosition, summon.Motor.FeetPosition), Is.LessThanOrEqualTo(followDistanceTolerance));
        }

        private IEnumerator ChooseResponse(string script)
        {
            Type argsType = FindIntegrationType("PixelCrushers.DialogueSystem.SelectedResponseEventArgs");
            for (int frame = 0; frame < 60; frame++)
            {
                object state = ReadDialogue("currentConversationState");
                object view = ReadDialogue("conversationView");
                if (state != null && view != null)
                {
                    Array responses = (Array)ReadField(state, "pcResponses");
                    foreach (object response in responses)
                    {
                        object entry = ReadField(response, "destinationEntry");
                        string userScript = (string)ReadField(entry, "userScript");
                        if ((bool)ReadField(response, "enabled") && userScript != null && userScript.Contains(script))
                        {
                            Assert.AreEqual(0f, Time.timeScale);
                            object selection = Activator.CreateInstance(argsType, response);
                            view.GetType().GetMethod("SelectResponse").Invoke(view, new[] { selection });
                            yield return null;
                            yield break;
                        }
                    }

                    view.GetType().GetMethod("OnConversationContinueAll").Invoke(view, null);
                }

                yield return null;
            }

            Assert.Fail($"The authored conversation did not provide a response for {script}.");
        }

        private IEnumerator CloseConversation()
        {
            if (IsConversationActive()) { _dialogueManager.GetMethod("StopConversation").Invoke(null, null); }
            yield return null;
            Assert.IsFalse(IsConversationActive());
            Assert.That(Time.timeScale, Is.GreaterThan(0f));
        }

        private bool IsConversationActive() => _dialogueManager != null && (bool)ReadDialogue("isConversationActive");

        private static void Submit(Button button)
        {
            Assert.IsNotNull(button);
            Assert.IsTrue(button.enabledInHierarchy);
            Assert.IsNotNull(button.panel);
            using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = button;
                button.SendEvent(submit);
            }
        }

        private object ReadDialogue(string property) => _dialogueManager.GetProperty(property).GetValue(null);

        private int ReadStage() => (int)RunLua("return ExpeditionQuestStage()", "asInt");

        private object RunLua(string script, string result)
        {
            MethodInfo run = _lua.GetMethod("Run", new[] { typeof(string), typeof(bool), typeof(bool) });
            object value = run.Invoke(null, new object[] { script, false, true });
            return value.GetType().GetProperty(result).GetValue(value);
        }

        private T FindInRoom<T>() where T : Component => _scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<T>(true)).Single();

        private static object ReadField(object instance, string name, BindingFlags visibility = BindingFlags.Public)
            => instance.GetType().GetField(name, BindingFlags.Instance | visibility).GetValue(instance);

        private static Type FindIntegrationType(string name) => Type.GetType($"{name}, Assembly-CSharp")
            ?? Type.GetType($"{name}, Assembly-CSharp-firstpass", throwOnError: true);
    }
}
#endif
