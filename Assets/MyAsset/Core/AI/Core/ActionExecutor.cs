using System;
using System.Collections.Generic;

namespace Game.Core
{
    public interface IActionExecutionBackend
    {
        bool IsExecuting { get; }
        bool Execute(int ownerHash, int targetHash, ActionSlot slot);
        void CancelCurrent();
        void Tick(float deltaTime);
        event Action OnActionCompleted;
    }

    /// <summary>
    /// Dispatches ActionSlot execution to registered ActionBase handlers.
    /// Manages the current action lifecycle: execute, tick, cancel, complete.
    /// </summary>
    public class ActionExecutor
    {
        private Dictionary<ActionExecType, ActionBase> _handlers;
        private ActionBase _currentAction;
        private IActionExecutionBackend _backend;
        private BroadcastActionHandler _fieldOrders;

        public ActionBase CurrentAction => _currentAction;
        public bool IsExecuting => _backend != null ? _backend.IsExecuting : _currentAction != null && _currentAction.IsExecuting;
        public ActionDispatchStatus LastDispatchStatus { get; private set; }

        public event Action OnActionCompleted;

        public ActionExecutor()
        {
            _handlers = new Dictionary<ActionExecType, ActionBase>();
        }

        public void SetExecutionBackend(IActionExecutionBackend backend)
        {
            if (ReferenceEquals(_backend, backend)) { return; }
            if (_backend != null) { _backend.OnActionCompleted -= HandleBackendCompleted; }
            CancelCurrent();
            _backend = backend;
            if (_backend != null) { _backend.OnActionCompleted += HandleBackendCompleted; }
        }

        /// <summary>
        /// Registers an ActionBase handler for its ExecType.
        /// Overwrites any existing handler for the same type.
        /// </summary>
        public void Register(ActionBase handler)
        {
            _handlers[handler.ExecType] = handler;
            if (handler.ExecType == ActionExecType.Broadcast) { _fieldOrders = handler as BroadcastActionHandler; }
        }

        /// <summary>
        /// Executes the action slot by dispatching to the registered handler.
        /// Cancels any currently executing action first.
        /// Returns false if no handler is registered for the slot's exec type.
        /// </summary>
        public bool Execute(int ownerHash, int targetHash, ActionSlot slot)
        {
            if (slot.execType == ActionExecType.Broadcast && _fieldOrders != null)
            {
                bool wasExecuting = IsExecuting;
                return FinishFieldOrder(_fieldOrders.TryIssue(ownerHash, targetHash, slot.paramId), wasExecuting);
            }
            if (ActionCommand.IsGimmickInteraction(slot))
            {
                // The bare slot has no sampled object identity, even if a legacy Instant handler exists.
                LastDispatchStatus = ActionDispatchStatus.InvalidParameters;
                return false;
            }
            if (_backend is IActionCommandBackend commandBackend)
            {
                ActionCommand command = ActionCommand.FromLegacySlot(slot);
                return Dispatch(commandBackend, ownerHash, targetHash, command);
            }
            if (_backend != null)
            {
                bool accepted = _backend.Execute(ownerHash, targetHash, slot);
                LastDispatchStatus = accepted ? ActionDispatchStatus.Accepted : ActionDispatchStatus.Rejected;
                return accepted;
            }
            if (!_handlers.TryGetValue(slot.execType, out ActionBase handler))
            {
                LastDispatchStatus = ActionDispatchStatus.Unsupported;
                return false;
            }

            if (_currentAction != null && _currentAction.IsExecuting)
            {
                _currentAction.OnCompleted -= HandleActionCompleted;
                _currentAction.Cancel();
            }

            _currentAction = handler;
            handler.OnCompleted += HandleActionCompleted;
            handler.Execute(ownerHash, targetHash, slot);
            LastDispatchStatus = ActionDispatchStatus.Accepted;
            return true;
        }

        internal bool ExecuteFieldOrder(in AIDecisionResult decision, int id, UnityEngine.Vector2? point)
        {
            bool wasExecuting = IsExecuting;
            return FinishFieldOrder(_fieldOrders != null && _fieldOrders.TryIssue(decision.owner, decision.target, id, point), wasExecuting);
        }

        private bool FinishFieldOrder(bool accepted, bool wasExecuting)
        {
            LastDispatchStatus = accepted ? ActionDispatchStatus.Accepted : ActionDispatchStatus.Rejected;
            if (accepted && !wasExecuting && !IsExecuting) { OnActionCompleted?.Invoke(); }
            return accepted;
        }

        internal bool ExecuteInteraction(in AIDecisionResult decision)
        {
            if (!decision.owner.IsValid || !decision.target.IsValid || decision.target.Hash != decision.targetHash
                || !decision.gimmick.IsValid)
            {
                LastDispatchStatus = ActionDispatchStatus.InvalidParameters;
                return false;
            }
            if (!(_backend is IActionCommandBackend backend) || !backend.Supports(ActionCommandKind.InteractObject))
            {
                LastDispatchStatus = ActionDispatchStatus.Unsupported;
                return false;
            }
            ActionCommand command = new ActionCommand
            {
                kind = ActionCommandKind.InteractObject,
                gimmick = decision.gimmick,
                gimmickOwner = decision.owner,
                gimmickTarget = decision.target,
                observationTick = decision.observationTick,
            };
            return Dispatch(backend, decision.owner.Hash, decision.target.Hash, command);
        }

        private bool Dispatch(IActionCommandBackend backend, int ownerHash, int targetHash, in ActionCommand command)
        {
            ActionDispatchResult result = backend.ExecuteCommand(ownerHash, targetHash, command);
            LastDispatchStatus = result.Status;
            return result.Accepted;
        }

        /// <summary>
        /// Cancels the currently executing action if any.
        /// </summary>
        public void CancelCurrent()
        {
            if (_backend != null) { _backend.CancelCurrent(); return; }
            if (_currentAction != null && _currentAction.IsExecuting)
            {
                _currentAction.OnCompleted -= HandleActionCompleted;
                _currentAction.Cancel();
                _currentAction = null;
            }
        }

        /// <summary>
        /// Ticks the current action for sustained execution.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (_backend != null) { _backend.Tick(deltaTime); return; }
            if (_currentAction != null && _currentAction.IsExecuting)
            {
                _currentAction.Tick(deltaTime);
            }
        }

        /// <summary>
        /// Returns true if a handler is registered for the given exec type.
        /// </summary>
        public bool HasHandler(ActionExecType type)
        {
            if (_backend is IActionCommandBackend backend)
            {
                switch (type)
                {
                    case ActionExecType.Broadcast: return _fieldOrders != null;
                    case ActionExecType.Attack: return backend.Supports(ActionCommandKind.Attack);
                    case ActionExecType.Cast: return backend.Supports(ActionCommandKind.Cast);
                    case ActionExecType.Instant: return backend.Supports(ActionCommandKind.Dodge)
                        || backend.Supports(ActionCommandKind.InteractObject);
                    case ActionExecType.Sustained: return backend.Supports(ActionCommandKind.Guard)
                        || backend.Supports(ActionCommandKind.MoveToTarget) || backend.Supports(ActionCommandKind.Idle);
                    default: return false;
                }
            }
            return _handlers.ContainsKey(type);
        }

        /// <summary>本番backendがある時は旧Handler登録ではなく、同じ要求変換から対応可否を照会する。</summary>
        public bool CanExecute(in ActionSlot slot)
        {
            if (slot.execType == ActionExecType.Broadcast && _fieldOrders != null) { return _fieldOrders.Supports(slot.paramId); }
            return _backend is IActionCommandBackend backend
                ? backend.Supports(ActionCommand.FromLegacySlot(slot).kind)
                : !ActionCommand.IsGimmickInteraction(slot) && HasHandler(slot.execType);
        }

        private void HandleActionCompleted()
        {
            if (_currentAction != null)
            {
                _currentAction.OnCompleted -= HandleActionCompleted;
            }
            _currentAction = null;
            OnActionCompleted?.Invoke();
        }

        private void HandleBackendCompleted() => OnActionCompleted?.Invoke();
    }
}
