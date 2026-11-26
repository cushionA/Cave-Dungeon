using System;
using Game.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Game.Runtime
{
    /// <summary>遠征中の騎士を、訪問済みの地点へ同じ物理ワールド内で移動する。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterActor3D), typeof(PlayerInput))]
    public sealed partial class ExpeditionFastTravel3D : MonoBehaviour
    {
        [Serializable]
        private struct Destination
        {
            public string pointId;
            public string displayName;
            [Tooltip("永続シーンの地点は空欄。加算区画は Build Settings の完全なパスを指定する。")]
            public string areaScenePath;
        }

        private const string k_VisitPrefix = "expedition.fast-travel.visited.";
        private const float k_FloorSearchHeight = 0.22f;

        [SerializeField] private UIDocument document;
        [SerializeField] private Destination[] destinations = Array.Empty<Destination>();
        [SerializeField] private LayerMask groundLayers = Physics.DefaultRaycastLayers;
        [SerializeField] private LayerMask obstructionLayers = Physics.DefaultRaycastLayers;

        private readonly Collider[] _arrivalOverlaps = new Collider[16];
        private CharacterActor3D _knight;
        private PlayerInput _input;
        private ExpeditionFastTravelPoint3D _source;
        private SaveManager _saves;
        private ActorRef _owner;
        private int _slot;
        private ulong _loadRevision;
        private ulong _selectionRevision;
        private ulong _slotRevision;
        private Destination? _pending;
        private LevelStreamingOrchestrator _streaming;
        private string _originArea;
        private InputActionMap _gameplayMap;
        private InputActionMap _menuMap;

        public bool IsOpen => _overlay != null;
        public bool IsTraveling => _pending.HasValue;

        private void Awake()
        {
            _knight = GetComponent<CharacterActor3D>();
            _input = GetComponent<PlayerInput>();
        }

        private void OnDisable() => Close();

        private void Update()
        {
            if (!IsOpen) { return; }
            if (!HasOpenSession()) { Close(); return; }
            if (_pending.HasValue) { ContinueTravel(_pending.Value); }
        }

        public bool TryOpen(ExpeditionFastTravelPoint3D source)
        {
            if (!isActiveAndEnabled || IsOpen || !CanUseKnight() || document == null || !document.isActiveAndEnabled
                || document.gameObject.scene != gameObject.scene || document.rootVisualElement?.panel == null
                || !TryReadSource(source) || FindPoint(source.gameObject.scene, source.PointId) != source
                || GameManager.Saves == null) { return false; }

            InputActionMap gameplayMap = _input.actions?.FindActionMap("Player");
            InputActionMap menuMap = _input.actions?.FindActionMap("UI");
            if (gameplayMap == null || menuMap == null || !ReferenceEquals(_input.currentActionMap, gameplayMap)) { return false; }

            RecordVisit(source);
            _source = source;
            _saves = GameManager.Saves;
            _owner = _knight.Actor;
            _slot = _saves.ActiveSlotIndex;
            _loadRevision = _saves.LoadRevision;
            _selectionRevision = _saves.ActiveSlotRevision;
            _gameplayMap = gameplayMap;
            _menuMap = menuMap;
            _input.SwitchCurrentActionMap(menuMap.name);
            OpenMenu(document.rootVisualElement);
            return true;
        }

        public bool TryTravel(string pointId)
        {
            if (!IsOpen || IsTraveling || !HasOpenSession() || pointId == _source.PointId
                || !TryFindDestination(pointId, out Destination destination) || !IsVisited(destination)) { return false; }

            _streaming = GameManager.LevelStreaming?.Orchestrator;
            _originArea = _streaming?.ActiveScene;
            _slotRevision = _saves.ActiveSlotContentRevision;
            _pending = destination;
            if (_streaming?.State == StreamingState.Unloading || _streaming?.HasPendingAreaActivation == true)
            {
                FailTravel();
                return false;
            }
            Scene room = GetDestinationScene(destination);
            if (!room.isLoaded)
            {
                string path = destination.areaScenePath;
                int index = SceneUtility.GetBuildIndexByScenePath(path);
                if (_streaming == null || index < 0 || !Application.CanStreamedLevelBeLoaded(index)
                    || !_streaming.RequestAreaPreload(path))
                {
                    FailTravel();
                    return false;
                }
            }
            RenderMenu("移動先を確認しています…");
            return true;
        }

        public void Close()
        {
            _overlay?.RemoveFromHierarchy();
            _overlay = null;
            _contents = null;
            _status = null;
            _source = null;
            _saves = null;
            _pending = null;
            _streaming = null;
            if (_gameplayMap != null && _input != null && _input.isActiveAndEnabled
                && ReferenceEquals(_input.currentActionMap, _menuMap))
            {
                if (_input.inputIsActive) { _input.SwitchCurrentActionMap(_gameplayMap.name); }
                else { _input.currentActionMap = null; }
            }
            _gameplayMap = _menuMap = null;
        }

        internal bool OwnsPoint(ExpeditionFastTravelPoint3D point) => point != null && point.isActiveAndEnabled
            && TryFindDestination(point.PointId, out Destination destination) && GetDestinationScene(destination) == point.gameObject.scene
            && point.gameObject.scene.GetPhysicsScene() == gameObject.scene.GetPhysicsScene();

        internal bool TryFindInteraction(float maxDistance, out NpcInteraction3D nearest)
        {
            nearest = null;
            if (!isActiveAndEnabled || !CanUseKnight()) { return false; }
            foreach (Destination destination in destinations)
            {
                ExpeditionFastTravelPoint3D point = FindPoint(GetDestinationScene(destination), destination.pointId);
                if (point == null || !OwnsPoint(point) || !point.TryGetComponent(out NpcInteraction3D interaction)
                    || interaction.InteractionType != InteractionType.SavePoint
                    || !interaction.TryGetDistanceSquared(_knight, out float distance) || distance >= maxDistance) { continue; }
                nearest = interaction;
                maxDistance = distance;
            }
            return nearest != null;
        }

        internal void RecordVisit(ExpeditionFastTravelPoint3D point)
        {
            if (OwnsPoint(point) && FindPoint(point.gameObject.scene, point.PointId) == point
                && TryFindDestination(point.PointId, out Destination destination))
            {
                GameManager.Flags?.SetGlobalFlag(VisitKey(destination), true);
            }
        }

        private void ContinueTravel(Destination destination)
        {
            if (_saves.ActiveSlotContentRevision != _slotRevision || !IsVisited(destination)
                || _streaming != GameManager.LevelStreaming?.Orchestrator || _originArea != _streaming?.ActiveScene
                || _streaming?.HasPendingAreaActivation == true)
            {
                FailTravel();
                return;
            }

            Scene room = GetDestinationScene(destination);
            bool additive = !string.IsNullOrWhiteSpace(destination.areaScenePath);
            string area = additive ? destination.areaScenePath : _streaming?.PersistentScene;
            if (_streaming?.State == StreamingState.Unloading || (additive && _streaming?.IsAreaLoadPending(area) == true)) { return; }
            if (!room.isLoaded || (additive && _streaming?.IsLoaded(area) != true)) { FailTravel(); return; }

            ExpeditionFastTravelPoint3D point = FindPoint(room, destination.pointId);
            if (!OwnsPoint(point) || !TryValidateArrival(point, out Pose arrival)) { FailTravel(); return; }

            ExpeditionSavePoint3D.PlaceKnight(_knight, arrival.position, arrival.rotation);
            if (area != null) { _streaming.TryActivateLoadedArea(area); }
            Close();
        }

        private bool TryValidateArrival(ExpeditionFastTravelPoint3D point, out Pose arrival)
        {
            arrival = default;
            Transform anchor = point.ArrivalAnchor;
            Scene scene = point.gameObject.scene;
            if (anchor == null || !anchor.gameObject.activeInHierarchy || anchor.gameObject.scene != scene
                || !float.IsFinite(anchor.position.sqrMagnitude) || Vector3.Dot(anchor.up, Vector3.up) < 0.995f) { return false; }

            Physics.SyncTransforms();
            Bounds bounds = _knight.Body.bounds;
            float radius = Mathf.Min(bounds.extents.x, bounds.extents.z);
            Vector3 offset = Quaternion.Inverse(_knight.transform.rotation) * (_knight.Motor.FeetPosition - _knight.transform.position);
            Vector3 feet = anchor.position + anchor.rotation * offset;
            PhysicsScene physics = scene.GetPhysicsScene();
            if (radius <= 0f || !physics.Raycast(feet + Vector3.up * k_FloorSearchHeight, Vector3.down, out RaycastHit support,
                k_FloorSearchHeight * 2f, groundLayers, QueryTriggerInteraction.Ignore)
                || support.collider == _knight.Body || support.collider.gameObject.scene != scene
                || support.normal.y <= 0f || support.normal.y < Mathf.Cos(_knight.Body.slopeLimit * Mathf.Deg2Rad)) { return false; }

            const float clearance = 0.025f;
            // 斜面上でもカプセルの球面と地面の間に、法線方向の隙間を確保する。
            float lift = (radius + clearance) / support.normal.y - radius;
            Vector3 bottom = support.point + Vector3.up * (radius + lift);
            Vector3 top = support.point + Vector3.up * (Mathf.Max(radius, bounds.size.y - radius) + lift);
            int count = physics.OverlapCapsule(bottom, top, radius, _arrivalOverlaps, obstructionLayers, QueryTriggerInteraction.Ignore);
            if (count == _arrivalOverlaps.Length) { return false; }
            for (int i = 0; i < count; i++)
            {
                if (_arrivalOverlaps[i] != _knight.Body) { return false; }
            }

            arrival = new Pose(anchor.position + Vector3.up * (support.point.y - feet.y + lift), anchor.rotation);
            return true;
        }

        private void FailTravel()
        {
            _pending = null;
            _streaming = null;
            RenderMenu("移動先を利用できません。");
        }

        private bool HasOpenSession() => CanUseKnight() && _owner == _knight.Actor && _saves != null
            && _saves == GameManager.Saves && _saves.ActiveSlotIndex == _slot && _saves.LoadRevision == _loadRevision
            && _saves.ActiveSlotRevision == _selectionRevision && !_saves.IsApplyingLoad
            && ReferenceEquals(_input.currentActionMap, _menuMap) && _overlay.panel != null && TryReadSource(_source);

        private bool CanUseKnight() => _knight != null && _knight.isActiveAndEnabled && _knight.IsAlive
            && _knight.Body != null && _knight.Body.enabled && _knight.Motor.isActiveAndEnabled
            && _input != null && _input.isActiveAndEnabled && _input.inputIsActive
            && !GameManager.Data.GetStatusEffects(_knight.Actor.Hash).BlocksActions;

        private bool TryReadSource(ExpeditionFastTravelPoint3D source) => OwnsPoint(source)
            && source.TryGetComponent(out NpcInteraction3D interaction) && interaction.InteractionType == InteractionType.SavePoint
            && interaction.TryGetDistanceSquared(_knight, out _);

        private bool TryFindDestination(string pointId, out Destination result)
        {
            result = default;
            if (string.IsNullOrWhiteSpace(pointId)) { return false; }
            bool found = false;
            foreach (Destination destination in destinations)
            {
                if (!string.Equals(destination.pointId, pointId, StringComparison.Ordinal)) { continue; }
                if (found) { return false; }
                found = true;
                result = destination;
            }
            return found;
        }

        private Scene GetDestinationScene(Destination destination) => string.IsNullOrWhiteSpace(destination.areaScenePath)
            ? gameObject.scene : SceneManager.GetSceneByPath(destination.areaScenePath);

        private bool IsVisited(Destination destination) => GameManager.Flags?.GetGlobalFlag(VisitKey(destination)) == true;

        private string VisitKey(Destination destination) => k_VisitPrefix + gameObject.scene.path + "#" + destination.pointId;

        private static ExpeditionFastTravelPoint3D FindPoint(Scene scene, string pointId)
        {
            if (!scene.isLoaded || string.IsNullOrWhiteSpace(pointId)) { return null; }
            ExpeditionFastTravelPoint3D found = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (ExpeditionFastTravelPoint3D point in root.GetComponentsInChildren<ExpeditionFastTravelPoint3D>())
                {
                    if (point.PointId != pointId) { continue; }
                    if (found != null) { return null; }
                    found = point;
                }
            }
            return found;
        }
    }
}
