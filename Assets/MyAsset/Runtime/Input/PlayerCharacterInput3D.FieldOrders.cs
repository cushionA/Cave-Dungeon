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
        [SerializeField] private ExpeditionSummonDeployment3D explorationOrders;
        [SerializeField, Min(0.1f)] private float fieldOrderAimDistance = 20f;
        [SerializeField] private LayerMask fieldOrderRayMask = Physics.DefaultRaycastLayers;

        private InputAction _fieldOrderAction;
        private ActorRef _fieldOrderIssuer;
        private EncounterCombat3D _pressedEncounter;
        private ExpeditionSummonDeployment3D _pressedDeployment;
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
            CancelFieldOrderPress();
            if (TryGetFieldOrderReceivers(out _pressedEncounter, out _pressedDeployment)) { _fieldOrderIssuer = _actor.Actor; }
        }

        private void CancelFieldOrder(InputAction.CallbackContext context) => CancelFieldOrderPress();

        private void CancelFieldOrderPress()
        {
            _fieldOrderIssuer = default;
            _pressedEncounter = null;
            _pressedDeployment = null;
        }

        private bool TryGetFieldOrderReceivers(out EncounterCombat3D battle, out ExpeditionSummonDeployment3D deployment)
        {
            battle = null;
            deployment = null;
            if (_fieldOrderAction == null || !_fieldOrderAction.enabled || _playerInput.currentActionMap != _gameplayMap
                || _actor == null || !_actor.isActiveAndEnabled || !_actor.IsAlive) { return false; }

            EncounterCombat3D linkedEncounter = explorationOrders != null ? explorationOrders.OrderEncounter : null;
            if (encounterOrders != null && encounterOrders.CanReceivePlayerOrders(_actor)) { battle = encounterOrders; }
            else if (linkedEncounter != null && linkedEncounter.CanReceivePlayerOrders(_actor)) { battle = linkedEncounter; }
            else if (explorationOrders != null && explorationOrders.CanReceiveExplorationOrders(_actor)) { deployment = explorationOrders; }
            return battle != null || deployment != null;
        }

        // The shipped FieldOrder action performs on release and cancels if its map is disabled mid-press.
        private void ReleaseFieldOrder(InputAction.CallbackContext context)
        {
            ActorRef issuer = _fieldOrderIssuer;
            EncounterCombat3D pressedEncounter = _pressedEncounter;
            ExpeditionSummonDeployment3D pressedDeployment = _pressedDeployment;
            CancelFieldOrderPress();
            if (!issuer.IsValid || !TryGetFieldOrderReceivers(out EncounterCombat3D battle, out ExpeditionSummonDeployment3D deployment)
                || _actor.Actor != issuer || !ReferenceEquals(battle, pressedEncounter)
                || !ReferenceEquals(deployment, pressedDeployment)) { return; }

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
            if (battle != null) { battle.TryIssue(_actor, action, target, position, surface); }
            else { deployment.TryIssueExplorationOrder(_actor, action, target, position, surface, fieldOrderAimDistance); }
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
