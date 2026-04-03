using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// 地上移動のピュアロジック。MonoBehaviour非依存。
    /// MoveParamsとMovementInfoから移動結果を計算する。
    /// スタミナコストはMoveParams（CharacterInfo由来）から取得する。
    /// </summary>
    public class GroundMovementLogic
    {
        public const float k_MinJumpHoldTime = 0.05f;
        public const float k_MaxJumpHoldTime = 0.3f;
        public const float k_DodgeDuration = 0.25f;
        public const float k_DodgeSpeedMultiplier = 2.5f;
        public const float k_SprintSpeedMultiplier = 1.6f;

        // テスト互換用デフォルト値
        public const float k_DodgeStaminaCost = 15f;

        private bool _isJumping;
        private float _jumpHoldTimer;
        private bool _isDodging;
        private float _dodgeTimer;
        private bool _isSprinting;

        public bool IsJumping => _isJumping;
        public bool IsDodging => _isDodging;
        public bool IsSprinting => _isSprinting;
        public bool IsDashing => _isDodging || _isSprinting;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>全内部状態をリセットする。テストハーネスの周回リセット用。リリースビルドでは除去。</summary>
        public void Reset()
        {
            _isJumping = false;
            _jumpHoldTimer = 0f;
            _isDodging = false;
            _dodgeTimer = 0f;
            _isSprinting = false;
        }
#endif

        /// <summary>水平移動速度を計算する。facingDir は回避時の方向（1=右, -1=左）。</summary>
        public float CalculateHorizontalSpeed(float inputX, MoveParams moveParams, float facingDir = 1f)
        {
            if (_isDodging)
            {
                // 入力方向が無い場合は向いている方向に回避
                float dodgeDir = Mathf.Abs(inputX) > 0.1f ? Mathf.Sign(inputX) : facingDir;
                return dodgeDir * moveParams.dashSpeed;
            }

            float speed = _isSprinting
                ? moveParams.moveSpeed * k_SprintSpeedMultiplier
                : moveParams.moveSpeed;
            return inputX * speed;
        }

        /// <summary>ジャンプ開始判定。スタミナ消費あり。</summary>
        public float TryStartJump(bool jumpPressed, bool isGrounded, MoveParams moveParams,
            ref float currentStamina)
        {
            if (!jumpPressed || !isGrounded)
            {
                return 0f;
            }

            if (moveParams.jumpStaminaCost > 0f && currentStamina < moveParams.jumpStaminaCost)
            {
                return 0f;
            }

            currentStamina -= moveParams.jumpStaminaCost;
            _isJumping = true;
            _jumpHoldTimer = 0f;
            return moveParams.jumpForce;
        }

        /// <summary>旧API互換（スタミナ消費なし版）。テスト用。</summary>
        public float TryStartJump(bool jumpPressed, bool isGrounded, MoveParams moveParams)
        {
            float dummy = float.MaxValue;
            return TryStartJump(jumpPressed, isGrounded, moveParams, ref dummy);
        }

        /// <summary>可変高度ジャンプの更新。</summary>
        public float UpdateJumpHold(bool jumpHeld, float deltaTime)
        {
            if (!_isJumping)
            {
                return 0f;
            }

            _jumpHoldTimer += deltaTime;

            if (_jumpHoldTimer >= k_MaxJumpHoldTime)
            {
                _isJumping = false;
                return 0f;
            }

            if (!jumpHeld && _jumpHoldTimer >= k_MinJumpHoldTime)
            {
                _isJumping = false;
                return 0f;
            }

            return 1f;
        }

        /// <summary>回避開始（単押し）。スタミナコストはMoveParamsから取得。</summary>
        public bool TryStartDodge(bool dodgePressed, ref float currentStamina, MoveParams moveParams)
        {
            float cost = moveParams.dodgeStaminaCost > 0f
                ? moveParams.dodgeStaminaCost
                : k_DodgeStaminaCost;

            if (!dodgePressed || _isDodging || currentStamina < cost)
            {
                return false;
            }

            currentStamina -= cost;
            _isDodging = true;
            _dodgeTimer = 0f;
            return true;
        }

        /// <summary>回避の時間経過処理。</summary>
        public bool UpdateDodge(float deltaTime, MoveParams moveParams)
        {
            if (!_isDodging)
            {
                return false;
            }

            _dodgeTimer += deltaTime;

            if (_dodgeTimer >= k_DodgeDuration)
            {
                _isDodging = false;
                return false;
            }

            return true;
        }

        /// <summary>スプリント更新（長押し）。スタミナコストはMoveParamsから取得。</summary>
        public void UpdateSprint(bool sprintHeld, ref float currentStamina,
            float deltaTime, MoveParams moveParams)
        {
            if (_isDodging)
            {
                _isSprinting = false;
                return;
            }

            if (!sprintHeld || currentStamina <= 0f)
            {
                _isSprinting = false;
                return;
            }

            float costPerSecond = moveParams.sprintStaminaPerSecond > 0f
                ? moveParams.sprintStaminaPerSecond
                : 10f;

            _isSprinting = true;
            currentStamina -= costPerSecond * deltaTime;
            if (currentStamina < 0f)
            {
                currentStamina = 0f;
                _isSprinting = false;
            }
        }

        /// <summary>旧API互換。</summary>
        public void UpdateSprint(bool sprintHeld, ref float currentStamina, float deltaTime)
        {
            MoveParams defaultParams = default;
            UpdateSprint(sprintHeld, ref currentStamina, deltaTime, defaultParams);
        }

        // --- 旧API互換 ---
        public bool TryStartDash(bool dashPressed, ref float currentStamina, MoveParams moveParams)
        {
            return TryStartDodge(dashPressed, ref currentStamina, moveParams);
        }

        public bool UpdateDash(float deltaTime, MoveParams moveParams)
        {
            return UpdateDodge(deltaTime, moveParams);
        }

        public static bool CheckGrounded(float positionY, float groundLevel, float threshold)
        {
            return positionY <= groundLevel + threshold;
        }
    }
}
