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
    public sealed class ExpeditionTitle3DPlayTests
    {
        private const string k_Title = "Assets/Scenes/ExpeditionTitle3DTestScene.unity";
        private const string k_Mock = "Assets/Scenes/ExpeditionMock3DScene.unity";
        private const string k_Point = "expedition.mock.trailhead";
        private const string k_Flag = "expedition.title-test";
        private const int k_WoundedHp = 37;
        private const float k_SpentMp = 12f;
        private Scene _testScene;
        private Scene _title;
        private string _folder;
        private SaveDataStore _store;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            TestSceneHelper.Cleanup();
            _testScene = SceneManager.GetActiveScene();
            _folder = Path.Combine(Application.temporaryCachePath, "ExpeditionTitleTests", Guid.NewGuid().ToString("N"));
            _store = new SaveDataStore(_folder);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_title.isLoaded)
            {
                SceneManager.SetActiveScene(_testScene);
                yield return SceneManager.UnloadSceneAsync(_title);
            }
            yield return UnloadWorld();
            if (Directory.Exists(_folder)) { Directory.Delete(_folder, recursive: true); }
        }

        [UnityTest]
        public IEnumerator Title_NewGameAndContinue_OpenSelectedSlotAndRestoreTheAuthoredAnchor()
        {
            WriteCheckpoint(0, k_Point);
            string beforeOtherSlot = File.ReadAllText(SlotFile(0));
            yield return LoadTitle();
            Assert.That(Slot(0).text, Is.EqualTo("つづきから"));
            Assert.That(Slot(1).text, Is.EqualTo("はじめから"));
            Submit(Slot(1));
            yield return WaitFor(WorldStarted, "The selected empty slot should start the expedition.");
            Assert.That(GameManager.Saves.ActiveSlotIndex, Is.EqualTo(1));
            Assert.That(GameManager.Saves.LoadRevision, Is.Zero);
            Assert.That(GameManager.Flags.GetGlobalFlag(k_Flag), Is.False);
            Assert.That(_store.HasSlotFile(1), Is.False);

            Scene world = SceneManager.GetActiveScene();
            CharacterActor3D knight = Find<CharacterActor3D>(world, "[PLACEHOLDER] Knight");
            ExpeditionSavePoint3D point = Find<ExpeditionSavePoint3D>(world, "[PLACEHOLDER] Expedition save point");
            Vector3 anchor = point.transform.Find("Resume anchor").position;
            knight.transform.position = anchor;
            Physics.SyncTransforms();
            GameManager.Flags.SetGlobalFlag(k_Flag, true);
            Assert.That(point.GetComponent<NpcInteraction3D>().TryInteract(knight), Is.True);
            Assert.That(_store.HasSlotFile(1), Is.True);
            GameManager.Data.GetVitals(knight.Actor.Hash).currentHp = k_WoundedHp;
            GameManager.Data.GetVitals(knight.Actor.Hash).currentMp = k_SpentMp;
            Assert.That(point.TryAutosaveCurrentSlot(knight.Actor), Is.True);

            yield return UnloadWorld();
            yield return LoadTitle();
            Assert.That(Slot(1).text, Is.EqualTo("つづきから"));
            bool placedBeforeNotification = false;
            ActorRef registeredBeforeNotification = default;
            void ObserveResume(Scene loaded, LoadSceneMode _)
            {
                if (loaded.path != k_Mock) { return; }
                CharacterActor3D candidate = Find<CharacterActor3D>(loaded, "[PLACEHOLDER] Knight");
                GameManager.Saves.Loaded += slot =>
                {
                    placedBeforeNotification = slot == 1 && Vector3.Distance(candidate.transform.position, anchor) < 0.01f;
                    registeredBeforeNotification = candidate.Actor;
                    candidate.gameObject.SetActive(false);
                    candidate.gameObject.SetActive(true);
                };
            }

            SceneManager.sceneLoaded += ObserveResume;
            try
            {
                Submit(Slot(1));
                yield return WaitFor(WorldStarted, "Continue should reopen the world from the selected slot.");
            }
            finally { SceneManager.sceneLoaded -= ObserveResume; }

            world = SceneManager.GetActiveScene();
            knight = Find<CharacterActor3D>(world, "[PLACEHOLDER] Knight");
            Assert.That(placedBeforeNotification, Is.True);
            Assert.That(registeredBeforeNotification.IsValid, Is.True);
            Assert.That(knight.Actor, Is.Not.EqualTo(registeredBeforeNotification));
            Assert.That(GameManager.Data.GetVitals(knight.Actor.Hash).currentHp, Is.EqualTo(k_WoundedHp));
            Assert.That(GameManager.Data.GetVitals(knight.Actor.Hash).currentMp, Is.EqualTo(k_SpentMp));
            Assert.That(GameManager.Saves.ActiveSlotIndex, Is.EqualTo(1));
            Assert.That(GameManager.Saves.LoadRevision, Is.EqualTo(1UL));
            Assert.That(GameManager.Flags.GetGlobalFlag(k_Flag), Is.True);
            Assert.That(Vector3.Distance(knight.transform.position, anchor), Is.LessThan(0.08f));
            Assert.That(Find<BossEncounter3D>(world, "Cave Sentinel encounter").IsRunning, Is.True);
            Assert.That(File.ReadAllText(SlotFile(0)), Is.EqualTo(beforeOtherSlot));
            yield return ExpeditionEvent3DScenePlayTests.WaitForJournalRestore();
        }

        [UnityTest]
        public IEnumerator Title_WhenSavedPointIsNotAuthored_ReturnsToTitleWithoutOverwritingTheSlot()
        {
            WriteCheckpoint(2, "missing.authored.point");
            string previous = File.ReadAllText(SlotFile(2));
            yield return LoadTitle();
            Assert.That(Slot(2).text, Is.EqualTo("つづきから"));
            Submit(Slot(2));
            yield return WaitFor(() => TitleRoot().Q<Label>("expedition-title-status").text == "このスロットを開けませんでした。",
                "A saved point missing from the destination scene should leave the player at the title.");

            Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(_title));
            Assert.That(SceneManager.GetSceneByPath(k_Mock).isLoaded, Is.False);
            Assert.That(GameManager.Instance, Is.Null);
            Assert.That(File.ReadAllText(SlotFile(2)), Is.EqualTo(previous));
        }

        [UnityTest]
        public IEnumerator Title_WhenSavedProgressChangesDuringContinue_RequiresReselection()
            => RejectChangedSlot(continueExisting: true);

        [UnityTest]
        public IEnumerator Title_WhenAnEmptySlotIsFilledDuringNewGame_RequiresReselection()
            => RejectChangedSlot(continueExisting: false);

        [UnityTest]
        public IEnumerator Title_WhenAListenerWritesDuringWorldActivation_RequiresReselection()
            => RejectChangedSlot(continueExisting: true, writeOnActivation: true);

        private IEnumerator RejectChangedSlot(bool continueExisting, bool writeOnActivation = false)
        {
            const int slot = 1;
            if (continueExisting) { WriteCheckpoint(slot, k_Point); }
            yield return LoadTitle();
            Behaviour[] presentation = TitlePresentation();
            Assert.That(presentation.All(component => component.isActiveAndEnabled), Is.True);
            bool requestedLoadApplied = false;
            string editedContents = null;
            void ChangeSlot(Scene scene)
            {
                if (scene.path != k_Mock) { return; }
                Assert.That(presentation.Any(component => component.isActiveAndEnabled), Is.False);
                GameManager.Saves.Loaded += _ => requestedLoadApplied = true;
                if (!continueExisting) { WriteCheckpoint(slot, k_Point); }
                SaveSlotData changed = _store.ReadFromDisk(slot);
                // Keep the original timestamp and destination while changing the actual saved progress.
                changed.entries["newer.external.progress"] = 7;
                Assert.That(_store.TryWriteToDisk(changed), Is.True);
                editedContents = File.ReadAllText(SlotFile(slot));
            }

            void WriteDuringLoad(Scene scene, LoadSceneMode _) => ChangeSlot(scene);
            void WriteDuringActivation(Scene _, Scene scene) => ChangeSlot(scene);

            if (writeOnActivation) { SceneManager.activeSceneChanged += WriteDuringActivation; }
            else { SceneManager.sceneLoaded += WriteDuringLoad; }
            try
            {
                Submit(Slot(slot));
                yield return WaitFor(() => TitleRoot().Q<Label>("expedition-title-status").text
                    == "セーブが変更されました。スロットを選び直してください。", "The changed slot should remain at the title.");
            }
            finally
            {
                SceneManager.sceneLoaded -= WriteDuringLoad;
                SceneManager.activeSceneChanged -= WriteDuringActivation;
            }

            Assert.That(requestedLoadApplied, Is.False);
            Assert.That(GameManager.Instance, Is.Null);
            Assert.That(SceneManager.GetSceneByPath(k_Mock).isLoaded, Is.False);
            Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(_title));
            Assert.That(presentation.All(component => component.isActiveAndEnabled), Is.True);
            Assert.That(Slot(slot).text, Is.EqualTo("つづきから"));
            Assert.That(editedContents, Is.Not.Null);
            Assert.That(File.ReadAllText(SlotFile(slot)), Is.EqualTo(editedContents));
        }

        [UnityTest]
        public IEnumerator Title_WhenDisabledDuringLaunch_RetiresTheUncommittedWorldAndRestoresTheMenu()
        {
            WriteCheckpoint(0, k_Point);
            yield return LoadTitle();
            Behaviour[] presentation = TitlePresentation();
            GameObject titleObject = Find<ExpeditionTitle3D>(_title, "Expedition title").gameObject;
            bool worldLoaded = false;
            bool requestedLoadApplied = false;
            void ObserveWorld(Scene scene, LoadSceneMode _)
            {
                if (scene.path != k_Mock) { return; }
                worldLoaded = true;
                GameManager.Saves.Loaded += _ => requestedLoadApplied = true;
            }

            SceneManager.sceneLoaded += ObserveWorld;
            try
            {
                Submit(Slot(0));
                titleObject.SetActive(false);
                yield return WaitFor(() => worldLoaded && !SceneManager.GetSceneByPath(k_Mock).isLoaded
                    && GameManager.Instance == null, "Disabling the title should retire the in-flight world.");
            }
            finally { SceneManager.sceneLoaded -= ObserveWorld; }

            Assert.That(requestedLoadApplied, Is.False);
            Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(_title));
            titleObject.SetActive(true);
            yield return WaitFor(() => Slot(0)?.enabledInHierarchy == true, "Re-enabling the title should give back its slots.");
            Assert.That(presentation.All(component => component.isActiveAndEnabled), Is.True);
            Assert.That(Slot(0).text, Is.EqualTo("つづきから"));
        }

        [UnityTest]
        public IEnumerator Title_WhenAnotherWorldStartsDuringContinue_LeavesItsManagerAndSlotAlone()
        {
            WriteCheckpoint(1, k_Point);
            string checkpoint = File.ReadAllText(SlotFile(1));
            yield return LoadTitle();

            Submit(Slot(1));
            GameObject otherWorld = new GameObject("Unrelated game world");
            otherWorld.SetActive(false);
            SceneManager.MoveGameObjectToScene(otherWorld, _testScene);
            GameManager otherManager = otherWorld.AddComponent<GameManager>();
            otherWorld.SetActive(true);
            Assert.That(GameManager.Instance, Is.SameAs(otherManager));
            Assert.That(SceneManager.SetActiveScene(_testScene), Is.True);
            GameManager.Saves.SetActiveSlot(2);

            yield return WaitFor(() => _title.isLoaded && TitleRoot().Q<Label>("expedition-title-status").text
                == "このスロットを開けませんでした。", "Continue should leave the other world running.");

            Assert.That(GameManager.Instance, Is.SameAs(otherManager));
            Assert.That(GameManager.Saves.ActiveSlotIndex, Is.EqualTo(2));
            Assert.That(GameManager.Saves.LoadRevision, Is.Zero);
            Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(_testScene));
            Assert.That(SceneManager.GetSceneByPath(k_Mock).isLoaded, Is.False);
            Assert.That(File.ReadAllText(SlotFile(1)), Is.EqualTo(checkpoint));
        }

        private IEnumerator LoadTitle()
        {
            void Configure(Scene scene, LoadSceneMode _)
            {
                if (scene.path != k_Title) { return; }
                _title = scene;
                Find<ExpeditionTitle3D>(scene, "Expedition title").SetStoreForTest(_store);
                Assert.That(SceneManager.SetActiveScene(scene), Is.True);
            }

            SceneManager.sceneLoaded += Configure;
            try
            {
                yield return EditorSceneManager.LoadSceneAsyncInPlayMode(k_Title, new LoadSceneParameters(LoadSceneMode.Additive));
                yield return null;
            }
            finally { SceneManager.sceneLoaded -= Configure; }
            Assert.That(_title.isLoaded, Is.True);
            VisualElement root = TitleRoot();
            Assert.That(root.panel, Is.Not.Null, "The authored title must be attached to a runtime panel.");
            Assert.That(Slot(0).panel, Is.SameAs(root.panel));
        }

        private IEnumerator UnloadWorld()
        {
            Scene world = SceneManager.GetSceneByPath(k_Mock);
            if (world.isLoaded)
            {
                SceneManager.SetActiveScene(_testScene);
                yield return SceneManager.UnloadSceneAsync(world);
            }
            TestSceneHelper.Cleanup();
            yield return null;
        }

        private bool WorldStarted() => SceneManager.GetActiveScene().path == k_Mock && !_title.isLoaded;

        private void WriteCheckpoint(int slot, string point)
        {
            using var source = new GameManagerCore();
            source.Saves.SetActiveSlot(slot);
            source.Flags.SetGlobalFlag(k_Flag, true);
            var vitals = new CharacterVitals { currentHp = 30, maxHp = 30, maxMp = 12, maxStamina = 20f };
            Assert.That(ExpeditionCheckpoint.TrySaveCurrentSlot(source.Saves, _store, k_Mock, point, vitals, out _), Is.True);
        }

        private string SlotFile(int slot) => Path.Combine(_folder, "saves", $"slot_{slot}.json");

        private VisualElement TitleRoot() => Find<UIDocument>(_title, "Expedition title").rootVisualElement;

        private Button Slot(int slot) => TitleRoot()?.Q<Button>($"expedition-slot-{slot}");

        private Behaviour[] TitlePresentation()
        {
            GameObject events = Find<Transform>(_title, "Expedition title UI events").gameObject;
            Behaviour[] presentation = events.GetComponents<Behaviour>()
                .Append(Find<AudioListener>(_title, "Title camera")).ToArray();
            Assert.That(presentation, Has.Length.EqualTo(3));
            return presentation;
        }

        private static T Find<T>(Scene scene, string name) where T : Component => scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<T>(true)).Single(component => component.name == name);

        private void Submit(Button button)
        {
            Assert.That(button.enabledInHierarchy, Is.True);
            using NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled();
            submit.target = button;
            button.SendEvent(submit);
            Assert.That(TitleRoot().Q<Label>("expedition-title-status").text, Is.EqualTo("読み込み中…"),
                $"Submitting {button.name} should reach the title's launch callback.");
        }

        private IEnumerator WaitFor(Func<bool> condition, string message)
        {
            float timeout = Time.realtimeSinceStartup + 8f;
            while (!condition() && Time.realtimeSinceStartup < timeout) { yield return null; }
            if (condition()) { yield break; }
            string titleStatus = _title.isLoaded ? TitleRoot()?.Q<Label>("expedition-title-status")?.text : "unloaded";
            Assert.Fail($"{message} Active scene was '{SceneManager.GetActiveScene().path}'. "
                + $"Expedition loaded = {SceneManager.GetSceneByPath(k_Mock).isLoaded}, title status = '{titleStatus}'.");
        }
    }
}
#endif
