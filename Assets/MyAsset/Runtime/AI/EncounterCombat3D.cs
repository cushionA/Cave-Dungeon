using System.Collections.Generic;
using Game.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Runtime
{
    /// <summary>陣営別の観測から任務を割り当て、同じ遭遇の3D個体を一度ずつ進める。</summary>
    [DisallowMultipleComponent]
    public sealed class EncounterCombat3D : MonoBehaviour
    {
        private const float StationArrival = 0.32f;
        private const float VerticalArrival = 0.6f;

        private sealed class Order
        {
            internal BroadcastAction action;
            internal ActorRef source;
            internal ActorRef target;
            internal Collider surface;
            internal Vector3 localPoint;
        }

        [SerializeField] private CharacterActor3D knight;
        [SerializeField] private CharacterActor3D alliedCommander;
        [SerializeField] private CharacterActor3D enemyCommander;
        [SerializeField] private EncounterCombatant3D[] initialCombatants;
        [SerializeField] private EncounterSession3D objective;
        [SerializeField, Min(0.1f)] private float sightRange = 16f;
        [SerializeField] private LayerMask obstructionLayers = Physics.DefaultRaycastLayers;
        [SerializeField, Min(1)] private int defendersPerOrder = 1;
        [SerializeField] private Transform alliedFrontStation;
        [SerializeField] private Transform alliedReserveStation;
        [SerializeField] private Transform enemyFrontStation;
        [SerializeField] private Transform enemyReserveStation;
        [SerializeField, Tooltip("戦闘で迂回できる床上の点。最大12点。空の場合は従来どおり直進する。")]
        private Transform[] approachWaypoints;
        [SerializeField, Tooltip("接近経路で歩行床として確認するレイヤー。")]
        private LayerMask approachGroundLayers = Physics.DefaultRaycastLayers;

        private readonly HashSet<EncounterCombatant3D> _combatants = new();
        private readonly List<EncounterCombatant3D> _activeCombatants = new();
        private readonly List<ActorRef> _participants = new();
        private readonly EncounterObservation3D _observation = new();
        private readonly Dictionary<CharacterBelong, Order> _orders = new();
        private readonly Dictionary<ActorRef, Transform> _assignedStations = new();
        private readonly HashSet<Transform> _occupiedStations = new();
        private readonly Dictionary<CharacterBelong, int> _orderAssignments = new();
        private readonly HashSet<CharacterBelong> _unfinishedRallies = new();
        private MissionAttackSlots3D _attackSlots;
        private EncounterApproachRoutes3D _approachRoutes;
        private bool _driving;

        private bool IsRunning => isActiveAndEnabled
            && (ReferenceEquals(objective, null) || (objective != null && objective.IsGroupBattle && objective.IsRunning));
        public int AssignedOrderCount => IsParticipant(knight) && _orderAssignments.TryGetValue(Faction(knight), out int count) ? count : 0;
        public BroadcastAction? ActiveOrder => IsParticipant(knight) && _orders.TryGetValue(Faction(knight), out Order order)
            ? order.action : null;

        private void Awake()
        {
            if (initialCombatants == null) { return; }
            foreach (EncounterCombatant3D combatant in initialCombatants) { Register(combatant); }
        }

        private void OnDisable() => ClearOrdersAndSlots();

        public bool Register(EncounterCombatant3D combatant)
            => combatant != null && combatant.gameObject.scene == gameObject.scene
                && combatant.Actor != knight && _combatants.Add(combatant);

        public void Unregister(EncounterCombatant3D combatant)
        {
            if (combatant == null || !_combatants.Remove(combatant)) { return; }
            ActorRef actor = combatant.Actor.Actor;
            _assignedStations.Remove(actor);
            _attackSlots?.Invalidate(actor);
            _approachRoutes?.Remove(actor);
        }

        public Transform GetAssignedStation(CharacterActor3D actor)
            => actor != null && _assignedStations.TryGetValue(actor.Actor, out Transform station) ? station : null;

        internal bool Drives(CharacterActor3D actor) => IsRunning && IsParticipant(actor)
            && actor.TryGetComponent(out EncounterCombatant3D combatant) && combatant.isActiveAndEnabled
            && _combatants.Contains(combatant);

        public bool TryIssue(CharacterActor3D source, BroadcastAction action, ActorRef target = default,
            Vector3? position = null, Collider surface = null)
            => TryIssue(source, FieldOrderIssuer.Player, action, target, position, surface);

        public bool TryIssue(CharacterActor3D source, FieldOrderIssuer issuer, BroadcastAction action,
            ActorRef target = default, Vector3? position = null, Collider surface = null)
        {
            if (!IsRunning || !IsParticipant(source) || !IsCommander(source, issuer)
                || !FieldOrderCatalog.Allows(action, issuer)) { return false; }

            CharacterBelong faction = Faction(source);
            if (action == BroadcastAction.ReleaseOrder)
            {
                _orders.Remove(faction);
                _orderAssignments.Remove(faction);
                return true;
            }

            Order order = new() { action = action, source = source.Actor };
            if (FieldOrderCatalog.RequiresTarget(action))
            {
                if (!_driving) { CaptureFrame(); }
                if (!_observation.CanSee(order.source, target)) { return false; }
                order.target = target;
            }
            else if (action == BroadcastAction.DefendPoint)
            {
                if (!position.HasValue || !TryGetFloorPoint(surface, position.Value, out Vector3 floorPoint)) { return false; }
                order.surface = surface;
                order.localPoint = surface.transform.InverseTransformPoint(floorPoint);
            }
            else if (action != BroadcastAction.Rally)
            {
                return false;
            }

            _orders[faction] = order;
            _orderAssignments.Remove(faction);
            return true;
        }

        private void FixedUpdate()
        {
            GameActionRuntime actions = GameManager.Actions;
            if (!IsRunning || actions == null)
            {
                ClearOrdersAndSlots();
                return;
            }

            _attackSlots ??= new MissionAttackSlots3D(actions, GameManager.Events);
            _attackSlots.Refresh();
            CaptureFrame();
            _assignedStations.Clear();
            _occupiedStations.Clear();
            _orderAssignments.Clear();
            _unfinishedRallies.Clear();
            ExpireOrder(CharacterBelong.Ally);
            ExpireOrder(CharacterBelong.Enemy);

            _driving = true;
            try
            {
                foreach (EncounterCombatant3D member in _activeCombatants)
                {
                    if (!IsRunning) { break; }
                    if (member == null || !_combatants.Contains(member) || !IsParticipant(member.Actor)) { continue; }
                    CharacterActor3D actor = member.Actor;
                    CharacterBelong faction = Faction(actor);
                    if (faction == CharacterBelong.Ally || faction == CharacterBelong.Enemy)
                    {
                        Drive(member, actor, faction, actions);
                    }
                }
            }
            finally { _driving = false; }
            FinishRally(CharacterBelong.Ally);
            FinishRally(CharacterBelong.Enemy);
        }

        private void CaptureFrame()
        {
            _participants.Clear();
            _activeCombatants.Clear();
            if (IsParticipant(knight)) { _participants.Add(knight.Actor); }
            _combatants.RemoveWhere(member => member == null);
            foreach (EncounterCombatant3D member in _combatants)
            {
                if (!member.isActiveAndEnabled || !IsParticipant(member.Actor)) { continue; }
                _activeCombatants.Add(member);
                _participants.Add(member.Actor.Actor);
            }
            _activeCombatants.Sort(CompareAssignmentPriority);
            _observation.Capture(_participants, sightRange, obstructionLayers);
            _approachRoutes?.Retain(_participants);
        }

        private void Drive(EncounterCombatant3D member, CharacterActor3D actor, CharacterBelong faction,
            GameActionRuntime actions)
        {
            ActorRef owner = actor.Actor;
            if (actions.IsMovementBlocked(owner.Hash) || GameManager.Data.GetStatusEffects(owner.Hash).BlocksActions)
            {
                if (_orders.TryGetValue(faction, out Order pending) && pending.action == BroadcastAction.Rally)
                {
                    _unfinishedRallies.Add(faction);
                }
                else if (actions.IsPrimaryExecuting(owner.Hash))
                {
                    AssignStation(member, owner, faction);
                }
                actor.Motor.SimulateFixedStep(Vector3.zero, false, Time.fixedDeltaTime);
                return;
            }

            bool hasSight = TryChooseTarget(faction, actor, default, out EncounterSighting3D sighting);
            Vector3? destination = null;
            bool canAttack = true;
            bool approachesTarget = false;
            if (_orders.TryGetValue(faction, out Order order))
            {
                int assigned = _orderAssignments.TryGetValue(faction, out int count) ? count : 0;
                switch (order.action)
                {
                    case BroadcastAction.Rally:
                        if (MultiLockTargeting3D.TryGetActor(order.source, out CharacterActor3D leader))
                        {
                            destination = RallyPosition(leader, assigned);
                            canAttack = false;
                            if (!CloseTo(actor.Motor.FeetPosition, destination.Value)) { _unfinishedRallies.Add(faction); }
                        }
                        break;
                    case BroadcastAction.DefendPoint:
                        if (assigned < defendersPerOrder)
                        {
                            Vector3 point = order.surface.transform.TransformPoint(order.localPoint);
                            if (ReserveDefendStation(owner, faction, point))
                            {
                                destination = point;
                                canAttack = CloseTo(actor.Motor.FeetPosition, point) && actor.Motor.Ground.Surface == order.surface;
                            }
                        }
                        break;
                    case BroadcastAction.FocusFire:
                        hasSight = TryChooseTarget(faction, actor, order.target, out sighting);
                        if (hasSight)
                        {
                            destination = sighting.Position;
                            approachesTarget = true;
                        }
                        else { canAttack = false; }
                        break;
                    case BroadcastAction.DesignateTarget when !hasSight:
                        if (TryChooseTarget(faction, actor, order.target, out EncounterSighting3D ordered))
                        {
                            hasSight = true;
                            sighting = ordered;
                            destination = ordered.Position;
                            approachesTarget = true;
                        }
                        break;
                }
                if (destination.HasValue) { _orderAssignments[faction] = assigned + 1; }
            }

            if (!destination.HasValue)
            {
                Transform station = AssignStation(member, owner, faction);
                if (station != null)
                {
                    destination = station.position;
                    canAttack &= CloseTo(actor.Motor.FeetPosition, station.position);
                }
                else if (member.Home != null) { destination = member.Home.position; }
                else if (hasSight && !member.PrefersFrontStation)
                {
                    destination = sighting.Position;
                    approachesTarget = true;
                }
            }

            Vector3 move = Vector3.zero;
            if (canAttack && hasSight && member.Attack != null && actor.Motor.IsGrounded
                && _observation.CanSee(owner, sighting.Target))
            {
                Vector3 offset = sighting.Position - actor.Motor.FeetPosition;
                if (offset.sqrMagnitude <= member.AttackRange * member.AttackRange)
                {
                    Vector3 facing = Horizontal(offset);
                    if (facing.sqrMagnitude > 0.001f) { actor.transform.rotation = Quaternion.LookRotation(facing); }
                    _attackSlots.TryExecute(actor, sighting.Target, member.Attack, out _);
                    _approachRoutes?.Remove(owner);
                    actor.Motor.SimulateFixedStep(Vector3.zero, false, Time.fixedDeltaTime);
                    return;
                }
            }

            if (approachesTarget && canAttack && member.Attack != null && approachWaypoints?.Length > 0)
            {
                _approachRoutes ??= new EncounterApproachRoutes3D();
                move = _approachRoutes.GetDirection(actor, sighting, member.AttackRange, approachWaypoints,
                    approachGroundLayers, obstructionLayers);
            }
            else
            {
                _approachRoutes?.Remove(owner);
                if (destination.HasValue && !CloseTo(actor.Motor.FeetPosition, destination.Value))
                {
                    move = Horizontal(destination.Value - actor.Motor.FeetPosition).normalized;
                }
            }
            actor.Motor.SimulateFixedStep(move, false, Time.fixedDeltaTime);
        }

        private Transform AssignStation(EncounterCombatant3D member, ActorRef owner, CharacterBelong faction)
        {
            if (!member.PrefersFrontStation) { return null; }
            Transform front = faction == CharacterBelong.Ally ? alliedFrontStation : enemyFrontStation;
            Transform reserve = faction == CharacterBelong.Ally ? alliedReserveStation : enemyReserveStation;
            Transform assigned = front != null && _occupiedStations.Add(front) ? front
                : reserve != null && _occupiedStations.Add(reserve) ? reserve : null;
            if (assigned != null) { _assignedStations.Add(owner, assigned); }
            return assigned;
        }

        private bool ReserveDefendStation(ActorRef owner, CharacterBelong faction, Vector3 destination)
        {
            Transform front = faction == CharacterBelong.Ally ? alliedFrontStation : enemyFrontStation;
            Transform reserve = faction == CharacterBelong.Ally ? alliedReserveStation : enemyReserveStation;
            Transform station = front != null && CloseTo(front.position, destination) ? front
                : reserve != null && CloseTo(reserve.position, destination) ? reserve : null;
            if (station == null) { return true; }
            if (!_occupiedStations.Add(station)) { return false; }
            _assignedStations.Add(owner, station);
            return true;
        }

        private bool TryChooseTarget(CharacterBelong faction, CharacterActor3D actor, ActorRef preferred,
            out EncounterSighting3D best)
        {
            best = default;
            float distance = float.PositiveInfinity;
            foreach (EncounterSighting3D sighting in _observation.SightingsFor(faction))
            {
                if (preferred.IsValid && sighting.Target != preferred) { continue; }
                float candidateDistance = (sighting.Position - actor.Motor.FeetPosition).sqrMagnitude;
                if (candidateDistance >= distance) { continue; }
                distance = candidateDistance;
                best = sighting;
            }
            return best.Target.IsValid;
        }

        private void ExpireOrder(CharacterBelong faction)
        {
            if (!_orders.TryGetValue(faction, out Order order)) { return; }
            if (!_participants.Contains(order.source)
                || !MultiLockTargeting3D.TryGetActor(order.source, out CharacterActor3D leader) || Faction(leader) != faction
                || (order.target.IsValid && (!_participants.Contains(order.target)
                    || !MultiLockTargeting3D.TryGetActor(order.target, out CharacterActor3D target)
                    || Faction(target) != (faction == CharacterBelong.Ally ? CharacterBelong.Enemy : CharacterBelong.Ally)))
                || (order.action == BroadcastAction.DefendPoint && !IsValidSurface(order.surface)))
            {
                _orders.Remove(faction);
            }
        }

        private void ClearOrdersAndSlots()
        {
            _attackSlots?.Dispose();
            _attackSlots = null;
            _approachRoutes?.Clear();
            _orders.Clear();
            _orderAssignments.Clear();
            _assignedStations.Clear();
        }

        private void FinishRally(CharacterBelong faction)
        {
            if (_orders.TryGetValue(faction, out Order order) && order.action == BroadcastAction.Rally
                && _orderAssignments.ContainsKey(faction) && !_unfinishedRallies.Contains(faction))
            {
                _orders.Remove(faction);
                _orderAssignments.Remove(faction);
            }
        }

        private bool IsParticipant(CharacterActor3D actor) => actor != null && actor.isActiveAndEnabled && actor.IsAlive
            && actor.gameObject.scene == gameObject.scene
            && !GameManager.Data.GetFlags(actor.Actor.Hash).IsVanished;

        private bool IsCommander(CharacterActor3D source, FieldOrderIssuer issuer) => issuer switch
        {
            FieldOrderIssuer.Player => source == knight && (GameManager.Data.GetFlags(source.Actor.Hash).Feature & CharacterFeature.Player) != 0,
            FieldOrderIssuer.CompanionAI => source == alliedCommander && Faction(source) == CharacterBelong.Ally,
            FieldOrderIssuer.EnemyCommander => source == enemyCommander && Faction(source) == CharacterBelong.Enemy,
            _ => false,
        };

        private bool TryGetFloorPoint(Collider surface, Vector3 point, out Vector3 floorPoint)
        {
            floorPoint = default;
            if (!IsValidSurface(surface) || !float.IsFinite(point.x) || !float.IsFinite(point.y) || !float.IsFinite(point.z)
                || !surface.Raycast(new Ray(point + Vector3.up * 0.10f, Vector3.down), out RaycastHit hit, 0.20f)
                || hit.normal.y <= 0.5f) { return false; }
            floorPoint = hit.point;
            return true;
        }

        private bool IsValidSurface(Collider surface) => surface != null && surface.enabled && surface.gameObject.activeInHierarchy
            && !surface.isTrigger && surface.gameObject.scene == gameObject.scene
            && surface.GetComponentInParent<CharacterActor3D>() == null;

        private static CharacterBelong Faction(CharacterActor3D actor)
            => GameManager.Data.GetFlags(actor.Actor.Hash).Belong & (CharacterBelong.Ally | CharacterBelong.Enemy);

        private static Vector3 RallyPosition(CharacterActor3D leader, int index)
        {
            float side = index % 2 == 0 ? -1f : 1f;
            float row = 1f + index / 2;
            return leader.Motor.FeetPosition + (side * leader.transform.right - leader.Motor.Facing * row) * 0.85f;
        }

        private static int CompareAssignmentPriority(EncounterCombatant3D left, EncounterCombatant3D right)
        {
            int order = left.AssignmentPriority.CompareTo(right.AssignmentPriority);
            return order != 0 ? order : left.Actor.Actor.Hash.CompareTo(right.Actor.Actor.Hash);
        }

        private static bool CloseTo(Vector3 origin, Vector3 destination)
        {
            Vector3 offset = destination - origin;
            return Mathf.Abs(offset.y) <= VerticalArrival && Horizontal(offset).sqrMagnitude <= StationArrival * StationArrival;
        }

        private static Vector3 Horizontal(Vector3 vector) => new(vector.x, 0f, vector.z);
    }
}
