#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests.PlayMode
{
    public sealed class ExpeditionSavePointMockPlayTests
    {
        private const string k_Mock = "Assets/Scenes/ExpeditionMock3DScene.unity";
        private Scene _mock;
        private Scene _previous;
        private string _saveFolder;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            TestSceneHelper.Cleanup();
            _previous = SceneManager.GetActiveScene();
            _saveFolder = Path.Combine(Application.temporaryCachePath, "ExpeditionCheckpointTests", Guid.NewGuid().ToString("N"));
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return UnloadMock();
            if (Directory.Exists(_saveFolder)) { Directory.Delete(_saveFolder, recursive: true); }
        }

        [UnityTest]
        public IEnumerator AuthoredSavePoint_WhenUsedAndReopened_RestoresFirstCycleAtTheSamePoint()
        {
            SaveDataStore store = new SaveDataStore(_saveFolder);
            yield return LoadMock();

            CharacterActor3D knight = Find<CharacterActor3D>("[PLACEHOLDER] Knight");
            ExpeditionSavePoint3D checkpoint = Find<ExpeditionSavePoint3D>("[PLACEHOLDER] Expedition save point");
            NpcInteraction3D interaction = checkpoint.GetComponent<NpcInteraction3D>();
            checkpoint.SetStoreForTest(store);
            Assert.That(interaction.InteractionType, Is.EqualTo(InteractionType.SavePoint));
            SerializedProperty targets = new SerializedObject(knight.GetComponent<PlayerCharacterInput3D>()).FindProperty("interactionTargets");
            Assert.That(Enumerable.Range(0, targets.arraySize)
                .Any(index => targets.GetArrayElementAtIndex(index).objectReferenceValue == interaction), Is.True);

            Vector3 respawn = checkpoint.transform.Find("Resume anchor").position;
            knight.transform.position = respawn;
            Physics.SyncTransforms();
            GameManager.Data.GetVitals(knight.Actor.Hash).currentHp = 1;
            GameManager.Data.GetVitals(knight.Actor.Hash).currentMp = 0;
            GameManager.Data.GetVitals(knight.Actor.Hash).currentStamina = 0f;
            GameManager.Flags.SetGlobalFlag("checkpoint-test", true);

            Assert.That(interaction.TryInteract(knight), Is.True);
            Assert.That(store.ReadFromDisk(0)?.entries.ContainsKey(ExpeditionCheckpoint.k_SaveId), Is.True);
            AssertRecovered(knight);
            Assert.That(checkpoint.GetComponentInChildren<TextMesh>().text, Is.EqualTo("SAVED"));

            yield return UnloadMock();
            yield return LoadMock();
            knight = Find<CharacterActor3D>("[PLACEHOLDER] Knight");
            checkpoint = Find<ExpeditionSavePoint3D>("[PLACEHOLDER] Expedition save point");
            checkpoint.SetStoreForTest(store);
            Assert.That(GameManager.Flags.GetGlobalFlag("checkpoint-test"), Is.False);
            Assert.That(SceneManager.SetActiveScene(_mock), Is.True);
            Assert.That(checkpoint.TryRestoreCurrentSlot(), Is.True);

            Assert.That(GameManager.Playthrough.CycleNumber, Is.EqualTo(1));
            Assert.That(GameManager.Flags.GetGlobalFlag("checkpoint-test"), Is.True);
            Assert.That(Vector3.Distance(knight.transform.position, respawn), Is.LessThan(0.01f));
            AssertRecovered(knight);
            yield return ExpeditionEvent3DScenePlayTests.WaitForJournalRestore();
        }

        private IEnumerator LoadMock()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(k_Mock, new LoadSceneParameters(LoadSceneMode.Additive));
            _mock = SceneManager.GetSceneByPath(k_Mock);
            yield return null;
        }

        private IEnumerator UnloadMock()
        {
            if (_previous.isLoaded) { SceneManager.SetActiveScene(_previous); }
            if (_mock.isLoaded) { yield return SceneManager.UnloadSceneAsync(_mock); }
            TestSceneHelper.Cleanup();
            yield return null;
        }

        private T Find<T>(string name) where T : Component => _mock.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<T>(true)).Single(component => component.name == name);

        private static void AssertRecovered(CharacterActor3D knight)
        {
            CharacterVitals vitals = GameManager.Data.GetVitals(knight.Actor.Hash);
            Assert.That(vitals.currentHp, Is.EqualTo(vitals.maxHp));
            Assert.That(vitals.currentMp, Is.EqualTo(vitals.maxMp));
            Assert.That(vitals.currentStamina, Is.EqualTo(vitals.maxStamina));
        }
    }
}
#endif
