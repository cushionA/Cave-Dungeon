using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Game.Runtime
{
    public partial class PlayerInputHandler
    {
        private const float GroundPickTolerance = 0.35f;
        private static readonly BroadcastAction[] DefaultFieldOrders =
        {
            BroadcastAction.Rally, BroadcastAction.FocusFire, BroadcastAction.DefendPoint, BroadcastAction.ReleaseOrder
        };

        [Header("現場指示")]
        [Tooltip("短押しで選択中の指示を出し、長押しして離すと次の枠に切り替えます。")]
        [SerializeField] private BroadcastAction[] _fieldOrderSlots = (BroadcastAction[])DefaultFieldOrders.Clone();
        [SerializeField, Min(0.1f)] private float _fieldOrderHoldSeconds = 0.35f;
        [SerializeField, Min(0.1f)] private float _fieldOrderAimDistance = 20f;
        [SerializeField] private LayerMask _fieldOrderGround = 1 << 6;

        private readonly List<RaycastHit2D> _orderHits = new List<RaycastHit2D>(8);
        private Collider2D _orderBody;
        private InputAction _fieldOrderAction;
        private InputAction _fieldOrderLook;
        private bool _fieldOrderPressed;
        private ActorRef _pressedIssuer;
        private string _fieldOrderHint;

        public int ActiveFieldOrderSlot { get; private set; }
        public BroadcastAction ActiveFieldOrder => _fieldOrderSlots[ActiveFieldOrderSlot];
        private bool FieldOrderInputActive => !_overrideActive && _fieldOrderAction != null && _fieldOrderAction.enabled
            && _playerInput.currentActionMap == _fieldOrderAction.actionMap;
        public string FieldOrderHint => isActiveAndEnabled && FieldOrderInputActive ? _fieldOrderHint : null;

        public bool TryAssignFieldOrder(int slot, BroadcastAction action)
        {
            if ((uint)slot >= _fieldOrderSlots.Length || !FieldOrderCatalog.Allows(action, FieldOrderIssuer.Player))
            {
                return false;
            }
            int assigned = Array.IndexOf(_fieldOrderSlots, action);
            if (assigned >= 0 && assigned != slot) { _fieldOrderSlots[assigned] = _fieldOrderSlots[slot]; }
            _fieldOrderSlots[slot] = action;
            _fieldOrderPressed = false;
            UpdateFieldOrderHint();
            return true;
        }

        // Repair inspector array resizing, legacy empty arrays, duplicates and AI-only commands.
        private void OnValidate() => ValidateFieldOrderSlots();

        private void ValidateFieldOrderSlots()
        {
            int authoredCount = _fieldOrderSlots?.Length ?? 0;
            int count = DefaultFieldOrders.Length;
            if (authoredCount != count) { Array.Resize(ref _fieldOrderSlots, count); }
            for (int i = 0; i < count; i++)
            {
                if (i >= authoredCount || !FieldOrderCatalog.Allows(_fieldOrderSlots[i], FieldOrderIssuer.Player)
                    || Array.IndexOf(_fieldOrderSlots, _fieldOrderSlots[i], 0, i) >= 0)
                {
                    _fieldOrderSlots[i] = (BroadcastAction)byte.MaxValue;
                }
            }
            for (int i = 0; i < count; i++)
            {
                if (_fieldOrderSlots[i] != (BroadcastAction)byte.MaxValue) { continue; }
                foreach (BroadcastAction replacement in DefaultFieldOrders)
                {
                    if (Array.IndexOf(_fieldOrderSlots, replacement) >= 0) { continue; }
                    _fieldOrderSlots[i] = replacement;
                    break;
                }
            }
        }

        private void BindFieldOrder()
        {
            _fieldOrderAction = _playerInput.actions?.FindAction("Player/FieldOrder");
            if (_fieldOrderAction == null) { return; }
            _fieldOrderLook = _playerInput.actions.FindAction("Player/Look");
            _fieldOrderAction.started += BeginFieldOrder;
            _fieldOrderAction.performed += ReleaseFieldOrder;
            _fieldOrderAction.canceled += CancelFieldOrder;
            UpdateFieldOrderHint();
        }

        private void UnbindFieldOrder()
        {
            _fieldOrderPressed = false;
            if (_fieldOrderAction == null) { return; }
            _fieldOrderAction.started -= BeginFieldOrder;
            _fieldOrderAction.performed -= ReleaseFieldOrder;
            _fieldOrderAction.canceled -= CancelFieldOrder;
            _fieldOrderAction = null;
            _fieldOrderLook = null;
        }

        private void OnApplicationFocus(bool focused)
        {
            if (focused) { return; }
            _fieldOrderPressed = false;
            CancelMultiLock();
        }

        private void CancelFieldOrder(InputAction.CallbackContext context) => _fieldOrderPressed = false;

        private void BeginFieldOrder(InputAction.CallbackContext context)
        {
            _fieldOrderPressed = FieldOrderInputActive && TryGetIssuer(out _pressedIssuer);
        }

        private bool TryGetIssuer(out ActorRef issuer)
        {
            issuer = default;
            return _character is PlayerCharacter && _character.isActiveAndEnabled && _character.IsAlive
                && _character.ObjectHash == CharacterRegistry.PlayerHash && GameManager.Actors != null
                && GameManager.Data.GetManaged(_character.ObjectHash) == _character
                && GameManager.Actors.TryGet(_character.ObjectHash, out issuer);
        }

        // Player/FieldOrder is Press(ReleaseOnly). Disabling the map cancels without performing.
        private void ReleaseFieldOrder(InputAction.CallbackContext context)
        {
            bool pressed = _fieldOrderPressed;
            _fieldOrderPressed = false;
            if (!pressed || !FieldOrderInputActive || !TryGetIssuer(out ActorRef issuer) || issuer != _pressedIssuer) { return; }
            if (context.duration >= _fieldOrderHoldSeconds)
            {
                ActiveFieldOrderSlot = (ActiveFieldOrderSlot + 1) % _fieldOrderSlots.Length;
                UpdateFieldOrderHint();
                return;
            }

            BroadcastAction action = ActiveFieldOrder;
            ActorRef target = default;
            Vector2? position = null;
            if ((FieldOrderCatalog.RequiresTarget(action) || FieldOrderCatalog.RequiresPosition(action))
                && !TryAimFieldOrder(action, context.control.device, out target, out position))
            {
                UpdateFieldOrderHint(FieldOrderCatalog.RequiresTarget(action) ? "敵に照準を合わせてください" : "地面を指してください");
                return;
            }

            var order = new FieldOrder(action, FieldOrderIssuer.Player, issuer, target, position);
            UpdateFieldOrderHint(GameManager.Events.TryIssueFieldOrder(order) ? "指示が届きました" : "指示が届きませんでした");
        }

        private bool TryAimFieldOrder(BroadcastAction action, InputDevice device, out ActorRef target, out Vector2? position)
        {
            target = default;
            position = null;
            Camera camera = GameplayCamera;
            if (camera == null || _orderBody == null) { return false; }

            Vector2 origin = _orderBody.bounds.center;
            Vector2 direction;
            float distance = _fieldOrderAimDistance;
            Vector2? mouseAim = null;
            if (device is Gamepad)
            {
                direction = _fieldOrderLook?.ReadValue<Vector2>() ?? Vector2.zero;
                if (direction.sqrMagnitude < 0.04f) { return false; }
            }
            else
            {
                Pointer pointer = null;
                foreach (InputDevice paired in _playerInput.devices)
                {
                    if (paired is Pointer found) { pointer = found; break; }
                }
                if (pointer == null) { return false; }
                Vector2 screen = pointer.position.ReadValue();
                if (!camera.pixelRect.Contains(screen)) { return false; }
                Ray ray = camera.ScreenPointToRay(screen);
                Plane plane = new Plane(Vector3.forward, _orderBody.bounds.center);
                if (!plane.Raycast(ray, out float depth)) { return false; }
                mouseAim = ray.GetPoint(depth);
                direction = mouseAim.Value - origin;
                distance = Mathf.Min(distance, direction.magnitude + GroundPickTolerance);
            }

            // The XY prototype uses the issuing actor's local physics scene. Stop at its first
            // solid body, including allies and walls. Never fall back to an enemy registry scan.
            ContactFilter2D filter = new ContactFilter2D { useTriggers = false };
            gameObject.scene.GetPhysicsScene2D().Raycast(origin, direction.normalized, distance, filter, _orderHits);
            foreach (RaycastHit2D hit in _orderHits)
            {
                BaseCharacter hitActor = hit.collider.GetComponentInParent<BaseCharacter>();
                if (hitActor == _character) { continue; }
                Vector3 viewport = camera.WorldToViewportPoint(new Vector3(hit.point.x, hit.point.y, transform.position.z));
                if (viewport.z <= 0f || viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f) { return false; }
                if (FieldOrderCatalog.RequiresTarget(action))
                {
                    return hitActor != null && (!mouseAim.HasValue || hit.collider.OverlapPoint(mouseAim.Value))
                        && GameManager.Data.GetManaged(hitActor.ObjectHash) == hitActor
                        && GameManager.Actors.TryGet(hitActor.ObjectHash, out target);
                }
                if (hitActor != null || (_fieldOrderGround.value & (1 << hit.collider.gameObject.layer)) == 0
                    || hit.normal.y < 0.5f || (mouseAim.HasValue
                        && (mouseAim.Value - hit.point).sqrMagnitude > GroundPickTolerance * GroundPickTolerance)) { return false; }
                position = hit.point;
                return true;
            }
            return false;
        }

        private void UpdateFieldOrderHint(string feedback = null)
        {
            string name = ActiveFieldOrder switch
            {
                BroadcastAction.Rally => "集合",
                BroadcastAction.FocusFire => "集中攻撃",
                BroadcastAction.DefendPoint => "地点防衛",
                BroadcastAction.ReleaseOrder => "指示解除",
                BroadcastAction.DesignateTarget => "目標指定",
                _ => ActiveFieldOrder.ToString()
            };
            string detail = feedback;
            if (detail == null && (FieldOrderCatalog.RequiresTarget(ActiveFieldOrder) || FieldOrderCatalog.RequiresPosition(ActiveFieldOrder)))
            {
                detail = "マウス／右スティックで照準";
            }
            _fieldOrderHint = $"指示 {ActiveFieldOrderSlot + 1}/{_fieldOrderSlots.Length}  {name}　F／十字↓ 短押しで発令・長押しで次";
            if (detail != null) { _fieldOrderHint += "\n" + detail; }
        }
    }
}
