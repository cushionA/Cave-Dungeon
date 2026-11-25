using Game.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Game.Runtime
{
    public sealed partial class PlayerCharacterInput3D
    {
        private const double k_DodgeTapSeconds = 0.25;

        private InputAction _sprint;
        private ButtonControl _dodgeButton;
        private ActorRef _dodgePressOwner;
        private double _dodgePressedAt;
        private InputDevice _pendingDodgeDevice;
        private ActorRef _pendingDodgeOwner;
        private Vector3 _pendingDodgeDirection;
        private ActorRef _activeDodgeOwner;
        private long _activeDodgeInstance;
        private int _dodgeInputRevision;

        private void BindDodge(InputAction action)
        {
            if (_sprint != null)
            {
                _sprint.performed -= BeginDodgePress;
                _sprint.canceled -= ReleaseDodgePress;
            }
            _sprint = action;
            if (_sprint != null)
            {
                _sprint.performed += BeginDodgePress;
                _sprint.canceled += ReleaseDodgePress;
            }
        }

        private void BeginDodgePress(InputAction.CallbackContext context)
        {
            if (context.control is not ButtonControl button || !button.isPressed || !TryGetDodgeOwner(out ActorRef owner)) { return; }
            _dodgeButton = button;
            _dodgePressOwner = owner;
            _dodgePressedAt = context.time;
        }

        private void ReleaseDodgePress(InputAction.CallbackContext context)
        {
            ButtonControl button = _dodgeButton;
            ActorRef pressedOwner = _dodgePressOwner;
            _dodgeButton = null;
            _dodgePressOwner = default;
            if (button == null || !ReferenceEquals(context.control, button) || !button.device.added || button.isPressed
                || !IsDodgeTap(context.time - _dodgePressedAt) || !TryGetDodgeOwner(out ActorRef owner) || owner != pressedOwner) { return; }

            _pendingDodgeDevice = button.device;
            _pendingDodgeOwner = owner;
            Vector3 direction = CameraRelativeDirection(_moveInput, GameplayCamera);
            _pendingDodgeDirection = direction == Vector3.zero ? _motor.Facing : direction;
        }

        internal static bool IsDodgeTap(double duration) => duration >= 0 && duration < k_DodgeTapSeconds;

        private bool TryGetDodgeOwner(out ActorRef owner)
        {
            owner = default;
            if (!isActiveAndEnabled || !_playerInput.isActiveAndEnabled || _sprint == null || !_sprint.enabled
                || _playerInput.currentActionMap != _gameplayMap || _actor == null || !_actor.isActiveAndEnabled
                || !_actor.IsAlive || !_motor.isActiveAndEnabled || !_actor.Body.enabled) { return false; }

            owner = _actor.Actor;
            return owner.IsValid;
        }

        private void TryStartDodge(GameActionRuntime runtime)
        {
            ActorRef requestedOwner = _pendingDodgeOwner;
            InputDevice device = _pendingDodgeDevice;
            Vector3 direction = _pendingDodgeDirection;
            _pendingDodgeOwner = default;
            _pendingDodgeDevice = null;
            if (!requestedOwner.IsValid || device == null || !device.added || runtime == null || !_motor.IsGrounded
                || !TryGetDodgeOwner(out ActorRef owner) || owner != requestedOwner) { return; }

            Vector2 planar = new Vector2(direction.x, direction.z);
            int revision = _dodgeInputRevision;
            if (planar.sqrMagnitude <= 0.0001f || !runtime.TryDodge(owner.Hash, planar, out long instance) || instance == 0) { return; }
            if (revision != _dodgeInputRevision || !TryGetDodgeOwner(out ActorRef acceptedOwner) || acceptedOwner != owner)
            {
                runtime.CancelDodge3D(owner, instance);
                return;
            }

            _activeDodgeOwner = owner;
            _activeDodgeInstance = instance;
            ClearPendingAttack();
            CancelMultiLock();
        }

        private bool TryGetDodgeMovement(GameActionRuntime runtime, out Vector3 velocity)
        {
            velocity = default;
            if (_activeDodgeInstance == 0) { return false; }
            if (runtime != null && TryGetDodgeOwner(out ActorRef owner) && owner == _activeDodgeOwner
                && runtime.TryGetDodge3DMovement(owner, _activeDodgeInstance, out velocity)) { return true; }

            CancelActiveDodge();
            return false;
        }

        private void ClearDodgeInput()
        {
            _dodgeInputRevision++;
            _dodgeButton = null;
            _dodgePressOwner = default;
            _pendingDodgeDevice = null;
            _pendingDodgeOwner = default;
            CancelActiveDodge();
        }

        private void CancelActiveDodge()
        {
            long instance = _activeDodgeInstance;
            ActorRef owner = _activeDodgeOwner;
            _activeDodgeInstance = 0;
            _activeDodgeOwner = default;
            if (instance != 0) { GameManager.Actions?.CancelDodge3D(owner, instance); }
        }
    }
}
