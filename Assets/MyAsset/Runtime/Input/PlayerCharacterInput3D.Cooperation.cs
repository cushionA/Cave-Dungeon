using Game.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Runtime
{
    public sealed partial class PlayerCharacterInput3D
    {
        [SerializeField] private CompanionController3D companion;

        private InputAction _cooperationAction;
        private ActorRef _pendingCooperation;

        private void BindCooperation(InputAction action)
        {
            if (_cooperationAction != null) { _cooperationAction.performed -= QueueCooperation; }
            _cooperationAction = action;
            if (_cooperationAction != null) { _cooperationAction.performed += QueueCooperation; }
        }

        private void QueueCooperation(InputAction.CallbackContext context)
        {
            if (TryGetCooperationRequester(out ActorRef requester)) { _pendingCooperation = requester; }
        }

        internal void ClearPendingCooperation() => _pendingCooperation = default;

        private void ConsumeCooperation()
        {
            if (!_pendingCooperation.IsValid) { return; }
            ActorRef pressed = _pendingCooperation;
            _pendingCooperation = default;
            if (TryGetCooperationRequester(out ActorRef requester) && requester == pressed)
            {
                companion.TryCooperate(_actor);
            }
        }

        private bool TryGetCooperationRequester(out ActorRef requester)
        {
            requester = default;
            if (!isActiveAndEnabled || _cooperationAction == null || !_cooperationAction.enabled
                || _playerInput.currentActionMap != _gameplayMap || companion == null || !companion.isActiveAndEnabled
                || _actor == null || !_actor.isActiveAndEnabled || !_actor.IsAlive) { return false; }
            requester = _actor.Actor;
            return !GameManager.Data.GetStatusEffects(requester.Hash).BlocksActions;
        }
    }
}
