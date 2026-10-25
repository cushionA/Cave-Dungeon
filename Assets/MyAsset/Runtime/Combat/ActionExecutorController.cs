using System;
using System.Collections.Generic;
using Game.Core;
using R3;
using UnityEngine;

namespace Game.Runtime
{
    public class ActionExecutorController : MonoBehaviour, IActionExecutionBackend
    {
        [SerializeField] private AttackInfo[] _attackInfos;
        private int _ownerHash;
        private HitBox _hitBox;
        private DamageDealer _damageDealer;
        private long _instanceId;
        private long _sourceInstanceId;
        private long _aiInstanceId;
        private long _aiSourceInstanceId;
        private bool _aiAwaitingCompletion;
        private long _aiMovementInstanceId;
        private int _aiMovementTarget;
        private SustainedAction _aiMovementKind;
        private IDisposable _executionSubscription;
        private readonly List<ActionExecution> _aiExecutions = new List<ActionExecution>();

        public bool IsExecuting => GameManager.Actions != null && GameManager.Actions.IsPrimaryExecuting(_ownerHash);
        public long CurrentInstanceId => _instanceId;
        public bool HasMovementRequest => _aiInstanceId != 0 && GameManager.Actions != null
            && GameManager.Actions.Execution.TryGetInstance(_aiInstanceId, out ActionExecution execution)
            && (execution.definition.resources & ActionResources.HorizontalMovement) != 0;
        bool IActionExecutionBackend.IsExecuting => IsAIExecuting();
        public event Action OnActionCompleted;

#if UNITY_INCLUDE_TESTS
        public void SetAttackInfosForTest(AttackInfo[] infos) { _attackInfos = infos; }
#endif

        private void Awake()
        {
            _ownerHash = gameObject.GetHashCode();
            _hitBox = GetComponentInChildren<HitBox>();
            _damageDealer = GetComponentInChildren<DamageDealer>();
        }

        private void OnEnable()
        {
            Attach();
        }

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
            _executionSubscription?.Dispose();
            _executionSubscription = null;
            GameManager.Actions?.Detach(_ownerHash, this);
        }

        private void Attach()
        {
            GameManager.Actions?.Attach(_ownerHash, this);
            if (_executionSubscription == null && GameManager.Events != null)
            {
                _executionSubscription = GameManager.Events.OnActionExecution.Subscribe(HandleAIExecution);
            }
        }

        public bool ExecuteAction(ActionSlot slot, int targetHash = 0)
        {
            return TryExecuteAction(slot, targetHash, out _);
        }

        private bool TryExecuteAction(ActionSlot slot, int targetHash, out long instanceId)
        {
            instanceId = 0;
            if (GameManager.Actions == null || !GameManager.IsCharacterValid(_ownerHash)) { return false; }
            Vector2 direction = transform.localScale.x >= 0f ? Vector2.right : Vector2.left;
            if (slot.execType == ActionExecType.Attack || slot.execType == ActionExecType.Cast)
            {
                if (_attackInfos == null || slot.paramId < 0 || slot.paramId >= _attackInfos.Length) { return false; }
                return GameManager.Actions.Execute(_ownerHash, _attackInfos[slot.paramId], targetHash, direction, out instanceId);
            }
            if (slot.execType == ActionExecType.Instant && slot.paramId == (int)InstantAction.Dodge)
            {
                return GameManager.Actions.TryDodge(_ownerHash, direction, out instanceId);
            }
            if (slot.execType == ActionExecType.Sustained)
            {
                if (slot.paramId == (int)SustainedAction.Guard) { return GameManager.Actions.TrySetGuarding(_ownerHash, true, out instanceId, slot.paramValue); }
                if (slot.paramId == (int)SustainedAction.Idle) { return GameManager.Actions.TryIdle(_ownerHash, out instanceId, slot.paramValue); }
                if (slot.paramId != (int)SustainedAction.MoveToTarget && slot.paramId != (int)SustainedAction.Follow
                    && slot.paramId != (int)SustainedAction.Retreat && slot.paramId != (int)SustainedAction.Flee) { return false; }
                if (GameManager.IsCharacterValid(targetHash))
                {
                    Vector2 delta = GameManager.Data.GetVitals(targetHash).position - GameManager.Data.GetVitals(_ownerHash).position;
                    if (slot.paramId == (int)SustainedAction.Retreat || slot.paramId == (int)SustainedAction.Flee) { delta = -delta; }
                    return GameManager.Actions.TrySetLocomotion(_ownerHash, delta.normalized,
                        EffectiveMoveSpeed(), out instanceId, slot.paramValue);
                }
            }
            return false;
        }

        bool IActionExecutionBackend.Execute(int ownerHash, int targetHash, ActionSlot slot)
        {
            long previous = _aiInstanceId;
            long previousSource = _aiSourceInstanceId;
            if (ownerHash != _ownerHash || !TryExecuteAction(slot, targetHash, out long instanceId)) { return false; }
            if (previous != 0 && previous != instanceId) { GameManager.Actions.Execution.Cancel(previous, true); }
            if (previousSource != 0 && previousSource != instanceId) { GameManager.Actions.Execution.CancelPendingFrom(previousSource); }
            _aiInstanceId = instanceId;
            _aiSourceInstanceId = instanceId;
            _aiAwaitingCompletion = true;
            _aiMovementInstanceId = slot.execType == ActionExecType.Sustained
                && (slot.paramId == (int)SustainedAction.MoveToTarget || slot.paramId == (int)SustainedAction.Follow
                    || slot.paramId == (int)SustainedAction.Retreat || slot.paramId == (int)SustainedAction.Flee) ? instanceId : 0;
            _aiMovementTarget = targetHash;
            _aiMovementKind = (SustainedAction)slot.paramId;
            CompleteAIIfSettled();
            return true;
        }

        void IActionExecutionBackend.CancelCurrent()
        {
            long instanceId = _aiInstanceId;
            long sourceInstanceId = _aiSourceInstanceId;
            _aiInstanceId = 0;
            _aiSourceInstanceId = 0;
            _aiAwaitingCompletion = false;
            _aiMovementInstanceId = 0;
            if (instanceId != 0) { GameManager.Actions?.Execution.Cancel(instanceId, true); }
            if (sourceInstanceId != 0) { GameManager.Actions?.Execution.CancelPendingFrom(sourceInstanceId); }
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

        private void RefreshAIMovement()
        {
            if (_aiMovementInstanceId == 0 || _aiMovementInstanceId != _aiInstanceId || GameManager.Actions == null
                || !GameManager.Actions.Execution.TryGetInstance(_aiMovementInstanceId, out ActionExecution execution)
                || !execution.definition.movementUsesInput) { return; }
            if (!GameManager.IsCharacterValid(_aiMovementTarget))
            {
                GameManager.Actions.Execution.Cancel(_aiMovementInstanceId, true);
                return;
            }
            Vector2 delta = GameManager.Data.GetVitals(_aiMovementTarget).position - GameManager.Data.GetVitals(_ownerHash).position;
            if (_aiMovementKind == SustainedAction.Retreat || _aiMovementKind == SustainedAction.Flee) { delta = -delta; }
            GameManager.Actions.Execution.UpdateInput(_aiMovementInstanceId, delta.normalized, EffectiveMoveSpeed());
        }

        private bool IsAIExecuting()
        {
            if (GameManager.Actions == null) { return false; }
            return (_aiInstanceId != 0 && GameManager.Actions.Execution.TryGetInstance(_aiInstanceId, out _))
                || (_aiSourceInstanceId != 0 && GameManager.Actions.Execution.HasPendingFrom(_aiSourceInstanceId));
        }

        private void CompleteAIIfSettled()
        {
            if (!_aiAwaitingCompletion || IsAIExecuting()) { return; }
            _aiAwaitingCompletion = false;
            _aiMovementInstanceId = 0;
            _aiInstanceId = 0;
            _aiSourceInstanceId = 0;
            OnActionCompleted?.Invoke();
        }

        private void HandleAIExecution(ActionExecutionEvent notification)
        {
            if (notification.kind == ActionSignalKind.Started && _aiSourceInstanceId != 0
                && notification.execution.sourceInstanceId == _aiSourceInstanceId)
            {
                _aiInstanceId = notification.execution.instanceId;
                _aiSourceInstanceId = _aiInstanceId;
            }
            if (notification.execution.instanceId != _aiInstanceId) { return; }
            if (notification.kind == ActionSignalKind.Completed || notification.kind == ActionSignalKind.Interrupted)
            {
                _aiInstanceId = 0;
                GameManager.Actions.Execution.CopyActive(_ownerHash, _aiExecutions);
                for (int i = 0; i < _aiExecutions.Count; i++)
                {
                    ActionExecution successor = _aiExecutions[i];
                    if (successor.sourceInstanceId != notification.execution.instanceId) { continue; }
                    _aiInstanceId = successor.instanceId;
                    _aiSourceInstanceId = successor.instanceId;
                    break;
                }
                CompleteAIIfSettled();
            }
        }

        public void CancelAction()
        {
            if (_instanceId != 0) { GameManager.Actions?.Execution.Cancel(_instanceId, true); }
            if (_sourceInstanceId != 0) { GameManager.Actions?.Execution.CancelPendingFrom(_sourceInstanceId); }
            Deactivate();
            _instanceId = 0;
            _sourceInstanceId = 0;
        }

        public void HandleExecution(ActionExecutionEvent notification, AttackMotionData motion, bool projectile)
        {
            if (notification.kind == ActionSignalKind.Started)
            {
                _instanceId = notification.execution.instanceId;
                _sourceInstanceId = _instanceId;
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
