#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using R3;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using CharacterInfo = Game.Core.CharacterInfo;
using Object = UnityEngine.Object;

namespace Game.Tests.PlayMode
{
    public sealed class ExpeditionAutosave3DPlayTests
    {
        private const string k_Origin = "Assets/Scenes/StreamingAreaA3DTestScene.unity";
        private const string k_Destination = "Assets/Scenes/StreamingAreaB3DTestScene.unity";
        private const string k_Checkpoint = "autosave-test-checkpoint";
        private const string k_Completion = "autosave-encounter-complete";
        private const int k_ActiveSlot = 1;

        private Scene _origin;
        private string _directory;
        private SaveDataStore _store;
        private LevelStreamingController _streaming;
        private ExpeditionSavePoint3D _checkpoint;
        private CharacterActor3D _knight;
        private CharacterInfo _knightInfo;
        private EncounterSession3D _encounter;
        private ChallengeDefinition _challenge;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            _directory = Path.Combine(Path.GetTempPath(), nameof(ExpeditionAutosave3DPlayTests), Guid.NewGuid().ToString("N"));
            _store = new SaveDataStore(_directory);
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(k_Origin, new LoadSceneParameters(LoadSceneMode.Additive));
            _origin = SceneManager.GetSceneByPath(k_Origin);
            Assert.IsTrue(_origin.isLoaded);

            GameObject world = NewInactiveRoot("Autosave game world");
            _streaming = world.AddComponent<LevelStreamingController>();
            SerializedObject config = new SerializedObject(_streaming);
            config.FindProperty("persistentSceneName").stringValue = _origin.name;
            config.ApplyModifiedPropertiesWithoutUndo();
            world.AddComponent<GameManager>();
            world.SetActive(true);
            GameManager.Instance.enabled = false;
            GameManager.Saves.SetActiveSlot(k_ActiveSlot);

            GameObject knight = NewInactiveRoot("Autosave knight");
            knight.transform.position = Vector3.right * 10f;
            _knight = knight.AddComponent<CharacterActor3D>();
            _knightInfo = TestSceneHelper.CreateTestCharacterInfo();
            _knight.SetCharacterInfoForTest(_knightInfo);
            knight.SetActive(true);

            GameObject marker = NewInactiveRoot("Autosave anchor");
            _checkpoint = marker.AddComponent<ExpeditionSavePoint3D>();
            _checkpoint.SetStoreForTest(_store);
            config = new SerializedObject(marker.GetComponent<NpcInteraction3D>());
            config.FindProperty("interactionType").intValue = (int)InteractionType.SavePoint;
            config.ApplyModifiedPropertiesWithoutUndo();
            config = new SerializedObject(_checkpoint);
            config.FindProperty("savePointId").stringValue = k_Checkpoint;
            config.FindProperty("knight").objectReferenceValue = _knight;
            config.FindProperty("respawnAnchor").objectReferenceValue = marker.transform;
            config.ApplyModifiedPropertiesWithoutUndo();
            marker.SetActive(true);

            GameObject encounter = NewInactiveRoot("Autosave encounter");
            _encounter = encounter.AddComponent<EncounterSession3D>();
            _challenge = ScriptableObject.CreateInstance<ChallengeDefinition>();
            _encounter.ConfigureForTest(_knight, _challenge);
            encounter.SetActive(true);

            GameObject source = NewInactiveRoot("Authored autosave hooks");
            ExpeditionAutosave3D autosave = source.AddComponent<ExpeditionAutosave3D>();
            config = new SerializedObject(autosave);
            config.FindProperty("savePoint").objectReferenceValue = _checkpoint;
            SerializedProperty areas = config.FindProperty("areaPaths");
            areas.arraySize = 1;
            areas.GetArrayElementAtIndex(0).stringValue = k_Destination;
            SerializedProperty encounters = config.FindProperty("encounters");
            encounters.arraySize = 1;
            encounters.GetArrayElementAtIndex(0).objectReferenceValue = _encounter;
            config.ApplyModifiedPropertiesWithoutUndo();
            source.SetActive(true);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Scene destination = SceneManager.GetSceneByPath(k_Destination);
            if (destination.isLoaded) { yield return SceneManager.UnloadSceneAsync(destination); }
            if (_origin.isLoaded) { yield return SceneManager.UnloadSceneAsync(_origin); }
            Object.Destroy(_knightInfo);
            Object.Destroy(_challenge);
            TestSceneHelper.Cleanup();
            if (Directory.Exists(_directory)) { Directory.Delete(_directory, recursive: true); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator Autosave_WhenConfiguredAreaFinishesLoading_SavesCurrentVitalsWithoutResting()
        {
            Assert.IsFalse(_checkpoint.GetComponent<NpcInteraction3D>().CanInteract(_knight.Actor.Hash));
            int owner = _knight.Actor.Hash;
            GameManager.Data.GetVitals(owner).currentHp = 37;
            GameManager.Data.GetVitals(owner).currentMp = 14f;
            GameManager.Data.GetVitals(owner).currentStamina = 31f;
            GameManager.Data.GetVitals(owner).currentArmor = 0f;
            int manualSaves = 0;
            int rests = 0;
            _checkpoint.Saved.AddListener(() => manualSaves++);
            using var restSubscription = GameManager.Events.OnSavePointUsed.Subscribe(_ => rests++);

            Assert.IsTrue(_streaming.RequestAreaLoad(k_Destination));
            Assert.IsFalse(_store.HasSlotFile(k_ActiveSlot));
            yield return WaitForSave();

            Assert.IsTrue(SceneManager.GetSceneByPath(k_Destination).isLoaded);
            Assert.IsFalse(_store.HasSlotFile(0));
            Assert.AreEqual(0, manualSaves);
            Assert.AreEqual(0, rests);
            Assert.AreEqual(0UL, GameManager.Saves.LoadRevision);
            CharacterVitals current = GameManager.Data.GetVitals(_knight.Actor.Hash);
            Assert.AreEqual(37, current.currentHp);
            Assert.AreEqual(14f, current.currentMp);
            Assert.AreEqual(31f, current.currentStamina);
            using var resumed = new GameManagerCore();
            resumed.Saves.SetActiveSlot(k_ActiveSlot);
            Assert.IsTrue(ExpeditionCheckpoint.TryRestoreCurrentSlot(resumed.Saves, _store, k_Origin, k_Checkpoint,
                out ExpeditionCheckpoint.Resources saved));
            Assert.AreEqual(37, saved.Hp);
            Assert.AreEqual(14f, saved.Mp);
            Assert.AreEqual(31f, saved.Stamina);
        }

        [UnityTest]
        public IEnumerator Autosave_WhenEncounterCompletes_CapturesCompletionAndDropsRequestsFromStaleSlotOrActor()
        {
            Assert.IsTrue(_encounter.TryStartEncounter());
            _encounter.AbortEncounter();
            yield return null;
            yield return null;
            Assert.IsFalse(_store.HasSlotFile(k_ActiveSlot));

            _encounter.Ended += state =>
            {
                if (state == ChallengeState.Completed) { GameManager.Flags.SetGlobalFlag(k_Completion, true); }
            };
            CompleteEncounter();
            Assert.IsFalse(_store.HasSlotFile(k_ActiveSlot));
            yield return WaitForSave();
            using var resumed = new GameManagerCore();
            resumed.Saves.SetActiveSlot(k_ActiveSlot);
            Assert.IsTrue(ExpeditionCheckpoint.TryRestoreCurrentSlot(resumed.Saves, _store, k_Origin, k_Checkpoint, out _));
            Assert.IsTrue(resumed.Flags.GetGlobalFlag(k_Completion));

            SaveManager saves = GameManager.Saves;
            SaveSlotData original = saves.GetSlotData(k_ActiveSlot);
            CompleteEncounter();
            saves.SetActiveSlot(2);
            yield return null;
            yield return null;
            Assert.IsFalse(_store.HasSlotFile(2));
            Assert.AreSame(original, saves.GetSlotData(k_ActiveSlot));

            saves.SetActiveSlot(k_ActiveSlot);
            CompleteEncounter();
            ActorRef before = _knight.Actor;
            _knight.enabled = false;
            _knight.enabled = true;
            Assert.AreNotEqual(before, _knight.Actor);
            yield return null;
            yield return null;
            Assert.AreSame(original, saves.GetSlotData(k_ActiveSlot));
        }

        private void CompleteEncounter()
        {
            Assert.IsTrue(_encounter.TryStartEncounter());
            _encounter.CompleteEncounter();
            Assert.AreEqual(ChallengeState.Completed, _encounter.Result.state);
        }

        private IEnumerator WaitForSave()
        {
            float timeout = Time.realtimeSinceStartup + 5f;
            while (!_store.HasSlotFile(k_ActiveSlot) && Time.realtimeSinceStartup < timeout) { yield return null; }
            Assert.IsTrue(_store.HasSlotFile(k_ActiveSlot), "The configured trigger must write the active slot.");
        }

        private GameObject NewInactiveRoot(string name)
        {
            GameObject root = new GameObject(name);
            root.SetActive(false);
            SceneManager.MoveGameObjectToScene(root, _origin);
            return root;
        }
    }
}
#endif
