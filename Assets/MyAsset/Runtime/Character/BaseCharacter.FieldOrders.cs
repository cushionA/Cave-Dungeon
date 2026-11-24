using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.Runtime
{
    public partial class BaseCharacter
    {
        private const float FieldOrderArrivalDistance = 0.65f;
        private FieldOrder _appliedFieldOrder;
        private bool _waitingToSwitchOrder;
        internal bool HoldsFieldPosition => _appliedFieldOrder.Source.IsValid
            && _appliedFieldOrder.Action == BroadcastAction.DefendPoint && !_waitingToSwitchOrder;
        internal virtual JudgmentLoop FieldOrderJudgment => null;

        // Run at the normal decision boundary. An order issued by another AI during job
        // application takes effect on a subsequent preparation, never halfway through this batch.
        protected bool PrepareFieldOrder(List<int> candidates)
        {
            RuntimeFieldOrders orders = GameManager.FieldOrders;
            if ((!_appliedFieldOrder.Source.IsValid && !orders.HasAssignments)
                || !GameManager.Actors.TryGet(ObjectHash, out ActorRef recipient)) { return false; }
            orders.TryRead(recipient, out FieldOrder next);
            _waitingToSwitchOrder = false;
            if (_appliedFieldOrder.Source != next.Source || _appliedFieldOrder.Action != next.Action
                || _appliedFieldOrder.Target != next.Target || _appliedFieldOrder.Position != next.Position)
            {
                // Finishing the current committed move takes precedence over switching assignments.
                if (!orders.CanSwitch(ObjectHash)) { _waitingToSwitchOrder = true; return true; }
                FieldOrderJudgment.ForceEvaluate();
                _appliedFieldOrder = next;
            }
            if (!_appliedFieldOrder.Source.IsValid) { return false; }
            if (_appliedFieldOrder.Action == BroadcastAction.Rally || _appliedFieldOrder.Action == BroadcastAction.DefendPoint)
            {
                if (!TryGetFieldOrderDestination(out Vector2 point, out Vector2 tolerance)) { return false; }
                Vector2 offset = point - PhysicsPosition;
                if (Mathf.Abs(offset.x) > tolerance.x || Mathf.Abs(offset.y) > tolerance.y) { return true; }
                if (_appliedFieldOrder.Action == BroadcastAction.Rally)
                {
                    orders.Complete(recipient);
                    _appliedFieldOrder = default;
                }
                // At the rally destination resume normal AI. At a post, allow combat judgment
                // while suppressing chase movement that would pull the defender off station.
                return false;
            }

            // Keep the authored role's own selection filters, attack rules and range.
            candidates.Clear();
            candidates.Add(_appliedFieldOrder.Action == BroadcastAction.Taunt
                ? _appliedFieldOrder.Source.Hash : _appliedFieldOrder.Target.Hash);
            return false;
        }

        private bool TryGetFieldOrderDestination(out Vector2 point, out Vector2 tolerance)
        {
            point = default;
            tolerance = default;
            if (!_appliedFieldOrder.Source.IsValid) { return false; }
            if (_appliedFieldOrder.Action == BroadcastAction.DefendPoint)
            {
                tolerance = FieldOrderTolerance(null);
                point = _appliedFieldOrder.Position.Value;
                return true;
            }
            if (_appliedFieldOrder.Action != BroadcastAction.Rally
                || !GameManager.Observation.TryGetActor(_appliedFieldOrder.Source.Hash, out AIActorObservation source)
                || source.actor != _appliedFieldOrder.Source) { return false; }
            if (!(GameManager.Data.GetManaged(source.actor.Hash) is BaseCharacter leader)) { return false; }
            tolerance = FieldOrderTolerance(leader);
            point = source.position;
            return true;
        }

        internal Vector2 FieldOrderTolerance(BaseCharacter leader)
        {
            Vector2 extents = BodyExtents();
            if (leader == null)
            {
                return new Vector2(FieldOrderArrivalDistance, Mathf.Max(FieldOrderArrivalDistance, extents.y + 0.25f));
            }
            extents += leader.BodyExtents();
            return new Vector2(Mathf.Max(FieldOrderArrivalDistance, extents.x + 0.2f),
                Mathf.Max(FieldOrderArrivalDistance, extents.y));
        }

        private Vector2 BodyExtents()
        {
            if (_collider == null) { return Vector2.zero; }
            // Box size is also available when a test or a suspended body has no physics bounds.
            Vector3 scale = _collider.transform.lossyScale;
            return new Vector2(_collider.size.x * Mathf.Abs(scale.x), _collider.size.y * Mathf.Abs(scale.y)) * 0.5f;
        }

        protected bool ApplyFieldOrderMovement()
        {
            if (_waitingToSwitchOrder)
            {
                if (_appliedFieldOrder.Source.IsValid && (_appliedFieldOrder.Action == BroadcastAction.Rally
                    || _appliedFieldOrder.Action == BroadcastAction.DefendPoint) && !GameManager.Actions.IsMovementBlocked(ObjectHash))
                {
                    SetPhysicsVelocity(new Vector2(0f, PhysicsVelocity.y));
                }
                return true;
            }
            if (!TryGetFieldOrderDestination(out Vector2 point, out Vector2 tolerance)) { return false; }
            if (GameManager.Actions.IsMovementBlocked(ObjectHash)) { return true; }
            float dx = point.x - PhysicsPosition.x;
            float speed = Mathf.Abs(dx) > tolerance.x
                ? Mathf.Sign(dx) * GetEffectiveMoveParams(GameConstants.k_FallbackMoveSpeed).moveSpeed : 0f;
            SetPhysicsVelocity(new Vector2(speed, PhysicsVelocity.y));
            if (speed != 0f) { SetFacing(speed > 0f); }
            return true;
        }

        protected void ForgetFieldOrders()
        {
            if (GameManager.Actors != null && GameManager.Actors.TryGet(ObjectHash, out ActorRef actor)
                && ReferenceEquals(GameManager.Data.GetManaged(ObjectHash), this))
            {
                GameManager.FieldOrders?.Forget(actor);
            }
            _appliedFieldOrder = default;
            _waitingToSwitchOrder = false;
        }
    }
}
