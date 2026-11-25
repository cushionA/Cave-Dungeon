using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Game.Runtime
{
    public sealed partial class PlayerCharacterInput3D
    {
        private const float StickSelectThresholdSquared = 0.25f;
        private const float StickRearmThresholdSquared = 0.0625f;
        private readonly MultiLockTargeting3D _targeting3D = new MultiLockTargeting3D();
        private readonly List<int> _candidateHashes = new List<int>(64);
        private readonly List<ActorRef> _multiLockTargets = new List<ActorRef>(ActionTargetSelection.MaxTargets);
        private InputAction _look;
        private InputAction _multiLockAction;
        private ButtonControl _multiLockButton;
        private AttackInfo _multiLockDefinition;
        private ActorRef _multiLockOwner;
        private Vector3 _lastMarkPosition;
        private bool _stickCentered;
        private bool _multiLockReleasePending;

        internal Camera GameplayCamera => _playerInput != null && _playerInput.camera != null
            ? _playerInput.camera : Camera.main;

        internal IReadOnlyList<ActorRef> MultiLockTargets => _multiLockButton != null && IsMultiLockInputActive()
            ? _multiLockTargets : Array.Empty<ActorRef>();

        private void BindMultiLockLook(InputAction action)
        {
            if (_look != null)
            {
                _look.performed -= SelectFromStick;
                _look.canceled -= SelectFromStick;
            }
            _look = action;
            if (_look != null)
            {
                _look.performed += SelectFromStick;
                _look.canceled += SelectFromStick;
            }
        }

        private void BeginMultiLock(InputAction.CallbackContext context, AttackInfo definition)
        {
            if (_multiLockButton != null || context.control is not ButtonControl button || !button.isPressed
                || GameManager.Data.GetStatusEffects(_actor.Actor.Hash).BlocksActions) { return; }

            CancelMultiLock();
            _multiLockAction = context.action;
            _multiLockButton = button;
            _multiLockDefinition = definition;
            _multiLockOwner = _actor.Actor;
            _stickCentered = _look == null || _look.activeControl?.device != button.device
                || _look.ReadValue<Vector2>().sqrMagnitude <= StickRearmThresholdSquared;
            SelectMultiLockTarget();
        }

        private void ReleaseMultiLock(InputAction.CallbackContext context)
        {
            if (_multiLockButton == null || context.action != _multiLockAction) { return; }
            if (!IsMultiLockInputActive() || _multiLockButton.isPressed || _multiLockTargets.Count == 0)
            {
                CancelMultiLock();
                return;
            }

            _multiLockButton = null;
            _multiLockReleasePending = true;
        }

        private bool IsMultiLockInputActive()
        {
            return _multiLockAction != null && _multiLockAction.enabled
                && _playerInput.currentActionMap == _gameplayMap
                && (_multiLockButton == null || _multiLockButton.device.added)
                && _motor.isActiveAndEnabled && _actor != null && _actor.isActiveAndEnabled
                && _actor.IsAlive && _actor.Actor == _multiLockOwner && _multiLockDefinition != null
                && !GameManager.Data.GetStatusEffects(_multiLockOwner.Hash).BlocksActions;
        }

        private void CancelMultiLock()
        {
            _multiLockButton = null;
            _multiLockAction = null;
            _multiLockDefinition = null;
            _multiLockOwner = default;
            _multiLockReleasePending = false;
            _multiLockTargets.Clear();
        }

        private void SelectFromStick(InputAction.CallbackContext context)
        {
            if (_multiLockButton?.device is not Gamepad pad || context.control?.device != pad
                || !IsMultiLockInputActive()) { return; }

            Vector2 stick = context.ReadValue<Vector2>();
            float magnitude = stick.sqrMagnitude;
            if (magnitude <= StickRearmThresholdSquared) { _stickCentered = true; }
            if (!_stickCentered || magnitude < StickSelectThresholdSquared) { return; }

            _stickCentered = false;
            if (_multiLockTargets.Count < ActionTargetSelection.MaxTargets)
            {
                SelectMultiLockTarget(PlayerInputHandler.ScreenDirection(stick));
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

            Vector3 ownerCenter = _actor.Body.bounds.center;
            _candidateHashes.Clear();
            GameManager.Data.GetAllHashes(_candidateHashes);
            ActorRef nearest = default;
            Vector3 nearestWorld = default;
            float nearestDistance = float.PositiveInfinity;
            foreach (int hash in _candidateHashes)
            {
                if (HasMultiLockMark(hash) || !GameManager.Actors.TryGet(hash, out ActorRef candidate)
                    || !_targeting3D.TryGetCandidate(_actor, candidate, _multiLockDefinition, out Vector3 world)
                    || !PlayerInputHandler.TryProjectMark(camera, world, out Vector2 screen)) { continue; }

                Vector2 offset = screen - (Vector2)lastScreen;
                if (!first && (offset.sqrMagnitude < 0.01f || PlayerInputHandler.ScreenDirection(offset) != direction)) { continue; }
                float distance = first ? (world - ownerCenter).sqrMagnitude : offset.sqrMagnitude;
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

        internal static bool TryGetMarkPosition(ActorRef target, out Vector3 position)
        {
            position = default;
            if (!MultiLockTargeting3D.TryGetActor(target, out CharacterActor3D actor)) { return false; }

            position = actor.Body.bounds.center;
            return true;
        }
    }
}
