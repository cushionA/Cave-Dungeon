using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.Runtime
{
    public class ActionExecutorController : MonoBehaviour, IActionCommandBackend
    {
        [SerializeField] private AttackInfo[] _attackInfos;
        private string[] _definitionIds;
        private int _ownerHash;
        private HitBox _hitBox;
        private DamageDealer _damageDealer;
        private long _instanceId;
        private long _actionGroupId;
        private long _aiGroupId;
        private bool _aiAwaitingCompletion;
        private int _dispatchDepth;
        private long _aiMovementInstanceId;
        private ActorRef _aiMovementTarget;
        private ActionCommandKind _aiMovementKind;
        private ActionExecutionSystem _subscribedExecution;
        private readonly List<ActionExecution> _aiExecutions = new List<ActionExecution>();

        public bool IsExecuting => GameManager.Actions != null
            && (GameManager.Actions.Execution.IsGroupActive(_actionGroupId) || GameManager.Actions.IsPrimaryExecuting(_ownerHash));
        public long CurrentInstanceId => GameManager.Actions != null
            && GameManager.Actions.Execution.GetGroupPrimary(_actionGroupId, out ActionExecution current)
                ? current.instanceId : _instanceId;
        public long CurrentGroupId => _actionGroupId;
        public ActionDispatchStatus LastDispatchStatus { get; private set; }
        public bool HasMovementRequest
        {
            get
            {
                if (GameManager.Actions == null || _aiGroupId == 0) { return false; }
                GameManager.Actions.Execution.CopyGroupActive(_aiGroupId, _aiExecutions);
                for (int i = 0; i < _aiExecutions.Count; i++)
                {
                    if ((_aiExecutions[i].definition.resources & ActionResources.HorizontalMovement) != 0) { return true; }
                }
                return false;
            }
        }
        bool IActionExecutionBackend.IsExecuting => IsAIExecuting();
        public event Action OnActionCompleted;

#if UNITY_INCLUDE_TESTS
        public void SetAttackInfosForTest(AttackInfo[] infos) { _attackInfos = infos; CacheDefinitionIds(); }
#endif

        private void Awake()
        {
            _ownerHash = gameObject.GetHashCode();
            _hitBox = GetComponentInChildren<HitBox>();
            _damageDealer = GetComponentInChildren<DamageDealer>();
            CacheDefinitionIds();
        }

        private void OnEnable() { Attach(); }

        private void Start()
        {
            if (_hitBox == null) { _hitBox = GetComponentInChildren<HitBox>(); }
            if (_damageDealer == null) { _damageDealer = GetComponentInChildren<DamageDealer>(); }
            Attach();
        }

        private void OnDisable()
        {
            ((IActionExecutionBackend)this).CancelCurrent();
            CancelAction();
            if (_subscribedExecution != null) { _subscribedExecution.GroupChanged -= HandleGroupChanged; }
            _subscribedExecution = null;
            GameManager.Actions?.Detach(_ownerHash, this);
        }

        private void Attach()
        {
            GameManager.Actions?.Attach(_ownerHash, this);
            ActionExecutionSystem execution = GameManager.Actions != null ? GameManager.Actions.Execution : null;
            if (ReferenceEquals(execution, _subscribedExecution)) { return; }
            if (_subscribedExecution != null) { _subscribedExecution.GroupChanged -= HandleGroupChanged; }
            _subscribedExecution = execution;
            if (_subscribedExecution != null) { _subscribedExecution.GroupChanged += HandleGroupChanged; }
        }

        private void CacheDefinitionIds()
        {
            _definitionIds = new string[_attackInfos != null ? _attackInfos.Length : 0];
            for (int i = 0; i < _definitionIds.Length; i++)
            {
                _definitionIds[i] = _attackInfos[i] != null ? _attackInfos[i].PersistentId : null;
            }
        }

        /// <summary>旧ActionSlot保存の互換入口。実行前に意味付き要求へ変換する。</summary>
        public bool ExecuteAction(ActionSlot slot, int targetHash = 0)
        {
            ActionCommand command = ActionCommand.FromLegacySlot(slot);
            long replaceGroupId = GameManager.Actions != null && GameManager.Actions.Execution.IsGroupActive(_actionGroupId)
                ? _actionGroupId : 0;
            ActionDispatchResult result = Dispatch(command, targetHash, replaceGroupId);
            if (result.Accepted) { TrackAcceptedGroup(result.GroupId); }
            return result.Accepted;
        }

        public bool ExecuteAttack(AttackInfo definition, int targetHash, float chargeMultiplier,
            ActionVariant variant, out long instanceId, out long groupId)
        {
            instanceId = groupId = 0;
            if (GameManager.Actions == null || !GameManager.IsCharacterValid(_ownerHash))
            {
                LastDispatchStatus = ActionDispatchStatus.InvalidOwner;
                return false;
            }
            if (definition == null)
            {
                LastDispatchStatus = ActionDispatchStatus.UnknownDefinition;
                return false;
            }
            long replaceGroupId = GameManager.Actions.Execution.IsGroupActive(_actionGroupId) ? _actionGroupId : 0;
            bool accepted = GameManager.Actions.Execute(_ownerHash, definition, targetHash, FacingDirection(),
                chargeMultiplier, variant, out instanceId, out groupId, replaceGroupId);
            LastDispatchStatus = accepted ? ActionDispatchStatus.Accepted : ActionDispatchStatus.Rejected;
            if (accepted) { TrackAcceptedGroup(groupId); }
            return accepted;
        }

        private Vector2 FacingDirection() => transform.localScale.x >= 0f ? Vector2.right : Vector2.left;

        private void TrackAcceptedGroup(long groupId) =>
            _actionGroupId = GameManager.Actions != null && GameManager.Actions.Execution.IsGroupActive(groupId) ? groupId : 0;

        private ActionDispatchResult Dispatch(in ActionCommand command, int targetHash, long replaceGroupId = 0)
        {
            if (GameManager.Actions == null || !GameManager.IsCharacterValid(_ownerHash))
            {
                return Result(ActionDispatchStatus.InvalidOwner);
            }
            if (!Supports(command.kind)) { return Result(ActionDispatchStatus.Unsupported); }
            ActorRef targetRef = default;
            if (targetHash != 0 && (GameManager.Actors == null || !GameManager.Actors.TryGet(targetHash, out targetRef)))
            {
                return Result(ActionDispatchStatus.InvalidTarget);
            }
            if (targetHash != 0 && GameManager.Data.GetFlags(targetHash).IsVanished) { return Result(ActionDispatchStatus.InvalidTarget); }
            Vector2 direction = FacingDirection();
            long instanceId = 0;
            bool accepted;
            if (command.kind == ActionCommandKind.Attack || command.kind == ActionCommandKind.Cast)
            {
                if (_definitionIds == null || _definitionIds.Length != (_attackInfos != null ? _attackInfos.Length : 0)) { CacheDefinitionIds(); }
                ActionDispatchStatus resolved = ActionDefinitionReference.Resolve(command.attack.definitionId,
                    command.attack.legacyIndex, _definitionIds, out int index);
                if (resolved != ActionDispatchStatus.Accepted) { return Result(resolved); }
                if (_attackInfos[index] == null) { return Result(ActionDispatchStatus.UnknownDefinition); }
                bool attackAccepted = GameManager.Actions.Execute(_ownerHash, _attackInfos[index], targetHash, direction,
                    command.attack.chargeMultiplier, command.attack.variant, out instanceId, out long attackGroup, replaceGroupId);
                if (!attackAccepted) { return Result(ActionDispatchStatus.Rejected); }
                TrackAcceptedGroup(attackGroup);
                return Result(ActionDispatchStatus.Accepted, instanceId, attackGroup);
            }
            if (float.IsNaN(command.sustained.duration) || float.IsInfinity(command.sustained.duration)
                || command.sustained.duration < 0f) { return Result(ActionDispatchStatus.InvalidParameters); }
            if (command.kind == ActionCommandKind.Dodge)
            {
                accepted = GameManager.Actions.TryDodge(_ownerHash, direction, out instanceId, replaceGroupId);
            }
            else if (command.kind == ActionCommandKind.Guard)
            {
                accepted = GameManager.Actions.TrySetGuarding(_ownerHash, true, out instanceId, command.sustained.duration, replaceGroupId);
            }
            else if (command.kind == ActionCommandKind.Idle)
            {
                accepted = GameManager.Actions.TryIdle(_ownerHash, out instanceId, command.sustained.duration, replaceGroupId);
            }
            else
            {
                if (!GameManager.IsCharacterValid(targetHash)) { return Result(ActionDispatchStatus.InvalidTarget); }
                Vector2 delta = ObservedPosition(targetHash) - ObservedPosition(_ownerHash);
                if (command.kind == ActionCommandKind.Retreat || command.kind == ActionCommandKind.Flee) { delta = -delta; }
                accepted = GameManager.Actions.TrySetLocomotion(_ownerHash, delta.normalized,
                    EffectiveMoveSpeed(), out instanceId, command.sustained.duration, targetRef, replaceGroupId);
            }
            long groupId = 0;
            if (accepted) { GameManager.Actions.Execution.TryGetGroupId(instanceId, out groupId); }
            return Result(accepted ? ActionDispatchStatus.Accepted : ActionDispatchStatus.Rejected, instanceId, groupId, targetRef);
        }

        private ActionDispatchResult Result(ActionDispatchStatus status, long instanceId = 0, long groupId = 0, ActorRef targetRef = default)
        {
            LastDispatchStatus = status;
            return new ActionDispatchResult(status, instanceId, groupId, targetRef);
        }

        public bool Supports(ActionCommandKind kind) => kind >= ActionCommandKind.Attack && kind <= ActionCommandKind.Flee;

        bool IActionExecutionBackend.Execute(int ownerHash, int targetHash, ActionSlot slot)
        {
            ActionCommand command = ActionCommand.FromLegacySlot(slot);
            return ExecuteCommand(ownerHash, targetHash, command).Accepted;
        }

        public ActionDispatchResult ExecuteCommand(int ownerHash, int targetHash, in ActionCommand command)
        {
            if (ownerHash != _ownerHash) { return Result(ActionDispatchStatus.InvalidOwner); }
            _dispatchDepth++;
            try
            {
                long replaceGroupId = IsAIExecuting() ? _aiGroupId : 0;
                ActionDispatchResult result = Dispatch(command, targetHash, replaceGroupId);
                if (!result.Accepted) { return result; }
                _aiAwaitingCompletion = false;
                _aiGroupId = result.GroupId;
                TrackAcceptedGroup(result.GroupId);
                _aiAwaitingCompletion = true;
                _aiMovementInstanceId = IsMovement(command.kind) ? result.InstanceId : 0;
                _aiMovementTarget = result.TargetRef;
                _aiMovementKind = command.kind;
                return result;
            }
            finally
            {
                _dispatchDepth--;
                CompleteAIIfSettled();
            }
        }

        private static bool IsMovement(ActionCommandKind kind) => kind == ActionCommandKind.MoveToTarget
            || kind == ActionCommandKind.Follow || kind == ActionCommandKind.Retreat || kind == ActionCommandKind.Flee;

        void IActionExecutionBackend.CancelCurrent()
        {
            long groupId = _aiGroupId;
            _aiGroupId = 0;
            _aiAwaitingCompletion = false;
            _aiMovementInstanceId = 0;
            if (groupId != 0) { GameManager.Actions?.Execution.CancelGroup(groupId); }
        }

        void IActionExecutionBackend.Tick(float deltaTime)
        {
            RefreshAIMovement();
            CompleteAIIfSettled();
        }

        private float EffectiveMoveSpeed()
        {
            CharacterStatusEffects effects = GameManager.Data.GetStatusEffects(_ownerHash);
            return effects.BlocksMovement ? 0f : GameManager.Data.GetMoveParams(_ownerHash).moveSpeed * effects.MoveSpeedMultiplier;
        }

        private static Vector2 ObservedPosition(int actorHash) =>
            GameManager.ObservedPosition(actorHash, GameManager.Data.GetVitals(actorHash).position);

        private void RefreshAIMovement()
        {
            if (_aiMovementInstanceId == 0 || GameManager.Actions == null
                || !GameManager.Actions.Execution.TryGetInstance(_aiMovementInstanceId, out ActionExecution execution)
                || !execution.definition.movementUsesInput) { return; }
            if (!_aiMovementTarget.IsValid || GameManager.Actors == null || !GameManager.Actors.IsCurrent(_aiMovementTarget))
            {
                GameManager.Actions.Execution.CancelGroup(_aiGroupId);
                return;
            }
            Vector2 delta = ObservedPosition(_aiMovementTarget.Hash) - ObservedPosition(_ownerHash);
            if (_aiMovementKind == ActionCommandKind.Retreat || _aiMovementKind == ActionCommandKind.Flee) { delta = -delta; }
            GameManager.Actions.Execution.UpdateInput(_aiMovementInstanceId, delta.normalized, EffectiveMoveSpeed());
        }

        private bool IsAIExecuting() => GameManager.Actions != null && GameManager.Actions.Execution.IsGroupActive(_aiGroupId);

        private void CompleteAIIfSettled()
        {
            if (_dispatchDepth > 0 || !_aiAwaitingCompletion || IsAIExecuting()) { return; }
            _aiAwaitingCompletion = false;
            _aiMovementInstanceId = 0;
            _aiGroupId = 0;
            OnActionCompleted?.Invoke();
        }

        private void HandleGroupChanged(ActionGroupEvent notification)
        {
            if (notification.groupId == _actionGroupId) { _actionGroupId = 0; }
            if (notification.groupId == _aiGroupId) { CompleteAIIfSettled(); }
        }

        public void CancelAction()
        {
            if (_actionGroupId != 0) { GameManager.Actions?.Execution.CancelGroup(_actionGroupId); }
            else if (_instanceId != 0) { GameManager.Actions?.Execution.Cancel(_instanceId, true); }
            Deactivate();
            _instanceId = _actionGroupId = 0;
        }

        public void HandleExecution(ActionExecutionEvent notification, AttackMotionData motion, bool projectile)
        {
            if (notification.kind == ActionSignalKind.Started)
            {
                _instanceId = notification.execution.instanceId;
                _actionGroupId = notification.execution.groupId;
            }
            if (notification.execution.instanceId != _instanceId) { return; }
            if (notification.kind == ActionSignalKind.PhaseChanged)
            {
                if (notification.execution.phase == AnimationPhase.Active && !projectile)
                {
                    if (_hitBox != null) { _hitBox.Activate(motion, _ownerHash); }
                    else if (_damageDealer != null) { _damageDealer.Activate(motion, _ownerHash); }
                }
                else { Deactivate(); }
            }
            if (notification.kind == ActionSignalKind.Completed || notification.kind == ActionSignalKind.Interrupted)
            {
                Deactivate();
                _instanceId = 0;
            }
        }

        private void Deactivate()
        {
            if (_hitBox != null) { _hitBox.Deactivate(); }
            if (_damageDealer != null) { _damageDealer.Deactivate(); }
        }
    }
}
