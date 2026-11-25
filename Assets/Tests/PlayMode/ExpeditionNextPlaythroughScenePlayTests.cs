#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
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
    public sealed class ExpeditionNextPlaythroughScenePlayTests
    {
        private const string k_ScenePath = "Assets/Scenes/BossArena3DTestScene.unity";
        private const string k_BossName = "[PLACEHOLDER] Cave Sentinel";
        private const string k_EncounterName = "Cave Sentinel encounter";
        private const string k_PanelName = "Expedition next playthrough";

        private Scene _initialScene;
        private Scene _sourceScene;
        private Scene _restartedScene;
        private int _restarts;
        private string _storePath;

        [UnityTearDown]
        public IEnumerator UnloadArena()
        {
            SceneManager.sceneLoaded -= RecordRestart;
            if (_restartedScene.isLoaded)
            {
                Scene replacement = _initialScene.isLoaded ? _initialScene : SceneManager.CreateScene(nameof(ExpeditionNextPlaythroughScenePlayTests));
                SceneManager.SetActiveScene(replacement);
                yield return SceneManager.UnloadSceneAsync(_restartedScene);
            }
            if (_sourceScene.isLoaded) { yield return SceneManager.UnloadSceneAsync(_sourceScene); }
            TestSceneHelper.Cleanup();
            yield return null;
            if (Directory.Exists(_storePath)) { Directory.Delete(_storePath, recursive: true); }
        }

        [UnityTest]
        public IEnumerator NextPlaythrough_AfterClearAndConfirmation_ReloadsArenaAndSavesActiveSlot()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            _initialScene = SceneManager.GetActiveScene();
            _storePath = Path.Combine(Path.GetTempPath(), $"expedition-next-cycle-{Guid.NewGuid():N}");
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(k_ScenePath,
                new LoadSceneParameters(LoadSceneMode.Additive, LocalPhysicsMode.Physics3D));
            _sourceScene = SceneManager.GetSceneByPath(k_ScenePath);

            var store = new SaveDataStore(_storePath);
            ExpeditionNextPlaythroughPanel panel = Find<ExpeditionNextPlaythroughPanel>(_sourceScene, k_PanelName);
            panel.SetStoreForTest(store);
            const int slot = 1;
            GameManager.Saves.SetActiveSlot(slot);
            yield return new WaitForFixedUpdate();
            yield return null;

            BossEncounter3D encounter = Find<BossEncounter3D>(_sourceScene, k_EncounterName);
            CharacterActor3D knight = Find<CharacterActor3D>(_sourceScene, "[PLACEHOLDER] Knight");
            CharacterActor3D boss = Find<CharacterActor3D>(_sourceScene, k_BossName);
            ActorRef originalBoss = boss.Actor;
            VisualElement root = panel.GetComponent<UIDocument>().rootVisualElement;
            Button open = root.Q<Button>("next-cycle-open");
            Button confirm = root.Q<Button>("next-cycle-confirm");
            Button cancel = root.Q<Button>("next-cycle-cancel");
            Label status = root.Q<Label>("next-cycle-status");
            Assert.IsTrue(encounter.IsRunning);
            Assert.AreEqual(1, GameManager.Playthrough.CycleNumber);
            Assert.AreEqual("第1周", root.Q<Label>("next-cycle-number").text);
            Assert.AreEqual(DisplayStyle.None, open.style.display.value);

            Defeat(knight, boss);
            Assert.AreEqual(ChallengeState.Completed, encounter.Result.state);
            yield return null;
            Submit(open);
            Assert.AreEqual(DisplayStyle.Flex, confirm.style.display.value);
            Submit(cancel);
            Assert.AreEqual(DisplayStyle.None, confirm.style.display.value);
            Assert.AreEqual(1, GameManager.Playthrough.CycleNumber);
            Assert.AreEqual(slot, GameManager.Saves.ActiveSlotIndex);
            Assert.IsFalse(store.HasSlotFile(slot));
            Assert.IsTrue(_sourceScene.isLoaded);

            SceneManager.sceneLoaded += RecordRestart;
            Submit(open);
            Submit(confirm);
            SendSubmit(confirm);

            float deadline = Time.realtimeSinceStartup + 10f;
            while (!_restartedScene.isLoaded && Time.realtimeSinceStartup < deadline) { yield return null; }
            Assert.IsTrue(_restartedScene.isLoaded, $"The next playthrough did not load the arena in Single mode. {status.text}");
            yield return new WaitForFixedUpdate();
            yield return null;

            Assert.IsFalse(_sourceScene.isLoaded);
            Assert.AreEqual(_restartedScene, SceneManager.GetActiveScene());
            Assert.AreEqual(1, _restarts);
            Assert.AreEqual(2, GameManager.Playthrough.CycleNumber);
            Assert.AreEqual(slot, GameManager.Saves.ActiveSlotIndex);
            Assert.AreEqual(k_ScenePath, GameManager.NextPlaythrough.StartScenePath);
            BossEncounter3D restartedEncounter = Find<BossEncounter3D>(_restartedScene, k_EncounterName);
            CharacterActor3D restartedBoss = Find<CharacterActor3D>(_restartedScene, k_BossName);
            Assert.IsTrue(restartedEncounter.IsRunning);
            Assert.AreEqual(0, restartedEncounter.CurrentPhase);
            Assert.IsTrue(restartedBoss.IsAlive);
            Assert.AreNotEqual(originalBoss, restartedBoss.Actor);

            VisualElement restartedRoot = Find<UIDocument>(_restartedScene, k_PanelName).rootVisualElement;
            Assert.AreEqual("第2周", restartedRoot.Q<Label>("next-cycle-number").text);
            Assert.AreEqual(DisplayStyle.None, restartedRoot.Q<Button>("next-cycle-open").style.display.value);
            SaveSlotData saved = new SaveDataStore(_storePath).ReadFromDisk(slot);
            Assert.IsNotNull(saved);
            Assert.AreEqual(slot, saved.slotIndex);
            Assert.AreEqual(2, saved.entries[GameManager.Playthrough.SaveId]);
            Assert.IsFalse(store.HasSlotFile(0));
        }

        private static void Defeat(CharacterActor3D knight, CharacterActor3D boss)
        {
            AttackMotionData motion = new AttackMotionData { motionValue = 100f, attackElement = Element.Slash };
            Assert.IsTrue(CombatDataHelper.TryCaptureAttack(knight.ObjectHash, motion,
                GameManager.Data, GameManager.Actors, out AttackContext context));
            DamageData damage = CombatDataHelper.BuildDamageData(knight.ObjectHash, boss.ObjectHash, motion, context.AttackStats);
            context.ApplyTo(ref damage, boss.Actor);
            DamageResult result = boss.GetComponent<DamageReceiver>().ReceiveDamage3D(damage, knight.transform.position);
            Assert.IsTrue(result.isKill);
        }

        private void RecordRestart(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single || scene.path != k_ScenePath) { return; }
            _restartedScene = scene;
            _restarts++;
        }

        private static void Submit(Button button)
        {
            Assert.AreEqual(DisplayStyle.Flex, button.style.display.value);
            Assert.IsTrue(button.enabledInHierarchy);
            Assert.IsNotNull(button.panel);
            SendSubmit(button);
        }

        private static void SendSubmit(Button button)
        {
            using NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled();
            submit.target = button;
            button.SendEvent(submit);
        }

        private static T Find<T>(Scene scene, string name) where T : Component => scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<T>(true))
            .Single(component => component.name == name);
    }
}
#endif
