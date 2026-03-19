using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// Dispatches ActionSlot execution to registered ActionBase handlers.
    /// Manages the current action lifecycle: execute, tick, cancel, complete.
    /// </summary>
    public class ActionExecutor
    {
        private Dictionary<ActionExecType, ActionBase> _handlers;
        private ActionBase _currentAction;

        public ActionBase CurrentAction => _currentAction;
        public bool IsExecuting => _currentAction != null && _currentAction.IsExecuting;

        public event Action OnActionCompleted;

        public ActionExecutor()
        {
            _handlers = new Dictionary<ActionExecType, ActionBase>();
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
            if (!_handlers.TryGetValue(slot.execType, out ActionBase handler))
            {
                return false;
            }

            if (_currentAction != null && _currentAction.IsExecuting)
            {
                _currentAction.Cancel();
            }

            _currentAction = handler;
            handler.OnCompleted += HandleActionCompleted;
            handler.Execute(ownerHash, targetHash, slot);
            return true;
        }

        /// <summary>
        /// Cancels the currently executing action if any.
        /// </summary>
        public void CancelCurrent()
        {
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
            return _handlers.ContainsKey(type);
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
    }
}
