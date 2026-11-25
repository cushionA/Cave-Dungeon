#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Game.Tests.PlayMode
{
    public sealed class ExpeditionMock3DScenePlayTests
    {
        private const string k_Mock = "Assets/Scenes/ExpeditionMock3DScene.unity";
        private const string k_PassageA = "Assets/Scenes/StreamingAreaA3DTestScene.unity";
        private const string k_PassageB = "Assets/Scenes/StreamingAreaB3DTestScene.unity";
        private Scene _mock;

        [UnityTearDown]
        public IEnumerator UnloadMock()
        {
            foreach (string path in new[] { k_PassageA, k_PassageB })
            {
                Scene passage = SceneManager.GetSceneByPath(path);
                if (passage.isLoaded) { yield return SceneManager.UnloadSceneAsync(passage); }
            }
            if (_mock.isLoaded) { yield return SceneManager.UnloadSceneAsync(_mock); }
            TestSceneHelper.Cleanup();
            yield return null;
        }

        [UnityTest]
        public IEnumerator AuthoredExpedition_SisterWardsKnightAndNativeRouteStreamsBothPassages()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(k_Mock,
                new LoadSceneParameters(LoadSceneMode.Additive));
            _mock = SceneManager.GetSceneByPath(k_Mock);
            yield return new WaitForFixedUpdate();

            CharacterActor3D knight = Find<CharacterActor3D>("[PLACEHOLDER] Knight");
            CharacterActor3D sister = Find<CharacterActor3D>("[PLACEHOLDER] Expedition sister");
            CompanionController3D companion = sister.GetComponent<CompanionController3D>();
            LevelStreamingController streaming = GameManager.LevelStreaming;
            PlayerCharacterInput3D input = knight.GetComponent<PlayerCharacterInput3D>();
            Assert.IsNotNull(companion);
            Assert.IsNotNull(streaming);
            Assert.IsTrue(knight.IsAlive);
            Assert.IsTrue(sister.IsAlive);
            ActorRef knightRef = knight.Actor;
            ActorRef sisterRef = sister.Actor;
            float sisterStart = sister.transform.position.z;
            input.enabled = false;

            bool warded = false;
            for (int step = 0; step < 100 && !warded; step++)
            {
                knight.Motor.Simulate(Vector3.zero, false, Time.fixedDeltaTime);
                warded = companion.TryCooperate(knight);
                if (!warded) { yield return new WaitForFixedUpdate(); }
            }
            Assert.IsTrue(warded);
            Assert.IsTrue(companion.IsCooperating);
            Assert.That(GameManager.Data.GetStatusEffects(knightRef.Hash).DefenseMultiplier, Is.GreaterThan(1f));

            yield return WalkForward(knight, 1f);
            yield return WaitForArea(streaming, k_PassageA);
            yield return WalkForward(knight, 5.8f);
            yield return WaitForArea(streaming, k_PassageB);
            Assert.IsTrue(SceneManager.GetSceneByPath(k_PassageA).isLoaded);
            Assert.That(sister.transform.position.z, Is.GreaterThan(sisterStart + 5f));

            yield return WalkForward(knight, 9.7f);
            float deadline = Time.realtimeSinceStartup + 5f;
            while (streaming.Orchestrator.IsLoaded(k_PassageA) && Time.realtimeSinceStartup < deadline) { yield return null; }
            Assert.IsFalse(SceneManager.GetSceneByPath(k_PassageA).isLoaded);
            Scene passageB = SceneManager.GetSceneByPath(k_PassageB);
            Assert.IsTrue(passageB.isLoaded);
            Assert.IsTrue(knight.Motor.IsGrounded);
            Assert.That(knight.Motor.Ground.Surface.gameObject.scene, Is.EqualTo(passageB));
            Assert.That(knight.Actor, Is.EqualTo(knightRef));
            Assert.That(sister.Actor, Is.EqualTo(sisterRef));
            Assert.That(knight.gameObject.scene, Is.EqualTo(_mock));
            Assert.That(sister.gameObject.scene, Is.EqualTo(_mock));

            BossEncounter3D encounter = Find<BossEncounter3D>("Cave Sentinel encounter");
            CharacterActor3D sentinel = Find<CharacterActor3D>("[PLACEHOLDER] Cave Sentinel");
            UIDocument cycleDocument = Find<UIDocument>("Expedition next playthrough");
            Button nextCycle = cycleDocument.rootVisualElement.Q<Button>("next-cycle-open");
            Assert.IsNotNull(nextCycle);
            Assert.That(nextCycle.style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.IsTrue(encounter.IsRunning);

            yield return WalkForward(knight, sentinel.transform.position.z - 1.75f);
            Assert.IsTrue(knight.Motor.IsGrounded);
            Assert.That(knight.Motor.Ground.Surface.gameObject.scene, Is.EqualTo(_mock));

            AttackInfo sword = new SerializedObject(input).FindProperty("attack3D").objectReferenceValue as AttackInfo;
            Assert.IsNotNull(sword);
            Assert.That(sword.attack3D.shape, Is.EqualTo(AttackShape3D.Melee));
            GameActionRuntime actions = GameManager.Actions;
            for (int strike = 0; strike < 16 && sentinel.IsAlive; strike++)
            {
                int healthBefore = GameManager.Data.GetVitals(sentinel.ObjectHash).currentHp;
                Assert.IsTrue(actions.Execute3D(knight, sword, out long instanceId));
                for (int step = 0; step < 60 && actions.Execution.TryGetInstance(instanceId, out _); step++)
                {
                    yield return new WaitForFixedUpdate();
                }
                Assert.IsFalse(actions.Execution.TryGetInstance(instanceId, out _));
                Assert.That(GameManager.Data.GetVitals(sentinel.ObjectHash).currentHp, Is.LessThan(healthBefore));
            }

            Assert.IsFalse(sentinel.IsAlive);
            Assert.That(encounter.Result.state, Is.EqualTo(ChallengeState.Completed));
            PlayerProgression3D growth = knight.GetComponent<PlayerProgression3D>();
            Assert.AreEqual(2, growth.Level);
            Assert.AreEqual(150, growth.CurrentExp);
            yield return null;
            Assert.IsTrue(GameManager.NextPlaythrough.CanBeginNext);
            Assert.That(nextCycle.style.display.value, Is.EqualTo(DisplayStyle.Flex));
        }

        [UnityTest]
        public IEnumerator AuthoredExpedition_EquipmentTableAndSurveyLeadToPlayerGrowth()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(k_Mock, new LoadSceneParameters(LoadSceneMode.Additive));
            _mock = SceneManager.GetSceneByPath(k_Mock);
            yield return new WaitForFixedUpdate();

            CharacterActor3D knight = Find<CharacterActor3D>("[PLACEHOLDER] Knight");
            knight.GetComponent<PlayerCharacterInput3D>().enabled = false;
            PlayerInput input = knight.GetComponent<PlayerInput>();
            PlayerCombatLoadout3D loadout = knight.GetComponent<PlayerCombatLoadout3D>();
            PlayerProgression3D progression = knight.GetComponent<PlayerProgression3D>();
            UIDocument document = Find<UIDocument>("Expedition loadout and growth");
            NpcInteraction3D table = Find<NpcInteraction3D>("Expedition equipment table");
            int hash = knight.Actor.Hash;
            Assert.AreEqual(4201, loadout.CurrentWeaponId);
            Assert.AreEqual(4211, loadout.CurrentCoreId);
            Assert.AreEqual(4221, loadout.GetSpellId(0));
            Assert.AreEqual(1, progression.Level);
            int intelligence = GameManager.Data.GetAttributes(hash).intel;

            yield return WalkNear(knight, table.transform.position);
            Assert.IsTrue(table.CanInteract(hash));
            table.Interact(hash);
            Assert.AreEqual("UI", input.currentActionMap.name);
            VisualElement root = document.rootVisualElement;
            Submit(root, "loadout-weapon-4202");
            Assert.AreEqual(4202, loadout.CurrentWeaponId);
            Submit(root, "loadout-tab-cores");
            Submit(root, "loadout-core-4212");
            Assert.AreEqual(4212, loadout.CurrentCoreId);
            Assert.AreEqual(intelligence + 4, GameManager.Data.GetAttributes(hash).intel);
            Submit(root, "loadout-tab-magic");
            Submit(root, "loadout-magic-slot-1");
            Submit(root, "loadout-magic-4222");
            Assert.AreEqual(1, loadout.ActiveSpellSlot);
            Assert.AreEqual(4222, loadout.GetSpellId(1));
            Submit(root, "loadout-close");
            Assert.AreEqual("Player", input.currentActionMap.name);

            ExplorationExperience3D survey = Find<ExplorationExperience3D>("Survey point");
            yield return WalkNear(knight, survey.transform.position);
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(2, progression.Level);
            Assert.AreEqual(3, progression.AvailablePoints);
            int strength = GameManager.Data.GetAttributes(hash).str;

            document.GetComponent<ExpeditionLoadoutPanel3D>().Open();
            Submit(root, "loadout-tab-growth");
            Submit(root, "loadout-stat-Str");
            Assert.AreEqual(2, progression.AvailablePoints);
            Assert.AreEqual(strength + 1, GameManager.Data.GetAttributes(hash).str);
            Submit(root, "loadout-close");
            Assert.AreEqual("Player", input.currentActionMap.name);
        }

        private T Find<T>(string name) where T : Component => _mock.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<T>(true))
            .Single(item => item.name == name);

        private static void Submit(VisualElement root, string name)
        {
            Button button = root.Q<Button>(name);
            Assert.IsNotNull(button, name);
            using NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled();
            submit.target = button;
            button.SendEvent(submit);
        }

        private static IEnumerator WalkNear(CharacterActor3D knight, Vector3 destination)
        {
            for (int step = 0; step < 250; step++)
            {
                Vector3 offset = destination - knight.Motor.FeetPosition;
                offset.y = 0f;
                if (offset.sqrMagnitude < 0.16f) { yield break; }
                knight.Motor.Simulate(offset.normalized, false, Time.fixedDeltaTime);
                yield return new WaitForFixedUpdate();
            }
            Assert.Fail($"Could not reach {destination} from {knight.Motor.FeetPosition}.");
        }

        private static IEnumerator WalkForward(CharacterActor3D knight, float targetZ)
        {
            for (int step = 0; step < 350 && knight.transform.position.z < targetZ; step++)
            {
                knight.Motor.Simulate(Vector3.forward, false, Time.fixedDeltaTime);
                yield return new WaitForFixedUpdate();
            }
            Assert.That(knight.transform.position.z, Is.GreaterThanOrEqualTo(targetZ));
        }

        private static IEnumerator WaitForArea(LevelStreamingController streaming, string path)
        {
            float deadline = Time.realtimeSinceStartup + 5f;
            while (!streaming.Orchestrator.IsLoaded(path) && Time.realtimeSinceStartup < deadline) { yield return null; }
            Assert.IsTrue(streaming.Orchestrator.IsLoaded(path));
            Assert.IsTrue(SceneManager.GetSceneByPath(path).isLoaded);
        }
    }
}
#endif
