using UnityEngine;
using Game.Core;

namespace Game.Runtime
{
    /// <summary>
    /// プレイヤーキャラクターMonoBehaviour。
    /// PlayerInputHandler → GroundMovementLogic → Rigidbody2D を橋渡しする。
    /// </summary>
    public class PlayerCharacter : BaseCharacter
    {
        private GroundMovementLogic _movementLogic;
        private PlayerInputHandler _inputHandler;
        private DamageDealer _damageDealer;
        private bool _isFacingRight = true;

        // 攻撃タイミング
        private float _attackTimer;
        private const float k_AttackDuration = 0.3f;
        private const float k_AttackCooldown = 0.5f;
        private float _attackCooldownTimer;

        protected override void Awake()
        {
            base.Awake();
            _movementLogic = new GroundMovementLogic();
            _inputHandler = GetComponent<PlayerInputHandler>();
            _damageDealer = GetComponentInChildren<DamageDealer>();
        }

        protected override void Start()
        {
            base.Start();
            CharacterRegistry.RegisterPlayer(ObjectHash);
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

            // ジャンプ
            float jumpForce = _movementLogic.TryStartJump(input.jumpPressed, IsGrounded, moveParams);
            float jumpHoldFactor = _movementLogic.UpdateJumpHold(input.jumpHeld, Time.fixedDeltaTime);

            // ダッシュ
            _movementLogic.TryStartDash(input.dashPressed, ref vitals.currentStamina, moveParams);
            _movementLogic.UpdateDash(Time.fixedDeltaTime, moveParams);

            // 水平速度
            float horizontalSpeed = _movementLogic.CalculateHorizontalSpeed(input.moveDirection.x, moveParams);

            // Rigidbody2Dに適用
            Vector2 velocity = _rb.linearVelocity;
            velocity.x = horizontalSpeed;

            if (jumpForce > 0f)
            {
                velocity.y = jumpForce;
            }
            else if (_movementLogic.IsJumping && jumpHoldFactor <= 0f && velocity.y > 0f)
            {
                // ジャンプ終了時、上昇中なら速度をカット
                velocity.y *= 0.5f;
            }

            _rb.linearVelocity = velocity;

            // 向き更新
            if (input.moveDirection.x > 0.1f && !_isFacingRight)
            {
                Flip();
            }
            else if (input.moveDirection.x < -0.1f && _isFacingRight)
            {
                Flip();
            }

            // スタミナ回復
            if (vitals.currentStamina < vitals.maxStamina && !_movementLogic.IsDashing)
            {
                vitals.currentStamina = Mathf.Min(
                    vitals.maxStamina,
                    vitals.currentStamina + vitals.staminaRecoveryRate * Time.fixedDeltaTime);
            }

            // 攻撃処理
            HandleAttack(input);

            // 位置同期
            SyncPositionToData();
        }

        private void HandleAttack(MovementInfo input)
        {
            _attackCooldownTimer -= Time.fixedDeltaTime;
            _attackTimer -= Time.fixedDeltaTime;

            if (_attackTimer <= 0f && _damageDealer != null)
            {
                _damageDealer.Deactivate();
            }

            if (input.attackInput.HasValue && _attackCooldownTimer <= 0f && _damageDealer != null)
            {
                float chargeMultiplier = input.chargeMultiplier > 0f ? input.chargeMultiplier : 1f;
                AttackMotionData motion = new AttackMotionData
                {
                    motionValue = 1.0f * chargeMultiplier,
                    attackElement = Element.Slash,
                    feature = AttackFeature.Light,
                    knockbackForce = new Vector2(_isFacingRight ? 3f : -3f, 1f),
                    armorBreakValue = 10f,
                    maxHitCount = 1
                };

                _damageDealer.Activate(motion, ObjectHash);
                _attackTimer = k_AttackDuration;
                _attackCooldownTimer = k_AttackCooldown;
            }
        }

        private void Flip()
        {
            _isFacingRight = !_isFacingRight;
            Vector3 scale = transform.localScale;
            scale.x = _isFacingRight ? Mathf.Abs(scale.x) : -Mathf.Abs(scale.x);
            transform.localScale = scale;
        }

        protected override void OnDestroy()
        {
            CharacterRegistry.Unregister(ObjectHash);
            base.OnDestroy();
        }
    }
}
