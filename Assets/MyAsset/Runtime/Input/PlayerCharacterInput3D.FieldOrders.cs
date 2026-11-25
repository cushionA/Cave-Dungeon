using System;
using Game.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Game.Runtime
{
    public sealed partial class PlayerCharacterInput3D
    {
        private const double FieldOrderHoldSeconds = 0.35;
        private const float FieldOrderStickThresholdSquared = 0.04f;

        private static readonly BroadcastAction[] FieldOrderSlots =
        {
            BroadcastAction.Rally,
            BroadcastAction.FocusFire,
            BroadcastAction.DefendPoint,
            BroadcastAction.ReleaseOrder,
        };

        [Header("現場指示")]
        [SerializeField] private EncounterCombat3D encounterOrders;
        [SerializeField, Min(0.1f)] private float fieldOrderAimDistance = 20f;
        [SerializeField] private LayerMask fieldOrderRayMask = Physics.DefaultRaycastLayers;

        private InputAction _fieldOrderAction;
        private ActorRef _fieldOrderIssuer;
        private int _fieldOrderSlot;
        private RaycastHit[] _fieldOrderHits = new RaycastHit[16];

        internal BroadcastAction ActiveFieldOrder => FieldOrderSlots[_fieldOrderSlot];

        private void BindFieldOrder(InputAction action)
        {
            if (_fieldOrderAction != null)
            {
                _fieldOrderAction.started -= BeginFieldOrder;
                _fieldOrderAction.performed -= ReleaseFieldOrder;
                _fieldOrderAction.canceled -= CancelFieldOrder;
            }
            _fieldOrderAction = action;
            if (_fieldOrderAction != null)
            {
                _fieldOrderAction.started += BeginFieldOrder;
                _fieldOrderAction.performed += ReleaseFieldOrder;
                _fieldOrderAction.canceled += CancelFieldOrder;
            }
        }

        private void BeginFieldOrder(InputAction.CallbackContext context)
        {
            _fieldOrderIssuer = FieldOrderInputActive() ? _actor.Actor : default;
        }

        private void CancelFieldOrder(InputAction.CallbackContext context) => CancelFieldOrderPress();

        private void CancelFieldOrderPress() => _fieldOrderIssuer = default;

        private bool FieldOrderInputActive() => _fieldOrderAction != null && _fieldOrderAction.enabled
            && _playerInput.currentActionMap == _gameplayMap && encounterOrders != null && encounterOrders.isActiveAndEnabled
            && _actor != null && _actor.isActiveAndEnabled && _actor.IsAlive;

        // The shipped FieldOrder action performs on release and cancels if its map is disabled mid-press.
        private void ReleaseFieldOrder(InputAction.CallbackContext context)
        {
            ActorRef issuer = _fieldOrderIssuer;
            CancelFieldOrderPress();
            if (!issuer.IsValid || !FieldOrderInputActive() || _actor.Actor != issuer) { return; }

            if (context.duration >= FieldOrderHoldSeconds)
            {
                _fieldOrderSlot = (_fieldOrderSlot + 1) % FieldOrderSlots.Length;
                return;
            }

            BroadcastAction action = ActiveFieldOrder;
            ActorRef target = default;
            Vector3? position = null;
            Collider surface = null;
            if (FieldOrderCatalog.RequiresTarget(action) || FieldOrderCatalog.RequiresPosition(action))
            {
                if (!TryAimFieldOrder(context.control?.device, out RaycastHit hit)) { return; }
                CharacterActor3D actor = hit.collider.GetComponentInParent<CharacterActor3D>();
                if (FieldOrderCatalog.RequiresTarget(action))
                {
                    if (actor == null || !actor.IsAlive) { return; }
                    target = actor.Actor;
                }
                else
                {
                    if (actor != null || hit.normal.y < 0.5f) { return; }
                    position = hit.point;
                    surface = hit.collider;
                }
            }
            encounterOrders.TryIssue(_actor, action, target, position, surface);
        }

        private bool TryAimFieldOrder(InputDevice device, out RaycastHit closest)
        {
            closest = default;
            Camera camera = GameplayCamera;
            if (camera == null || !TryGetFieldOrderAim(camera, device, out Ray ray)) { return false; }

            // The ray starts at the camera. Check the resulting surface against the knight's own order range.
            PhysicsScene world = _actor.gameObject.scene.GetPhysicsScene();
            Vector3 issuerCenter = _actor.Body.bounds.center;
            float rayDistance = fieldOrderAimDistance + Vector3.Distance(ray.origin, issuerCenter);
            int count;
            while (true)
            {
                count = world.Raycast(ray.origin, ray.direction, _fieldOrderHits, rayDistance,
                    fieldOrderRayMask, QueryTriggerInteraction.Ignore);
                if (count < _fieldOrderHits.Length) { break; }
                Array.Resize(ref _fieldOrderHits, _fieldOrderHits.Length * 2);
            }
            float closestDistance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _fieldOrderHits[i];
                if (hit.distance >= closestDistance || hit.collider.GetComponentInParent<CharacterActor3D>() == _actor) { continue; }
                closest = hit;
                closestDistance = hit.distance;
            }
            return closest.collider != null && (closest.point - issuerCenter).sqrMagnitude <= fieldOrderAimDistance * fieldOrderAimDistance;
        }

        private bool TryGetFieldOrderAim(Camera camera, InputDevice device, out Ray ray)
        {
            ray = default;
            if (device is Gamepad gamepad)
            {
                Vector2 stick = _look != null && _look.activeControl?.device == gamepad
                    ? _look.ReadValue<Vector2>() : Vector2.zero;
                if (stick.sqrMagnitude < FieldOrderStickThresholdSquared) { return false; }
                ray = camera.ViewportPointToRay(Vector2.one * 0.5f + stick * 0.5f);
                return true;
            }

            foreach (InputDevice paired in _playerInput.devices)
            {
                if (paired is not Pointer pointer) { continue; }
                Vector2 screen = pointer.position.ReadValue();
                if (!camera.pixelRect.Contains(screen)) { return false; }
                ray = camera.ScreenPointToRay(screen);
                return true;
            }
            return false;
        }
    }
}
