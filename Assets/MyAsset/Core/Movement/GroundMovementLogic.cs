using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// 地上移動のピュアロジック。MonoBehaviour非依存。
    /// MoveParamsとMovementInfoから移動結果を計算する。
    /// </summary>
    public class GroundMovementLogic
    {
        public const float k_MinJumpHoldTime = 0.05f;
        public const float k_MaxJumpHoldTime = 0.3f;
        public const float k_DashStaminaCost = 15f;

        private bool _isJumping;
        private float _jumpHoldTimer;
        private bool _isDashing;
        private float _dashTimer;

        public bool IsJumping => _isJumping;
        public bool IsDashing => _isDashing;

        /// <summary>
        /// 水平移動速度を計算する。
        /// moveDirection.x * moveSpeed。ダッシュ中はdashSpeedを使用。
        /// </summary>
        public float CalculateHorizontalSpeed(float inputX, MoveParams moveParams)
        {
            float speed = _isDashing ? moveParams.dashSpeed : moveParams.moveSpeed;
            return inputX * speed;
        }

        /// <summary>
        /// ジャンプ開始判定。接地中にjumpPressedならジャンプ開始。
        /// 戻り値: ジャンプ初速（jumpForce）。ジャンプしない場合は0。
        /// </summary>
        public float TryStartJump(bool jumpPressed, bool isGrounded, MoveParams moveParams)
        {
            if (!jumpPressed || !isGrounded)
            {
                return 0f;
            }

            _isJumping = true;
            _jumpHoldTimer = 0f;
            return moveParams.jumpForce;
        }

        /// <summary>
        /// 可変高度ジャンプの更新。jumpHeld中はジャンプ力を維持。
        /// 離すか最大時間に達すると減衰開始。
        /// 戻り値: 現フレームのジャンプ速度係数（0.0~1.0）。
        /// 1.0 = フルジャンプ維持中、0.0 = ジャンプ終了。
        /// </summary>
        public float UpdateJumpHold(bool jumpHeld, float deltaTime)
        {
            if (!_isJumping)
            {
                return 0f;
            }

            _jumpHoldTimer += deltaTime;

            // 最大ホールド時間超過 → ジャンプ終了
            if (_jumpHoldTimer >= k_MaxJumpHoldTime)
            {
                _isJumping = false;
                return 0f;
            }

            // ボタン離した & 最小ホールド時間経過 → ジャンプ終了
            if (!jumpHeld && _jumpHoldTimer >= k_MinJumpHoldTime)
            {
                _isJumping = false;
                return 0f;
            }

            return 1f;
        }

        /// <summary>
        /// ダッシュ開始判定。dashPressed かつ スタミナ十分 かつ ダッシュ中でない。
        /// 戻り値: ダッシュ開始したか。
        /// </summary>
        public bool TryStartDash(bool dashPressed, ref float currentStamina, MoveParams moveParams)
        {
            if (!dashPressed || _isDashing || currentStamina < k_DashStaminaCost)
            {
                return false;
            }

            currentStamina -= k_DashStaminaCost;
            _isDashing = true;
            _dashTimer = 0f;
            return true;
        }

        /// <summary>
        /// ダッシュの時間経過処理。
        /// 戻り値: ダッシュ中か。
        /// </summary>
        public bool UpdateDash(float deltaTime, MoveParams moveParams)
        {
            if (!_isDashing)
            {
                return false;
            }

            _dashTimer += deltaTime;

            if (_dashTimer >= moveParams.dashDuration)
            {
                _isDashing = false;
                return false;
            }

            return true;
        }

        /// <summary>
        /// 簡易接地判定ロジック。位置ベース（Y座標がgroundLevel以下かどうか）。
        /// 実際のRaycast接地判定はMonoBehaviour側で行い、ここは判定ロジックのテスト用。
        /// </summary>
        public static bool CheckGrounded(float positionY, float groundLevel, float threshold)
        {
            return positionY <= groundLevel + threshold;
        }
    }
}
