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
        private MovementInfo _currentInput;

        // ボタン状態追跡
        private bool _jumpPressed;
        private bool _jumpHeld;
        private bool _dashPressed;
        private bool _attackPressed;
        private Vector2 _moveDirection;

        public MovementInfo CurrentInput => _currentInput;

        private void Awake()
        {
            _playerInput = GetComponent<PlayerInput>();
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
                _attackPressed = true;
            }
        }

        private void Update()
        {
            // MovementInfoを更新
            _currentInput = new MovementInfo
            {
                moveDirection = _moveDirection,
                jumpPressed = _jumpPressed,
                jumpHeld = _jumpHeld,
                dashPressed = _dashPressed,
                attackInput = _attackPressed ? (AttackInputType?)AttackInputType.LightAttack : null,
                guardHeld = false,
                interactPressed = false,
                cooperationPressed = false,
                weaponSwitchPressed = false,
                gripSwitchPressed = false,
                menuPressed = false,
                mapPressed = false
            };
        }

        private void LateUpdate()
        {
            // per-frameフラグをクリア（consumed）
            _jumpPressed = false;
            _dashPressed = false;
            _attackPressed = false;
        }
    }
}
