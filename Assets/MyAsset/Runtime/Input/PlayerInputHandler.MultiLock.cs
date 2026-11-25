using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Game.Runtime
{
    public partial class PlayerInputHandler
    {
        private const float StickSelectThresholdSquared = 0.25f;
        private const float StickRearmThresholdSquared = 0.0625f;

        private readonly MultiLockTargeting _multiLockTargeting = new MultiLockTargeting();
        private readonly List<int> _multiLockHashes = new List<int>(64);
        private readonly List<ActorRef> _multiLockTargets = new List<ActorRef>(MultiLockTargeting.MaxTargets);
        private InputAction _skillAction;
        private InputAction _multiLockLook;
        private InputAction _multiLockAction;
        private ButtonControl _multiLockControl;
        private PlayerCharacter.MultiLockAttack _multiLockAttack;
        private ActorRef _multiLockIssuer;
        private Vector3 _lastMarkPosition;
        private bool _multiLockStickCentered;
        private bool _multiLockReleasePending;
        private bool _suppressLightAttack;
        private bool _suppressHeavyAttack;

        internal Camera GameplayCamera => _playerInput != null && _playerInput.camera != null
            ? _playerInput.camera : Camera.main;

        internal IReadOnlyList<ActorRef> MultiLockTargets => _multiLockControl != null && MultiLockInputActive()
            ? _multiLockTargets : Array.Empty<ActorRef>();

        private void BindMultiLock()
        {
            _skillAction = _playerInput.actions?.FindAction("Player/Skill");
            _multiLockLook = _playerInput.actions?.FindAction("Player/Look");
            BindMultiLockButton(_attackAction);
            BindMultiLockButton(_heavyAttackAction);
            BindMultiLockButton(_skillAction);
            if (_multiLockLook != null)
            {
                _multiLockLook.performed += ChangeMultiLockDirection;
                _multiLockLook.canceled += ChangeMultiLockDirection;
            }
            InputSystem.onActionChange += HandleMultiLockActionChange;
        }

        private void UnbindMultiLock()
        {
            CancelMultiLock();
            _suppressLightAttack = _suppressHeavyAttack = false;
            UnbindMultiLockButton(_attackAction);
            UnbindMultiLockButton(_heavyAttackAction);
            UnbindMultiLockButton(_skillAction);
            if (_multiLockLook != null)
            {
                _multiLockLook.performed -= ChangeMultiLockDirection;
                _multiLockLook.canceled -= ChangeMultiLockDirection;
            }
            _skillAction = null;
            _multiLockLook = null;
            InputSystem.onActionChange -= HandleMultiLockActionChange;
        }

        private void BindMultiLockButton(InputAction action)
        {
            if (action == null) { return; }
            action.performed += BeginMultiLock;
            action.canceled += ReleaseMultiLock;
        }

        private void UnbindMultiLockButton(InputAction action)
        {
            if (action == null) { return; }
            action.performed -= BeginMultiLock;
            action.canceled -= ReleaseMultiLock;
        }

        private bool HasMultiLockSkill() => _character is PlayerCharacter player
            && player.TryGetMultiLockAttack(AttackButtonId.Skill, out _);

        private void BeginMultiLock(InputAction.CallbackContext context)
        {
            if (_overrideActive || context.control is not ButtonControl control || !control.isPressed
                || _playerInput.currentActionMap != context.action.actionMap
                || !(_character is PlayerCharacter player)) { return; }

            AttackButtonId button = context.action == _attackAction ? AttackButtonId.Light
                : context.action == _heavyAttackAction ? AttackButtonId.Heavy : AttackButtonId.Skill;
            if (!player.TryGetMultiLockAttack(button, out PlayerCharacter.MultiLockAttack attack)) { return; }
            if (button == AttackButtonId.Light) { _suppressLightAttack = true; }
            if (button == AttackButtonId.Heavy) { _suppressHeavyAttack = true; }

            if (_multiLockControl != null || !TryGetIssuer(out ActorRef issuer)
                || GameManager.Data.GetStatusEffects(issuer.Hash).BlocksActions) { return; }

            CancelMultiLock();
            _chargeHandler.CancelCharge();
            _isHoldingButton = false;
            _attackBuffer = 0f;
            _multiLockAction = context.action;
            _multiLockControl = control;
            _multiLockAttack = attack;
            _multiLockIssuer = issuer;
            _multiLockStickCentered = _multiLockLook == null
                || _multiLockLook.ReadValue<Vector2>().sqrMagnitude <= StickRearmThresholdSquared;
            SelectMultiLockTarget();
        }

        private void ReleaseMultiLock(InputAction.CallbackContext context)
        {
            if (_multiLockControl == null || context.action != _multiLockAction) { return; }

            // Disabling or rebinding can cancel an action while its physical button is still down.
            if (!MultiLockInputActive() || _multiLockControl.isPressed || _multiLockTargets.Count == 0)
            {
                CancelMultiLock();
                return;
            }

            _multiLockControl = null;
            _multiLockReleasePending = true;
            AdvanceSampling(Time.time);
            BufferAttack(_multiLockAttack.Button);
        }

        private void BufferAttack(AttackButtonId button)
        {
            _chargeHandler.CancelCharge();
            _isHoldingButton = false;
            _attackBuffer = _inputBufferDuration;
            _bufferedAttackButtonId = button;
            _bufferedIsCharging = false;
            _bufferedChargeMultiplier = 1f;
        }

        private void HandleMultiLockActionChange(object changed, InputActionChange change)
        {
            if (_multiLockAction == null) { return; }
            if ((change == InputActionChange.ActionDisabled && ReferenceEquals(changed, _multiLockAction))
                || (change == InputActionChange.ActionMapDisabled && ReferenceEquals(changed, _multiLockAction.actionMap)))
            {
                CancelMultiLock();
            }
        }

        internal void CancelMultiLock()
        {
            if (_multiLockReleasePending && _bufferedAttackButtonId == _multiLockAttack.Button)
            {
                _attackBuffer = 0f;
                _currentInput.attackInput = null;
                _currentInput.chargeMultiplier = 1f;
            }
            _multiLockReleasePending = false;
            _multiLockTargets.Clear();
            _multiLockAction = null;
            _multiLockControl = null;
            _multiLockAttack = default;
            _multiLockIssuer = default;
        }

        private bool MultiLockInputActive() => !_overrideActive && _multiLockAction != null
            && _multiLockAction.enabled && _playerInput.currentActionMap == _multiLockAction.actionMap
            && (_multiLockControl == null || _multiLockControl.device.added)
            && _multiLockAttack.Definition != null && TryGetIssuer(out ActorRef issuer) && issuer == _multiLockIssuer
            && !GameManager.Data.GetStatusEffects(issuer.Hash).BlocksActions;

        private void ValidateMultiLock()
        {
            if (_multiLockAction != null && (!MultiLockInputActive()
                || (_multiLockReleasePending && (_attackBuffer <= 0f || _bufferedAttackButtonId != _multiLockAttack.Button))))
            {
                CancelMultiLock();
            }
        }

        internal bool TryGetMultiLockCast(out PlayerCharacter.MultiLockAttack attack, out IReadOnlyList<ActorRef> targets)
        {
            attack = _multiLockAttack;
            targets = _multiLockTargets;
            return _multiLockReleasePending && MultiLockInputActive();
        }

        private void ChangeMultiLockDirection(InputAction.CallbackContext context)
        {
            if (_multiLockControl?.device is not Gamepad gamepad || context.control.device != gamepad
                || !MultiLockInputActive()) { return; }

            Vector2 stick = context.ReadValue<Vector2>();
            float magnitude = stick.sqrMagnitude;
            if (magnitude <= StickRearmThresholdSquared) { _multiLockStickCentered = true; }
            if (!_multiLockStickCentered || magnitude < StickSelectThresholdSquared) { return; }

            _multiLockStickCentered = false;
            if (_multiLockTargets.Count < MultiLockTargeting.MaxTargets)
            {
                SelectMultiLockTarget(ScreenDirection(stick));
            }
        }

        private void SelectMultiLockTarget(int direction = -1)
        {
            Camera camera = GameplayCamera;
            if (camera == null) { return; }

            bool first = _multiLockTargets.Count == 0;
            if (!first && TryGetMarkPosition(_multiLockTargets[_multiLockTargets.Count - 1], out Vector3 last))
            {
                _lastMarkPosition = last;
            }
            Vector3 lastScreen = first ? default : camera.WorldToScreenPoint(_lastMarkPosition);
            if (!first && lastScreen.z <= 0f) { return; }
            float range = MultiLockTargeting.GetRange(_multiLockAttack.Definition);
            Vector2 issuerPosition = _character.TargetingCenter;
            _multiLockHashes.Clear();
            GameManager.Data.GetAllHashes(_multiLockHashes);
            ActorRef nearest = default;
            Vector3 nearestWorld = default;
            float nearestDistance = float.PositiveInfinity;
            foreach (int hash in _multiLockHashes)
            {
                if (HasMultiLockMark(hash)
                    || !GameManager.Actors.TryGet(hash, out ActorRef candidate)
                    || !TryGetMarkPosition(candidate, out Vector3 world)
                    || !TryProjectMark(camera, world, out Vector2 screen)
                    || !_multiLockTargeting.TryGetCandidate(_multiLockIssuer, candidate, range, out Vector2 position))
                {
                    continue;
                }

                Vector2 offset = screen - (Vector2)lastScreen;
                if (!first && (offset.sqrMagnitude < 0.01f || ScreenDirection(offset) != direction)) { continue; }
                float distance = first ? (position - issuerPosition).sqrMagnitude : offset.sqrMagnitude;
                if (distance > nearestDistance || (distance == nearestDistance && hash >= nearest.Hash)) { continue; }

                nearest = candidate;
                nearestWorld = world;
                nearestDistance = distance;
            }
            if (nearest.IsValid)
            {
                _multiLockTargets.Add(nearest);
                _lastMarkPosition = nearestWorld;
            }
        }

        private bool HasMultiLockMark(int hash)
        {
            foreach (ActorRef mark in _multiLockTargets)
            {
                if (mark.Hash == hash) { return true; }
            }
            return false;
        }

        internal static int ScreenDirection(Vector2 vector)
        {
            float angle = Mathf.Atan2(vector.y, vector.x) * Mathf.Rad2Deg;
            return Mathf.FloorToInt((angle + 22.5f) / 45f) & 7;
        }

        internal static bool TryGetMarkPosition(ActorRef target, out Vector3 position)
        {
            position = default;
            if (!MultiLockTargeting.TryGetActor(target, out BaseCharacter actor)) { return false; }
            Vector2 center = actor.TargetingCenter;
            position = new Vector3(center.x, center.y, actor.transform.position.z);
            return true;
        }

        internal static bool TryProjectMark(Camera camera, Vector3 world, out Vector2 screen)
        {
            Vector3 projected = camera.WorldToScreenPoint(world);
            screen = projected;
            return projected.z > 0f && camera.pixelRect.Contains(screen);
        }
    }
}
