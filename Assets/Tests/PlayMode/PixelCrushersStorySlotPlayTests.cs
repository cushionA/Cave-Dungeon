using System;
using System.Collections;
using System.IO;
using System.Reflection;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Game.Tests.PlayMode
{
    public sealed class PixelCrushersStorySlotPlayTests
    {
        private const string k_StoryKey = "PixelCrushers.Story";
        private const string k_QuestId = "story.slot-test";
        private const string k_Variable = "StorySlotChoice";
        private const string k_Completion = "story.slot-test.complete";
        private const string k_Species = "slot-test-scout";

        private GameObject _story;
        private GameObject _serializer;
        private ScriptableObject _questAsset;
        private ScriptableObject _messageAction;
        private ScriptableObject _dialogueDatabase;
        private Component _journal;
        private Behaviour _bridge;
        private string _directory;
        private Scene _room;

        [UnityTest]
        public IEnumerator VendorStory_WhenLoadingDiskSlots_RestoresJournalAndDialogueBeforeLoadedWithoutRegrantingAid()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            TestSceneHelper.CreateGameManager();
            CreateVendorStory();
            yield return null;
            yield return null;
            SaveManager saves = GameManager.Saves;
            SaveSlotData withoutAward = saves.Save(1);
            Assert.That(QuestState(), Is.EqualTo("WaitingToStart"));
            Assert.That(Choice(), Is.EqualTo("unanswered"));
            Assert.IsFalse(GameManager.ExpeditionAid.IsUnlocked(k_Species));

            CompleteQuest();
            SetChoice("accepted");
            Assert.IsTrue(GameManager.ExpeditionAid.IsUnlocked(k_Species));
            SaveSlotData withAward = saves.Save(0);
            Assert.That(withAward.entries[k_StoryKey], Is.TypeOf<string>());
            withoutAward.entries[k_StoryKey] = withAward.entries[k_StoryKey];

            _directory = Path.Combine(Path.GetTempPath(), "PixelCrushersStorySlot-" + Guid.NewGuid().ToString("N"));
            SaveDataStore disk = new SaveDataStore(_directory);
            disk.WriteToDisk(withAward);
            disk.WriteToDisk(withoutAward);
            disk.WriteToDisk(new SaveSlotData(2));
            for (int slot = 0; slot < SaveManager.k_MaxSlots; slot++)
            {
                SaveSlotData restored = disk.ReadFromDisk(slot);
                Assert.IsNotNull(restored);
                saves.SetSlotData(slot, restored);
            }

            (int slot, string state, string choice, bool unlocked) observed = default;
            saves.Loaded += slot => observed = (slot, QuestState(), Choice(), GameManager.ExpeditionAid.IsUnlocked(k_Species));
            Assert.IsTrue(saves.Load(2));
            Assert.That(observed, Is.EqualTo((2, "WaitingToStart", "unanswered", false)));
            yield return null;
            yield return null;

            Assert.IsTrue(saves.Load(1));
            Assert.That(observed, Is.EqualTo((1, "Successful", "accepted", false)));
            yield return null;
            yield return null;
            yield return null;
            Assert.IsFalse(GameManager.ExpeditionAid.IsUnlocked(k_Species), "Restoring the completed quest does not grant aid again.");

            SetChoice("changed in memory");
            Assert.IsTrue(saves.Load(0));
            Assert.That(observed, Is.EqualTo((0, "Successful", "accepted", true)));
        }

        [UnityTest]
        public IEnumerator VendorStory_WhenBeginningNextPlaythrough_ResetsJournalAndLuaAndKeepsUnlockedAid()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            TestSceneHelper.CreateGameManager();
            CreateVendorStory();
            yield return null;
            yield return null;

            CompleteQuest();
            SetChoice("accepted");
            Assert.That(QuestState(), Is.EqualTo("Successful"));
            Assert.That(Choice(), Is.EqualTo("accepted"));
            Assert.IsTrue(GameManager.ExpeditionAid.IsUnlocked(k_Species));

            SaveManager saves = GameManager.Saves;
            int slot = saves.ActiveSlotIndex;
            int cycle = GameManager.Playthrough.CycleNumber;
            _directory = Path.Combine(Path.GetTempPath(), "PixelCrushersStorySlot-" + Guid.NewGuid().ToString("N"));
            SaveDataStore disk = new SaveDataStore(_directory);
            SaveSlotData previous = saves.Save(slot);
            Assert.IsTrue(previous.entries.ContainsKey(k_StoryKey));
            Assert.IsTrue(disk.TryWriteToDisk(previous));

            ExpeditionNextPlaythrough next = GameManager.NextPlaythrough;
            next.MarkCleared();
            Assert.IsTrue(next.TryBeginNextPlaythrough(slot, saves.LoadRevision, cycle, "start", disk));
            Assert.That(GameManager.Playthrough.CycleNumber, Is.EqualTo(cycle + 1));
            Assert.IsFalse(disk.ReadFromDisk(slot).entries.ContainsKey(k_StoryKey));

            yield return null;
            yield return null;
            Assert.That(QuestState(), Is.EqualTo("WaitingToStart"));
            Assert.That(Choice(), Is.EqualTo("unanswered"));
            Assert.IsTrue(GameManager.ExpeditionAid.IsUnlocked(k_Species));
        }

        [UnityTest]
        public IEnumerator VendorStory_WhenEnabledAfterLoad_RestoresOnlyTheMostRecentSuccessfulLoad()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            TestSceneHelper.CreateGameManager();
            CreateVendorStory();
            yield return null;
            yield return null;

            SaveManager saves = GameManager.Saves;
            SetChoice("snapshot");
            SaveSlotData storySlot = saves.Save(0);
            Assert.IsTrue(storySlot.entries.ContainsKey(k_StoryKey));
            _bridge.enabled = false;
            SetChoice("working values");
            Assert.IsFalse(saves.Save(1).entries.ContainsKey(k_StoryKey));
            saves.SetActiveSlot(0);
            _bridge.enabled = true;
            Assert.That(Choice(), Is.EqualTo("working values"));

            _bridge.enabled = false;
            Assert.IsTrue(saves.Load(0));
            saves.SetActiveSlot(1);
            Assert.IsFalse(saves.Load(2));
            _bridge.enabled = true;
            Assert.That(Choice(), Is.EqualTo("snapshot"));
            yield return null;
            yield return null;
            yield return null;

            _bridge.enabled = false;
            SetChoice("changed again");
            Assert.IsTrue(saves.Load(0));
            Assert.AreSame(storySlot, saves.LastLoadedSlot);
            _bridge.enabled = true;
            Assert.That(Choice(), Is.EqualTo("snapshot"));
        }

        [UnityTest]
        public IEnumerator VendorStory_WhenUnloadingDuringJournalRestore_ReleasesVendorLoadingState()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            TestSceneHelper.CreateGameManager();
            CreateVendorStory();
            _room = SceneManager.CreateScene("Story load cancellation");
            SceneManager.MoveGameObjectToScene(_story, _room);
            yield return null;
            yield return null;

            SaveManager saves = GameManager.Saves;
            Assert.IsTrue(saves.Save(0).entries.ContainsKey(k_StoryKey));
            Assert.IsTrue(saves.Load(0));
            PropertyInfo loading = FindType("PixelCrushers.QuestMachine.QuestMachine").GetProperty("isLoadingGame");
            PropertyInfo receivesMessages = FindType("PixelCrushers.MessageSystem").GetProperty("allowReceiveSameFrameAdded");
            Assert.AreEqual(true, loading.GetValue(null));
            Assert.AreEqual(false, receivesMessages.GetValue(null));

            // シーンの非同期破棄を待つ前にベンダーのロード解除コルーチンが確実に止まるようにする。
            _story.SetActive(false);
            yield return SceneManager.UnloadSceneAsync(_room);
            yield return null;
            yield return null;
            yield return null;
            Assert.AreEqual(false, loading.GetValue(null));
            Assert.AreEqual(true, receivesMessages.GetValue(null));
        }

        private void CreateVendorStory()
        {
            _serializer = new GameObject("Story test serialization");
            _serializer.AddComponent(FindType("PixelCrushers.SaveSystem"));
            _serializer.AddComponent(FindType("PixelCrushers.JsonDataSerializer"));

            _story = new GameObject("Story test controllers");
            _story.SetActive(false);
            _story.AddComponent(FindType("PixelCrushers.QuestMachine.QuestMachineConfiguration"));
            _journal = _story.AddComponent(FindType("PixelCrushers.QuestMachine.QuestJournal"));
            _journal.GetType().GetProperty("includeInSavedGameData").SetValue(_journal, true);
            _journal.GetType().GetProperty("saveQuestsToJson").SetValue(_journal, true);

            Type questType = FindType("PixelCrushers.QuestMachine.Quest");
            Type stringField = FindType("PixelCrushers.StringField");
            _questAsset = ScriptableObject.CreateInstance(questType);
            questType.GetMethod("Initialize").Invoke(_questAsset, null);
            questType.GetProperty("id").SetValue(_questAsset, Activator.CreateInstance(stringField, k_QuestId));
            IList quests = (IList)_journal.GetType().GetProperty("questList").GetValue(_journal);
            quests.Add(_questAsset);

            _messageAction = ScriptableObject.CreateInstance(FindType("PixelCrushers.QuestMachine.MessageQuestAction"));
            _messageAction.GetType().GetProperty("message").SetValue(_messageAction,
                Activator.CreateInstance(stringField, ExpeditionAidUnlocks.k_CompletionMessage));
            _messageAction.GetType().GetProperty("parameter").SetValue(_messageAction,
                Activator.CreateInstance(stringField, k_Completion));
            object successInfo = questType.GetMethod("GetStateInfo").Invoke(_questAsset, new[] { SuccessfulState() });
            ((IList)successInfo.GetType().GetProperty("actionList").GetValue(successInfo)).Add(_messageAction);

            Component questBridge = _story.AddComponent(FindType("Game.Runtime.QuestMachineBridge"));
            SetPrivateField(questBridge, "aidCompletions", new[] { new ExpeditionAidUnlocks.Completion(k_Completion, k_Species) });

            Type controllerType = FindType("PixelCrushers.DialogueSystem.DialogueSystemController");
            Component controller = _story.AddComponent(controllerType);
            Type databaseType = FindType("PixelCrushers.DialogueSystem.DialogueDatabase");
            _dialogueDatabase = ScriptableObject.CreateInstance(databaseType);
            Type templateType = FindType("PixelCrushers.DialogueSystem.Template");
            object template = templateType.GetMethod("FromDefault").Invoke(null, null);
            object defaultChoice = templateType.GetMethod("CreateVariable", new[] { typeof(int), typeof(string), typeof(string) })
                .Invoke(template, new object[] { 1, k_Variable, "unanswered" });
            ((IList)databaseType.GetField("variables").GetValue(_dialogueDatabase)).Add(defaultChoice);
            controllerType.GetField("initialDatabase").SetValue(controller, _dialogueDatabase);
            controllerType.GetField("dontDestroyOnLoad").SetValue(controller, false);
            Component dialogueSaver = _story.AddComponent(FindType("PixelCrushers.DialogueSystem.DialogueSystemSaver"));

            _bridge = (Behaviour)_story.AddComponent(FindType("Game.Runtime.PixelCrushersSaveSlotBridge"));
            SetPrivateField(_bridge, "questJournal", _journal);
            SetPrivateField(_bridge, "dialogueSystemSaver", dialogueSaver);
            _story.SetActive(true);
        }

        private object ReadQuest()
            => _journal.GetType().GetMethod("FindQuest", new[] { typeof(string) }).Invoke(_journal, new object[] { k_QuestId });

        private string QuestState()
        {
            object quest = ReadQuest();
            return quest?.GetType().GetMethod("GetState").Invoke(quest, null).ToString();
        }

        private void CompleteQuest()
        {
            object quest = ReadQuest();
            Assert.IsNotNull(quest);
            quest.GetType().GetMethod("SetState").Invoke(quest, new object[] { SuccessfulState(), true });
        }

        private static object SuccessfulState()
            => Enum.Parse(FindType("PixelCrushers.QuestMachine.QuestState"), "Successful");

        private static string Choice()
            => (string)FindType("PixelCrushers.DialogueSystem.DialogueLua")
                .GetMethod("GetVariable", new[] { typeof(string), typeof(string) })
                .Invoke(null, new object[] { k_Variable, string.Empty });

        private static void SetChoice(string choice)
            => FindType("PixelCrushers.DialogueSystem.DialogueLua")
                .GetMethod("SetVariable", new[] { typeof(string), typeof(object) }).Invoke(null, new object[] { k_Variable, choice });

        private static void SetPrivateField(Component component, string name, object value)
            => component.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(component, value);

        private static Type FindType(string name)
        {
            // アダプターとベンダーが既定アセンブリにあり、テストの asmdef から直接参照できない。
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(name);
                if (type != null) { return type; }
            }
            throw new InvalidOperationException(name);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // Quest Machine はロード中フラグを2フレーム後に解除する。
            yield return null;
            yield return null;
            yield return null;
            if (_room.isLoaded) { yield return SceneManager.UnloadSceneAsync(_room); }
            Object.Destroy(_story);
            Object.Destroy(_serializer);
            Object.Destroy(_questAsset);
            Object.Destroy(_messageAction);
            Object.Destroy(_dialogueDatabase);
            TestSceneHelper.Cleanup();
            yield return null;
            if (Directory.Exists(_directory)) { Directory.Delete(_directory, recursive: true); }
        }
    }
}
