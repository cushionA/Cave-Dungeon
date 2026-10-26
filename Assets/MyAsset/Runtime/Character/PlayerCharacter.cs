using UnityEngine;
using Game.Core;
using CharacterInfo = Game.Core.CharacterInfo;

namespace Game.Runtime
{
    /// <summary>
    /// プレイヤーキャラクターMonoBehaviour。
    /// PlayerInputHandler → GroundMovementLogic → Rigidbody2D を橋渡しする。
    /// 攻撃は ActionExecutorController.ExecuteAttack 経由で共通実行グループへ渡す。
    /// コンボ段数管理と空中物理は受理した実行の登録済み設定を参照する。
    /// </summary>
    public class PlayerCharacter : BaseCharacter
    {
        [SerializeField] private AttackInfo[] _lightAttacks;

        private GroundMovementLogic _movementLogic;
        private PlayerInputHandler _inputHandler;
        private ActionExecutorController _actionExecutor;
        private CharacterCollisionController _collisionController;
        private SpriteRenderer _spriteRenderer;
        private AudioSource _audioSource;

        // CharacterInfoからキャッシュする値
        private float _jumpBufferTime;
        private float _jumpReleaseVelocityDamping;
        private float _moveInputDeadzone;
        private float _staminaExhaustionPenalty;
        private float _staminaExhaustionRecoveryRatio;

        // ジャンプ入力バッファ
        private float _jumpBufferTimer;

        // --- コンボの入力選択（実行はActionExecutorController経由）---
        private int _comboStep;           // 0=なし、1~N
        private float _comboWindowTimer;  // コンボ受付タイマー
        private bool _comboQueued;        // コンボ次段の先行入力
        private float _queuedComboChargeMultiplier = 1f;

        // --- 攻撃入力消費 ---
        private bool _attackConsumed;     // 今回の固定tickで攻撃入力を消費済みか

        // --- フラッシュ（デバッグUI補助）---
        private const float k_AttackFlashDuration = 0.12f;
        private float _flashTimer;
        private Color _originalColor;
        private static readonly Color[] k_ComboColors = {
            Color.yellow,
            new Color(1f, 0.5f, 0f),
            new Color(1f, 0.2f, 0.2f),
            new Color(1f, 0f, 1f)
        };

        // 空中攻撃の寿命は共通実行が所有する。ここには参照と物理パラメータだけを置く。
        private long _aerialInstanceId;
        private long _aerialGroupId;
        private AerialPhysicsProfile _aerialPhysics;
        private ActionExecutionSystem _observedExecution;

        private readonly struct AerialPhysicsProfile
        {
            public readonly ActionVariant Variant;
            public readonly float HangDuration;
            public readonly float HangGravityScale;
            public readonly float DiveGravityScale;
            public readonly float DiveForwardSpeed;
            public readonly Vector2 MoveDirection;
            public readonly float MoveSpeed;
            public readonly AudioClip LandingSound;

            public AerialPhysicsProfile(AttackExecutionSettings settings, ActionVariant variant)
            {
                Variant = variant;
                HangDuration = settings.AirHangDuration;
                HangGravityScale = settings.AirHangGravityScale;
                DiveGravityScale = GameConstants.k_GravityScale * settings.DivingGravityMultiplier;
                DiveForwardSpeed = settings.DivingForwardSpeed;
                MoveDirection = settings.AerialMoveDirection.sqrMagnitude > 0.01f
                    ? settings.AerialMoveDirection.normalized : new Vector2(0f, 0.3f);
                MoveSpeed = settings.MovementSpeed > 0f ? settings.MovementSpeed : settings.AerialMoveSpeedDefault;
                LandingSound = settings.LandingSound;
            }
        }

        // --- 空中攻撃制限 ---
        private bool _aerialComboUsed;    // 空中コンボを使い切ったら着地までロック

        // --- スタミナ回復遅延 ---
        private float _staminaRecoveryDelayTimer;

        // --- スタミナ枯渇ペナルティ ---
        private bool _isExhausted;        // スタミナ枯渇状態

        // --- チャージ中フラグ ---
        private bool _isCharging;

        // --- 壁蹴り左右交互制約 ---
        private int _lastKickedWallId;

        // --- Drop-through (一方通行プラットフォームすり抜け) ---
        // 発動中はプレイヤーのレイヤーを CharaInvincible (Ground とは衝突するため足場貫通には別途 PlatformEffector2D が必要) へ切替え、
        // タイマー経過後に CharaPassThrough に戻す。
        private float _dropThroughTimer;

        /// <summary>drop-through 発動中か（外部参照・テスト用）。</summary>
        public bool IsDropThrough => _dropThroughTimer > 0f;

#if UNITY_INCLUDE_TESTS
        public void SetLightAttacksForTest(AttackInfo[] attacks) { _lightAttacks = attacks; }
#endif

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>
        /// AutoInputTester等から呼ばれる状態リセット。
        /// 落下攻撃・枯渇ペナルティ・コンボ等の内部フラグを初期状態に戻す。
        /// リリースビルドでは除去される。
        /// </summary>
        public void ResetInternalState()
        {
            ResetPlayerState();
        }
#endif

        private void ResetPlayerState()
        {
            GameManager.Actions?.StopActor(ObjectHash);
            _jumpBufferTimer = 0f;
            ClearAerialPhysics();
            _aerialComboUsed = false;

            // コンボ
            _comboStep = 0;
            _comboQueued = false;
            _queuedComboChargeMultiplier = 1f;
            _comboWindowTimer = 0f;
            _attackConsumed = false;

            // スタミナ枯渇
            _isExhausted = false;
            _staminaRecoveryDelayTimer = 0f;

            // チャージ・回避
            _isCharging = false;
            if (_inputHandler != null)
            {
                _inputHandler.ConsumeAttackInput();
                _inputHandler.ConsumeDodgeInput();
            }
            if (_collisionController != null)
            {
                _collisionController.SetInvincible(false);
            }

            // Drop-through
            if (_dropThroughTimer > 0f)
            {
                _dropThroughTimer = 0f;
                EndDropThrough();
            }

            // 壁蹴り交互制約リセット
            _lastKickedWallId = AdvancedMovementLogic.k_NoWallId;

            // MovementLogicの状態リセット（ジャンプ・回避・スプリント）
            _movementLogic = new GroundMovementLogic();

            // 重力リセット
            if (PhysicsBackend != null)
            {
                SetPhysicsGravityScale(GameConstants.k_GravityScale);
            }
        }

        public override void OnPoolReturn()
        {
            ResetPlayerState();
            base.OnPoolReturn();
        }

        public override void OnPoolAcquire()
        {
            ResetPlayerState();
            base.OnPoolAcquire();
        }

        protected override void Awake()
        {
            base.Awake();
            _movementLogic = new GroundMovementLogic();
            _inputHandler = GetComponent<PlayerInputHandler>();
            _actionExecutor = GetComponent<ActionExecutorController>();
            _collisionController = GetComponent<CharacterCollisionController>();
            _spriteRenderer = GetComponent<SpriteRenderer>();
            _audioSource = GetComponent<AudioSource>();
            if (_spriteRenderer != null)
            {
                _originalColor = _spriteRenderer.color;
            }

            // CharacterInfoからキャッシュ
            CharacterInfo info = _characterInfo;
            _jumpBufferTime = info != null ? info.jumpBufferTime : 0.1f;
            _jumpReleaseVelocityDamping = info != null ? info.jumpReleaseVelocityDamping : 0.5f;
            _moveInputDeadzone = info != null ? info.moveInputDeadzone : 0.1f;
            _staminaExhaustionPenalty = info != null ? info.staminaExhaustionPenalty : 2f;
            _staminaExhaustionRecoveryRatio = info != null ? info.staminaExhaustionRecoveryRatio : 0.3f;
        }

        protected override void Start()
        {
            base.Start();
            ObserveExecution();
            CharacterRegistry.RegisterPlayer(ObjectHash);
        }

        private void OnEnable()
        {
            ObserveExecution();
        }

        private void OnDisable()
        {
            ResetPlayerState();
            StopObservingExecution();
        }

        private int MaxCombo => _lightAttacks != null ? _lightAttacks.Length : 0;

        public override void PrepareSimulationInput(float currentTime)
        {
            if (_inputHandler != null) { _inputHandler.PrepareSimulationInput(currentTime); }
        }

        public override void SimulateCharacterTick(float deltaTime, float currentTime)
        {
            if (!IsAlive)
            {
                CancelAerialExecution();
                return;
            }


            MovementInfo input = _inputHandler != null ? _inputHandler.CurrentInput : default;
            CharacterStatusEffects effects = GameManager.Data.GetStatusEffects(ObjectHash);
            input = StatusEffectSystem.RestrictInput(input, effects);
            Vector2 requestedMoveDirection = input.moveDirection;
            bool movementBlocked = GameManager.Actions.IsMovementBlocked(ObjectHash);
            ActState actState = GameManager.Data.GetFlags(ObjectHash).ActState;
            bool preserveVelocity = actState == ActState.Knockbacked;
            bool reactionBlocksMovement = HitReactionLogic.IsArmorResetStun(actState) || WakeUpLogic.IsWakeUpState(actState);
            if (movementBlocked)
            {
                input.moveDirection = Vector2.zero;
                input.jumpPressed = false;
                input.jumpHeld = false;
                input.sprintHeld = false;
                _jumpBufferTimer = 0f;
            }
            if (effects.BlocksMovement || reactionBlocksMovement)
            {
                _jumpBufferTimer = 0f;
            }
            MoveParams moveParams = GetEffectiveMoveParams();
            ref CharacterVitals vitals = ref GameManager.Data.GetVitals(ObjectHash);

            // Drop-through タイマー更新 (接地より先に行い、発動判定時にはタイマー 0 で正しく評価する)
            UpdateDropThroughTimer(deltaTime);

            // 下入力 + ジャンプ入力 + 接地 + 真下が DropThroughPlatform → 発動
            TryStartDropThrough(ref input);

            // 着地時に空中攻撃制限・壁蹴りIDをリセット
            if (IsGrounded)
            {
                if (PhysicsVelocity.y <= 0f)
                {
                    GameManager.Actions.EndJump(ObjectHash);
                    if (!GameManager.IsCharacterValid(ObjectHash))
                    {
                        return;
                    }
                    vitals = ref GameManager.Data.GetVitals(ObjectHash);
                }
                _lastKickedWallId = AdvancedMovementLogic.k_NoWallId;

                if (_aerialComboUsed)
                {
                    _aerialComboUsed = false;
                }

                NotifyAerialGrounded();
            }
            RefreshAerialGravity();
            // 接地による後続行動も同じ占有情報から判断する。
            movementBlocked = GameManager.Actions.IsMovementBlocked(ObjectHash);
            if (movementBlocked)
            {
                input.moveDirection = Vector2.zero;
                input.jumpPressed = false;
                input.jumpHeld = false;
                input.sprintHeld = false;
                _jumpBufferTimer = 0f;
            }

            // チャージ状態更新（InputHandlerのボタンホールド状態を参照）
            _isCharging = !effects.BlocksActions && _inputHandler != null && _inputHandler.IsCharging;

            // ジャンプ入力バッファ
            if (input.jumpPressed)
            {
                _jumpBufferTimer = _jumpBufferTime;
            }
            else
            {
                _jumpBufferTimer -= deltaTime;
            }

            bool bufferedJump = _jumpBufferTimer > 0f;

            // Coyote Time 判定のため接地状態を MovementLogic に通知する (TryStartJump の直前)
            _movementLogic.UpdateGroundedState(IsGrounded, deltaTime);

            float prevStamina = vitals.currentStamina;
            float jumpForce = 0f;
            bool canJump = bufferedJump && !movementBlocked && moveParams.jumpForce > 0f
                && (IsGrounded || _movementLogic.IsInCoyoteWindowFor(moveParams));
            if (canJump && GameManager.Actions.BeginJump(ObjectHash))
            {
                if (!GameManager.IsCharacterValid(ObjectHash))
                {
                    return;
                }
                vitals = ref GameManager.Data.GetVitals(ObjectHash);
                MoveParams jumpParams = moveParams;
                jumpParams.jumpStaminaCost = 0f;
                float unusedStamina = 0f;
                jumpForce = _movementLogic.TryStartJump(true, IsGrounded, jumpParams, ref unusedStamina);
            }
            if (jumpForce > 0f)
            {
                _jumpBufferTimer = 0f;
                if (_inputHandler != null) { _inputHandler.ConsumeJumpInput(); }
                if (vitals.currentStamina < prevStamina)
                {
                    ResetStaminaRecoveryDelay(ref vitals);
                }
            }
            float jumpHoldFactor = _movementLogic.UpdateJumpHold(input.jumpHeld, deltaTime);

            prevStamina = vitals.currentStamina;
            if (input.dodgePressed)
            {
                Vector2 dodgeDirection = Mathf.Abs(requestedMoveDirection.x) > _moveInputDeadzone
                    ? new Vector2(Mathf.Sign(requestedMoveDirection.x), 0f)
                    : new Vector2(_isFacingRight ? 1f : -1f, 0f);
                if (GameManager.Actions.TryDodge(ObjectHash, dodgeDirection))
                {
                    if (!GameManager.IsCharacterValid(ObjectHash))
                    {
                        return;
                    }
                    vitals = ref GameManager.Data.GetVitals(ObjectHash);
                    if (_inputHandler != null) { _inputHandler.ConsumeDodgeInput(); }
                    // 実行系が割込みを受理してから、前の攻撃の入力・物理補助を片付ける。
                    _comboStep = 0;
                    _comboQueued = false;
                    _comboWindowTimer = 0f;
                    ClearAerialPhysics();
                    if (vitals.currentStamina < prevStamina)
                    {
                        ResetStaminaRecoveryDelay(ref vitals);
                    }
                }
            }
            bool dodging = GameManager.Actions.IsDodging(ObjectHash);
            movementBlocked = GameManager.Actions.IsMovementBlocked(ObjectHash);

            // スプリント（長押し・継続スタミナ消費、移動入力がある場合のみ）
            bool hasMovement = Mathf.Abs(input.moveDirection.x) > _moveInputDeadzone;
            prevStamina = vitals.currentStamina;
            _movementLogic.UpdateSprint(
                input.sprintHeld && hasMovement && !movementBlocked,
                ref vitals.currentStamina, deltaTime, moveParams);
            if (vitals.currentStamina < prevStamina)
            {
                ResetStaminaRecoveryDelay(ref vitals);
            }

            float facingDir = _isFacingRight ? 1f : -1f;
            float horizontalSpeed = movementBlocked ? 0f
                : _movementLogic.CalculateHorizontalSpeed(input.moveDirection.x, moveParams, facingDir);

            // 共通物理境界へ渡す速度を計算
            Vector2 velocity = PhysicsVelocity;
            velocity.x = horizontalSpeed;

            if (jumpForce > 0f)
            {
                velocity.y = jumpForce;
            }
            else if (bufferedJump && !IsGrounded)
            {
                // 壁蹴り: 通常ジャンプが成立しなかった空中ジャンプ入力時、
                // WallKick Ability 所持 + 壁接触 + 前回と異なる壁なら蹴れる。
                AbilityFlag abilities = GameManager.Data.GetFlags(ObjectHash).AbilityFlags;
                bool touchingWall = CheckWallContact(facingDir, out int currentWallId);
                int newLastId;
                Vector2 wallKick = AdvancedMovementLogic.TryWallKickWithAlternation(
                    abilities, touchingWall, jumpPressed: true, _isFacingRight,
                    currentWallId, _lastKickedWallId, out newLastId);
                if (wallKick.sqrMagnitude > 0.0001f)
                {
                    velocity.x = wallKick.x;
                    velocity.y = wallKick.y;
                    _jumpBufferTimer = 0f;
                    if (_inputHandler != null) { _inputHandler.ConsumeJumpInput(); }
                    _lastKickedWallId = newLastId;
                }
            }
            else if (_movementLogic.IsJumping && jumpHoldFactor <= 0f && velocity.y > 0f)
            {
                velocity.y *= _jumpReleaseVelocityDamping;
            }

            ApplyAerialMovement(ref velocity);

            if (effects.BlocksMovement)
            {
                velocity.x = 0f;
            }
            GameManager.Actions.SetLocomotion(ObjectHash,
                new Vector2(velocity.x == 0f ? 0f : Mathf.Sign(velocity.x), 0f), Mathf.Abs(velocity.x));
            if (!GameManager.IsCharacterValid(ObjectHash))
            {
                return;
            }
            vitals = ref GameManager.Data.GetVitals(ObjectHash);
            if (!preserveVelocity)
            {
                SetPhysicsVelocity(velocity);
            }

            // 向き更新
            if (input.moveDirection.x > _moveInputDeadzone)
            {
                SetFacing(true);
            }
            else if (input.moveDirection.x < -_moveInputDeadzone)
            {
                SetFacing(false);
            }

            // スタミナ回復（回避中・スプリント中・チャージ中は回復しない、遅延考慮）
            _staminaRecoveryDelayTimer -= deltaTime;

            // スタミナ枯渇判定
            if (vitals.currentStamina <= 0f && !_isExhausted)
            {
                _isExhausted = true;
                _staminaRecoveryDelayTimer = _staminaExhaustionPenalty;
            }
            if (_isExhausted && vitals.currentStamina > vitals.maxStamina * _staminaExhaustionRecoveryRatio)
            {
                _isExhausted = false;
            }

            if (vitals.currentStamina < vitals.maxStamina
                && !dodging
                && !_movementLogic.IsSprinting
                && !_isCharging
                && _staminaRecoveryDelayTimer <= 0f)
            {
                vitals.currentStamina = Mathf.Min(
                    vitals.maxStamina,
                    vitals.currentStamina + vitals.staminaRecoveryRate * deltaTime);
            }

            // ガード接続
            if (_damageReceiver != null)
            {
                _damageReceiver.SetGuarding(input.guardHeld);
                if (!GameManager.IsCharacterValid(ObjectHash))
                {
                    return;
                }
                vitals = ref GameManager.Data.GetVitals(ObjectHash);
            }

            // 攻撃処理（ActionExecutorController経由）
            _attackConsumed = false;
            HandleAttack(input, ref vitals, deltaTime);

            // フラッシュ更新（デバッグ補助）
            UpdateAttackFlash(deltaTime);

            // 位置同期
            if (TryGetAerialExecution(out ActionExecution aerial))
            {
                GameManager.Actions.SetMovementOutput(ObjectHash, aerial.instanceId, PhysicsVelocity);
            }
        }

        // ============================================================
        //  攻撃・コンボ（ActionExecutorController経由）
        // ============================================================

        private void HandleAttack(MovementInfo input, ref CharacterVitals vitals, float deltaTime)
        {
            float dt = deltaTime;
            _comboWindowTimer -= dt;

            // 受付窓を過ぎた先行入力は、行動が継続中でも持ち越さない。
            if (_comboWindowTimer <= 0f)
            {
                _comboQueued = false;
                if (!IsActionExecutorBusy()) { _comboStep = 0; }
            }

            bool wantsAttack = input.attackInput.HasValue && !_attackConsumed;
            bool executorBusy = IsActionExecutorBusy();

            // 空中コンボ使い切りチェック
            bool isAerial = !IsGrounded;
            if (isAerial && _aerialComboUsed && wantsAttack)
            {
                AttackInputType at = input.attackInput.Value;
                // 空中弱攻撃系はロック（強攻撃=落下攻撃は許可）
                if (at == AttackInputType.AerialLight || at == AttackInputType.LightAttack)
                {
                    return;
                }
            }

            bool isLightAttack = wantsAttack && input.attackInput.HasValue
                && (input.attackInput.Value == AttackInputType.LightAttack
                    || input.attackInput.Value == AttackInputType.AerialLight
                    || input.attackInput.Value == AttackInputType.ChargeLight);
            bool inComboWindow = _comboStep > 0 && _comboWindowTimer > 0f && _comboStep < MaxCombo;
            // 入力側は次段の先行入力を保持し、占有・キャンセル時刻は実行系に判断させる。
            if (isLightAttack && executorBusy && inComboWindow)
            {
                _comboQueued = true;
                _queuedComboChargeMultiplier = input.chargeMultiplier > 0f ? input.chargeMultiplier : 1f;
            }
            bool canCombo = inComboWindow && (isLightAttack || (_comboQueued && !wantsAttack));
            bool canStartAttack = isLightAttack && !canCombo;

            if ((canStartAttack || canCombo) && _actionExecutor != null && MaxCombo > 0)
            {
                int nextStep = canCombo ? _comboStep + 1 : 1;
                int paramId = nextStep - 1;
                AttackInfo info = _lightAttacks[paramId];

                float chargeMultiplier = canCombo && !wantsAttack ? _queuedComboChargeMultiplier
                    : input.chargeMultiplier > 0f ? input.chargeMultiplier : 1f;
                ActionVariant variant = isAerial ? ActionVariant.Aerial : ActionVariant.Default;
                bool executed = _actionExecutor.ExecuteAttack(info, 0, chargeMultiplier, variant,
                    out long instanceId, out long groupId);
                if (!executed)
                {
                    return;
                }
                if (!GameManager.IsCharacterValid(ObjectHash))
                {
                    return;
                }
                vitals = ref GameManager.Data.GetVitals(ObjectHash);
                GameManager.Actions.TryGetAttackSettings(info, variant, out AttackExecutionSettings settings);

                _attackConsumed = true;
                if (_inputHandler != null) { _inputHandler.ConsumeAttackInput(); }

                // スタミナ消費があった場合、回復遅延リセット
                ResetStaminaRecoveryDelay(ref vitals);

                _comboStep = nextStep;
                _comboQueued = false;

                float comboWindow = settings.InputWindow > 0f ? settings.InputWindow : 0.6f;
                _comboWindowTimer = comboWindow;

                // デバッグフラッシュ
                _flashTimer = k_AttackFlashDuration;
                if (_spriteRenderer != null)
                {
                    int colorIdx = Mathf.Min(nextStep - 1, k_ComboColors.Length - 1);
                    _spriteRenderer.color = k_ComboColors[colorIdx];
                }

                TrackAerialExecution(settings, variant, instanceId, groupId);

                // 空中コンボ最終段で使い切り判定
                if (isAerial && nextStep >= MaxCombo)
                {
                    _aerialComboUsed = true;
                }
            }

            // 強攻撃
            bool isHeavyAttack = wantsAttack && input.attackInput.HasValue
                && (input.attackInput.Value == AttackInputType.HeavyAttack
                    || input.attackInput.Value == AttackInputType.AerialHeavy
                    || input.attackInput.Value == AttackInputType.ChargeHeavy);
            if (isHeavyAttack && _actionExecutor != null && MaxCombo > 0)
            {
                // 空中強攻撃 → 落下攻撃
                bool aerialHeavy = isAerial
                    && (input.attackInput.Value == AttackInputType.AerialHeavy
                        || input.attackInput.Value == AttackInputType.ChargeHeavy);

                // フィニッシュ技（最後のAttackInfo）を強攻撃として使用
                int heavyParamId = MaxCombo - 1;
                AttackInfo heavyInfo = _lightAttacks[heavyParamId];

                float chargeMultiplier = input.chargeMultiplier > 0f ? input.chargeMultiplier : 1f;
                ActionVariant variant = aerialHeavy ? ActionVariant.Diving
                    : isAerial ? ActionVariant.Aerial : ActionVariant.Default;
                bool executed = _actionExecutor.ExecuteAttack(heavyInfo, 0, chargeMultiplier, variant,
                    out long instanceId, out long groupId);
                if (executed)
                {
                    if (!GameManager.IsCharacterValid(ObjectHash))
                    {
                        return;
                    }
                    vitals = ref GameManager.Data.GetVitals(ObjectHash);
                    GameManager.Actions.TryGetAttackSettings(heavyInfo, variant, out AttackExecutionSettings settings);
                    _attackConsumed = true;
                    if (_inputHandler != null) { _inputHandler.ConsumeAttackInput(); }
                    ResetStaminaRecoveryDelay(ref vitals);
                    _comboStep = 0;
                    _comboQueued = false;
                    _comboWindowTimer = 0f;
                    TrackAerialExecution(settings, variant, instanceId, groupId);

                    if (aerialHeavy)
                    {
                        // フラッシュ
                        _flashTimer = k_AttackFlashDuration;
                        if (_spriteRenderer != null)
                        {
                            _spriteRenderer.color = new Color(1f, 0f, 0.5f);
                        }
                    }
                    else
                    {
                        _flashTimer = k_AttackFlashDuration;
                        if (_spriteRenderer != null)
                        {
                            _spriteRenderer.color = new Color(1f, 0f, 1f);
                        }
                    }
                }
            }
        }

        private bool IsActionExecutorBusy()
        {
            return _actionExecutor != null && _actionExecutor.IsExecuting;
        }

        // ============================================================
        //  Drop-through (一方通行プラットフォームすり抜け)
        // ============================================================

        /// <summary>
        /// 下入力 + ジャンプ入力 + 接地中 + 真下に DropThroughPlatform あり → drop-through 発動。
        /// プレイヤーのレイヤーを CharaInvincible に一時切替し、
        /// 一定時間後に CharaPassThrough へ復帰させる。
        /// </summary>
        private void TryStartDropThrough(ref MovementInfo input)
        {
            // 既に drop-through 中なら再発動しない (二重呼びガード)
            if (_dropThroughTimer > 0f)
            {
                return;
            }

            bool downPressed = DropThroughLogic.IsDownInput(input.moveDirection.y);
            bool jumpPressed = input.jumpPressed;

            float duration;
            if (!DropThroughLogic.TryDropThrough(downPressed, jumpPressed, IsGrounded, out duration))
            {
                return;
            }

            // 真下の Collider に DropThroughPlatform が付いているか確認
            if (!TryGetDropThroughDuration(out float durationOverride))
            {
                return;
            }

            // Inspector で個別時間指定があれば優先
            if (durationOverride > 0f)
            {
                duration = durationOverride;
            }

            BeginDropThrough(duration);

            // ジャンプ入力はすり抜けに消費したとみなしバッファをクリア (誤ジャンプ防止)
            _jumpBufferTimer = 0f;
            input.jumpPressed = false;
            if (_inputHandler != null) { _inputHandler.ConsumeJumpInput(); }
        }

        /// <summary>drop-through を開始してレイヤー切替とタイマー開始を行う。</summary>
        private void BeginDropThrough(float duration)
        {
            BeginPhysicsDropThrough();
            _dropThroughTimer = duration;
        }

        /// <summary>drop-through 中ならタイマーを消化し、0 になったらレイヤーを元に戻す。</summary>
        private void UpdateDropThroughTimer(float deltaTime)
        {
            if (_dropThroughTimer <= 0f)
            {
                return;
            }

            _dropThroughTimer -= deltaTime;
            if (_dropThroughTimer <= 0f)
            {
                _dropThroughTimer = 0f;
                EndDropThrough();
            }
        }

        /// <summary>drop-through を終了してレイヤーを復帰させる。</summary>
        private void EndDropThrough()
        {
            EndPhysicsDropThrough();
        }

        /// <summary>
        /// スタミナ消費時に回復遅延タイマーをリセットする。
        /// </summary>
        private void ResetStaminaRecoveryDelay(ref CharacterVitals vitals)
        {
            float delay = vitals.staminaRecoveryDelay > 0f ? vitals.staminaRecoveryDelay : 1f;
            _staminaRecoveryDelayTimer = delay;
        }

        // ============================================================
        //  共通実行から空中攻撃の物理出力を作る
        // ============================================================

        private void ObserveExecution()
        {
            ActionExecutionSystem execution = GameManager.Actions?.Execution;
            if (_observedExecution == execution) { return; }
            StopObservingExecution();
            _observedExecution = execution;
            if (_observedExecution != null)
            {
                _observedExecution.Changed += HandleAerialExecution;
            }
        }

        private void StopObservingExecution()
        {
            if (_observedExecution != null)
            {
                _observedExecution.Changed -= HandleAerialExecution;
                _observedExecution = null;
            }
        }

        private void HandleAerialExecution(ActionExecutionEvent notification)
        {
            if (notification.execution.ownerHash != ObjectHash || notification.execution.instanceId != _aerialInstanceId)
            {
                return;
            }
            if (notification.kind == ActionSignalKind.Completed || notification.kind == ActionSignalKind.Interrupted)
            {
                ClearAerialPhysics();
                if (notification.kind == ActionSignalKind.Interrupted)
                {
                    _comboStep = 0;
                    _comboQueued = false;
                    _comboWindowTimer = 0f;
                }
            }
        }

        private bool TryGetAerialExecution(out ActionExecution execution)
        {
            execution = default;
            return _aerialInstanceId != 0 && GameManager.Actions != null
                && GameManager.Actions.IsGroupActive(_aerialGroupId)
                && GameManager.Actions.Execution.TryGetInstance(_aerialInstanceId, out execution)
                && execution.ownerHash == ObjectHash && execution.groupId == _aerialGroupId;
        }

        private void TrackAerialExecution(AttackExecutionSettings settings, ActionVariant variant, long instanceId, long groupId)
        {
            ClearAerialPhysics();
            if (variant == ActionVariant.Default || GameManager.Actions == null
                || !GameManager.Actions.Execution.TryGetInstance(instanceId, out _))
            {
                return;
            }
            _aerialInstanceId = instanceId;
            _aerialGroupId = groupId;
            _aerialPhysics = new AerialPhysicsProfile(settings, variant);
            RefreshAerialGravity();
            if (variant == ActionVariant.Aerial && PhysicsVelocity.y < 0f)
            {
                Vector2 velocity = PhysicsVelocity;
                velocity.y = 0f;
                SetPhysicsVelocity(velocity);
            }
        }

        private void RefreshAerialGravity()
        {
            if (_aerialInstanceId == 0) { return; }
            if (!TryGetAerialExecution(out ActionExecution execution))
            {
                ClearAerialPhysics();
                return;
            }
            SetPhysicsGravityScale(_aerialPhysics.Variant == ActionVariant.Diving
                ? _aerialPhysics.DiveGravityScale
                : !IsGrounded && execution.elapsed < _aerialPhysics.HangDuration
                    ? _aerialPhysics.HangGravityScale : GameConstants.k_GravityScale);
        }

        private void ApplyAerialMovement(ref Vector2 velocity)
        {
            if (IsGrounded || !TryGetAerialExecution(out ActionExecution execution)) { return; }
            if (_aerialPhysics.Variant == ActionVariant.Diving)
            {
                velocity.x = (_isFacingRight ? 1f : -1f) * _aerialPhysics.DiveForwardSpeed;
            }
            else if (execution.elapsed < _aerialPhysics.HangDuration)
            {
                velocity.x += _aerialPhysics.MoveDirection.x * _aerialPhysics.MoveSpeed;
                velocity.y = _aerialPhysics.MoveDirection.y * _aerialPhysics.MoveSpeed;
            }
        }

        private void NotifyAerialGrounded()
        {
            if (_aerialPhysics.Variant != ActionVariant.Diving || PhysicsVelocity.y > 0f
                || !TryGetAerialExecution(out _)) { return; }
            AudioClip landingSound = _aerialPhysics.LandingSound;
            if (!GameManager.Actions.NotifyGrounded(ObjectHash, _aerialGroupId, out _)) { return; }
            SetPhysicsGravityScale(GameConstants.k_GravityScale);
            SetPhysicsVelocity(new Vector2(0f, PhysicsVelocity.y));
            if (landingSound != null && _audioSource != null)
            {
                _audioSource.PlayOneShot(landingSound);
            }
            _flashTimer = k_AttackFlashDuration;
            if (_spriteRenderer != null)
            {
                _spriteRenderer.color = new Color(1f, 0f, 1f);
            }
        }

        private void CancelAerialExecution()
        {
            if (_aerialGroupId != 0) { GameManager.Actions?.Execution.CancelGroup(_aerialGroupId); }
            ClearAerialPhysics();
        }

        private void ClearAerialPhysics()
        {
            if (_aerialInstanceId != 0 && PhysicsBackend != null)
            {
                SetPhysicsGravityScale(GameConstants.k_GravityScale);
            }
            _aerialInstanceId = 0;
            _aerialGroupId = 0;
            _aerialPhysics = default;
        }

        // ============================================================
        //  フラッシュ（デバッグ補助）
        // ============================================================

        private void UpdateAttackFlash(float deltaTime)
        {
            if (_flashTimer <= 0f)
            {
                return;
            }

            _flashTimer -= deltaTime;
            if (_flashTimer <= 0f && _spriteRenderer != null)
            {
                _spriteRenderer.color = _originalColor;
            }
        }

        protected override void OnDestroy()
        {
            GameManager.Actions?.StopActor(ObjectHash);
            ClearAerialPhysics();
            StopObservingExecution();
            if (_collisionController != null)
            {
                _collisionController.SetInvincible(false);
            }
            CharacterRegistry.Unregister(ObjectHash, this);
            base.OnDestroy();
        }
    }
}
