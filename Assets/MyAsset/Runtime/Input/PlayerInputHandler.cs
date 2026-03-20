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

        // ボタン状態追跡
        private bool _jumpPressed;
        private bool _jumpHeld;
        private bool _dashPressed;
        private int _attackButtonId = -1; // -1=none, 0=Light, 1=Heavy, 2=Skill
        private bool _guardHeld;
        private bool _isCharging;
        private Vector2 _moveDirection;

        public MovementInfo CurrentInput => _currentInput;

        private void Awake()
        {
            _playerInput = GetComponent<PlayerInput>();
            _character = GetComponent<BaseCharacter>();
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
                _attackButtonId = 0; // Light
            }
        }

        public void OnHeavyAttack(InputValue value)
        {
            if (value.isPressed)
            {
                _attackButtonId = 1; // Heavy
            }
        }

        public void OnSkill(InputValue value)
        {
            if (value.isPressed)
            {
                _attackButtonId = 2; // Skill
            }
        }

        public void OnGuard(InputValue value)
        {
            _guardHeld = value.isPressed;
        }

        private void Update()
        {
            // InputConverterで攻撃タイプを判定
            AttackInputType? attackInput = null;
            if (_attackButtonId >= 0)
            {
                bool isAirborne = _character != null && !_character.IsGrounded;
                attackInput = InputConverter.ConvertAttackInput(_attackButtonId, isAirborne, _isCharging);
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
            _attackButtonId = -1;
        }
    }
}
