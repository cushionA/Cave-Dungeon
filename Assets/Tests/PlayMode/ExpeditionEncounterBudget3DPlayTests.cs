using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine.UIElements;
#endif
using CharacterInfo = Game.Core.CharacterInfo;

namespace Game.Tests.PlayMode
{
    public sealed class ExpeditionEncounterBudget3DPlayTests
    {
        private static readonly ExpeditionSquad.Selection[] s_ExtraScouts = { new ExpeditionSquad.Selection("scout", 2) };
        private readonly List<ScriptableObject> _assets = new List<ScriptableObject>();
        private Scene _room;
        private CharacterActor3D _knight;
        private ExpeditionSquad _squad;
        private ExpeditionSummonDeployment3D _deployment;

        [UnitySetUp]
        public IEnumerator CreateRoom()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            _room = SceneManager.CreateScene(nameof(ExpeditionEncounterBudget3DPlayTests),
                new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            _squad = CreateSquad();
            _knight = CreateActor("Knight", CharacterFeature.Player, new Vector3(0f, 0f, -5f));
            _deployment = CreatePresentDeployment();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_room.isLoaded) { yield return SceneManager.UnloadSceneAsync(_room); }
            foreach (ScriptableObject asset in _assets) { Object.Destroy(asset); }
            _assets.Clear();
            TestSceneHelper.Cleanup();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Reinforcements_UseOnlyDesignatedCapacity_AndClearOnWithdrawalOrGoal()
        {
            EncounterGoal3D regular = CreateEncounter("ordinary", new Vector3(-4f, 1f, 4f));
            EncounterGoal3D designated = CreateEncounter("bridge", new Vector3(0f, 1f, 4f), EncounterTraits.GroupBattle);
            ExpeditionEncounterBudget3D budget = CreateBudget(designated);
            Assert.IsTrue(_deployment.TryDeployConfirmed());
            List<ExpeditionSummonDeployment3D.DeployedSummon> active = new List<ExpeditionSummonDeployment3D.DeployedSummon>();
            _deployment.CopyActiveSummons(active);
            Assert.AreEqual(1, active.Count);
            ActorRef normal = active[0].Actor;

            Assert.IsTrue(regular.TryStartEncounter());
            Assert.IsFalse(budget.TryDeployReinforcements(s_ExtraScouts));
            regular.AbortEncounter();

            Assert.IsTrue(designated.TryStartEncounter());
            Assert.AreEqual(2, budget.AdditionalCountLimit);
            Assert.AreEqual(2, budget.AdditionalCostLimit);
            Assert.IsFalse(budget.TryDeployReinforcements(new[] { new ExpeditionSquad.Selection("scout", 3) }));
            Assert.IsFalse(budget.TryDeployReinforcements(new[] { new ExpeditionSquad.Selection("guard", 2) }));
            Assert.AreEqual(1, _deployment.ActiveCount);
            Assert.IsTrue(budget.TryDeployReinforcements(s_ExtraScouts));
            Assert.IsFalse(budget.TryDeployReinforcements(s_ExtraScouts));
            _deployment.CopyActiveSummons(active);
            Assert.AreEqual(3, active.Count);
            Assert.IsTrue(active[1].IsReinforcement && active[2].IsReinforcement);
            ActorRef firstExtra = active[1].Actor;
            ActorRef secondExtra = active[2].Actor;

            SaveSlotData slot = GameManager.Saves.Save(0);
            ExpeditionSquad.Selection[] saved = (ExpeditionSquad.Selection[])slot.entries[_squad.SaveId];
            Assert.AreEqual(1, saved.Length);
            Assert.AreEqual(1, saved[0].Count);
            designated.AbortEncounter();
            Assert.IsFalse(GameManager.Actors.IsCurrent(firstExtra));
            Assert.IsFalse(GameManager.Actors.IsCurrent(secondExtra));
            Assert.AreEqual(0, budget.AdditionalCountLimit);
            Assert.AreEqual(1, _deployment.ActiveCount);
            Assert.IsTrue(GameManager.Actors.IsCurrent(normal));

            Assert.IsTrue(designated.TryStartEncounter());
            Assert.IsTrue(budget.TryDeployReinforcements(s_ExtraScouts));
            Assert.IsFalse(GameManager.Saves.Load(2));
            Assert.AreEqual(3, _deployment.ActiveCount);
            Assert.IsTrue(GameManager.Saves.Load(0));
            Assert.IsFalse(designated.IsRunning);
            Assert.IsFalse(budget.IsOpen);
            Assert.AreEqual(1, _deployment.ActiveCount);
            Assert.IsTrue(GameManager.Actors.IsCurrent(normal));

            Assert.IsTrue(designated.TryStartEncounter());
            Assert.IsTrue(budget.TryDeployReinforcements(s_ExtraScouts));
            _knight.transform.position = new Vector3(0f, 0f, 4f);
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(ChallengeState.Completed, designated.Result.state);
            Assert.AreEqual(1, _deployment.ActiveCount);
            Assert.IsTrue(GameManager.Actors.IsCurrent(normal));
            Assert.AreEqual(1, _squad.ConfirmedCount);
            Assert.IsTrue(GameManager.ExpeditionAid.IsUnlocked("scout"));
        }

        [UnityTest]
        public IEnumerator Reinforcements_OtherAttemptEnds_PreservesTheOwningAttemptsActors()
        {
            EncounterGoal3D first = CreateEncounter("first", new Vector3(-4f, 1f, 4f), EncounterTraits.GroupBattle);
            EncounterGoal3D second = CreateEncounter("second", new Vector3(4f, 1f, 4f), EncounterTraits.GroupBattle);
            ExpeditionEncounterBudget3D firstBudget = CreateBudget(first);
            ExpeditionEncounterBudget3D secondBudget = CreateBudget(second);
            Assert.IsTrue(first.TryStartEncounter());
            Assert.IsTrue(second.TryStartEncounter());
            Assert.IsTrue(firstBudget.TryDeployReinforcements(s_ExtraScouts));
            Assert.IsFalse(secondBudget.TryDeployReinforcements(s_ExtraScouts));

            second.AbortEncounter();
            Assert.AreEqual(2, _deployment.ActiveCount);
            firstBudget.enabled = false;
            Assert.AreEqual(0, _deployment.ActiveCount);
            Assert.IsTrue(first.IsRunning);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Reinforcements_UseGroupTraitIndependentlyOfBossAndResetOnlyGroupSessionsOnLoad()
        {
            EncounterSession3D boss = CreateManualEncounter("boss", EncounterTraits.Boss);
            EncounterSession3D group = CreateManualEncounter("group", EncounterTraits.GroupBattle);
            EncounterSession3D both = CreateManualEncounter("group boss", EncounterTraits.GroupBattle | EncounterTraits.Boss);
            GameManager.Saves.Save(0);

            Assert.IsTrue(boss.TryStartEncounter());
            ExpeditionEncounterBudget3D bossBudget = CreateBudget(boss);
            Assert.IsTrue(boss.IsBossBattle);
            Assert.IsFalse(boss.IsGroupBattle);
            Assert.IsFalse(bossBudget.SupportsReinforcements);
            Assert.IsFalse(bossBudget.IsOpen);
            Assert.AreEqual(0, bossBudget.AdditionalCountLimit);
            Assert.IsFalse(bossBudget.TryDeployReinforcements(s_ExtraScouts));
            Assert.AreEqual(0, _deployment.ActiveCount);

            ExpeditionEncounterBudget3D groupBudget = CreateBudget(group);
            Assert.IsTrue(groupBudget.SupportsReinforcements);
            Assert.IsFalse(groupBudget.IsOpen);
            Assert.IsTrue(group.TryStartEncounter());
            Assert.IsTrue(group.IsGroupBattle);
            Assert.IsFalse(group.IsBossBattle);
            Assert.IsTrue(groupBudget.TryDeployReinforcements(s_ExtraScouts));
            Assert.AreEqual(2, _deployment.ActiveCount);
            group.CompleteEncounter();
            Assert.AreEqual(ChallengeState.Completed, group.Result.state);
            Assert.IsFalse(groupBudget.IsOpen);
            Assert.AreEqual(0, _deployment.ActiveCount);

            Assert.IsTrue(both.TryStartEncounter());
            ExpeditionEncounterBudget3D bothBudget = CreateBudget(both);
            Assert.IsTrue(both.IsGroupBattle && both.IsBossBattle);
            Assert.IsTrue(bothBudget.IsOpen);
            Assert.IsTrue(bothBudget.TryDeployReinforcements(s_ExtraScouts));
            Assert.AreEqual(2, _deployment.ActiveCount);
            Assert.IsTrue(GameManager.Saves.Load(0));
            Assert.IsTrue(boss.IsRunning);
            Assert.IsFalse(both.IsRunning);
            Assert.IsFalse(bothBudget.IsOpen);
            Assert.AreEqual(0, _deployment.ActiveCount);
            yield return null;
        }

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator ReinforcementPanel_PreparesPresetsBeforeOpeningAndDeploysOnlyWithinEncounterCaps()
        {
            EncounterGoal3D encounter = CreateEncounter("designated", new Vector3(0f, 1f, 4f), EncounterTraits.GroupBattle);
            ExpeditionEncounterBudget3D budget = CreateBudget(encounter);
            ExpeditionSquadManager manager = GameManager.GetSubManager<ExpeditionSquadManager>();
            Assert.IsTrue(manager.Presets.TrySave(1, new[] { new ExpeditionSquad.Selection("guard", 2) }));
            Assert.IsTrue(_deployment.TryDeployConfirmed());
            IReadOnlyList<ExpeditionSquad.Selection> normal = _squad.Confirmed;
            Scene initialScene = SceneManager.GetActiveScene();
            try
            {
                Assert.IsTrue(SceneManager.SetActiveScene(_room));
                GameObject hud = new GameObject("Reinforcement panel");
                hud.SetActive(false);
                UIDocument document = hud.AddComponent<UIDocument>();
                document.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/MyAsset/UI/GamePanelSettings.asset");
                document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                    "Assets/MyAsset/UI/ExpeditionSquad/ExpeditionSquadPanel.uxml");
                ExpeditionSquadPanel panel = hud.AddComponent<ExpeditionSquadPanel>();
                SetField(panel, "encounterBudget", budget);
                hud.SetActive(true);
                yield return null;
                panel.Refresh();

                VisualElement root = document.rootVisualElement;
                Button confirm = root.Q<Button>("expedition-confirm");
                Submit(root.Q<Button>("expedition-reinforcements"));
                Assert.That(confirm.enabledInHierarchy, Is.False);
                Assert.That(root.Q<Label>("expedition-status").text, Is.EqualTo("増援は遭遇開始後に投入できます"));
                Assert.That(budget.AdditionalCountLimit, Is.Zero);
                Assert.That(root.Q<Label>("expedition-count").text, Is.EqualTo("体数 0 / 2"));

                Button addScout = FindSpeciesButton(root, "scout", "expedition-increase");
                Submit(addScout);
                Submit(addScout);
                Assert.That(addScout.enabledInHierarchy, Is.False);
                Assert.That(confirm.enabledInHierarchy, Is.False);
                Assert.That(_deployment.ActiveCount, Is.EqualTo(1));
                Submit(root.Q<Button>("expedition-preset-save"));
                Assert.IsTrue(manager.Presets.TryGet(0, out IReadOnlyList<ExpeditionSquad.Selection> prepared));
                Assert.That(prepared.Count, Is.EqualTo(1));
                Assert.That(prepared[0].SpeciesId, Is.EqualTo("scout"));
                Assert.That(prepared[0].Count, Is.EqualTo(2));

                Assert.IsTrue(encounter.TryStartEncounter());
                panel.Refresh();
                Assert.That(root.Q<Label>("expedition-count").text, Is.EqualTo("体数 2 / 2"));
                Assert.That(confirm.enabledInHierarchy, Is.True);
                DropdownField slot = root.Q<DropdownField>("expedition-preset-slot");
                slot.index = 1;
                Submit(root.Q<Button>("expedition-preset-load"));
                Assert.That(root.Q<Label>("expedition-count").text, Is.EqualTo("体数 2 / 2"));
                Assert.That(root.Q<Label>("expedition-cost").text, Is.EqualTo("コスト 4 / 2"));
                Assert.That(root.Q<Label>("expedition-status").text, Does.Contain("上限"));
                Assert.That(confirm.enabledInHierarchy, Is.False);

                slot.index = 0;
                Submit(root.Q<Button>("expedition-preset-load"));
                Assert.That(root.Q<Label>("expedition-cost").text, Is.EqualTo("コスト 2 / 2"));
                Submit(confirm);
                Assert.That(_deployment.ActiveCount, Is.EqualTo(3));
                Assert.That(_squad.Confirmed, Is.SameAs(normal));
                Assert.That(confirm.enabledInHierarchy, Is.False);

                encounter.AbortEncounter();
                panel.Refresh();
                Assert.That(_deployment.ActiveCount, Is.EqualTo(1));
                Assert.That(budget.AdditionalCountLimit, Is.Zero);
                Assert.That(confirm.enabledInHierarchy, Is.False);
                Assert.That(root.Q<Label>("expedition-count").text, Is.EqualTo("体数 0 / 2"));
                Submit(root.Q<Button>("expedition-sortie"));
                Assert.That(root.Q<Label>("expedition-count").text, Is.EqualTo("体数 1 / 1"));
                Assert.That(_squad.Confirmed, Is.SameAs(normal));
            }
            finally
            {
                if (initialScene.isLoaded) { SceneManager.SetActiveScene(initialScene); }
            }
        }

        private static Button FindSpeciesButton(VisualElement root, string species, string control)
        {
            foreach (VisualElement row in root.Query<VisualElement>("expedition-species-row").ToList())
            {
                if (row.Q<Label>("expedition-species-name").text == species) { return row.Q<Button>(control); }
            }
            return null;
        }

        private static void Submit(Button button)
        {
            Assert.IsNotNull(button);
            Assert.IsTrue(button.enabledInHierarchy);
            using NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled();
            submit.target = button;
            button.SendEvent(submit);
        }
#endif

        private ExpeditionSquad CreateSquad()
        {
            ExpeditionSquadRules rules = ScriptableObject.CreateInstance<ExpeditionSquadRules>();
            _assets.Add(rules);
            SetField(rules, "maxCount", 1);
            SetField(rules, "maxCost", 1);
            SetField(rules, "species", new[] { new ExpeditionSquad.Species("scout", 1), new ExpeditionSquad.Species("guard", 2) });
            GameObject world = new GameObject("Expedition session");
            world.SetActive(false);
            ExpeditionSquadManager inventory = world.AddComponent<ExpeditionSquadManager>();
            SetField(inventory, "rules", rules);
            world.AddComponent<GameManager>();
            world.SetActive(true);
            ExpeditionAidUnlocks.Completion[] unlocks =
            {
                new ExpeditionAidUnlocks.Completion("scout.helped", "scout"),
                new ExpeditionAidUnlocks.Completion("guard.helped", "guard"),
            };
            Assert.IsTrue(GameManager.ExpeditionAid.TryComplete("scout.helped", unlocks));
            Assert.IsTrue(GameManager.ExpeditionAid.TryComplete("guard.helped", unlocks));
            Assert.IsTrue(inventory.Squad.TryConfirm(new[] { new ExpeditionSquad.Selection("scout", 1) }));
            return inventory.Squad;
        }

        private ExpeditionSummonDeployment3D CreatePresentDeployment()
        {
            CharacterActor3D sister = CreateActor("Sister", CharacterFeature.Companion, new Vector3(0f, 0f, -5f));
            CharacterActor3D template = CreateActor("Scout prefab", CharacterFeature.Summon, Vector3.zero, activate: false);
            ExpeditionSummonDeployment3D deployment = sister.gameObject.AddComponent<ExpeditionSummonDeployment3D>();
            Transform[] points = new Transform[4];
            for (int i = 0; i < points.Length; i++)
            {
                points[i] = CreateRoomObject($"Spawn {i}", new Vector3(i * 2f, 0f, -2f)).transform;
            }
            deployment.ConfigureForTest(new[] { new ExpeditionSummonDeployment3D.SpeciesPrefab("scout", template) },
                points, Physics.DefaultRaycastLayers);
            return deployment;
        }

        private CharacterActor3D CreateActor(string title, CharacterFeature feature, Vector3 position, bool activate = true)
        {
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(CharacterBelong.Ally, feature);
            _assets.Add(info);
            GameObject actor = CreateRoomObject(title, position);
            actor.SetActive(false);
            CharacterController body = actor.AddComponent<CharacterController>();
            body.height = 1.6f;
            body.center = Vector3.up * 0.8f;
            body.radius = 0.3f;
            CharacterActor3D character = actor.AddComponent<CharacterActor3D>();
            character.SetCharacterInfoForTest(info);
            actor.SetActive(activate);
            return character;
        }

        private EncounterGoal3D CreateEncounter(string title, Vector3 position, EncounterTraits traits = EncounterTraits.None)
        {
            GameObject target = CreateRoomObject(title, position);
            BoxCollider volume = target.AddComponent<BoxCollider>();
            volume.isTrigger = true;
            volume.size = new Vector3(2f, 2f, 2f);
            EncounterGoal3D encounter = target.AddComponent<EncounterGoal3D>();
            encounter.ConfigureForTest(_knight, CreateChallenge(title), volume, traits);
            return encounter;
        }

        private EncounterSession3D CreateManualEncounter(string title, EncounterTraits traits)
        {
            GameObject owner = CreateRoomObject(title, Vector3.zero);
            EncounterSession3D encounter = owner.AddComponent<EncounterSession3D>();
            encounter.ConfigureForTest(_knight, CreateChallenge(title), traits);
            return encounter;
        }

        private ChallengeDefinition CreateChallenge(string title)
        {
            ChallengeDefinition challenge = ScriptableObject.CreateInstance<ChallengeDefinition>();
            _assets.Add(challenge);
            challenge.SetForTest(title, title, ChallengeType.TimeAttack);
            return challenge;
        }

        private ExpeditionEncounterBudget3D CreateBudget(EncounterSession3D encounter)
        {
            GameObject owner = CreateRoomObject("Designated reinforcements", Vector3.zero);
            owner.SetActive(false);
            ExpeditionEncounterBudget3D budget = owner.AddComponent<ExpeditionEncounterBudget3D>();
            budget.ConfigureForTest(encounter, _deployment, 2, 2);
            owner.SetActive(true);
            return budget;
        }

        private GameObject CreateRoomObject(string title, Vector3 position)
        {
            GameObject gameObject = new GameObject(title);
            SceneManager.MoveGameObjectToScene(gameObject, _room);
            gameObject.transform.position = position;
            return gameObject;
        }

        private static void SetField(Object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field);
            field.SetValue(target, value);
        }
    }
}
