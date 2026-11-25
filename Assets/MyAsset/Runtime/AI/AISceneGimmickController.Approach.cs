using System.Collections.Generic;
using Game.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Runtime
{
    public sealed partial class AISceneGimmickController
    {
        private const float k_ApproachTimeout = 4f;
        private const float k_ApproachFloorTolerance = 0.08f;

        private struct OperationApproach
        {
            public ulong token;
            public int generation;
            public ActorRef owner;
            public ActorRef target;
            public PhysicsScene2D world;
            public double expiresAt;
            public Vector2 operation;
            public Vector2 destination;
        }

        internal readonly struct ApproachReservation
        {
            private readonly AISceneGimmickController _source;
            private readonly ulong _token;
            internal static float Duration => k_ApproachTimeout;

            internal ApproachReservation(AISceneGimmickController source, ulong token)
            {
                _source = source;
                _token = token;
            }

            internal bool IsAssigned => _token != 0;
            internal bool IsCurrent => _source != null && _source.IsApproachCurrent(_token);

            internal bool TryGetStep(List<int> liveActors, out Vector2 destination, out bool arrived)
            {
                destination = default;
                arrived = false;
                return IsCurrent && _source.TryGetApproachStep(liveActors, out destination, out arrived);
            }

            internal void Activate()
            {
                if (IsCurrent) { _source.Activate(); }
            }

            internal void Release()
            {
                if (_source != null && _source._approach.token == _token) { _source._approach = default; }
            }
        }

        private OperationApproach _approach;
        private ulong _nextApproachToken;
        private readonly RaycastHit2D[] _approachCast = new RaycastHit2D[1];

        private bool ReserveApproach(in ActionCommand command, in AISceneGimmick gimmick, Vector2 destination,
            out ApproachReservation reservation)
        {
            ulong token = checked(++_nextApproachToken);
            _approach = new OperationApproach
            {
                token = token, generation = gimmick.generation, owner = command.gimmickOwner, target = command.gimmickTarget,
                world = gimmick.physicsScene, expiresAt = Time.fixedTimeAsDouble + k_ApproachTimeout,
                operation = gimmick.operationPosition, destination = destination,
            };
            reservation = new ApproachReservation(this, token);
            return true;
        }

        private bool IsApproachCurrent(ulong token)
        {
            if (token == 0 || _approach.token != token) { return false; }
            if (isActiveAndEnabled && !consumed && !requireTargetContact && generation == _approach.generation
                && Time.fixedTimeAsDouble < _approach.expiresAt && gameObject.scene.GetPhysicsScene2D() == _approach.world
                && GameManager.Actors is ActorRegistry actors && actors.IsCurrent(_approach.owner)
                && actors.IsCurrent(_approach.target))
            {
                return true;
            }
            _approach = default;
            return false;
        }

        private bool TryGetApproachStep(List<int> liveActors, out Vector2 destination, out bool arrived)
        {
            destination = default;
            arrived = false;
            OperationApproach active = _approach;
            AISceneGimmick current = Snapshot;
            if (!current.IsValid || current.approachDistance <= 0f || current.operationPosition != active.operation
                || !TryActor(active.owner.Hash, active.world, out BaseCharacter owner) || !CanApproach(owner)
                || !TryActor(active.target.Hash, active.world, out BaseCharacter target)
                || !IsHostile(active.owner, active.target, out CharacterBelong ownerBelong)
                || !TryGetPosition(active.owner.Hash, owner, out Vector2 ownerPosition)
                || Mathf.Abs(ownerPosition.y - active.destination.y) > k_ApproachFloorTolerance
                || !TryGetPosition(active.target.Hash, target, out Vector2 targetPosition)
                || !TryGetVelocity(current.effectDelay, target, out Vector2 targetVelocity)
                || !current.WillAffectTarget(targetPosition, targetVelocity)
                || !HasSafeEffect(current, active.owner, ownerBelong, liveActors))
            {
                return false;
            }

            arrived = current.CanOperateFrom(ownerPosition);
            destination = arrived ? ownerPosition : new Vector2(active.destination.x, ownerPosition.y);
            Physics2D.SyncTransforms();
            return HasClearApproach(current, owner, destination);
        }

        private static bool CanApproach(BaseCharacter owner)
        {
            if (owner == null || !owner.isActiveAndEnabled || owner.HoldsFieldPosition || owner.PhysicsBackend == null
                || owner.PhysicsBackend.IsSuspended || !owner.PhysicsBackend.BodyColliderEnabled
                || !float.IsFinite(owner.PhysicsVelocity.y) || Mathf.Abs(owner.PhysicsVelocity.y) > 0.3f) { return false; }
            int hash = owner.ObjectHash;
            CharacterStatusEffects effects = GameManager.Data.GetStatusEffects(hash);
            float speed = GameManager.Data.GetMoveParams(hash).moveSpeed * effects.MoveSpeedMultiplier;
            return !effects.BlocksActions && !effects.BlocksMovement && float.IsFinite(speed) && speed > 0f;
        }

        private bool HasClearApproach(in AISceneGimmick gimmick, BaseCharacter owner, Vector2 destination)
        {
            if (owner == null || !owner.TryGetComponent(out BoxCollider2D body) || !body.enabled
                || !gimmick.CanOperateFrom(destination) || gimmick.ContainsEffect(destination)
                || !HasClearOperation(gimmick.physicsScene, destination, gimmick.operationPosition))
            {
                return false;
            }

            PhysicsScene2D world = gimmick.physicsScene;
            Bounds bounds = body.bounds;
            float dx = destination.x - owner.PhysicsPosition.x;
            float distance = Mathf.Abs(dx);
            float spacing = Mathf.Min(0.25f, bounds.size.x * 0.5f);
            if (!(spacing > 0f) || !(bounds.size.y > 0f) || distance > gimmick.approachDistance
                || distance > spacing * 96f) { return false; }

            ContactFilter2D floor = new ContactFilter2D { useTriggers = false };
            floor.SetLayerMask(owner.GroundLayers);
            Vector2 probeStart = new Vector2(bounds.center.x, bounds.min.y + k_ApproachFloorTolerance);
            int samples = Mathf.Max(1, Mathf.CeilToInt(distance / spacing));
            for (int i = 0; i <= samples; i++)
            {
                Vector2 probe = probeStart + Vector2.right * (dx * i / samples);
                RaycastHit2D hit = world.Raycast(probe, Vector2.down, k_ApproachFloorTolerance * 2f, floor);
                if (hit.collider == null || hit.normal.y < 0.95f) { return false; }
            }

            if (distance == 0f) { return true; }
            ContactFilter2D blockers = new ContactFilter2D { useTriggers = false };
            blockers.SetLayerMask(owner.GroundLayers.value | operationBlockers.value);
            Vector2 center = (Vector2)bounds.center + Vector2.up * k_ApproachFloorTolerance;
            bool clear = world.BoxCast(center, bounds.size, 0f, Vector2.right * Mathf.Sign(dx),
                distance, blockers, _approachCast) == 0;
            _approachCast[0] = default;
            return clear;
        }
    }
}
