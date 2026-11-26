#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using R3;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using CharacterInfo = Game.Core.CharacterInfo;
using Object = UnityEngine.Object;

namespace Game.Tests.PlayMode
{
    public sealed class ExpeditionFastTravel3DPlayTests
    {
        private const string k_OriginPath = "Assets/Scenes/StreamingAreaA3DTestScene.unity";
        private const string k_DestinationPath = "Assets/Scenes/StreamingAreaB3DTestScene.unity";
        private const string k_ConcurrentPath = "Assets/Scenes/ExpeditionEvent3DScene.unity";
        private const string k_Home = "test-camp";
        private const string k_Passage = "test-passage";
        private const string k_LiveProgress = "test.fast-travel.unsaved-progress";
        private const int k_Slot = 1;
        private const float k_ArrivalSlopeAngle = 35f;
        private static readonly Vector3 s_Home = new Vector3(0f, 0.04f, 2f);
        private static readonly Vector3 s_Passage = new Vector3(0f, 0.04f, 10.7f);

        private Scene _initialScene;
        private Scene _origin;
        private string _directory;
        private SaveDataStore _store;
        private LevelStreamingController _streaming;
        private CharacterActor3D _knight;
        private CharacterInfo _knightInfo;
        private PlayerInput _input;
        private InputActionAsset _actions;
        private ExpeditionFastTravel3D _travel;
        private ExpeditionSavePoint3D _checkpoint;
        private ExpeditionFastTravelPoint3D _home;
        private ExpeditionFastTravelPoint3D _passage;
        private UIDocument _document;
        private bool _blockArrival;
        private bool _blockWithCombinedSurface;
        private bool _slopedArrival;
        private Mesh _combinedSurface;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            _blockArrival = false;
            _blockWithCombinedSurface = false;
            _slopedArrival = false;
            _passage = null;
            _initialScene = SceneManager.GetActiveScene();
            _directory = Path.Combine(Path.GetTempPath(), nameof(ExpeditionFastTravel3DPlayTests), Guid.NewGuid().ToString("N"));
            _store = new SaveDataStore(_directory);
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(k_OriginPath, new LoadSceneParameters(LoadSceneMode.Additive));
            _origin = SceneManager.GetSceneByPath(k_OriginPath);
            Assert.IsTrue(SceneManager.SetActiveScene(_origin));

            GameObject manager = CreateInactive(_origin, "Fast travel world", Vector3.zero);
            _streaming = manager.AddComponent<LevelStreamingController>();
            SetField(_streaming, "persistentSceneName", _origin.name);
            manager.AddComponent<GameManager>();
            manager.SetActive(true);
            GameManager.Instance.enabled = false;
            GameManager.Saves.SetActiveSlot(k_Slot);
            _streaming.Orchestrator.AttachFlagManager(GameManager.Flags);
            GameManager.Flags.SwitchMap(_origin.name);

            GameObject panel = CreateInactive(_origin, "Fast travel panel", Vector3.zero);
            _document = panel.AddComponent<UIDocument>();
            _document.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/MyAsset/UI/GamePanelSettings.asset");
            Assert.IsNotNull(_document.panelSettings);
            panel.SetActive(true);

            GameObject knight = CreateInactive(_origin, "Fast travel knight", s_Home);
            CharacterController body = knight.AddComponent<CharacterController>();
            body.radius = 0.32f;
            body.height = 1.65f;
            body.center = Vector3.up * 0.825f;
            body.slopeLimit = 45f;
            _knight = knight.AddComponent<CharacterActor3D>();
            _knightInfo = TestSceneHelper.CreateTestCharacterInfo();
            _knight.SetCharacterInfoForTest(_knightInfo);
            _actions = ScriptableObject.CreateInstance<InputActionAsset>();
            _actions.AddActionMap("Player").AddAction("Interact", binding: "<Keyboard>/e");
            _actions.AddActionMap("UI").AddAction("Submit", binding: "<Keyboard>/enter");
            _input = knight.AddComponent<PlayerInput>();
            _input.actions = _actions;
            _input.defaultActionMap = "Player";
            _travel = knight.AddComponent<ExpeditionFastTravel3D>();
            SetField(_travel, "document", _document);
            SetDestinations(_travel);
            knight.SetActive(true);

            _home = CreatePoint(_origin, k_Home, s_Home, manualCheckpoint: true);
            SceneManager.sceneLoaded += AddPassagePoint;
            yield return null;
            Assert.IsTrue(_knight.Actor.IsValid);
            Assert.AreEqual("Player", _input.currentActionMap?.name);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            SceneManager.sceneLoaded -= AddPassagePoint;
            if (_streaming != null && _streaming.Orchestrator is LevelStreamingOrchestrator streaming)
            {
                // A canceled trip may leave the additive load running. Await it before unloading the scene.
                yield return Until(() => !streaming.IsAreaLoadPending(k_DestinationPath) && !streaming.IsAreaLoadPending(k_ConcurrentPath)
                    && streaming.State != StreamingState.Unloading, "The fixture's scene operations should finish before teardown.");
            }
            if (_initialScene.isLoaded) { SceneManager.SetActiveScene(_initialScene); }
            Scene destination = SceneManager.GetSceneByPath(k_DestinationPath);
            if (destination.isLoaded) { yield return SceneManager.UnloadSceneAsync(destination); }
            Scene concurrentArea = SceneManager.GetSceneByPath(k_ConcurrentPath);
            if (concurrentArea.isLoaded) { yield return SceneManager.UnloadSceneAsync(concurrentArea); }
            if (_origin.isLoaded) { yield return SceneManager.UnloadSceneAsync(_origin); }
            Object.Destroy(_actions);
            Object.Destroy(_knightInfo);
            Object.Destroy(_combinedSurface);
            _combinedSurface = null;
            TestSceneHelper.Cleanup();
            yield return null;
            if (Directory.Exists(_directory)) { Directory.Delete(_directory, recursive: true); }
        }

        [UnityTest]
        public IEnumerator FastTravel_WhenVisitIsSaved_ReopensUnloadedAreaWithSameKnightAndSlot()
        {
            Assert.IsTrue(_home.GetComponent<NpcInteraction3D>().TryInteract(_knight));
            Assert.IsTrue(_travel.IsOpen);
            Assert.IsNull(ButtonFor(k_Passage));
            Assert.AreEqual("UI", _input.currentActionMap?.name);
            Submit(_document.rootVisualElement.Q<Button>("fast-travel-close"));
            Assert.AreEqual("Player", _input.currentActionMap?.name);

            yield return VisitPassageAndReturn();
            Assert.IsTrue(_checkpoint.TrySaveCurrentSlot());
            SaveManager saves = GameManager.Saves;
            saves.SetSlotData(k_Slot, _store.ReadFromDisk(k_Slot));
            saves.SetSlotData(2, new SaveSlotData(2));
            Assert.IsTrue(saves.Load(2));
            Assert.IsTrue(_travel.TryOpen(_home));
            Assert.IsNull(ButtonFor(k_Passage), "An older slot without unlock flags cannot borrow a visited point.");
            _travel.Close();
            Assert.IsTrue(saves.Load(k_Slot));

            ActorRef owner = _knight.Actor;
            SaveSlotData saved = saves.GetSlotData(k_Slot);
            ulong revision = saves.LoadRevision;
            string file = File.ReadAllText(Path.Combine(_directory, "saves", $"slot_{k_Slot}.json"));
            GameManager.Data.GetVitals(owner.Hash).currentHp = 37;
            GameManager.Data.GetVitals(owner.Hash).currentMp = 14f;
            GameManager.Data.GetVitals(owner.Hash).currentStamina = 31f;
            GameManager.Flags.SetGlobalFlag(k_LiveProgress, true);
            var transitions = new List<(string from, string to)>();
            using var transition = GameManager.Events.OnAreaTransition.Subscribe(transitions.Add);

            Assert.IsTrue(_travel.TryOpen(_home));
            Assert.IsFalse(SceneManager.GetSceneByPath(k_DestinationPath).isLoaded);
            Submit(ButtonFor(k_Passage));
            Assert.IsTrue(_travel.IsTraveling);
            Assert.IsFalse(_travel.TryTravel(k_Passage), "A pending travel must reject another launch.");
            yield return Until(() => !_travel.IsTraveling, "The unloaded area should become available through level streaming.");
            AssertArrived(s_Passage, k_DestinationPath);
            Assert.AreEqual(owner, _knight.Actor);
            Assert.AreEqual(_origin, _knight.gameObject.scene);
            Assert.AreEqual(k_Slot, saves.ActiveSlotIndex);
            Assert.AreEqual(revision, saves.LoadRevision);
            Assert.AreSame(saved, saves.GetSlotData(k_Slot));
            Assert.AreEqual(file, File.ReadAllText(Path.Combine(_directory, "saves", $"slot_{k_Slot}.json")));
            Assert.IsTrue(GameManager.Flags.GetGlobalFlag(k_LiveProgress));
            CharacterVitals current = GameManager.Data.GetVitals(owner.Hash);
            Assert.AreEqual(37, current.currentHp);
            Assert.AreEqual(14f, current.currentMp);
            Assert.AreEqual(31f, current.currentStamina);
            Assert.That(transitions, Is.EqualTo(new[] { (_origin.name, k_DestinationPath) }));

            for (int step = 0; step < 15 && !_knight.Motor.IsGrounded; step++)
            {
                _knight.Motor.Simulate(Vector3.zero, false, Time.fixedDeltaTime);
                yield return new WaitForFixedUpdate();
            }
            Assert.IsTrue(_knight.Motor.IsGrounded);
            Assert.AreEqual(SceneManager.GetSceneByPath(k_DestinationPath), _knight.Motor.Ground.Surface.gameObject.scene);
        }

        [UnityTest]
        public IEnumerator FastTravel_WhenArrivalIsBlocked_KeepsTheSourceMapAndKnight()
        {
            yield return VisitPassageAndReturn();
            _blockArrival = true;
            Assert.IsTrue(_travel.TryOpen(_home));
            ActorRef owner = _knight.Actor;
            Vector3 source = _knight.transform.position;
            var transitions = new List<(string from, string to)>();
            using var transition = GameManager.Events.OnAreaTransition.Subscribe(transitions.Add);
            var areaUnloads = new List<string>();
            _streaming.Orchestrator.OnAreaUnloadCompleted += areaUnloads.Add;

            Submit(ButtonFor(k_Passage));
            Assert.IsTrue(_travel.IsTraveling);
            yield return Until(() => !_travel.IsTraveling, "The unsafe arrival should be rejected after loading.");
            yield return Until(IsPassageUnloaded, "The unsafe arrival's preload should be released.");
            Assert.AreEqual(source, _knight.transform.position);
            Assert.AreEqual(owner, _knight.Actor);
            Assert.AreEqual(_origin.name, _streaming.Orchestrator.ActiveScene);
            Assert.AreEqual(_origin.name, GameManager.Flags.CurrentMapId);
            Assert.That(transitions, Is.Empty, "Preloading a blocked arrival must not publish a transition.");
            Assert.That(areaUnloads, Is.Empty, "Discarding a preload must not trigger gameplay area's global pool cleanup.");
        }

        [UnityTest]
        public IEnumerator FastTravel_WhenTheSupportMeshAlsoContainsAnObstructingWall_RejectsArrival()
        {
            yield return VisitPassageAndReturn();
            _blockWithCombinedSurface = true;
            Assert.IsTrue(_travel.TryOpen(_home));
            Vector3 source = _knight.transform.position;
            Submit(ButtonFor(k_Passage));
            Assert.IsTrue(_travel.IsTraveling);

            yield return Until(() => !_travel.IsTraveling, "A wall belonging to the support collider should block arrival.");
            yield return Until(IsPassageUnloaded, "The failed arrival's preload should be released.");
            Assert.AreEqual(source, _knight.transform.position);
            Assert.AreEqual(_origin.name, GameManager.Flags.CurrentMapId);
        }

        [UnityTest]
        public IEnumerator FastTravel_WhenArrivalIsOnAWalkableSlope_PlacesTheKnightClearOfTheFloor()
        {
            yield return VisitPassageAndReturn();
            _slopedArrival = true;
            Assert.IsTrue(_travel.TryOpen(_home));
            Submit(ButtonFor(k_Passage));
            Assert.IsTrue(_travel.IsTraveling);

            yield return Until(() => !_travel.IsTraveling, "The walkable slope should be a valid landing.");
            PhysicsScene physics = SceneManager.GetSceneByPath(k_DestinationPath).GetPhysicsScene();
            Assert.IsTrue(physics.Raycast(s_Passage + Vector3.up * 0.22f, Vector3.down, out RaycastHit support, 0.44f,
                LayerMask.GetMask("Ground"), QueryTriggerInteraction.Ignore));
            Assert.That(Vector3.Angle(support.normal, Vector3.up), Is.EqualTo(k_ArrivalSlopeAngle).Within(0.5f));
            AssertArrived(support.point + Vector3.up * 0.1f, k_DestinationPath);
        }

        [UnityTest]
        public IEnumerator FastTravel_WhenSlotIsSwitchedDuringPreload_DiscardsTheRequest()
        {
            yield return VisitPassageAndReturn();
            Assert.IsTrue(_travel.TryOpen(_home));
            Vector3 source = _knight.transform.position;
            Submit(ButtonFor(k_Passage));
            Assert.IsTrue(_travel.IsTraveling);
            _streaming.Orchestrator.ProcessQueue();
            Assert.AreEqual(StreamingState.Loading, _streaming.Orchestrator.State);

            GameManager.Saves.SetActiveSlot(2);
            GameManager.Saves.SetActiveSlot(k_Slot);
            yield return Until(() => !_travel.IsTraveling, "Changing save slots must invalidate a queued travel.");
            yield return Until(IsPassageUnloaded, "The in-flight preload should be released after cancellation.");
            Assert.AreEqual(source, _knight.transform.position);
            Assert.AreEqual(_origin.name, _streaming.Orchestrator.ActiveScene);
            Assert.AreEqual(_origin.name, GameManager.Flags.CurrentMapId);
            Assert.AreEqual("Player", _input.currentActionMap?.name);
        }

        [UnityTest]
        public IEnumerator FastTravel_WhenRetriedImmediately_ReusesTheInFlightPreload()
        {
            yield return VisitPassageAndReturn();
            var started = new List<string>();
            using var sceneLoad = GameManager.Events.OnSceneLoadStarted.Subscribe(started.Add);
            Assert.IsTrue(_travel.TryOpen(_home));
            Submit(ButtonFor(k_Passage));
            _streaming.Orchestrator.ProcessQueue();
            Assert.AreEqual(StreamingState.Loading, _streaming.Orchestrator.State);
            Submit(_document.rootVisualElement.Q<Button>("fast-travel-close"));
            Assert.IsFalse(_travel.IsOpen);

            Assert.IsTrue(_travel.TryOpen(_home));
            Submit(ButtonFor(k_Passage));
            Assert.IsTrue(_travel.IsTraveling);
            yield return Until(() => !_travel.IsTraveling, "The reopened menu should reuse the in-flight load.");
            AssertArrived(s_Passage, k_DestinationPath);
            Assert.That(started, Is.EqualTo(new[] { k_DestinationPath }));
        }

        [UnityTest]
        public IEnumerator Streaming_WhenOrdinaryMovementTargetsACanceledTravelPreload_ActivatesTheAreaOnce(
            [Values(StreamingState.Idle, StreamingState.Loading, StreamingState.Unloading)] StreamingState activationState)
        {
            yield return VisitPassageAndReturn();
            LevelStreamingOrchestrator streaming = _streaming.Orchestrator;
            var arrivals = new List<string>();
            streaming.OnAreaLoadCompleted += arrivals.Add;
            var started = new List<string>();
            using var sceneLoad = GameManager.Events.OnSceneLoadStarted.Subscribe(started.Add);
            Assert.IsTrue(_travel.TryOpen(_home));
            Submit(ButtonFor(k_Passage));
            Assert.IsTrue(_travel.IsTraveling);
            if (activationState != StreamingState.Idle) { streaming.ProcessQueue(); }
            Submit(_document.rootVisualElement.Q<Button>("fast-travel-close"));
            if (activationState == StreamingState.Unloading)
            {
                yield return Until(() => streaming.State == StreamingState.Unloading, "Canceled preload should begin unloading.");
            }
            Assert.AreEqual(activationState, streaming.State);

            Assert.IsTrue(_streaming.RequestAreaLoad(k_DestinationPath));
            yield return Until(() => streaming.ActiveScene == k_DestinationPath, "The normal area request should activate the scene.");
            Assert.That(arrivals, Is.EqualTo(new[] { k_DestinationPath }));
            Assert.AreEqual(activationState == StreamingState.Unloading ? 2 : 1, started.Count);
            Assert.IsTrue(SceneManager.GetSceneByPath(k_DestinationPath).isLoaded);
            Assert.AreEqual(k_DestinationPath, GameManager.Flags.CurrentMapId);
            Assert.AreEqual(s_Home.x, _knight.transform.position.x);
            Assert.AreEqual(s_Home.z, _knight.transform.position.z);
        }

        [UnityTest]
        public IEnumerator FastTravel_WhenArrivalIsAlreadyUnloading_RejectsTheRequest()
        {
            yield return VisitPassageAndReturn(unloadPassage: false);
            Assert.IsTrue(_travel.TryOpen(_home));
            Vector3 source = _knight.transform.position;
            Assert.IsTrue(_streaming.RequestAreaUnload(k_DestinationPath));
            Assert.IsFalse(_travel.TryTravel(k_Passage));
            yield return Until(() => !_streaming.Orchestrator.IsLoaded(k_DestinationPath), "The original unload should be able to finish.");
            Assert.AreEqual(source, _knight.transform.position);
            Assert.AreEqual(_origin.name, GameManager.Flags.CurrentMapId);
        }

        [UnityTest]
        public IEnumerator Streaming_WhenWalkingIntoAnAreaLeftResidentByTravel_ActivatesItAgain()
        {
            yield return VisitPassageAndReturn(unloadPassage: false);
            Scene destination = SceneManager.GetSceneByPath(k_DestinationPath);
            Assert.IsTrue(destination.isLoaded);
            Assert.IsTrue(_streaming.RequestAreaLoad(k_DestinationPath));
            Assert.AreEqual(k_DestinationPath, _streaming.Orchestrator.ActiveScene);
            Assert.AreEqual(k_DestinationPath, GameManager.Flags.CurrentMapId);
            Assert.AreEqual(destination, SceneManager.GetSceneByPath(k_DestinationPath));
        }

        [UnityTest]
        public IEnumerator FastTravel_WhenOrdinaryAreaLoadIsQueuedOrRunning_LeavesTheResidentDestinationUntouched()
        {
            yield return VisitPassageAndReturn(unloadPassage: false);
            Assert.IsTrue(_travel.TryOpen(_home));
            Vector3 source = _knight.transform.position;
            var transitions = new List<(string from, string to)>();
            using var transition = GameManager.Events.OnAreaTransition.Subscribe(transitions.Add);
            Submit(ButtonFor(k_Passage));
            Assert.IsTrue(_travel.IsTraveling);

            _streaming.enabled = false;
            try
            {
                Assert.IsTrue(_streaming.RequestAreaLoad(k_ConcurrentPath));
                yield return Until(() => !_travel.IsTraveling, "An ordinary queued load must invalidate fast travel.");
                Assert.IsFalse(_travel.TryTravel(k_Passage), "The resident destination cannot supersede a pending load.");
                _streaming.Orchestrator.ProcessQueue();
                Assert.AreEqual(StreamingState.Loading, _streaming.Orchestrator.State);
                Assert.IsFalse(_travel.TryTravel(k_Passage), "The resident destination cannot supersede a running load.");
                Assert.AreEqual(source, _knight.transform.position);
                Assert.AreEqual(_origin.name, GameManager.Flags.CurrentMapId);
                Assert.That(transitions, Is.Empty);
            }
            finally { _streaming.enabled = true; }

            yield return Until(() => _streaming.Orchestrator.ActiveScene == k_ConcurrentPath, "The ordinary load should finish normally.");
            Assert.AreEqual(source, _knight.transform.position);
            Assert.That(transitions, Is.EqualTo(new[] { (_origin.name, k_ConcurrentPath) }));
            Assert.IsTrue(_streaming.Orchestrator.IsLoaded(k_DestinationPath), "An already resident area belongs to the world, not the canceled trip.");
        }

        private bool IsPassageUnloaded() => !_streaming.Orchestrator.IsAreaLoadPending(k_DestinationPath)
            && !_streaming.Orchestrator.IsLoaded(k_DestinationPath) && !SceneManager.GetSceneByPath(k_DestinationPath).isLoaded;

        private IEnumerator VisitPassageAndReturn(bool unloadPassage = true)
        {
            Assert.IsTrue(_streaming.RequestAreaLoad(k_DestinationPath));
            yield return Until(() => _streaming.Orchestrator.IsLoaded(k_DestinationPath) && _passage != null,
                "The native destination should load before its first visit.");
            StandAt(s_Passage);

            Scene passageScene = _passage.gameObject.scene;
            Scene loadedPassage = SceneManager.GetSceneByPath(k_DestinationPath);
            Assert.AreEqual(passageScene, loadedPassage, $"Point scene {passageScene.handle} and path lookup {loadedPassage.handle} must agree.");
            Assert.IsTrue(_passage.GetComponent<NpcInteraction3D>().TryInteract(_knight), "An additive checkpoint is interactable within the same physics scene.");
            Assert.IsTrue(_travel.IsOpen);
            _travel.Close();
            StandAt(s_Home);
            Assert.IsTrue(_home.GetComponent<NpcInteraction3D>().TryInteract(_knight));
            Assert.IsTrue(_travel.IsOpen);
            Submit(ButtonFor(k_Passage));
            Assert.IsTrue(_travel.IsTraveling);
            yield return Until(() => !_travel.IsTraveling, "The discovered destination should be reachable.");
            AssertArrived(s_Passage, k_DestinationPath);
            Assert.IsTrue(_passage.GetComponent<NpcInteraction3D>().TryInteract(_knight));
            Submit(ButtonFor(k_Home));
            yield return Until(() => !_travel.IsTraveling, "The persistent camp should remain a valid return destination.");
            AssertArrived(s_Home, _origin.name);
            if (!unloadPassage) { yield break; }
            yield return SceneManager.UnloadSceneAsync(SceneManager.GetSceneByPath(k_DestinationPath));
            Assert.IsFalse(_streaming.Orchestrator.IsLoaded(k_DestinationPath));
        }

        private void StandAt(Vector3 position)
        {
            _knight.Body.enabled = false;
            _knight.transform.position = position;
            _knight.Body.enabled = true;
            Physics.SyncTransforms();
        }

        private void AssertArrived(Vector3 position, string activeArea)
        {
            string status = _document.rootVisualElement.Q<Label>("fast-travel-status")?.text;
            Assert.That(Vector3.Distance(_knight.transform.position, position), Is.LessThan(0.08f),
                $"Position {_knight.transform.position}, active map {_streaming.Orchestrator.ActiveScene}, status '{status}'.");
            Assert.AreEqual(activeArea, _streaming.Orchestrator.ActiveScene);
            Assert.AreEqual(activeArea, GameManager.Flags.CurrentMapId);
            Assert.IsFalse(_travel.IsOpen);
            Assert.AreEqual("Player", _input.currentActionMap?.name);
        }

        private ExpeditionFastTravelPoint3D CreatePoint(Scene scene, string id, Vector3 position, bool manualCheckpoint)
        {
            GameObject marker = CreateInactive(scene, id, position);
            ExpeditionFastTravelPoint3D point = marker.AddComponent<ExpeditionFastTravelPoint3D>();
            NpcInteraction3D interaction = marker.GetComponent<NpcInteraction3D>();
            SetField(interaction, "interactionType", InteractionType.SavePoint);
            SetField(point, "pointId", id);
            SetField(point, "arrivalAnchor", marker.transform);
            if (manualCheckpoint)
            {
                _checkpoint = marker.AddComponent<ExpeditionSavePoint3D>();
                SetField(_checkpoint, "savePointId", id);
                SetField(_checkpoint, "knight", _knight);
                SetField(_checkpoint, "respawnAnchor", marker.transform);
                _checkpoint.SetStoreForTest(_store);
                UnityEvent used = new UnityEvent();
                used.AddListener(_checkpoint.SaveCurrentSlot);
                SetField(interaction, "onInteracted", used);
            }
            marker.SetActive(true);
            return point;
        }

        private void AddPassagePoint(Scene scene, LoadSceneMode mode)
        {
            if (scene.path != k_DestinationPath || mode != LoadSceneMode.Additive) { return; }
            _passage = CreatePoint(scene, k_Passage, s_Passage, manualCheckpoint: false);
            if (_blockWithCombinedSurface) { CreateCombinedSurface(scene); }
            if (_slopedArrival)
            {
                GameObject slope = CreateInactive(scene, "Sloped travel floor", s_Passage + Vector3.up * 0.04f);
                slope.layer = LayerMask.NameToLayer("Ground");
                slope.transform.rotation = Quaternion.Euler(0f, 0f, k_ArrivalSlopeAngle);
                slope.AddComponent<BoxCollider>().size = new Vector3(1.4f, 0.06f, 1.4f);
                slope.SetActive(true);
            }
            if (!_blockArrival) { return; }
            GameObject obstruction = CreateInactive(scene, "Blocked travel arrival", s_Passage + Vector3.up * 0.7f);
            obstruction.AddComponent<BoxCollider>().size = new Vector3(1f, 1.4f, 1f);
            obstruction.SetActive(true);
        }

        private void CreateCombinedSurface(Scene scene)
        {
            GameObject geometry = CreateInactive(scene, "Travel floor and wall", s_Passage);
            _combinedSurface = new Mesh { name = "One collider for the floor and wall" };
            _combinedSurface.vertices = new[]
            {
                new Vector3(-0.8f, 0.08f, -0.8f), new Vector3(-0.8f, 0.08f, 0.8f),
                new Vector3(0.8f, 0.08f, 0.8f), new Vector3(0.8f, 0.08f, -0.8f),
                new Vector3(0.2f, 0.08f, -0.8f), new Vector3(0.2f, 1.7f, -0.8f),
                new Vector3(0.2f, 1.7f, 0.8f), new Vector3(0.2f, 0.08f, 0.8f),
            };
            _combinedSurface.triangles = new[] { 0, 1, 2, 0, 2, 3, 4, 6, 5, 4, 7, 6 };
            geometry.AddComponent<MeshCollider>().sharedMesh = _combinedSurface;
            geometry.SetActive(true);
        }

        private static void SetDestinations(ExpeditionFastTravel3D travel)
        {
            var config = new SerializedObject(travel);
            SerializedProperty destinations = config.FindProperty("destinations");
            destinations.arraySize = 2;
            SetDestination(destinations.GetArrayElementAtIndex(0), k_Home, "Camp", string.Empty);
            SetDestination(destinations.GetArrayElementAtIndex(1), k_Passage, "Passage", k_DestinationPath);
            config.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetDestination(SerializedProperty entry, string id, string name, string path)
        {
            entry.FindPropertyRelative("pointId").stringValue = id;
            entry.FindPropertyRelative("displayName").stringValue = name;
            entry.FindPropertyRelative("areaScenePath").stringValue = path;
        }

        private static GameObject CreateInactive(Scene scene, string name, Vector3 position)
        {
            GameObject root = new GameObject(name);
            root.SetActive(false);
            SceneManager.MoveGameObjectToScene(root, scene);
            root.transform.position = position;
            return root;
        }

        private Button ButtonFor(string id) => _document.rootVisualElement.Q<Button>($"fast-travel-{id}");

        private static void Submit(Button button)
        {
            Assert.IsNotNull(button);
            Assert.IsTrue(button.enabledInHierarchy);
            using NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled();
            submit.target = button;
            button.SendEvent(submit);
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(field, name);
            field.SetValue(target, value);
        }

        private static IEnumerator Until(Func<bool> condition, string message)
        {
            float deadline = Time.realtimeSinceStartup + 8f;
            while (!condition() && Time.realtimeSinceStartup < deadline) { yield return null; }
            Assert.IsTrue(condition(), message);
        }
    }
}
#endif
