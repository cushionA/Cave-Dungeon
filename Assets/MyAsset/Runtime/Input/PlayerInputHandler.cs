using UnityEngine;
using UnityEngine.InputSystem;
using Game.Core;

namespace Game.Runtime
{
    /// <summary>
    /// Unity InputSystem → MovementInfo変換。
    /// PlayerInputコンポーネントからコールバックを受け取り、
    /// 純ロジックが使うMovementInfo構造体に変換する。
    /// </summary>
    [RequireComponent(typeof(PlayerInput))]
    public class PlayerInputHandler : MonoBehaviour
    {
        private PlayerInput _playerInput;
        private BaseCharacter _character;
        private MovementInfo _currentInput;

        // チャージ攻撃
        private ChargeInputHandler _chargeHandler;
        private ChargeAttackLogic _chargeLogic;

        // ボタン状態追跡
        private bool _jumpPressed;
        private bool _jumpHeld;
        private bool _dashPressed;
        private bool _guardHeld;
        private bool _interactPressed;
        private bool _weaponSwitchPressed;
        private bool _gripSwitchPressed;
        private bool _cooperationPressed;
        private bool _menuPressed;
        private bool _mapPressed;
        private Vector2 _moveDirection;

        public MovementInfo CurrentInput => _currentInput;

        private void Awake()
        {
            _playerInput = GetComponent<PlayerInput>();
            _character = GetComponent<BaseCharacter>();
            _chargeLogic = new ChargeAttackLogic();
            _chargeHandler = new ChargeInputHandler(_chargeLogic);
        }

        // --- InputSystem Callbacks (Message mode) ---

        public void OnMove(InputValue value)
        {
            _moveDirection = InputConverter.NormalizeDirection(value.Get<Vector2>());
        }

        public void OnJump(InputValue value)
        {
            if (value.isPressed)
            {
                _jumpPressed = true;
                _jumpHeld = true;
            }
            else
            {
                _jumpHeld = false;
            }
        }

        public void OnSprint(InputValue value)
        {
            if (value.isPressed)
            {
                _dashPressed = true;
            }
        }

        public void OnAttack(InputValue value)
        {
            if (value.isPressed)
            {
                _chargeHandler.BeginHold(0); // Light
            }
            else
            {
                _chargeHandler.EndHold(0);
            }
        }

        public void OnHeavyAttack(InputValue value)
        {
            if (value.isPressed)
            {
                _chargeHandler.BeginHold(1); // Heavy
            }
            else
            {
                _chargeHandler.EndHold(1);
            }
        }

        public void OnSkill(InputValue value)
        {
            if (value.isPressed)
            {
                _chargeHandler.CancelCharge();
                _chargeHandler.BeginHold(2); // Skill — チャージなし、即発動
                _chargeHandler.EndHold(2);
            }
        }

        public void OnGuard(InputValue value)
        {
            _guardHeld = value.isPressed;
            if (value.isPressed)
            {
                _chargeHandler.CancelCharge();
            }
        }

        public void OnInteract(InputValue value)
        {
            if (value.isPressed)
            {
                _interactPressed = true;
            }
        }

        public void OnWeaponSwitch(InputValue value)
        {
            if (value.isPressed)
            {
                _weaponSwitchPressed = true;
            }
        }

        public void OnGripSwitch(InputValue value)
        {
            if (value.isPressed)
            {
                _gripSwitchPressed = true;
            }
        }

        public void OnCooperation(InputValue value)
        {
            if (value.isPressed)
            {
                _cooperationPressed = true;
            }
        }

        public void OnMenu(InputValue value)
        {
            if (value.isPressed)
            {
                _menuPressed = true;
            }
        }

        public void OnMap(InputValue value)
        {
            if (value.isPressed)
            {
                _mapPressed = true;
            }
        }

        private void Update()
        {
            // チャージ更新
            _chargeHandler.UpdateHold(Time.deltaTime);

            // 攻撃入力変換
            AttackInputType? attackInput = null;
            if (_chargeHandler.HasAttackInput)
            {
                bool isAirborne = _character != null && !_character.IsGrounded;
                attackInput = InputConverter.ConvertAttackInput(
                    _chargeHandler.AttackButtonId, isAirborne, _chargeHandler.IsCharging);
            }

            // MovementInfoを更新
            _currentInput = new MovementInfo
            {
                moveDirection = _moveDirection,
                jumpPressed = _jumpPressed,
                jumpHeld = _jumpHeld,
                dashPressed = _dashPressed,
                attackInput = attackInput,
                guardHeld = _guardHeld,
                interactPressed = _interactPressed,
                cooperationPressed = _cooperationPressed,
                weaponSwitchPressed = _weaponSwitchPressed,
                gripSwitchPressed = _gripSwitchPressed,
                menuPressed = _menuPressed,
                mapPressed = _mapPressed,
                chargeMultiplier = _chargeHandler.ChargeMultiplier
            };
        }

        private void LateUpdate()
        {
            // per-frameフラグをクリア
            _jumpPressed = false;
            _dashPressed = false;
            _interactPressed = false;
            _weaponSwitchPressed = false;
            _gripSwitchPressed = false;
            _cooperationPressed = false;
            _menuPressed = false;
            _mapPressed = false;
            _chargeHandler.ConsumeAttack();
        }
    }
}
