using Game.Core;
using UnityEngine;

namespace Game.Runtime
{
    public partial class ActionExecutorController
    {
        private AISceneGimmickController.ApproachReservation _gimmickApproach;

        private ActionDispatchResult DispatchApproach(AISceneGimmickController.ApproachReservation approach,
            ActorRef target, long replaceGroupId)
        {
            GameActionRuntime actions = GameManager.Actions;
            float speed = EffectiveMoveSpeed();
            if (!approach.TryGetStep(_gimmickActors, out Vector2 destination, out _)
                || GameManager.Data.GetManaged(_ownerHash) is not BaseCharacter owner
                || !actions.TrySetLocomotion(_ownerHash, Vector2.right * Mathf.Sign(destination.x - owner.PhysicsPosition.x),
                    speed, out long instance, AISceneGimmickController.ApproachReservation.Duration, target, replaceGroupId)
                || !actions.Execution.TryGetGroupId(instance, out long group))
            {
                approach.Release();
                return Result(ActionDispatchStatus.Rejected);
            }
            if (!approach.IsCurrent)
            {
                actions.Execution.CancelGroup(group);
                return Result(ActionDispatchStatus.Rejected);
            }
            _gimmickApproach = approach;
            return Result(ActionDispatchStatus.Accepted, instance, group, target);
        }

        // 別のAIの行動開始コールバックが、判断後に床や操作点を変えることがある。
        internal void RecheckGimmickApproachBeforePhysics()
        {
            if (_gimmickApproach.IsAssigned && GameManager.Actions is GameActionRuntime actions
                && actions.Execution.TryGetInstance(_aiMovementInstanceId, out ActionExecution current)
                && current.ownerHash == _ownerHash)
            {
                RefreshGimmickApproach(current, Time.fixedDeltaTime, activateOnArrival: false);
            }
        }

        internal void CancelGimmickApproach()
        {
            if (!_gimmickApproach.IsAssigned) { return; }
            _gimmickApproach.Release();
            _gimmickApproach = default;
            if (GameManager.Actions is not GameActionRuntime actions
                || !actions.Execution.TryGetInstance(_aiMovementInstanceId, out ActionExecution current)
                || current.ownerHash != _ownerHash) { return; }

            actions.Execution.UpdateInput(current.instanceId, Vector2.zero, 0f);
            if (GameManager.Data.GetManaged(_ownerHash) is BaseCharacter owner && owner != null)
            {
                owner.SetPhysicsVelocity(new Vector2(0f, owner.PhysicsVelocity.y));
            }
            actions.Execution.CancelGroup(current.groupId);
        }

        private void RefreshGimmickApproach(in ActionExecution current, float deltaTime, bool activateOnArrival = true)
        {
            ActionExecutionSystem execution = GameManager.Actions.Execution;
            if (_gimmickApproach.TryGetStep(_gimmickActors, out Vector2 destination, out bool arrived)
                && GameManager.Data.GetManaged(_ownerHash) is BaseCharacter owner)
            {
                if (!arrived)
                {
                    float dx = destination.x - owner.PhysicsPosition.x;
                    float travel = deltaTime > 0f ? Mathf.Min(EffectiveMoveSpeed(), Mathf.Abs(dx) / deltaTime) : 0f;
                    execution.UpdateInput(current.instanceId, Vector2.right * Mathf.Sign(dx), travel);
                    return;
                }

                execution.UpdateInput(current.instanceId, Vector2.zero, 0f);
                if (!activateOnArrival) { return; }
                owner.SetPhysicsVelocity(new Vector2(0f, owner.PhysicsVelocity.y));
                AISceneGimmickController.ApproachReservation completed = _gimmickApproach;
                _gimmickApproach = default;
                try { completed.Activate(); }
                finally
                {
                    completed.Release();
                    execution.CancelGroup(current.groupId);
                }
                return;
            }

            CancelGimmickApproach();
        }

        private void ReleaseGimmickTravel()
        {
            ReleaseLure();
            _gimmickApproach.Release();
            _gimmickApproach = default;
        }
    }
}
