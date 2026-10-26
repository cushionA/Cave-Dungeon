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
        }

        /// <summary>
        /// Executes the action slot by dispatching to the registered handler.
        /// Cancels any currently executing action first.
        /// Returns false if no handler is registered for the slot's exec type.
        /// </summary>
        public bool Execute(int ownerHash, int targetHash, ActionSlot slot)
        {
            if (_backend is IActionCommandBackend commandBackend)
            {
                ActionCommand command = ActionCommand.FromLegacySlot(slot);
                ActionDispatchResult result = commandBackend.ExecuteCommand(ownerHash, targetHash, command);
                LastDispatchStatus = result.Status;
                return result.Accepted;
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
                    case ActionExecType.Attack: return backend.Supports(ActionCommandKind.Attack);
                    case ActionExecType.Cast: return backend.Supports(ActionCommandKind.Cast);
                    case ActionExecType.Instant: return backend.Supports(ActionCommandKind.Dodge);
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
            return _backend is IActionCommandBackend backend
                ? backend.Supports(ActionCommand.FromLegacySlot(slot).kind) : HasHandler(slot.execType);
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
