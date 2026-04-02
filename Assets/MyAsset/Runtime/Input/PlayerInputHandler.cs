using UnityEngine;
using UnityEngine.InputSystem;
using Game.Core;
using CharacterInfo = Game.Core.CharacterInfo;

namespace Game.Runtime
{
    /// <summary>
    /// Unity InputSystem → MovementInfo変換。
    /// PlayerInputコンポーネントからコールバックを受け取り、
    /// 純ロジックが使うMovementInfo構造体に変換する。
    ///
    /// 攻撃入力はInputActionを直接ポーリングして
    /// 押下/リリースを検出する（SendMessagesのcanceled未到達問題の回避）。
    ///
    /// スプリント: 長押しで継続スプリント、単押しで回避。
    /// </summary>
    [RequireComponent(typeof(PlayerInput))]
    public class PlayerInputHandler : MonoBehaviour
    {
        private PlayerInput _playerInput;
        private BaseCharacter _character;
        private MovementInfo _currentInput;

        // CharacterInfoから読み込む入力設定値（Awakeでキャッシュ）
        private float _inputBufferDuration;
        private float _chargeThreshold;
        private float _sprintHoldThreshold;

        // チャージ攻撃
        private ChargeInputHandler _chargeHandler;
        private ChargeAttackLogic _chargeLogic;

        // 連続入力
        private bool _jumpHeld;
        private bool _guardHeld;
        private Vector2 _moveDirection;

        // 一発入力バッファ
        private float _jumpBuffer;
        private float _dodgeBuffer;
        private float _interactBuffer;
        private float _weaponSwitchBuffer;
        private float _gripSwitchBuffer;
        private float _cooperationBuffer;
        private float _menuBuffer;
        private float _mapBuffer;

        // 攻撃バッファ
        private float _attackBuffer;
        private AttackButtonId _bufferedAttackButtonId;
        private bool _bufferedIsCharging;

        // 攻撃ボタン直接ポーリング用
        private InputAction _attackAction;
        private InputAction _heavyAttackAction;
        private bool _attackHeldLastFrame;
        private bool _heavyHeldLastFrame;
        private float _attackHoldTime;
        private float _heavyHoldTime;
        private AttackButtonId _holdingButtonId;
        private bool _isHoldingButton;

        // スプリント/回避
        private InputAction _sprintAction;
        private bool _sprintHeld;
        private float _sprintHoldTime;
        private bool _sprintHeldLastFrame;

        // --- 入力オーバーライド（自動テスト用）---
        private bool _overrideActive;
        private MovementInfo _overrideInput;

        public MovementInfo CurrentInput => _overrideActive ? _overrideInput : _currentInput;

        /// <summary>チャージ中かどうか（ボタンホールド中）。</summary>
        public bool IsCharging => _overrideActive ? false : (_chargeHandler != null && _chargeHandler.IsHolding);

        /// <summary>
        /// 自動テスト用: 入力をオーバーライドする。実入力を無視してこの値を返す。
        /// </summary>
        public void SetOverrideInput(MovementInfo input)
        {
            _overrideActive = true;
            _overrideInput = input;
        }

        /// <summary>
        /// 自動テスト用: オーバーライドを解除して実入力に戻す。
        /// </summary>
        public void ClearOverrideInput()
        {
            _overrideActive = false;
        }

        /// <summary>
        /// 攻撃入力を消費する。PlayerCharacterが攻撃実行後に呼ぶ。
        /// バッファをクリアして同一入力で複数攻撃が出るのを防ぐ。
        /// </summary>
        public void ConsumeAttackInput()
        {
            _attackBuffer = 0f;
        }

        private void Awake()
        {
            _playerInput = GetComponent<PlayerInput>();
            _character = GetComponent<BaseCharacter>();
            _chargeLogic = new ChargeAttackLogic();
            _chargeHandler = new ChargeInputHandler(_chargeLogic);

            // CharacterInfoから入力設定値をキャッシュ（フォールバック付き）
            CharacterInfo info = _character != null ? _character.CharacterInfoRef : null;
            _inputBufferDuration = info != null ? info.inputBufferDuration : 0.15f;
            _chargeThreshold = info != null ? info.chargeThreshold : 0.3f;
            _sprintHoldThreshold = info != null ? info.sprintHoldThreshold : 0.25f;
        }

        private void OnEnable()
        {
            // InputActionを直接取得してポーリング用に保持
            if (_playerInput != null && _playerInput.actions != null)
            {
                _attackAction = _playerInput.actions["Attack"];
                _heavyAttackAction = _playerInput.actions["HeavyAttack"];
                _sprintAction = _playerInput.actions["Sprint"];
            }
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
                _jumpBuffer = _inputBufferDuration;
                _jumpHeld = true;
            }
            else
            {
                _jumpHeld = false;
            }
        }

        // OnSprint: 長押し判定はUpdateのポーリングで行うため、ここでは何もしない。
        // SendMessagesではcanceledが来ないためポーリング必須。

        public void OnSkill(InputValue value)
        {
            if (value.isPressed)
            {
                _chargeHandler.CancelCharge();
                _attackBuffer = _inputBufferDuration;
                _bufferedAttackButtonId = AttackButtonId.Skill;
                _bufferedIsCharging = false;
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
                _interactBuffer = _inputBufferDuration;
            }
        }

        public void OnWeaponSwitch(InputValue value)
        {
            if (value.isPressed)
            {
                _weaponSwitchBuffer = _inputBufferDuration;
            }
        }

        public void OnGripSwitch(InputValue value)
        {
            if (value.isPressed)
            {
                _gripSwitchBuffer = _inputBufferDuration;
            }
        }

        public void OnCooperation(InputValue value)
        {
            if (value.isPressed)
            {
                _cooperationBuffer = _inputBufferDuration;
            }
        }

        public void OnMenu(InputValue value)
        {
            if (value.isPressed)
            {
                _menuBuffer = _inputBufferDuration;
            }
        }

        public void OnMap(InputValue value)
        {
            if (value.isPressed)
            {
                _mapBuffer = _inputBufferDuration;
            }
        }

        private void Update()
        {
            if (_chargeHandler == null)
            {
                return;
            }

            float dt = Time.deltaTime;

            // --- 攻撃ボタン直接ポーリング（SendMessagesのcanceled未到達を回避）---
            PollAttackButtons(dt);

            // --- スプリント/回避ポーリング ---
            PollSprintButton(dt);

            // チャージ更新
            _chargeHandler.UpdateHold(dt);

            // バッファタイマー減算
            _jumpBuffer -= dt;
            _dodgeBuffer -= dt;
            _interactBuffer -= dt;
            _weaponSwitchBuffer -= dt;
            _gripSwitchBuffer -= dt;
            _cooperationBuffer -= dt;
            _menuBuffer -= dt;
            _mapBuffer -= dt;
            _attackBuffer -= dt;

            // 攻撃入力変換
            AttackInputType? attackInput = null;
            if (_attackBuffer > 0f)
            {
                bool isAirborne = _character != null && !_character.IsGrounded;
                attackInput = InputConverter.ConvertAttackInput(
                    _bufferedAttackButtonId, isAirborne, _bufferedIsCharging);
            }

            // MovementInfoを更新
            _currentInput = new MovementInfo
            {
                moveDirection = _moveDirection,
                jumpPressed = _jumpBuffer > 0f,
                jumpHeld = _jumpHeld,
                dodgePressed = _dodgeBuffer > 0f,
                sprintHeld = _sprintHeld,
                attackInput = attackInput,
                guardHeld = _guardHeld,
                interactPressed = _interactBuffer > 0f,
                cooperationPressed = _cooperationBuffer > 0f,
                weaponSwitchPressed = _weaponSwitchBuffer > 0f,
                gripSwitchPressed = _gripSwitchBuffer > 0f,
                menuPressed = _menuBuffer > 0f,
                mapPressed = _mapBuffer > 0f,
                chargeMultiplier = _chargeHandler.ChargeMultiplier
            };
        }

        /// <summary>
        /// 攻撃ボタンをポーリングして押下/リリースを検出する。
        /// 短押し=即攻撃、長押し(>_chargeThreshold)=ため攻撃（リリースで発動）。
        /// </summary>
        private void PollAttackButtons(float dt)
        {
            // Light Attack
            bool attackHeld = _attackAction != null && _attackAction.IsPressed();
            if (attackHeld && !_attackHeldLastFrame)
            {
                // 押下開始
                _holdingButtonId = AttackButtonId.Light;
                _isHoldingButton = true;
                _attackHoldTime = 0f;
                _chargeHandler.BeginHold((int)AttackButtonId.Light);
                // 即座に通常攻撃バッファをセット（短押し想定）
                _attackBuffer = _inputBufferDuration;
                _bufferedAttackButtonId = AttackButtonId.Light;
                _bufferedIsCharging = false;
            }
            else if (!attackHeld && _attackHeldLastFrame)
            {
                // リリース
                if (_isHoldingButton && _holdingButtonId == AttackButtonId.Light)
                {
                    if (_attackHoldTime >= _chargeThreshold)
                    {
                        // ため攻撃として上書き（押下時の通常攻撃バッファをチャージに差し替え）
                        _chargeHandler.EndHold((int)AttackButtonId.Light);
                        _attackBuffer = _inputBufferDuration;
                        _bufferedAttackButtonId = AttackButtonId.Light;
                        _bufferedIsCharging = true;
                        _chargeHandler.ConsumeAttack();
                    }
                    else
                    {
                        // 短押し: 押下時にバッファ済みなのでここでは何もしない
                        _chargeHandler.CancelCharge();
                    }
                    _isHoldingButton = false;
                }
            }
            else if (attackHeld && _isHoldingButton && _holdingButtonId == AttackButtonId.Light)
            {
                _attackHoldTime += dt;
            }
            _attackHeldLastFrame = attackHeld;

            // Heavy Attack — リリース時確定方式
            // 押下: ホールド計測開始のみ。リリース: 秒数で通常/ため攻撃を確定
            bool heavyHeld = _heavyAttackAction != null && _heavyAttackAction.IsPressed();
            if (heavyHeld && !_heavyHeldLastFrame)
            {
                _holdingButtonId = AttackButtonId.Heavy;
                _isHoldingButton = true;
                _heavyHoldTime = 0f;
                _chargeHandler.BeginHold((int)AttackButtonId.Heavy);
            }
            else if (!heavyHeld && _heavyHeldLastFrame)
            {
                if (_isHoldingButton && _holdingButtonId == AttackButtonId.Heavy)
                {
                    if (_heavyHoldTime >= _chargeThreshold)
                    {
                        _chargeHandler.EndHold((int)AttackButtonId.Heavy);
                        _attackBuffer = _inputBufferDuration;
                        _bufferedAttackButtonId = AttackButtonId.Heavy;
                        _bufferedIsCharging = true;
                        _chargeHandler.ConsumeAttack();
                    }
                    else
                    {
                        _chargeHandler.CancelCharge();
                        _attackBuffer = _inputBufferDuration;
                        _bufferedAttackButtonId = AttackButtonId.Heavy;
                        _bufferedIsCharging = false;
                    }
                    _isHoldingButton = false;
                }
            }
            else if (heavyHeld && _isHoldingButton && _holdingButtonId == AttackButtonId.Heavy)
            {
                _heavyHoldTime += dt;
            }
            _heavyHeldLastFrame = heavyHeld;
        }

        /// <summary>
        /// スプリントボタンをポーリング。
        /// 短押し(&lt;_sprintHoldThreshold)=回避、長押し=スプリント。
        /// </summary>
        private void PollSprintButton(float dt)
        {
            bool held = _sprintAction != null && _sprintAction.IsPressed();

            if (held && !_sprintHeldLastFrame)
            {
                // 押下開始
                _sprintHoldTime = 0f;
            }
            else if (held)
            {
                _sprintHoldTime += dt;
            }

            if (!held && _sprintHeldLastFrame)
            {
                // リリース
                if (_sprintHoldTime < _sprintHoldThreshold)
                {
                    // 短押し → 回避
                    _dodgeBuffer = _inputBufferDuration;
                }
                _sprintHeld = false;
            }
            else if (held && _sprintHoldTime >= _sprintHoldThreshold)
            {
                // 長押し → スプリント継続中
                _sprintHeld = true;
            }
            else if (!held)
            {
                _sprintHeld = false;
            }

            _sprintHeldLastFrame = held;
        }
    }
}
