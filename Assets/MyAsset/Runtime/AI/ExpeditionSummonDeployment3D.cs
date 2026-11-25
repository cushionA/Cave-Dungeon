using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Runtime
{
    /// <summary>シスターが編成した召喚種を、この3D区画の出現地点へ転送する。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterActor3D))]
    public sealed class ExpeditionSummonDeployment3D : MonoBehaviour
    {
        [Serializable]
        public struct SpeciesPrefab
        {
            [SerializeField] private string speciesId;
            [SerializeField] private CharacterActor3D prefab;

            public string SpeciesId => speciesId;
            public CharacterActor3D Prefab => prefab;

            public SpeciesPrefab(string speciesId, CharacterActor3D prefab)
            {
                this.speciesId = speciesId;
                this.prefab = prefab;
            }
        }

        public readonly struct DeployedSummon
        {
            public string SpeciesId { get; }
            public ActorRef Actor { get; }
            public bool IsReinforcement { get; }

            internal DeployedSummon(string speciesId, ActorRef actor, bool isReinforcement)
            {
                SpeciesId = speciesId;
                Actor = actor;
                IsReinforcement = isReinforcement;
            }
        }

        private struct Placement
        {
            public CharacterActor3D prefab;
            public string speciesId;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 center;
            public float halfSegment;
            public float radius;
        }

        private sealed class Deployment
        {
            public GameObject root;
            public object reinforcementAttempt;
            public readonly List<(CharacterActor3D actor, string speciesId)> units = new();
        }

        [SerializeField] private SpeciesPrefab[] speciesPrefabs = Array.Empty<SpeciesPrefab>();
        [SerializeField] private Transform[] deploymentPoints = Array.Empty<Transform>();
        [SerializeField] private LayerMask obstructionLayers = Physics.DefaultRaycastLayers;
        [SerializeField] private EncounterCombat3D encounter;

        private readonly Collider[] _overlap = new Collider[1];
        private CharacterActor3D _sister;
        private Deployment _normal;
        private Deployment _reinforcements;
        private object _pendingReinforcementAttempt;
        private bool _changing;
        private uint _revision;

        public int ActiveCount => CountLiveSummons(_normal) + CountLiveSummons(_reinforcements);

        private void Awake() => _sister = GetComponent<CharacterActor3D>();

        private void LateUpdate()
        {
            if (_changing) { return; }
            _changing = true;
            try
            {
                RemoveRetiredUnits(ref _normal);
                RemoveRetiredUnits(ref _reinforcements);
            }
            finally { _changing = false; }
        }

        private void OnDisable() => DismissAll();

        /// <summary>シーンのイベントから、確定した通常編成を呼び出す。</summary>
        public void DeployConfirmed() => TryDeployConfirmed();

        public bool TryDeployConfirmed()
        {
            ExpeditionSquad squad = GameManager.GetSubManager<ExpeditionSquadManager>()?.Squad;
            return squad != null && TryDeploy(squad, squad.Confirmed, null, ref _normal);
        }

        internal bool TryDeployReinforcements(object attempt, IReadOnlyList<ExpeditionSquad.Selection> selections)
        {
            if (attempt == null || _changing) { return false; }
            ExpeditionSquad squad = GameManager.GetSubManager<ExpeditionSquadManager>()?.Squad;
            if (squad == null) { return false; }
            _pendingReinforcementAttempt = attempt;
            try { return TryDeploy(squad, selections, attempt, ref _reinforcements); }
            finally { _pendingReinforcementAttempt = null; }
        }

        /// <summary>現在の世代で生存している召喚個体を、出現時の順序で返す。</summary>
        public void CopyActiveSummons(List<DeployedSummon> result)
        {
            result.Clear();
            CopyLiveSummons(_normal, false, result);
            CopyLiveSummons(_reinforcements, true, result);
        }

        public bool TryDismiss(ActorRef actor)
        {
            if (_changing || !actor.IsValid) { return false; }
            _changing = true;
            try { return TryDismiss(ref _normal, actor) || TryDismiss(ref _reinforcements, actor); }
            finally { _changing = false; }
        }

        public void DismissAll()
        {
            _revision++;
            bool previous = _changing;
            _changing = true;
            try
            {
                Dismiss(ref _normal);
                Dismiss(ref _reinforcements);
            }
            finally { _changing = previous; }
        }

        internal void DismissReinforcements(object attempt)
        {
            if (attempt == null) { return; }
            bool ownsActive = ReferenceEquals(attempt, _reinforcements?.reinforcementAttempt);
            if (!ownsActive && !ReferenceEquals(attempt, _pendingReinforcementAttempt)) { return; }
            _revision++;
            if (!ownsActive) { return; }
            bool previous = _changing;
            _changing = true;
            try { Dismiss(ref _reinforcements); }
            finally { _changing = previous; }
        }

        private bool TryDeploy(ExpeditionSquad squad, IReadOnlyList<ExpeditionSquad.Selection> selections,
            object reinforcementAttempt, ref Deployment current)
        {
            if (_changing || !CanSummon()) { return false; }
            _changing = true;
            Deployment pending = null;
            uint revision = _revision;
            try
            {
                RemoveRetiredUnits(ref current);
                if (current != null || _revision != revision
                    || !squad.TryMeasure(selections, out int count, out int cost) || count == 0
                    || (reinforcementAttempt == null && (count > squad.MaxCount || cost > squad.MaxCost))
                    || count > deploymentPoints.Length || count > GameManager.Data.MaxCapacity - GameManager.Data.Count)
                {
                    return false;
                }

                Scene scene = gameObject.scene;
                if (encounter != null && encounter.gameObject.scene != scene) { return false; }
                List<Placement> placements = new List<Placement>(count);
                if (!TryPlanPlacements(scene, selections, placements)) { return false; }

                GameObject root = new GameObject("[Expedition summons]");
                pending = new Deployment { root = root, reinforcementAttempt = reinforcementAttempt };
                root.SetActive(false);
                SceneManager.MoveGameObjectToScene(root, scene);
                foreach (Placement placement in placements)
                {
                    CharacterActor3D actor = Instantiate(placement.prefab, placement.position, placement.rotation, root.transform);
                    pending.units.Add((actor, placement.speciesId));
                    actor.gameObject.SetActive(true);
                }

                root.SetActive(true);
                foreach ((CharacterActor3D actor, string _) in pending.units)
                {
                    if (!IsLive(actor) || !CanSummon() || _revision != revision) { return false; }
                    if (encounter != null && actor.TryGetComponent(out EncounterCombatant3D combatant)
                        && !encounter.Register(combatant))
                    {
                        return false;
                    }
                }

                current = pending;
                pending = null;
                return true;
            }
            finally
            {
                Dismiss(ref pending);
                _changing = false;
            }
        }

        private bool CanSummon()
        {
            if (!isActiveAndEnabled || _sister == null || !_sister.IsAlive || !gameObject.scene.isLoaded) { return false; }
            ref readonly CharacterFlags flags = ref GameManager.Data.GetFlags(_sister.Actor.Hash);
            return flags.Belong == CharacterBelong.Ally && (flags.Feature & CharacterFeature.Companion) != 0
                && !flags.IsVanished;
        }

        private bool TryPlanPlacements(Scene scene, IReadOnlyList<ExpeditionSquad.Selection> selections, List<Placement> placements)
        {
            PhysicsScene world = scene.GetPhysicsScene();
            if (!world.IsValid()) { return false; }
            Physics.SyncTransforms();
            int blockingLayers = obstructionLayers.value | (1 << _sister.gameObject.layer)
                | OccupiedLayers(_normal) | OccupiedLayers(_reinforcements);
            foreach (ExpeditionSquad.Selection selection in selections)
            {
                if (!TryGetPrefab(selection.SpeciesId, out CharacterActor3D prefab, out CharacterController body))
                {
                    return false;
                }
                for (int unit = 0; unit < selection.Count; unit++)
                {
                    if (!TryPlace(scene, world, blockingLayers, selection.SpeciesId, prefab, body, placements)) { return false; }
                }
            }
            return true;
        }

        private bool TryGetPrefab(string speciesId, out CharacterActor3D prefab, out CharacterController body)
        {
            prefab = null;
            body = null;
            bool found = false;
            foreach (SpeciesPrefab entry in speciesPrefabs)
            {
                if (!string.Equals(speciesId, entry.SpeciesId, StringComparison.Ordinal)) { continue; }
                if (found) { return false; }
                found = true;
                prefab = entry.Prefab;
            }
            if (prefab == null || prefab.transform.parent != null || !prefab.enabled
                || prefab.GetComponentsInChildren<ManagedCharacter>(true).Length != 1
                || !prefab.TryGetComponent(out body) || !body.enabled) { return false; }

            Game.Core.CharacterInfo definition = prefab.Definition;
            if (definition == null || definition.belong != CharacterBelong.Ally || definition.maxHp <= 0
                || (definition.feature & CharacterFeature.Summon) == 0
                || (definition.feature & (CharacterFeature.Player | CharacterFeature.Companion)) != 0) { return false; }
            // 実体化に先立ち、登録が使う防御データの検証も済ませる。
            try { definition.CreateRegistration(Physics.gravity.magnitude); }
            catch (ArgumentException) { return false; }
            return true;
        }

        private bool TryPlace(Scene scene, PhysicsScene world, int blockingLayers, string speciesId, CharacterActor3D prefab,
            CharacterController body, List<Placement> placements)
        {
            Vector3 scale = prefab.transform.localScale;
            if (!Finite(scale) || scale.x <= 0f || scale.y <= 0f || scale.z <= 0f) { return false; }
            float radius = body.radius * Mathf.Max(scale.x, scale.z);
            float halfSegment = Mathf.Max(0f, body.height * scale.y * 0.5f - radius);
            if (!float.IsFinite(radius) || radius <= 0f || !float.IsFinite(halfSegment)) { return false; }

            foreach (Transform point in deploymentPoints)
            {
                if (point == null || point.gameObject.scene != scene || !Finite(point.position)) { continue; }
                Quaternion rotation = Quaternion.Euler(0f, point.eulerAngles.y, 0f);
                Vector3 center = point.position + rotation * Vector3.Scale(body.center, scale);
                if (!Finite(center) || OverlapsPlanned(center, halfSegment, radius, placements)) { continue; }

                Vector3 axis = Vector3.up * halfSegment;
                // 接地面に触れた状態は許し、肉厚のある遮蔽物との重なりを調べる。
                float skin = Mathf.Min(body.skinWidth, radius * 0.1f);
                int blocked = world.OverlapCapsule(center - axis, center + axis, radius - skin, _overlap,
                    blockingLayers, QueryTriggerInteraction.Ignore);
                _overlap[0] = null;
                if (blocked != 0) { continue; }
                placements.Add(new Placement
                {
                    prefab = prefab, speciesId = speciesId, position = point.position, rotation = rotation,
                    center = center, halfSegment = halfSegment, radius = radius,
                });
                return true;
            }
            return false;
        }

        private static bool OverlapsPlanned(Vector3 center, float halfSegment, float radius, List<Placement> placements)
        {
            foreach (Placement previous in placements)
            {
                Vector3 delta = center - previous.center;
                delta.y = Mathf.Max(0f, Mathf.Abs(delta.y) - (halfSegment + previous.halfSegment));
                float combinedRadius = radius + previous.radius;
                if (delta.sqrMagnitude < combinedRadius * combinedRadius) { return true; }
            }
            return false;
        }

        private void RemoveRetiredUnits(ref Deployment deployment)
        {
            Deployment current = deployment;
            if (current == null) { return; }
            for (int i = current.units.Count - 1; i >= 0 && ReferenceEquals(deployment, current); i--)
            {
                if (!IsLive(current.units[i].actor)) { RemoveUnit(current, i); }
            }
            if (ReferenceEquals(deployment, current) && current.units.Count == 0) { Dismiss(ref deployment); }
        }

        private bool TryDismiss(ref Deployment deployment, ActorRef actor)
        {
            if (deployment == null) { return false; }
            Deployment current = deployment;
            for (int i = 0; i < current.units.Count; i++)
            {
                CharacterActor3D instance = current.units[i].actor;
                if (instance == null || instance.Actor != actor) { continue; }
                RemoveUnit(current, i);
                if (ReferenceEquals(deployment, current) && current.units.Count == 0) { Dismiss(ref deployment); }
                return true;
            }
            return false;
        }

        private void RemoveUnit(Deployment deployment, int index)
        {
            CharacterActor3D actor = deployment.units[index].actor;
            deployment.units.RemoveAt(index);
            if (actor == null) { return; }
            LeaveEncounter(actor);
            actor.gameObject.SetActive(false);
            Destroy(actor.gameObject);
        }

        private void Dismiss(ref Deployment deployment)
        {
            Deployment dismissed = deployment;
            deployment = null;
            if (dismissed == null || dismissed.root == null) { return; }
            foreach ((CharacterActor3D actor, string _) in dismissed.units)
            {
                if (actor != null) { LeaveEncounter(actor); }
            }
            dismissed.root.SetActive(false);
            Destroy(dismissed.root);
        }

        private void LeaveEncounter(CharacterActor3D actor)
        {
            if (encounter != null && actor.TryGetComponent(out EncounterCombatant3D combatant))
            {
                encounter.Unregister(combatant);
            }
        }

        private static int CountLiveSummons(Deployment deployment)
        {
            if (deployment == null) { return 0; }
            int count = 0;
            foreach ((CharacterActor3D actor, string _) in deployment.units)
            {
                if (IsLive(actor)) { count++; }
            }
            return count;
        }

        private static int OccupiedLayers(Deployment deployment)
        {
            if (deployment == null) { return 0; }
            int layers = 0;
            foreach ((CharacterActor3D actor, string _) in deployment.units)
            {
                if (IsLive(actor)) { layers |= 1 << actor.Body.gameObject.layer; }
            }
            return layers;
        }

        private static void CopyLiveSummons(Deployment deployment, bool reinforcements, List<DeployedSummon> result)
        {
            if (deployment == null) { return; }
            foreach ((CharacterActor3D actor, string speciesId) in deployment.units)
            {
                if (IsLive(actor)) { result.Add(new DeployedSummon(speciesId, actor.Actor, reinforcements)); }
            }
        }

        private static bool IsLive(CharacterActor3D actor) => actor != null && actor.isActiveAndEnabled && actor.IsAlive;

        private static bool Finite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

#if UNITY_INCLUDE_TESTS
        internal void ConfigureForTest(SpeciesPrefab[] prefabs, Transform[] points, LayerMask blockers, EncounterCombat3D coordinator = null)
        {
            speciesPrefabs = prefabs;
            deploymentPoints = points;
            obstructionLayers = blockers;
            encounter = coordinator;
        }
#endif
    }
}
