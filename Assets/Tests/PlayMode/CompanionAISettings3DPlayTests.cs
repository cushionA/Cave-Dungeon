#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using CharacterInfo = Game.Core.CharacterInfo;

namespace Game.Tests.PlayMode
{
    public sealed class CompanionAISettings3DPlayTests
    {
        private readonly List<CharacterInfo> _characters = new List<CharacterInfo>();
        private Scene _room;
        private UIDocument _document;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            TestSceneHelper.CreateGameManager();
            _room = SceneManager.CreateScene(nameof(CompanionAISettings3DPlayTests),
                new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_room.isLoaded) { yield return SceneManager.UnloadSceneAsync(_room); }
            foreach (CharacterInfo definition in _characters) { Object.Destroy(definition); }
            _characters.Clear();
            TestSceneHelper.Cleanup();
            yield return null;
        }

        [UnityTest]
        public IEnumerator CurrentTactic_SaveButtonAppliesToTheOpened3DCompanion()
        {
            CompanionController3D sister = CreateCompanion("Sister", Vector3.zero);
            CompanionController3D scout = CreateCompanion("Scout", Vector3.right * 4f);
            CompanionAISettingsController panel = CreatePanel();
            yield return null;
            sister.SetAIConfig(NewTactic("Sister wait"));
            scout.SetAIConfig(NewTactic("Scout patrol"));
            panel.OpenFor(sister);
            yield return null;

            Assert.AreEqual("Sister wait", Root.Q("mode-slots-container").Q<Button>().text);
            AIMode edited = panel.Logic.EditingBuffer.modes[0];
            edited.modeName = "Cover the knight";
            Assert.IsTrue(panel.Logic.UpdateModeInBuffer(0, edited));
            panel.Logic.SetTransitionRulesInBuffer(new[] { new ModeTransitionRule { sourceModeIndex = 0, targetModeIndex = 1 } });
            Assert.IsTrue(panel.Logic.SetShortcutBinding(0, 1));
            Assert.AreEqual("Sister wait", sister.GetAIConfig().modes[0].modeName);

            Submit(Root.Q<Button>("save-button"));

            CompanionAIConfig applied = sister.GetAIConfig();
            Assert.AreEqual("Cover the knight", applied.modes[0].modeName);
            Assert.AreEqual(1, applied.modeTransitionRules.Single().targetModeIndex);
            Assert.AreEqual(1, applied.shortcutModeBindings[0]);
            Assert.AreEqual("Scout patrol", scout.GetAIConfig().modes[0].modeName);
            Assert.IsFalse(panel.Logic.IsDirty);
        }

        [UnityTest]
        public IEnumerator Preset_SavingDoesNotApplyUntilThePlayerConfirms()
        {
            CompanionController3D sister = CreateCompanion("Sister", Vector3.zero);
            CompanionAISettingsController panel = CreatePanel();
            yield return null;
            sister.SetAIConfig(NewTactic("Sister wait"));
            panel.OpenFor(sister);
            yield return null;
            Assert.IsNotNull(panel.Logic.TacticalRegistry.Save("Prepared", NewTactic("Retreat")));
            panel.OpenFor(sister);

            Submit(Preset("Prepared"));
            Submit(DialogButton("編集する"));
            AIMode edited = panel.Logic.EditingBuffer.modes[0];
            edited.modeName = "Hold the door";
            Assert.IsTrue(panel.Logic.UpdateModeInBuffer(0, edited));
            Submit(Root.Q<Button>("save-button"));
            Assert.AreEqual("Sister wait", sister.GetAIConfig().modes[0].modeName);

            Submit(Preset("Prepared"));
            Submit(DialogButton("現在の戦術に適用"));

            Assert.AreEqual("Hold the door", sister.GetAIConfig().modes[0].modeName);
            Assert.IsNull(panel.Logic.EditingConfigId);
        }

        [UnityTest]
        public IEnumerator CurrentTactic_WhenTheCompanionIsRecycled_LeavesTheNewGenerationUntouched()
        {
            CompanionController3D sister = CreateCompanion("Sister", Vector3.zero);
            CompanionAISettingsController panel = CreatePanel();
            yield return null;
            sister.SetAIConfig(NewTactic("First generation"));
            panel.OpenFor(sister);
            yield return null;

            AIMode draft = panel.Logic.EditingBuffer.modes[0];
            draft.modeName = "Unconfirmed tactic";
            Assert.IsTrue(panel.Logic.UpdateModeInBuffer(0, draft));
            CharacterActor3D actor = sister.GetComponent<CharacterActor3D>();
            ActorRef firstGeneration = actor.Actor;
            sister.gameObject.SetActive(false);
            sister.gameObject.SetActive(true);
            sister.SetAIConfig(NewTactic("New generation"));
            Assert.IsTrue(actor.Actor.IsValid);
            Assert.AreNotEqual(firstGeneration, actor.Actor);

            Submit(Root.Q<Button>("save-button"));

            Assert.AreEqual("New generation", sister.GetAIConfig().modes[0].modeName);
            Assert.IsTrue(panel.Logic.IsDirty);
        }

        private VisualElement Root => _document.rootVisualElement;

        private Button Preset(string title) => Root.Q<ScrollView>("preset-scroll").Query<Button>().ToList()
            .Single(button => button.text == title);

        private Button DialogButton(string title) => Root.Q("dialog-layer").Query<Button>().ToList()
            .Single(button => button.text == title);

        private CompanionAISettingsController CreatePanel()
        {
            GameObject panel = NewObject("Companion AI settings", Vector3.zero);
            _document = panel.AddComponent<UIDocument>();
            _document.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/MyAsset/UI/GamePanelSettings.asset");
            _document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Assets/MyAsset/UI/CompanionAISettings/CompanionAISettings.uxml");
            Assert.IsNotNull(_document.panelSettings);
            Assert.IsNotNull(_document.visualTreeAsset);
            return panel.AddComponent<CompanionAISettingsController>();
        }

        private CompanionController3D CreateCompanion(string name, Vector3 position)
        {
            GameObject body = NewObject(name, position);
            CharacterInfo character = TestSceneHelper.CreateTestCharacterInfo(CharacterBelong.Ally, CharacterFeature.Companion);
            _characters.Add(character);
            CharacterActor3D actor = body.AddComponent<CharacterActor3D>();
            actor.SetCharacterInfoForTest(character);
            CompanionController3D controller = body.AddComponent<CompanionController3D>();
            body.SetActive(true);
            return controller;
        }

        private GameObject NewObject(string name, Vector3 position)
        {
            GameObject instance = new GameObject(name);
            instance.SetActive(false);
            SceneManager.MoveGameObjectToScene(instance, _room);
            instance.transform.position = position;
            return instance;
        }

        private static CompanionAIConfig NewTactic(string firstMode) => new CompanionAIConfig
        {
            modes = new[]
            {
                new AIMode { modeName = firstMode, judgeInterval = new Vector2(0.4f, 0.6f) },
                new AIMode { modeName = "Fallback", judgeInterval = new Vector2(0.4f, 0.6f) },
            },
            shortcutModeBindings = CompanionAISettingsLogic.CreateDefaultShortcutBindings(),
        };

        private static void Submit(Button button)
        {
            Assert.IsNotNull(button);
            Assert.IsTrue(button.enabledInHierarchy);
            Assert.IsNotNull(button.panel);
            using NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled();
            submit.target = button;
            button.SendEvent(submit);
        }
    }
}
#endif
