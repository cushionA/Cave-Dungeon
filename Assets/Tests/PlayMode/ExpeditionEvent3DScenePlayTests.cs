#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Game.Tests.PlayMode
{
    public sealed class ExpeditionEvent3DScenePlayTests
    {
        private const string k_MockScene = "Assets/Scenes/ExpeditionMock3DScene.unity";
        private const string k_EventScene = "Assets/Scenes/ExpeditionEvent3DScene.unity";
        private const string k_CompletionFlag = "expedition.mock.mine-seal-opened";
        private readonly HashSet<GameObject> _existingVendorSaves = new HashSet<GameObject>();
        private Scene _previous;
        private Scene _mock;
        private string _saveDirectory;
        private ExpeditionEventEntry3D _entry;
        private Gamepad _pad;
        private float _originalTimeScale;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _originalTimeScale = Time.timeScale;
            Time.timeScale = 1f;
            _previous = SceneManager.GetActiveScene();
            TestSceneHelper.Cleanup();
            yield return null;
            _existingVendorSaves.Clear();
            _existingVendorSaves.UnionWith(VendorSaveRoots());
            _pad = InputSystem.AddDevice<Gamepad>();
            _saveDirectory = Path.Combine(Application.temporaryCachePath, nameof(ExpeditionEvent3DScenePlayTests),
                Guid.NewGuid().ToString("N"));
            var store = new SaveDataStore(_saveDirectory);
            void Configure(Scene scene, LoadSceneMode _)
            {
                if (scene.path != k_MockScene) { return; }
                _mock = scene;
                UseSaveStore(scene, store);
                Assert.IsTrue(SceneManager.SetActiveScene(scene));
            }

            SceneManager.sceneLoaded += Configure;
            try
            {
                yield return EditorSceneManager.LoadSceneAsyncInPlayMode(k_MockScene, new LoadSceneParameters(LoadSceneMode.Additive));
                yield return new WaitForFixedUpdate();
            }
            finally { SceneManager.sceneLoaded -= Configure; }
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_entry != null && _entry.IsPlaying)
            {
                _entry.Skip();
                float deadline = Time.realtimeSinceStartup + 4f;
                while (_entry.IsPlaying && Time.realtimeSinceStartup < deadline) { yield return null; }
            }
            // Quest Machine releases its global restore guard over render frames while its scene is still loaded.
            yield return null;
            yield return null;
            yield return null;
            if (_previous.isLoaded) { SceneManager.SetActiveScene(_previous); }
            Scene playback = SceneManager.GetSceneByPath(k_EventScene);
            if (playback.isLoaded) { yield return SceneManager.UnloadSceneAsync(playback); }
            if (_mock.isLoaded) { yield return SceneManager.UnloadSceneAsync(_mock); }
            foreach (GameObject root in VendorSaveRoots())
            {
                if (!_existingVendorSaves.Contains(root)) { Object.Destroy(root); }
            }
            _existingVendorSaves.Clear();
            Time.timeScale = _originalTimeScale;
            TestSceneHelper.Cleanup();
            yield return null;
            if (_pad != null && _pad.added) { InputSystem.RemoveDevice(_pad); }
            _pad = null;
            if (Directory.Exists(_saveDirectory)) { Directory.Delete(_saveDirectory, recursive: true); }
        }

        [UnityTest]
        public IEnumerator AuthoredMemorial_WhenTimelineFinishes_RestoresGameplayForTheSameKnightOnce()
        {
            yield return VerifyEvent(skip: false);
        }

        [UnityTest]
        public IEnumerator AuthoredMemorial_WhenSkipped_RestoresGameplayAndDoesNotReplay()
        {
            yield return VerifyEvent(skip: true);
        }

        private IEnumerator VerifyEvent(bool skip)
        {
            CharacterActor3D knight = FindRoot(_mock, "[PLACEHOLDER] Knight").GetComponent<CharacterActor3D>();
            PlayerInput input = knight.GetComponent<PlayerInput>();
            input.SwitchCurrentControlScheme("Gamepad", _pad);
            Camera gameplayCamera = input.camera;
            Assert.IsNotNull(gameplayCamera);
            Assert.IsTrue(gameplayCamera.isActiveAndEnabled);
            InputActionMap playerMap = input.currentActionMap;
            Assert.AreEqual("Player", playerMap.name);
            InputAction jump = playerMap.FindAction("Jump", throwIfNotFound: true);
            jump.Disable();
            Assert.IsTrue(input.inputIsActive);
            ActorRef owner = knight.Actor;
            Assert.IsTrue(owner.IsValid);

            GameObject marker = FindRoot(_mock, "Expedition event entrance");
            NpcInteraction3D interaction = marker.GetComponent<NpcInteraction3D>();
            _entry = marker.GetComponent<ExpeditionEventEntry3D>();
            Assert.IsNotNull(interaction);
            Assert.IsNotNull(_entry);
            Assert.IsFalse(_entry.IsPlaying);
            Assert.IsFalse(_entry.HasCompleted);
            PlaceAtMarker(knight, marker);
            yield return new WaitForFixedUpdate();
            Assert.IsTrue(interaction.CanInteract(owner.Hash));
            GameManager.Saves.Save(0);

            yield return HoldInteract();
            ExpeditionEventPlayback3D playback = null;
            yield return WaitFor(() =>
            {
                Scene scene = SceneManager.GetSceneByPath(k_EventScene);
                if (!scene.isLoaded) { return false; }
                playback = FindRoot(scene, "Expedition event playback").GetComponent<ExpeditionEventPlayback3D>();
                return playback != null && playback.Director != null && playback.Director.state == PlayState.Playing;
            }, "The authored interaction must start the dedicated Timeline scene.");
            Assert.AreEqual("UnityEngine.Timeline.TimelineAsset", playback.Director.playableAsset.GetType().FullName);
            Assert.IsTrue(_entry.IsPlaying);
            Assert.IsFalse(_entry.HasCompleted);
            Assert.IsFalse(_entry.TryPlay());
            Assert.IsTrue(_mock.isLoaded);
            Assert.AreEqual(_mock, knight.gameObject.scene);
            Assert.AreEqual(owner, knight.Actor);
            Assert.IsFalse(gameplayCamera.isActiveAndEnabled);
            Assert.IsFalse(input.inputIsActive);
            Assert.AreSame(playerMap, input.currentActionMap);
            Assert.IsFalse(playerMap.enabled);
            Assert.IsTrue(playback.EventCamera.isActiveAndEnabled);

            UIDocument beltDocument = FindRoot(_mock, "Expedition consumable belt").GetComponent<UIDocument>();
            VisualElement belt = beltDocument.rootVisualElement;
            Submit(belt.Q<Button>("consumable-belt-open"));
            Assert.AreEqual(DisplayStyle.None, belt.Q("consumable-belt-editor").style.display.value);
            Assert.IsFalse(input.inputIsActive);
            Assert.AreSame(playerMap, input.currentActionMap);
            Assert.IsFalse(playerMap.enabled);

            SetButton(GamepadButton.LeftShoulder, true);
            yield return null;
            Assert.IsFalse(GameManager.Data.GetDefenseState(owner.Hash).isGuarding);
            SetButton(GamepadButton.LeftShoulder, false);
            if (skip)
            {
                UIDocument document = playback.GetComponent<UIDocument>();
                Assert.IsNotNull(document);
                Submit(document.rootVisualElement.Q<Button>("expedition-event-skip"));
            }

            yield return WaitFor(() => _entry.HasCompleted && !_entry.IsPlaying && !SceneManager.GetSceneByPath(k_EventScene).isLoaded,
                "The event must finish and unload its dedicated scene.");
            Assert.AreEqual(_mock, SceneManager.GetActiveScene());
            Assert.AreEqual(_mock, knight.gameObject.scene);
            Assert.AreEqual(owner, knight.Actor);
            Assert.IsTrue(GameManager.Actors.IsCurrent(owner));
            Assert.AreSame(knight, GameManager.Data.GetManaged(owner.Hash));
            Assert.IsTrue(knight.IsAlive);
            Assert.IsTrue(gameplayCamera.isActiveAndEnabled);
            Assert.IsTrue(input.inputIsActive);
            Assert.AreSame(playerMap, input.currentActionMap);
            Assert.IsTrue(playerMap.enabled);
            Assert.IsFalse(jump.enabled);
            Assert.AreEqual(1f, Time.timeScale);

            SetButton(GamepadButton.LeftShoulder, true);
            yield return WaitFor(() => GameManager.Data.GetDefenseState(owner.Hash).isGuarding,
                "Fresh gameplay input must be accepted after returning from the event.");
            SetButton(GamepadButton.LeftShoulder, false);
            Assert.IsFalse(_entry.TryPlay());
            Assert.IsFalse(interaction.CanInteract(owner.Hash));
            interaction.Interact(owner.Hash);
            yield return null;
            Assert.IsFalse(_entry.IsPlaying);
            Assert.IsFalse(SceneManager.GetSceneByPath(k_EventScene).isLoaded);
            Assert.IsTrue(GameManager.Flags.GetGlobalFlag(k_CompletionFlag));
            Assert.IsFalse(interaction.enabled);

            GameManager.Saves.Save(1);
            Assert.IsTrue(GameManager.Saves.Load(0));
            Assert.IsFalse(_entry.HasCompleted);
            Assert.IsTrue(interaction.CanInteract(owner.Hash));
            yield return WaitForJournalRestore();
            Assert.IsTrue(GameManager.Saves.Load(1));
            Assert.IsTrue(_entry.HasCompleted);
            Assert.IsFalse(interaction.enabled);
            Assert.IsFalse(_entry.TryPlay());
            yield return WaitForJournalRestore();
        }

        internal static IEnumerator WaitForJournalRestore()
        {
            PropertyInfo loading = VendorType("PixelCrushers.QuestMachine.QuestMachine").GetProperty("isLoadingGame");
            PropertyInfo receivesNewMessages = VendorType("PixelCrushers.MessageSystem").GetProperty("allowReceiveSameFrameAdded");
            yield return WaitFor(() => !(bool)loading.GetValue(null) && (bool)receivesNewMessages.GetValue(null),
                "The mock's quest restore must settle before the next load or scene unload.");
        }

        internal static void UseSaveStore(Scene scene, SaveDataStore store)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (ExpeditionSavePoint3D point in root.GetComponentsInChildren<ExpeditionSavePoint3D>(true))
                {
                    point.SetStoreForTest(store);
                }
                foreach (ExpeditionNextPlaythroughPanel panel in root.GetComponentsInChildren<ExpeditionNextPlaythroughPanel>(true))
                {
                    panel.SetStoreForTest(store);
                }
            }
        }

        internal static IEnumerable<GameObject> VendorSaveRoots()
        {
            foreach (Object save in Resources.FindObjectsOfTypeAll(VendorType("PixelCrushers.SaveSystem")))
            {
                if (save is Component component && component.gameObject.scene.name == "DontDestroyOnLoad")
                {
                    yield return component.transform.root.gameObject;
                }
            }
        }

        private static Type VendorType(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name)).First(type => type != null);

        private static void Submit(Button button)
        {
            Assert.IsNotNull(button);
            using NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled();
            submit.target = button;
            button.SendEvent(submit);
        }

        private static void PlaceAtMarker(CharacterActor3D knight, GameObject marker)
        {
            knight.Body.enabled = false;
            knight.transform.position = marker.transform.position + Vector3.up * 0.05f;
            knight.Body.enabled = true;
            Physics.SyncTransforms();
        }

        private IEnumerator HoldInteract()
        {
            SetButton(GamepadButton.North, true);
            yield return new WaitForSecondsRealtime(InputSystem.settings.defaultHoldTime + 0.1f);
            SetButton(GamepadButton.North, false);
        }

        private void SetButton(GamepadButton button, bool pressed)
        {
            InputSystem.QueueStateEvent(_pad, new GamepadState().WithButton(button, pressed));
            InputSystem.Update();
        }

        private static IEnumerator WaitFor(Func<bool> condition, string failure)
        {
            float deadline = Time.realtimeSinceStartup + 8f;
            while (!condition() && Time.realtimeSinceStartup < deadline) { yield return null; }
            Assert.IsTrue(condition(), failure);
        }

        private static GameObject FindRoot(Scene scene, string name) => scene.GetRootGameObjects().Single(root => root.name == name);
    }
}
#endif
