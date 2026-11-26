using Game.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Runtime
{
    public sealed partial class ExpeditionSummonDeployment3D
    {
        private const float k_RallyArrival = 1.15f;
        private const float k_DefendArrival = 0.3f;

        private struct ExplorationOrder
        {
            public FieldOrder command;
            public Collider surface;
            public Vector3 surfacePoint;
        }

        internal EncounterCombat3D OrderEncounter => encounter;

        public BroadcastAction? ActiveExplorationOrder => _normal != null && _normal.order.command.Source.IsValid
            ? _normal.order.command.Action : null;

        internal bool CanReceiveExplorationOrders(CharacterActor3D source) => isActiveAndEnabled && !_changing
            && source != null && source == explorationLeader && source.gameObject.scene == gameObject.scene
            && IsExplorationLeaderAvailable() && (encounter == null || !encounter.CanReceivePlayerOrders(source));

        public bool TryIssueExplorationOrder(CharacterActor3D source, BroadcastAction action, ActorRef target = default,
            Vector3? position = null, Collider surface = null, float aimRange = 20f)
        {
            Deployment group = _normal;
            SaveManager saves = GameManager.Saves;
            if (group == null || saves == null || !CanReceiveExplorationOrders(source)
                || !FieldOrderCatalog.Allows(action, FieldOrderIssuer.Player)) { return false; }

            if (action == BroadcastAction.ReleaseOrder)
            {
                group.order = default;
                return true;
            }
            if (!AnyOrderRecipients(group, action)) { return false; }

            var order = new ExplorationOrder { command = new FieldOrder(action, FieldOrderIssuer.Player, source.Actor, target) };
            switch (action)
            {
                case BroadcastAction.Rally:
                    break;
                case BroadcastAction.FocusFire:
                    if (!(aimRange > 0f) || !float.IsFinite(aimRange) || !TryGetHostile(source, target, out CharacterActor3D hostile)
                        || !CompanionTactics3D.CanSee(source, hostile, aimRange, followObstructionLayers)) { return false; }
                    break;
                case BroadcastAction.DefendPoint:
                    if (!(aimRange > 0f) || !float.IsFinite(aimRange) || !position.HasValue
                        || !TryGetOrderFloor(surface, position.Value, out Vector3 point)
                        || (source.Body.bounds.center - point).sqrMagnitude > aimRange * aimRange) { return false; }
                    order.surface = surface;
                    order.surfacePoint = surface.transform.InverseTransformPoint(point);
                    break;
                default:
                    return false;
            }

            group.order = order;
            group.orderSaves = saves;
            group.orderLoadRevision = saves.LoadRevision;
            return true;
        }

        private void PrepareExplorationOrder(Deployment group, CharacterActor3D leader)
        {
            if (leader == null || leader.gameObject.scene != gameObject.scene
                || (encounter != null && encounter.CanReceivePlayerOrders(leader))
                || (group.orderSaves != null && (!ReferenceEquals(group.orderSaves, GameManager.Saves)
                    || group.orderLoadRevision != group.orderSaves.LoadRevision)))
            {
                ClearExplorationOrder(group);
                return;
            }

            group.orderAttackSlots?.Refresh();
            FieldOrder command = group.order.command;
            if (!command.Source.IsValid) { return; }
            if (command.Source != leader.Actor
                || command.Action == BroadcastAction.FocusFire && !TryGetHostile(leader, command.Target, out _)
                || command.Action == BroadcastAction.DefendPoint && !IsOrderSurface(group.order.surface)
                || command.Action == BroadcastAction.Rally && IsRallyComplete(group, leader))
            {
                group.order = default;
            }
        }

        private static void ClearExplorationOrder(Deployment group)
        {
            group.order = default;
            group.orderSaves = null;
            MissionAttackSlots3D attackSlots = group.orderAttackSlots;
            group.orderAttackSlots = null;
            attackSlots?.Dispose();
        }

        private bool TryDriveExplorationOrder(Deployment group, CharacterActor3D actor, CharacterActor3D leader,
            EncounterApproachRoutes3D routes, GameActionRuntime actions, ref bool transformsSynced, out Vector3 direction)
        {
            direction = Vector3.zero;
            ExplorationOrder order = group.order;
            if (leader == null || order.command.Source != leader.Actor || actor.gameObject.scene != leader.gameObject.scene) { return false; }

            switch (order.command.Action)
            {
                case BroadcastAction.Rally:
                    direction = FollowExplorationLeader(actor, leader, Mathf.Min(followDistance, k_RallyArrival), routes, ref transformsSynced);
                    return true;
                case BroadcastAction.DefendPoint:
                    routes?.Remove(actor.Actor);
                    SyncOrderTransforms(ref transformsSynced);
                    if (IsOrderSurface(order.surface))
                    {
                        Vector3 point = order.surface.transform.TransformPoint(order.surfacePoint);
                        if (TryGetOrderFloor(order.surface, point, out Vector3 ground))
                        {
                            direction = EncounterApproachRoutes3D.WalkStepTowardPoint(actor, ground, k_DefendArrival,
                                followGroundLayers, followObstructionLayers);
                        }
                    }
                    return true;
                case BroadcastAction.FocusFire:
                    if (!TryGetOrderAttack(actor, out EncounterCombatant3D combatant)
                        || !TryGetHostile(leader, order.command.Target, out CharacterActor3D target)) { return false; }
                    SyncOrderTransforms(ref transformsSynced);
                    direction = FocusExplorationAttack(group, combatant, actor, target, routes, actions);
                    return true;
                default:
                    return false;
            }
        }

        private Vector3 FocusExplorationAttack(Deployment group, EncounterCombatant3D combatant, CharacterActor3D actor,
            CharacterActor3D target, EncounterApproachRoutes3D routes, GameActionRuntime actions)
        {
            ActorRef owner = actor.Actor;
            float range = combatant.AttackRange;
            Vector3 targetFeet = target.Motor.FeetPosition;
            Vector3 offset = targetFeet - actor.Motor.FeetPosition;
            if (actions.IsPrimaryExecuting(owner.Hash) || GameManager.Data.GetStatusEffects(owner.Hash).BlocksActions)
            {
                return Vector3.zero;
            }
            if (actor.Motor.IsGrounded && offset.sqrMagnitude <= range * range
                && CompanionTactics3D.CanSee(actor, target, range, followObstructionLayers))
            {
                routes?.Remove(owner);
                offset.y = 0f;
                if (offset.sqrMagnitude > 0.001f) { actor.transform.rotation = Quaternion.LookRotation(offset); }
                group.orderAttackSlots ??= new MissionAttackSlots3D(actions, GameManager.Events);
                group.orderAttackSlots.TryExecute(actor, target.Actor, combatant.Attack, out _);
                return Vector3.zero;
            }

            var sighting = new EncounterSighting3D(owner, target.Actor, group.followTick, targetFeet, target.Body.bounds.center);
            if (routes != null && routes.TryPrepare(actor, sighting, range, followWaypoints, null, followGroundLayers,
                followObstructionLayers, out EncounterApproachRoutes3D.Decision route))
            {
                if (routes.TryFollow(actor, target.Actor, route, false, followGroundLayers,
                    followObstructionLayers, out Vector3 direction, out _)) { return direction; }
            }
            return EncounterApproachRoutes3D.WalkStepTowardPoint(actor, targetFeet, range * 0.8f,
                followGroundLayers, followObstructionLayers);
        }

        private Vector3 FollowExplorationLeader(CharacterActor3D actor, CharacterActor3D leader, float distance,
            EncounterApproachRoutes3D routes, ref bool transformsSynced)
        {
            if (routes == null) { return CompanionTactics3D.FollowDirection(actor, leader, distance); }
            SyncOrderTransforms(ref transformsSynced);
            return routes.FollowLeader(actor, leader, distance, followWaypoints, followGroundLayers, followObstructionLayers);
        }

        private static void SyncOrderTransforms(ref bool transformsSynced)
        {
            if (transformsSynced) { return; }
            Physics.SyncTransforms();
            transformsSynced = true;
        }

        private static bool TryGetHostile(CharacterActor3D source, ActorRef candidate, out CharacterActor3D target)
            => MultiLockTargeting3D.TryGetActor(candidate, out target) && target.gameObject.scene == source.gameObject.scene
                && AttackTargetRules.CanAffect(source.Actor, GameManager.Data.GetFlags(source.Actor.Hash).Belong,
                    candidate, GameManager.Data.GetFlags(candidate.Hash).Belong, AttackTargetRelation.Hostile);

        private static bool TryGetOrderAttack(CharacterActor3D actor, out EncounterCombatant3D combatant)
            => actor.TryGetComponent(out combatant) && combatant.isActiveAndEnabled && combatant.Attack != null
                && combatant.AttackRange > 0f && float.IsFinite(combatant.AttackRange);

        private bool AnyOrderRecipients(Deployment group, BroadcastAction action)
        {
            if (action == BroadcastAction.Rally && IsSisterRallyRecipient()) { return true; }
            foreach ((CharacterActor3D actor, string _) in group.units)
            {
                if (IsOrderRecipient(actor) && (action != BroadcastAction.FocusFire || TryGetOrderAttack(actor, out _))) { return true; }
            }
            return false;
        }

        private bool IsRallyComplete(Deployment group, CharacterActor3D leader)
        {
            float range = Mathf.Min(followDistance, k_RallyArrival);
            if (!(range > 0f)) { return false; }
            if (IsSisterRallyRecipient() && (_sister.Motor.FeetPosition - leader.Motor.FeetPosition).sqrMagnitude > range * range)
            {
                return false;
            }
            foreach ((CharacterActor3D actor, string _) in group.units)
            {
                if (IsOrderRecipient(actor) && (actor.Motor.FeetPosition - leader.Motor.FeetPosition).sqrMagnitude > range * range)
                {
                    return false;
                }
            }
            return true;
        }

        internal bool TryGetSisterRally(CharacterActor3D sister, CharacterActor3D leader, out float distance)
        {
            distance = Mathf.Min(followDistance, k_RallyArrival);
            Deployment group = _normal;
            if (group == null || distance <= 0f || !float.IsFinite(distance) || sister != _sister || !IsSisterRallyRecipient()
                || !CanReceiveExplorationOrders(leader) || group.orderSaves == null || !ReferenceEquals(group.orderSaves, GameManager.Saves)
                || group.orderSaves.LoadRevision != group.orderLoadRevision) { return false; }
            FieldOrder command = group.order.command;
            return command.Action == BroadcastAction.Rally && command.Source == leader.Actor;
        }

        private bool IsSisterRallyRecipient() => IsOrderRecipient(_sister)
            && _sister.TryGetComponent(out CompanionController3D controller) && controller.isActiveAndEnabled;

        private bool IsOrderRecipient(CharacterActor3D actor) => IsLive(actor) && actor.Body.enabled
            && actor.gameObject.scene == gameObject.scene && (encounter == null || !encounter.Drives(actor))
            && GameManager.Data.GetFlags(actor.Actor.Hash).Belong == CharacterBelong.Ally
            && !GameManager.Data.GetFlags(actor.Actor.Hash).IsVanished;

        private bool TryGetOrderFloor(Collider surface, Vector3 point, out Vector3 floor)
        {
            floor = default;
            if (!IsOrderSurface(surface) || !Finite(point)
                || !surface.Raycast(new Ray(point + Vector3.up * 0.1f, Vector3.down), out RaycastHit hit, 0.2f)
                || hit.normal.y <= 0.5f) { return false; }
            floor = hit.point;
            return true;
        }

        private bool IsOrderSurface(Collider surface) => surface != null && surface.enabled && surface.gameObject.activeInHierarchy
            && !surface.isTrigger && surface.gameObject.scene == gameObject.scene
            && (followGroundLayers.value & (1 << surface.gameObject.layer)) != 0
            && surface.GetComponentInParent<CharacterActor3D>() == null;
    }
}
