#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using System.Reflection;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Game.Tests.PlayMode
{
    public sealed class ExpeditionSquadPanelPlayTests
    {
        private GameObject _hud;
        private UIDocument _document;
        private ExpeditionSquadRules _rules;
        private Scene _originalScene;
        private Scene _newScene;

        [UnitySetUp]
        public IEnumerator CleanSession()
        {
            TestSceneHelper.Cleanup();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_originalScene.isLoaded) { SceneManager.SetActiveScene(_originalScene); }
            if (_newScene.isLoaded) { yield return SceneManager.UnloadSceneAsync(_newScene); }
            if (_hud != null) { Object.Destroy(_hud); }
            if (_rules != null) { Object.Destroy(_rules); }
            TestSceneHelper.Cleanup();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Panel_WhenDraftReachesLimits_OnlyTheConfirmButtonCommitsIt()
        {
            ExpeditionSquadManager manager = CreateSquad(3, 4);
            Unlock("scout");
            Unlock("guard");
            Assert.IsTrue(manager.Squad.TryConfirm(new[] { new ExpeditionSquad.Selection("scout", 1) }));
            ExpeditionSquadPanel panel = CreatePanel(manager);
            yield return null;
            panel.Refresh();

            Assert.That(VisibleSpecies(), Is.EqualTo(new[] { "scout", "guard" }));
            Assert.That(LabelText("expedition-count"), Is.EqualTo("体数 1 / 3"));
            Assert.That(LabelText("expedition-cost"), Is.EqualTo("コスト 1 / 4"));
            VisualElement scout = SpeciesRow("scout");
            VisualElement guard = SpeciesRow("guard");
            Assert.That(guard.Q<Label>("expedition-unit-cost").text, Is.EqualTo("単価 2"));

            Submit(guard.Q<Button>("expedition-increase"));
            Assert.That(LabelText("expedition-count"), Is.EqualTo("体数 2 / 3"));
            Assert.That(LabelText("expedition-cost"), Is.EqualTo("コスト 3 / 4"));
            Assert.IsFalse(guard.Q<Button>("expedition-increase").enabledInHierarchy);
            Assert.IsTrue(scout.Q<Button>("expedition-increase").enabledInHierarchy);

            Submit(scout.Q<Button>("expedition-increase"));
            Assert.That(LabelText("expedition-count"), Is.EqualTo("体数 3 / 3"));
            Assert.That(LabelText("expedition-cost"), Is.EqualTo("コスト 4 / 4"));
            Assert.IsFalse(scout.Q<Button>("expedition-increase").enabledInHierarchy);
            Assert.That(manager.Squad.ConfirmedCount, Is.EqualTo(1));

            Submit(_document.rootVisualElement.Q<Button>("expedition-confirm"));
            Assert.That(manager.Squad.ConfirmedCount, Is.EqualTo(3));
            Assert.That(manager.Squad.ConfirmedCost, Is.EqualTo(4));
            Assert.That(manager.Squad.Confirmed.Select(selection => selection.Count), Is.EqualTo(new[] { 2, 1 }));
            Assert.That(LabelText("expedition-status"), Is.EqualTo("編成を確定しました"));

            Submit(scout.Q<Button>("expedition-decrease"));
            Assert.That(LabelText("expedition-count"), Is.EqualTo("体数 2 / 3"));
            panel.enabled = false;
            panel.enabled = true;
            panel.Refresh();
            Assert.That(LabelText("expedition-count"), Is.EqualTo("体数 3 / 3"));
            Assert.That(manager.Squad.ConfirmedCount, Is.EqualTo(3));
        }

        [UnityTest]
        public IEnumerator Panel_WhenSessionChanges_DiscardsAnUnconfirmedDraftAndRefreshesUnlocks()
        {
            ExpeditionSquadManager manager = CreateSquad(1, 8);
            ExpeditionSquadPanel panel = CreatePanel(null);
            yield return null;
            panel.Refresh();
            Assert.That(VisibleSpecies(), Is.Empty);
            Assert.IsFalse(_document.rootVisualElement.Q<Button>("expedition-confirm").enabledInHierarchy);

            Unlock("scout");
            panel.Refresh();
            VisualElement scout = SpeciesRow("scout");
            Submit(scout.Q<Button>("expedition-increase"));
            Assert.That(LabelText("expedition-count"), Is.EqualTo("体数 1 / 1"));
            Assert.That(LabelText("expedition-cost"), Is.EqualTo("コスト 1 / 8"));
            Assert.IsFalse(scout.Q<Button>("expedition-increase").enabledInHierarchy);

            _originalScene = SceneManager.GetActiveScene();
            _newScene = SceneManager.CreateScene("ExpeditionSquadPanelOtherScene");
            Assert.IsTrue(SceneManager.SetActiveScene(_newScene));
            panel.Refresh();
            Assert.That(LabelText("expedition-count"), Is.EqualTo("体数 0 / 1"));
            Assert.That(VisibleSpecies(), Is.EqualTo(new[] { "scout" }));
            Assert.That(manager.Squad.ConfirmedCount, Is.Zero);

            GameManager.Saves.SetSlotData(1, new SaveSlotData(1));
            Assert.IsTrue(GameManager.Saves.Load(1));
            panel.Refresh();
            Assert.That(VisibleSpecies(), Is.Empty);
            Assert.That(LabelText("expedition-status"), Is.EqualTo("解放済みの種がありません"));
            Assert.That(manager.Squad.Confirmed, Is.Empty);

            Unlock("scout");
            SaveSlotData unlockedSlot = GameManager.Saves.Save(1);
            Assert.IsTrue(unlockedSlot.entries.Remove(manager.Squad.SaveId));
            panel.Refresh();
            Submit(SpeciesRow("scout").Q<Button>("expedition-increase"));
            Assert.That(LabelText("expedition-count"), Is.EqualTo("体数 1 / 1"));
            var confirmed = manager.Squad.Confirmed;

            Assert.IsTrue(GameManager.Saves.Load(1));
            panel.Refresh();
            Assert.That(manager.Squad.Confirmed, Is.SameAs(confirmed));
            Assert.That(VisibleSpecies(), Is.EqualTo(new[] { "scout" }));
            Assert.That(LabelText("expedition-count"), Is.EqualTo("体数 0 / 1"));
        }

        [UnityTest]
        public IEnumerator Panel_PresetSlotsSaveAndLoadDistinctDraftsBeforeAnExplicitConfirm()
        {
            ExpeditionSquadManager manager = CreateSquad(3, 4);
            Unlock("scout");
            Unlock("guard");
            ExpeditionSquadPanel panel = CreatePanel(manager);
            yield return null;
            panel.Refresh();

            VisualElement root = _document.rootVisualElement;
            DropdownField slot = root.Q<DropdownField>("expedition-preset-slot");
            Button load = root.Q<Button>("expedition-preset-load");
            Button save = root.Q<Button>("expedition-preset-save");
            Assert.That(slot.choices.Count, Is.EqualTo(3));
            Assert.That(load.enabledInHierarchy, Is.False);
            Assert.That(manager.Squad.Confirmed, Is.Empty);

            Submit(SpeciesRow("scout").Q<Button>("expedition-increase"));
            Submit(save);
            Assert.That(load.enabledInHierarchy, Is.True);
            Assert.That(slot.value, Is.EqualTo("枠 1"));

            slot.index = 1;
            Assert.That(load.enabledInHierarchy, Is.False);
            Submit(SpeciesRow("scout").Q<Button>("expedition-decrease"));
            Submit(SpeciesRow("guard").Q<Button>("expedition-increase"));
            Submit(SpeciesRow("guard").Q<Button>("expedition-increase"));
            Submit(save);
            Assert.That(slot.value, Is.EqualTo("枠 2"));
            Assert.That(LabelText("expedition-cost"), Is.EqualTo("コスト 4 / 4"));
            Assert.That(manager.Squad.Confirmed, Is.Empty);

            slot.index = 0;
            Submit(load);
            Assert.That(LabelText("expedition-count"), Is.EqualTo("体数 1 / 3"));
            Assert.That(SpeciesRow("scout").Q<Label>("expedition-quantity").text, Is.EqualTo("1"));
            Assert.That(manager.Squad.Confirmed, Is.Empty);
            slot.index = 1;
            Submit(load);
            Assert.That(SpeciesRow("guard").Q<Label>("expedition-quantity").text, Is.EqualTo("2"));
            Assert.That(manager.Squad.Confirmed, Is.Empty);
            Submit(root.Q<Button>("expedition-confirm"));
            Assert.That(manager.Squad.Confirmed.Single().SpeciesId, Is.EqualTo("guard"));
            Assert.That(manager.Squad.ConfirmedCount, Is.EqualTo(2));

            Submit(root.Q<Button>("expedition-close"));
            Assert.That(panel.isActiveAndEnabled, Is.False);
            Assert.That(_document.isActiveAndEnabled, Is.True);
            Assert.That(root.Q("expedition-squad").style.display.value, Is.EqualTo(DisplayStyle.None));
            panel.enabled = true;
            Assert.That(LabelText("expedition-count"), Is.EqualTo("体数 2 / 3"));
        }

        private ExpeditionSquadManager CreateSquad(int count, int budget)
        {
            _rules = ScriptableObject.CreateInstance<ExpeditionSquadRules>();
            SetField(_rules, "maxCount", count);
            SetField(_rules, "maxCost", budget);
            SetField(_rules, "species", new[]
            {
                new ExpeditionSquad.Species("scout", 1),
                new ExpeditionSquad.Species("guard", 2),
                new ExpeditionSquad.Species("locked", 1),
            });

            GameObject world = new GameObject("ExpeditionSquadPanelWorld");
            world.SetActive(false);
            GameObject squad = new GameObject("Squad");
            squad.transform.SetParent(world.transform, false);
            ExpeditionSquadManager manager = squad.AddComponent<ExpeditionSquadManager>();
            SetField(manager, "rules", _rules);
            world.AddComponent<GameManager>();
            world.SetActive(true);
            GameManager.Instance.enabled = false;
            Assert.IsNotNull(manager.Squad);
            return manager;
        }

        private ExpeditionSquadPanel CreatePanel(ExpeditionSquadManager manager)
        {
            _hud = new GameObject("ExpeditionSquadPanel");
            _hud.SetActive(false);
            _document = _hud.AddComponent<UIDocument>();
            _document.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/MyAsset/UI/GamePanelSettings.asset");
            _document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Assets/MyAsset/UI/ExpeditionSquad/ExpeditionSquadPanel.uxml");
            Assert.IsNotNull(_document.panelSettings);
            Assert.IsNotNull(_document.visualTreeAsset);
            ExpeditionSquadPanel panel = _hud.AddComponent<ExpeditionSquadPanel>();
            SetField(panel, "squadManager", manager);
            _hud.SetActive(true);
            return panel;
        }

        private static void Unlock(string species)
        {
            string completion = species + ".helped";
            Assert.IsTrue(GameManager.ExpeditionAid.TryComplete(completion,
                new[] { new ExpeditionAidUnlocks.Completion(completion, species) }));
        }

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

        private string LabelText(string name) => _document.rootVisualElement.Q<Label>(name).text;

        private string[] VisibleSpecies() => _document.rootVisualElement.Query<Label>("expedition-species-name")
            .ToList().Select(label => label.text).ToArray();

        private VisualElement SpeciesRow(string id) => _document.rootVisualElement.Query<VisualElement>("expedition-species-row")
            .ToList().Single(row => row.Q<Label>("expedition-species-name").text == id);

        private static void SetField(object target, string field, object value) =>
            target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
    }
}
#endif
