using Game.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Runtime
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerInput), typeof(CharacterMotor3D))]
    public sealed partial class PlayerCharacterInput3D : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float _jumpBufferDuration = 0.12f;
        [SerializeField] private AttackInfo attack3D;
        [SerializeField] private AttackInfo skill3D;

        private PlayerInput _playerInput;
        private CharacterMotor3D _motor;
        private CharacterActor3D _actor;
        private PlayerCombatLoadout3D _combatLoadout;
        private InputActionAsset _actions;
        private InputActionMap _gameplayMap;
        private InputAction _move;
        private InputAction _jump;
        private InputAction _guard;
        private InputAction _attack;
        private InputAction _skill;
        private InputAction _pendingAttackAction;
        private AttackInfo _pendingAttack;
        private ActorRef _pendingAttackOwner;
        private Vector2 _moveInput;
        private float _jumpBufferRemaining;
        private bool _jumpPending;
        private ActorRef _heldGuardOwner;
        private ActorRef _guardingOwner;

        internal ConsumableBeltPanel3D ConsumableBeltPanel { get; set; }

        private void Awake()
        {
            _playerInput = GetComponent<PlayerInput>();
            _motor = GetComponent<CharacterMotor3D>();
            _actor = GetComponent<CharacterActor3D>();
            _combatLoadout = GetComponent<PlayerCombatLoadout3D>();
            _itemUse = GetComponent<PlayerItemUse3D>();
        }

        private void OnEnable()
        {
            BindActions(_playerInput.actions);
            InputSystem.onActionChange += OnActionChange;
        }

        private void OnDisable()
        {
            InputSystem.onActionChange -= OnActionChange;
            BindActions(null);
        }

        private void OnApplicationFocus(bool focused)
        {
            if (focused) { return; }
            ClearInput();
        }

        private void BindActions(InputActionAsset actions)
        {
            if (_move != null)
            {
                _move.performed -= CaptureMove;
                _move.canceled -= CancelMove;
            }
            if (_jump != null) { _jump.performed -= QueueJump; }
            if (_guard != null)
            {
                _guard.performed -= BeginGuard;
                _guard.canceled -= CancelGuard;
            }
            UnbindAttackAction(_attack);
            UnbindAttackAction(_skill);
            ClearInput();
            _actions = actions;
            _gameplayMap = _actions != null ? _actions.FindActionMap("Player") : null;
            _move = _gameplayMap?.FindAction("Move");
            _jump = _gameplayMap?.FindAction("Jump");
            _guard = _gameplayMap?.FindAction("Guard");
            _attack = _gameplayMap?.FindAction("Attack");
            _skill = _gameplayMap?.FindAction("Skill");
            BindMultiLockLook(_gameplayMap?.FindAction("Look"));
            BindFieldOrder(_gameplayMap?.FindAction("FieldOrder"));
            BindInteraction(_gameplayMap?.FindAction("Interact"));
            BindCooperation(_gameplayMap?.FindAction("Cooperation"));
            BindItems(_gameplayMap);
            BindDodge(_gameplayMap?.FindAction("Sprint"));
            if (_move != null)
            {
                _move.performed += CaptureMove;
                _move.canceled += CancelMove;
            }
            if (_jump != null) { _jump.performed += QueueJump; }
            if (_guard != null)
            {
                _guard.performed += BeginGuard;
                _guard.canceled += CancelGuard;
            }
            BindAttackAction(_attack);
            BindAttackAction(_skill);
        }

        private void BindAttackAction(InputAction action)
        {
            if (action == null) { return; }
            action.performed += QueueAttack;
            action.canceled += ReleaseMultiLock;
        }

        private void UnbindAttackAction(InputAction action)
        {
            if (action == null) { return; }
            action.performed -= QueueAttack;
            action.canceled -= ReleaseMultiLock;
        }

        private void CaptureMove(InputAction.CallbackContext context)
        {
            // Read the composite while the Input System is processing its input update.
            _moveInput = context.ReadValue<Vector2>();
        }

        private void CancelMove(InputAction.CallbackContext context) => _moveInput = Vector2.zero;

        private void QueueJump(InputAction.CallbackContext context)
        {
            if (_playerInput.currentActionMap != context.action.actionMap) { return; }
            _jumpPending = true;
            _jumpBufferRemaining = _jumpBufferDuration;
        }

        private void BeginGuard(InputAction.CallbackContext context)
        {
            if (_playerInput.currentActionMap != context.action.actionMap
                || _actor == null || !_actor.isActiveAndEnabled || !_actor.IsAlive) { return; }
            _heldGuardOwner = _actor.Actor;
        }

        private void CancelGuard(InputAction.CallbackContext context) => ClearGuardInput();

        private void ClearGuardInput()
        {
            _heldGuardOwner = default;
            StopGuarding();
        }

        private void ApplyGuardInput(GameActionRuntime runtime)
        {
            ActorRef owner = _heldGuardOwner;
            if (!owner.IsValid) { return; }
            if (runtime == null || _guard == null || !_guard.enabled || _playerInput.currentActionMap != _gameplayMap
                || _actor == null || !_actor.IsAlive || _actor.Actor != owner)
            {
                ClearGuardInput();
                return;
            }
            if (GameManager.Data.GetStatusEffects(owner.Hash).BlocksActions)
            {
                StopGuarding();
                return;
            }

            if (runtime.TrySetGuarding(owner.Hash, true, out _))
            {
                _guardingOwner = owner;
                if (_heldGuardOwner != owner || _actor == null || !_actor.IsAlive || _actor.Actor != owner)
                {
                    StopGuarding();
                }
            }
        }

        private void StopGuarding()
        {
            ActorRef owner = _guardingOwner;
            _guardingOwner = default;
            if (owner.IsValid && GameManager.Actors != null && GameManager.Actors.IsCurrent(owner))
            {
                GameManager.Actions?.TrySetGuarding(owner.Hash, false, out _);
            }
        }

        private void QueueAttack(InputAction.CallbackContext context)
        {
            if (_playerInput.currentActionMap != context.action.actionMap
                || _actor == null || !_actor.isActiveAndEnabled || !_actor.IsAlive) { return; }
            if (context.control.device is Mouse mouse && ReferenceEquals(context.control, mouse.leftButton)
                && ConsumableBeltPanel != null && ConsumableBeltPanel.BlocksGameplayClick(mouse.position.ReadValue())) { return; }
            ActorRef owner = _actor.Actor;
            bool skill = context.action == _skill;
            AttackInfo definition = _combatLoadout != null && _combatLoadout.isActiveAndEnabled
                ? (skill ? _combatLoadout.ResolveSpellAttack() : _combatLoadout.ResolveWeaponAttack())
                : (skill ? skill3D : attack3D);
            if (definition == null || _actor.Actor != owner || !context.action.enabled
                || _playerInput.currentActionMap != context.action.actionMap) { return; }
            if (MultiLockTargeting3D.GetRange(definition) > 0f)
            {
                BeginMultiLock(context, definition);
                return;
            }
            _pendingAttackAction = context.action;
            _pendingAttack = definition;
            _pendingAttackOwner = owner;
        }

        internal void ClearLoadoutCombatInput()
        {
            ClearPendingAttack();
            CancelMultiLock();
        }

        private void OnActionChange(object changed, InputActionChange change)
        {
            if (change == InputActionChange.ActionMapDisabled && ReferenceEquals(changed, _gameplayMap))
            {
                ClearInput();
            }
            else if (change == InputActionChange.ActionDisabled)
            {
                if (ReferenceEquals(changed, _jump)) { ClearJump(); }
                if (ReferenceEquals(changed, _guard)) { ClearGuardInput(); }
                if (ReferenceEquals(changed, _sprint)) { ClearDodgeInput(); }
                if (ReferenceEquals(changed, _fieldOrderAction)) { CancelFieldOrderPress(); }
                if (ReferenceEquals(changed, _interactAction)) { ClearInteraction(); }
                if (ReferenceEquals(changed, _cooperationAction)) { _pendingCooperation = default; }
                if (ReferenceEquals(changed, _pendingAttackAction)) { ClearPendingAttack(); }
                if (ReferenceEquals(changed, _multiLockAction)) { CancelMultiLock(); }
                CancelItemInputIfDisabled(changed);
            }
        }

        private void ClearInput()
        {
            _moveInput = Vector2.zero;
            ClearPendingAttack();
            CancelMultiLock();
            ClearJump();
            ClearGuardInput();
            ClearDodgeInput();
            CancelFieldOrderPress();
            ClearInteraction();
            _pendingCooperation = default;
            _pendingItemInputs.Clear();
        }

        private void ClearPendingAttack()
        {
            _pendingAttackAction = null;
            _pendingAttack = null;
            _pendingAttackOwner = default;
        }

        private void ClearJump()
        {
            _jumpPending = false;
            _jumpBufferRemaining = 0f;
        }

        private void FixedUpdate()
        {
            InputActionAsset actions = _playerInput.actions;
            if (_actions != actions) { BindActions(actions); }
            if (!_motor.isActiveAndEnabled || (_actor != null && (!_actor.isActiveAndEnabled || !_actor.IsAlive)))
            {
                ClearJump();
                ClearPendingAttack();
                CancelMultiLock();
                ClearGuardInput();
                ClearDodgeInput();
                CancelFieldOrderPress();
                ClearInteraction();
                _pendingCooperation = default;
                _pendingItemInputs.Clear();
                return;
            }

            if (ConsumeInteraction()) { return; }
            GameActionRuntime actionsRuntime = GameManager.Actions;
            ApplyGuardInput(actionsRuntime);
            TryStartDodge(actionsRuntime);
            if (_multiLockAction != null && !IsMultiLockInputActive()) { CancelMultiLock(); }
            if (_pendingAttack != null || _multiLockReleasePending)
            {
                AttackInfo immediateAttack = _pendingAttack;
                ActorRef immediateOwner = _pendingAttackOwner;
                ClearPendingAttack();
                if (actionsRuntime != null && _actor != null)
                {
                    ActorRef actor = _actor.Actor;
                    if (_multiLockReleasePending)
                    {
                        actionsRuntime.Execute3D(_actor, _multiLockDefinition, _multiLockTargets, out _);
                    }
                    else if (actor == immediateOwner)
                    {
                        actionsRuntime.Execute3D(_actor, immediateAttack, out _);
                    }
                    if (_actor == null || !_actor.isActiveAndEnabled || !_actor.IsAlive || _actor.Actor != actor)
                    {
                        ClearInput();
                        return;
                    }
                }
                if (_multiLockReleasePending) { CancelMultiLock(); }
            }

            bool movementBlocked = _actor != null && actionsRuntime != null
                && actionsRuntime.IsMovementBlocked(_actor.Actor.Hash);
            float deltaTime = Time.fixedDeltaTime;
            Vector3 moveDirection = Vector3.zero;
            if (!movementBlocked && _move != null && _move.enabled && _playerInput.currentActionMap == _gameplayMap)
            {
                moveDirection = CameraRelativeDirection(_moveInput, GameplayCamera);
            }

            bool jumpPressed = _jumpPending && !movementBlocked && _motor.IsGrounded;
            if (_jumpPending)
            {
                _jumpBufferRemaining -= deltaTime;
                if (jumpPressed || _jumpBufferRemaining <= 0f) { ClearJump(); }
            }

            if (TryGetDodgeMovement(actionsRuntime, out Vector3 dodgeVelocity))
            {
                _motor.SimulateVelocityStep(dodgeVelocity, deltaTime);
            }
            else { _motor.Simulate(moveDirection, jumpPressed, deltaTime); }
            ConsumeCooperation();
            ConsumeItemInput();
        }

        internal static Vector3 CameraRelativeDirection(Vector2 stick, Camera camera)
        {
            Vector2 input = InputConverter.NormalizeDirection(stick);
            if (input == Vector2.zero) { return Vector3.zero; }

            Vector3 right = camera != null ? Vector3.ProjectOnPlane(camera.transform.right, Vector3.up) : Vector3.right;
            right = right.sqrMagnitude > 0.0001f ? right.normalized : Vector3.right;
            Vector3 forward = Vector3.Cross(right, Vector3.up);
            return right * input.x + forward * input.y;
        }
    }
}
