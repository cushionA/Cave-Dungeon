using System;
using Game.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Runtime
{
    public sealed partial class PlayerCharacterInput3D
    {
        [Header("NPCとの会話")]
        [SerializeField] private NpcInteraction3D[] interactionTargets = Array.Empty<NpcInteraction3D>();

        private InputAction _interactAction;
        private NpcInteraction3D _heldInteractionTarget;
        private ActorRef _heldInteractionOwner;
        private NpcInteraction3D _pendingInteractionTarget;
        private ActorRef _pendingInteractionOwner;

        private void BindInteraction(InputAction action)
        {
            if (_interactAction != null)
            {
                _interactAction.started -= BeginInteraction;
                _interactAction.performed -= QueueInteraction;
                _interactAction.canceled -= CancelInteractionPress;
            }
            _interactAction = action;
            if (_interactAction != null)
            {
                _interactAction.started += BeginInteraction;
                _interactAction.performed += QueueInteraction;
                _interactAction.canceled += CancelInteractionPress;
            }
        }

        private void BeginInteraction(InputAction.CallbackContext context)
        {
            _heldInteractionTarget = null;
            _heldInteractionOwner = default;
            if (!TryGetInteractionOwner(out ActorRef owner)) { return; }

            float nearestDistance = float.PositiveInfinity;
            foreach (NpcInteraction3D target in interactionTargets ?? Array.Empty<NpcInteraction3D>())
            {
                if (target == null || !target.TryGetDistanceSquared(_actor, out float distance)
                    || distance >= nearestDistance) { continue; }
                nearestDistance = distance;
                _heldInteractionTarget = target;
                _heldInteractionOwner = owner;
            }
            if (_actor.TryGetComponent(out ExpeditionFastTravel3D fastTravel)
                && fastTravel.TryFindInteraction(nearestDistance, out NpcInteraction3D nearbyCheckpoint))
            {
                _heldInteractionTarget = nearbyCheckpoint;
                _heldInteractionOwner = owner;
            }
        }

        private void QueueInteraction(InputAction.CallbackContext context)
        {
            if (_heldInteractionTarget == null || !TryGetInteractionOwner(out ActorRef owner)
                || owner != _heldInteractionOwner || !_heldInteractionTarget.TryGetDistanceSquared(_actor, out _))
            {
                return;
            }
            _pendingInteractionTarget = _heldInteractionTarget;
            _pendingInteractionOwner = owner;
        }

        private void CancelInteractionPress(InputAction.CallbackContext context)
        {
            _heldInteractionTarget = null;
            _heldInteractionOwner = default;
        }

        private void ClearInteraction()
        {
            _heldInteractionTarget = null;
            _heldInteractionOwner = default;
            _pendingInteractionTarget = null;
            _pendingInteractionOwner = default;
        }

        private bool ConsumeInteraction()
        {
            NpcInteraction3D target = _pendingInteractionTarget;
            ActorRef pressedOwner = _pendingInteractionOwner;
            _pendingInteractionTarget = null;
            _pendingInteractionOwner = default;
            return target != null && TryGetInteractionOwner(out ActorRef owner) && owner == pressedOwner
                && target.TryInteract(_actor);
        }

        private bool TryGetInteractionOwner(out ActorRef owner)
        {
            owner = default;
            if (_interactAction == null || !_interactAction.enabled || _playerInput.currentActionMap != _gameplayMap
                || _actor == null || !_actor.isActiveAndEnabled || !_actor.IsAlive) { return false; }
            owner = _actor.Actor;
            return !GameManager.Data.GetStatusEffects(owner.Hash).BlocksActions;
        }
    }
}
