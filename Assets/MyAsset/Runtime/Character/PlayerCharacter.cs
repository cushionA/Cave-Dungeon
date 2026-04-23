using UnityEngine;
using Game.Core;
using CharacterInfo = Game.Core.CharacterInfo;

namespace Game.Runtime
{
    /// <summary>
    /// プレイヤーキャラクターMonoBehaviour。
    /// PlayerInputHandler → GroundMovementLogic → Rigidbody2D を橋渡しする。
    /// 攻撃は ActionExecutorController.ExecuteAction(ActionSlot) 経由で実行する。
    /// コンボ段数管理は AttackInfo[] の inputWindow を参照して PlayerCharacter 側で行う。
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

        // --- コンボ（paramId管理。実行はActionExecutorController経由）---
        private int _comboStep;           // 0=なし、1~N
        private float _comboWindowTimer;  // コンボ受付タイマー
        private bool _comboQueued;        // コンボ次段の先行入力

        // --- 攻撃入力消費 ---
        private bool _attackConsumed;     // 今回のFixedUpdateで攻撃入力を消費済みか

        // --- 攻撃移動 ---
        private float _attackMoveTimer;
        private float _attackMoveSpeed;
        private float _attackMoveDir;

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

        // --- 空中攻撃浮遊 ---
        private float _airHangTimer;
        private bool _isAirAttacking;
        private Vector2 _aerialMoveDir;
        private float _aerialMoveSpeed;

        // --- 空中攻撃パラメータ（現在の攻撃のAttackInfoからキャッシュ）---
        private float _currentAirHangDuration;
        private float _currentAirHangGravityScale;

        // --- 空中攻撃制限 ---
        private bool _aerialComboUsed;    // 空中コンボを使い切ったら着地までロック

        // --- 空中強攻撃（落下攻撃）---
        private bool _isDivingAttack;     // 落下攻撃中（接地まで攻撃継続）
        private AttackInfo _divingAttackInfo; // 落下攻撃に使用したAttackInfo（着地処理用）
        private float _divingLandingTimer;   // 着地リカバリータイマー
        private bool _isDivingLanding;       // 着地リカバリー中

        // --- スタミナ回復遅延 ---
        private float _staminaRecoveryDelayTimer;

        // --- スタミナ枯渇ペナルティ ---
        private bool _isExhausted;        // スタミナ枯渇状態

        // --- チャージ中フラグ ---
        private bool _isCharging;

        // --- 回避無敵追跡 ---
        private bool _wasDodging;

        // --- Drop-through (一方通行プラットフォームすり抜け) ---
        // 発動中はプレイヤーのレイヤーを CharaInvincible (Ground とは衝突するため足場貫通には別途 PlatformEffector2D が必要) へ切替え、
        // タイマー経過後に CharaPassThrough に戻す。
        private float _dropThroughTimer;
        private int _dropThroughOriginalLayer = -1;

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
            // 落下攻撃
            _isDivingAttack = false;
            _isDivingLanding = false;
            _divingLandingTimer = 0f;
            _divingAttackInfo = null;

            // 空中攻撃
            _isAirAttacking = false;
            _aerialComboUsed = false;

            // コンボ
            _comboStep = 0;
            _comboQueued = false;
            _comboWindowTimer = 0f;
            _attackConsumed = false;

            // スタミナ枯渇
            _isExhausted = false;
            _staminaRecoveryDelayTimer = 0f;

            // チャージ・回避
            _isCharging = false;
            _wasDodging = false;
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

            // MovementLogicの状態リセット（ジャンプ・回避・スプリント）
            _movementLogic?.Reset();

            // 重力リセット
            if (_rb != null)
            {
                _rb.gravityScale = GameConstants.k_GravityScale;
            }
        }
#endif

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
            CharacterRegistry.RegisterPlayer(ObjectHash);
        }

        private int MaxCombo => _lightAttacks != null ? _lightAttacks.Length : 0;

        /// <summary>
        /// 壁接触判定。BaseCollider の左右方向に薄いボックスを飛ばし、
        /// BaseCharacter._groundLayer (壁と地形は同一レイヤー想定) と重なっているか確認する。
        /// 壁蹴り Ability 発動判定に使用する。
        /// </summary>
        private bool IsTouchingWall(float facingDir)
        {
            if (_collider == null)
            {
                return false;
            }

            Bounds bounds = _collider.bounds;
            // コライダー外側 0.05u の薄い検出ボックス (コライダー高さの 90% を使用してフチの誤検知抑制)
            Vector2 origin = new Vector2(
                bounds.center.x + facingDir * (bounds.extents.x + 0.05f),
                bounds.center.y);
            Vector2 size = new Vector2(0.05f, bounds.size.y * 0.9f);
            return Physics2D.OverlapBox(origin, size, 0f, _groundLayer) != null;
        }

        private void FixedUpdate()
        {
            if (!IsAlive)
            {
                return;
            }

            UpdateGroundCheck();

            MovementInfo input = _inputHandler != null ? _inputHandler.CurrentInput : default;
            ref MoveParams moveParams = ref GameManager.Data.GetMoveParams(ObjectHash);
            ref CharacterVitals vitals = ref GameManager.Data.GetVitals(ObjectHash);

            // Drop-through タイマー更新 (接地より先に行い、発動判定時にはタイマー 0 で正しく評価する)
            UpdateDropThroughTimer(Time.fixedDeltaTime);

            // 下入力 + ジャンプ入力 + 接地 + 真下が DropThroughPlatform → 発動
            TryStartDropThrough(input);

            // 着地時に空中攻撃制限をリセット
            if (IsGrounded)
            {
                if (_aerialComboUsed)
                {
                    _aerialComboUsed = false;
                }

                // 落下攻撃着地 → リカバリーフェーズへ遷移
                if (_isDivingAttack && !_isDivingLanding)
                {
                    BeginDivingLanding();
                }
            }

            // 落下攻撃着地リカバリー中
            if (_isDivingLanding)
            {
                UpdateDivingLanding();
                // リカバリー中は他の入力を受け付けない
                SyncPositionToData();
                return;
            }

            // チャージ状態更新（InputHandlerのボタンホールド状態を参照）
            _isCharging = _inputHandler != null && _inputHandler.IsCharging;

            // ジャンプ入力バッファ
            if (input.jumpPressed)
            {
                _jumpBufferTimer = _jumpBufferTime;
            }
            else
            {
                _jumpBufferTimer -= Time.fixedDeltaTime;
            }

            bool bufferedJump = _jumpBufferTimer > 0f;

            // Coyote Time 判定のため接地状態を MovementLogic に通知する (TryStartJump の直前)
            _movementLogic.UpdateGroundedState(IsGrounded, Time.fixedDeltaTime);

            // ジャンプ（スタミナ消費あり）
            float prevStamina = vitals.currentStamina;
            float jumpForce = _movementLogic.TryStartJump(
                bufferedJump, IsGrounded, moveParams, ref vitals.currentStamina);
            if (jumpForce > 0f)
            {
                _jumpBufferTimer = 0f;
                if (vitals.currentStamina < prevStamina)
                {
                    ResetStaminaRecoveryDelay(ref vitals);
                }
            }
            float jumpHoldFactor = _movementLogic.UpdateJumpHold(input.jumpHeld, Time.fixedDeltaTime);

            // 回避（単押し）+ 無敵連動
            prevStamina = vitals.currentStamina;
            bool dodgeStarted = _movementLogic.TryStartDodge(
                input.dodgePressed, ref vitals.currentStamina, moveParams);
            if (dodgeStarted)
            {
                _wasDodging = true;
                if (_collisionController != null)
                {
                    _collisionController.SetInvincible(true);
                }
                if (vitals.currentStamina < prevStamina)
                {
                    ResetStaminaRecoveryDelay(ref vitals);
                }
            }
            _movementLogic.UpdateDodge(Time.fixedDeltaTime, moveParams);

            // 回避終了時に無敵解除
            if (_wasDodging && !_movementLogic.IsDodging)
            {
                _wasDodging = false;
                if (_collisionController != null)
                {
                    _collisionController.SetInvincible(false);
                }
            }

            // 攻撃中は通常移動・スプリントを無効化（攻撃移動のみ許可）
            bool actionBusy = IsActionExecutorBusy();

            // スプリント（長押し・継続スタミナ消費、移動入力がある場合のみ）
            bool hasMovement = Mathf.Abs(input.moveDirection.x) > _moveInputDeadzone;
            prevStamina = vitals.currentStamina;
            _movementLogic.UpdateSprint(
                input.sprintHeld && hasMovement && !actionBusy,
                ref vitals.currentStamina, Time.fixedDeltaTime, moveParams);
            if (vitals.currentStamina < prevStamina)
            {
                ResetStaminaRecoveryDelay(ref vitals);
            }

            // 水平速度（回避時はfacingDir使用）
            float facingDir = _isFacingRight ? 1f : -1f;
            float horizontalSpeed;
            if (_attackMoveTimer > 0f)
            {
                // 攻撃移動（AttackInfoのデータ駆動）
                _attackMoveTimer -= Time.fixedDeltaTime;
                horizontalSpeed = _attackMoveDir * _attackMoveSpeed;
            }
            else if (actionBusy)
            {
                // 攻撃中は停止（攻撃移動終了後）
                horizontalSpeed = 0f;
            }
            else
            {
                horizontalSpeed = _movementLogic.CalculateHorizontalSpeed(
                    input.moveDirection.x, moveParams, facingDir);
            }

            // Rigidbody2Dに適用
            Vector2 velocity = _rb.linearVelocity;
            velocity.x = horizontalSpeed;

            if (jumpForce > 0f)
            {
                velocity.y = jumpForce;
            }
            else if (bufferedJump && !IsGrounded)
            {
                // 壁蹴り: 通常ジャンプが成立しなかった空中ジャンプ入力時、
                // WallKick Ability 所持 + 壁接触なら AdvancedMovementLogic.TryWallKick を呼ぶ。
                AbilityFlag abilities = GameManager.Data.GetFlags(ObjectHash).AbilityFlags;
                bool touchingWall = IsTouchingWall(facingDir);
                Vector2 wallKick = AdvancedMovementLogic.TryWallKick(
                    abilities, touchingWall, jumpPressed: true, _isFacingRight);
                if (wallKick.sqrMagnitude > 0.0001f)
                {
                    velocity.x = wallKick.x;
                    velocity.y = wallKick.y;
                    _jumpBufferTimer = 0f;
                    // 壁蹴り後は接地から離れた直後として扱い、Coyote 誤発動を防ぐ
                }
            }
            else if (_movementLogic.IsJumping && jumpHoldFactor <= 0f && velocity.y > 0f)
            {
                velocity.y *= _jumpReleaseVelocityDamping;
            }

            // 空中攻撃: 移動方向ベクトル適用
            if (_isAirAttacking && _airHangTimer > 0f && !_isDivingAttack)
            {
                velocity.x += _aerialMoveDir.x * _aerialMoveSpeed;
                velocity.y = _aerialMoveDir.y * _aerialMoveSpeed;
            }

            // 落下攻撃: 高速落下
            if (_isDivingAttack)
            {
                float fwdSpeed = _divingAttackInfo != null ? _divingAttackInfo.divingForwardSpeed : 1f;
                velocity.x = facingDir * fwdSpeed;
                // y速度はgravityScaleによる自然落下（高重力）
            }

            _rb.linearVelocity = velocity;

            // 空中攻撃浮遊の重力制御
            UpdateAirHang();

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
            _staminaRecoveryDelayTimer -= Time.fixedDeltaTime;

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
                && !_movementLogic.IsDashing
                && !_isCharging
                && _staminaRecoveryDelayTimer <= 0f)
            {
                vitals.currentStamina = Mathf.Min(
                    vitals.maxStamina,
                    vitals.currentStamina + vitals.staminaRecoveryRate * Time.fixedDeltaTime);
            }

            // ガード接続
            if (_damageReceiver != null)
            {
                _damageReceiver.SetGuarding(input.guardHeld);
            }

            // 攻撃処理（ActionExecutorController経由）
            _attackConsumed = false;
            HandleAttack(input, ref vitals);

            // フラッシュ更新（デバッグ補助）
            UpdateAttackFlash();

            // 位置同期
            SyncPositionToData();
        }

        // ============================================================
        //  攻撃・コンボ（ActionExecutorController経由）
        // ============================================================

        private void HandleAttack(MovementInfo input, ref CharacterVitals vitals)
        {
            // 落下攻撃中は新しい攻撃を受け付けない
            if (_isDivingAttack)
            {
                return;
            }

            float dt = Time.fixedDeltaTime;
            _comboWindowTimer -= dt;

            // コンボウィンドウ終了 → リセット
            if (_comboWindowTimer <= 0f && _comboStep > 0 && !IsActionExecutorBusy())
            {
                _comboStep = 0;
                _comboQueued = false;
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

            // コンボ先行入力（弱攻撃系のみコンボ対象）
            if (wantsAttack && executorBusy && _comboStep < MaxCombo)
            {
                AttackInputType attackType = input.attackInput.Value;
                if (attackType == AttackInputType.LightAttack || attackType == AttackInputType.AerialLight)
                {
                    _comboQueued = true;
                }
            }

            // 弱攻撃コンボ判定
            bool isLightAttack = wantsAttack && input.attackInput.HasValue
                && (input.attackInput.Value == AttackInputType.LightAttack
                    || input.attackInput.Value == AttackInputType.AerialLight
                    || input.attackInput.Value == AttackInputType.ChargeLight);
            bool canStartAttack = isLightAttack && !executorBusy && _comboStep == 0;
            bool canCombo = _comboQueued && !executorBusy
                && _comboWindowTimer > 0f && _comboStep < MaxCombo;

            if ((canStartAttack || canCombo) && _actionExecutor != null && MaxCombo > 0)
            {
                int nextStep = canCombo ? _comboStep + 1 : 1;
                int paramId = nextStep - 1;
                AttackInfo info = _lightAttacks[paramId];

                ActionSlot slot = new ActionSlot
                {
                    execType = ActionExecType.Attack,
                    paramId = paramId,
                    paramValue = input.chargeMultiplier > 0f ? input.chargeMultiplier : 1f,
                    displayName = info.attackName
                };

                bool executed = _actionExecutor.ExecuteAction(slot);
                if (!executed)
                {
                    return;
                }

                _attackConsumed = true;
                if (_inputHandler != null) { _inputHandler.ConsumeAttackInput(); }

                // スタミナ消費があった場合、回復遅延リセット
                ResetStaminaRecoveryDelay(ref vitals);

                _comboStep = nextStep;
                _comboQueued = false;

                float comboWindow = info.inputWindow > 0f ? info.inputWindow : 0.6f;
                _comboWindowTimer = comboWindow;

                float dir = _isFacingRight ? 1f : -1f;

                // 攻撃移動（AttackInfoのデータ駆動）
                if (info.attackMoveDuration > 0f)
                {
                    _attackMoveTimer = info.attackMoveDuration;
                    _attackMoveSpeed = info.attackMoveDistance / info.attackMoveDuration;
                    _attackMoveDir = dir;
                }

                // デバッグフラッシュ
                _flashTimer = k_AttackFlashDuration;
                if (_spriteRenderer != null)
                {
                    int colorIdx = Mathf.Min(nextStep - 1, k_ComboColors.Length - 1);
                    _spriteRenderer.color = k_ComboColors[colorIdx];
                }

                // 空中攻撃浮遊
                if (isAerial && !_isAirAttacking)
                {
                    _isAirAttacking = true;
                    _currentAirHangDuration = info.airHangDuration;
                    _currentAirHangGravityScale = info.airHangGravityScale;
                    _airHangTimer = _currentAirHangDuration;
                    _rb.gravityScale = _currentAirHangGravityScale;

                    _aerialMoveDir = info.aerialMoveDirection.sqrMagnitude > 0.01f
                        ? info.aerialMoveDirection.normalized
                        : new Vector2(0f, 0.3f);
                    _aerialMoveSpeed = info.attackMoveDistance > 0f
                        ? info.attackMoveDistance / Mathf.Max(info.attackMoveDuration, 0.1f)
                        : info.aerialMoveSpeedDefault;

                    Vector2 vel = _rb.linearVelocity;
                    if (vel.y < 0f)
                    {
                        vel.y = 0f;
                        _rb.linearVelocity = vel;
                    }
                }

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
            if (isHeavyAttack && !executorBusy && _actionExecutor != null && MaxCombo > 0)
            {
                // 空中強攻撃 → 落下攻撃
                bool aerialHeavy = isAerial
                    && (input.attackInput.Value == AttackInputType.AerialHeavy
                        || input.attackInput.Value == AttackInputType.ChargeHeavy);

                // フィニッシュ技（最後のAttackInfo）を強攻撃として使用
                int heavyParamId = MaxCombo - 1;
                AttackInfo heavyInfo = _lightAttacks[heavyParamId];

                ActionSlot slot = new ActionSlot
                {
                    execType = ActionExecType.Attack,
                    paramId = heavyParamId,
                    paramValue = input.chargeMultiplier > 0f ? input.chargeMultiplier : 1f,
                    displayName = heavyInfo.attackName
                };

                bool executed = _actionExecutor.ExecuteAction(slot);
                if (executed)
                {
                    _attackConsumed = true;
                    if (_inputHandler != null) { _inputHandler.ConsumeAttackInput(); }
                    ResetStaminaRecoveryDelay(ref vitals);
                    _comboStep = 0;
                    _comboQueued = false;
                    _comboWindowTimer = 0f;

                    float dir = _isFacingRight ? 1f : -1f;

                    if (aerialHeavy)
                    {
                        // 落下攻撃開始
                        _isDivingAttack = true;
                        _isAirAttacking = true;
                        _divingAttackInfo = heavyInfo;
                        _rb.gravityScale = GameConstants.k_GravityScale * heavyInfo.divingGravityMultiplier;
                        _attackMoveTimer = 0f; // 水平移動なし

                        // フラッシュ
                        _flashTimer = k_AttackFlashDuration;
                        if (_spriteRenderer != null)
                        {
                            _spriteRenderer.color = new Color(1f, 0f, 0.5f);
                        }
                    }
                    else
                    {
                        // 地上強攻撃
                        if (heavyInfo.attackMoveDuration > 0f)
                        {
                            _attackMoveTimer = heavyInfo.attackMoveDuration;
                            _attackMoveSpeed = heavyInfo.attackMoveDistance / heavyInfo.attackMoveDuration;
                            _attackMoveDir = dir;
                        }

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
        private void TryStartDropThrough(MovementInfo input)
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
            DropThroughPlatform platform = FindDropThroughPlatformBelow();
            if (platform == null)
            {
                return;
            }

            // Inspector で個別時間指定があれば優先
            float durationOverride = platform.DropThroughDurationOverride;
            if (durationOverride > 0f)
            {
                duration = durationOverride;
            }

            BeginDropThrough(duration);

            // ジャンプ入力はすり抜けに消費したとみなしバッファをクリア (誤ジャンプ防止)
            _jumpBufferTimer = 0f;
        }

        /// <summary>
        /// プレイヤーの足元にある DropThroughPlatform を探す。
        /// 接地判定用と同等の BoxCast を下方向に飛ばし、命中した Collider から取得する。
        /// </summary>
        private DropThroughPlatform FindDropThroughPlatformBelow()
        {
            if (_collider == null)
            {
                return null;
            }

            Bounds bounds = _collider.bounds;
            Vector2 origin = new Vector2(bounds.center.x, bounds.min.y);
            // 接地時の ground check より少し長めに飛ばして真下をしっかり拾う
            const float k_DropThroughCastDistance = 0.2f;
            Vector2 size = new Vector2(bounds.size.x * 0.9f, 0.05f);

            RaycastHit2D hit = Physics2D.BoxCast(
                origin, size, 0f, Vector2.down, k_DropThroughCastDistance, _groundLayer);
            if (hit.collider == null)
            {
                return null;
            }

            return hit.collider.GetComponent<DropThroughPlatform>();
        }

        /// <summary>drop-through を開始してレイヤー切替とタイマー開始を行う。</summary>
        private void BeginDropThrough(float duration)
        {
            _dropThroughOriginalLayer = gameObject.layer;
            gameObject.layer = GameConstants.k_LayerCharaInvincible;
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
            int restore = _dropThroughOriginalLayer >= 0
                ? _dropThroughOriginalLayer
                : GameConstants.k_LayerCharaPassThrough;
            gameObject.layer = restore;
            _dropThroughOriginalLayer = -1;
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
        //  空中攻撃浮遊
        // ============================================================

        private void UpdateAirHang()
        {
            // 落下攻撃は接地で終了（FixedUpdateで処理）
            if (_isDivingAttack)
            {
                return;
            }

            if (!_isAirAttacking)
            {
                return;
            }

            _airHangTimer -= Time.fixedDeltaTime;

            if (_airHangTimer <= 0f || IsGrounded)
            {
                _isAirAttacking = false;
                _rb.gravityScale = GameConstants.k_GravityScale;
            }
        }

        // ============================================================
        //  落下攻撃着地リカバリー
        // ============================================================

        /// <summary>
        /// 落下攻撃が接地した瞬間に呼ばれる。
        /// 着地SE再生 → リカバリータイマー開始 → 攻撃判定終了。
        /// </summary>
        private void BeginDivingLanding()
        {
            _isDivingAttack = false;
            _isAirAttacking = false;
            _isDivingLanding = true;
            _rb.gravityScale = GameConstants.k_GravityScale;
            _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);

            float recoveryDuration = _divingAttackInfo != null
                ? _divingAttackInfo.divingLandingRecoveryDuration
                : 0.3f;
            _divingLandingTimer = recoveryDuration;

            // ActionExecutorの攻撃判定を終了（ヒットボックス停止）
            if (_actionExecutor != null && _actionExecutor.IsExecuting)
            {
                _actionExecutor.CancelAction();
            }

            // 着地SE再生
            if (_divingAttackInfo != null
                && _divingAttackInfo.effectSoundInfo.landingSound != null
                && _audioSource != null)
            {
                _audioSource.PlayOneShot(_divingAttackInfo.effectSoundInfo.landingSound);
            }

            // 着地フラッシュ
            _flashTimer = k_AttackFlashDuration;
            if (_spriteRenderer != null)
            {
                _spriteRenderer.color = new Color(1f, 0f, 1f);
            }
        }

        /// <summary>
        /// 落下攻撃着地リカバリーの更新。タイマー消化で通常状態に復帰。
        /// </summary>
        private void UpdateDivingLanding()
        {
            _divingLandingTimer -= Time.fixedDeltaTime;

            // リカバリー中は水平速度を0に
            Vector2 vel = _rb.linearVelocity;
            vel.x = 0f;
            _rb.linearVelocity = vel;

            UpdateAttackFlash();

            if (_divingLandingTimer <= 0f)
            {
                _isDivingLanding = false;
                _divingAttackInfo = null;
            }
        }

        // ============================================================
        //  フラッシュ（デバッグ補助）
        // ============================================================

        private void UpdateAttackFlash()
        {
            if (_flashTimer <= 0f)
            {
                return;
            }

            _flashTimer -= Time.fixedDeltaTime;
            if (_flashTimer <= 0f && _spriteRenderer != null)
            {
                _spriteRenderer.color = _originalColor;
            }
        }

        protected override void OnDestroy()
        {
            if (_collisionController != null)
            {
                _collisionController.SetInvincible(false);
            }
            CharacterRegistry.Unregister(ObjectHash);
            base.OnDestroy();
        }
    }
}
