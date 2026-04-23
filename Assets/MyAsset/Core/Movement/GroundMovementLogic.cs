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

        // デフォルト値（MoveParams に値が設定されていない場合のフォールバック）。
        // データ駆動化: キャラ別調整値は MoveParams.dodgeDuration / dodgeSpeedMultiplier /
        // sprintSpeedMultiplier を使用する (GameManager.RegisterCharacter で CharacterInfo から転記)。
        public const float k_DodgeDuration = 0.25f;
        public const float k_DodgeSpeedMultiplier = 2.5f;
        public const float k_SprintSpeedMultiplier = 1.6f;

        /// <summary>
        /// Coyote Time: 接地から離れた後でもこの秒数以内はジャンプ入力を許容する猶予時間。
        /// プラットフォーマ系の定番ゲームフィール補正。
        /// データ駆動化: キャラ別値は MoveParams.coyoteTime を使用
        /// (GameManager.RegisterCharacter で CharacterInfo.coyoteTime から転記)。未設定時は本定数にフォールバック。
        /// </summary>
        public const float k_CoyoteTimeSeconds = 0.1f;

        // テスト互換用デフォルト値
        public const float k_DodgeStaminaCost = 15f;

        private bool _isJumping;
        private float _jumpHoldTimer;
        private bool _isDodging;
        private float _dodgeTimer;
        private bool _isSprinting;

        // Coyote Time: 直近接地からの経過秒数（常時累積。接地中は 0 に保たれる）。
        // 初期値 float.MaxValue は「未だ一度も接地していない」状態（空中スポーン等）で
        // Coyote ジャンプが誤発動しないようにするためのガード。
        private float _timeSinceLeftGround = float.MaxValue;

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
            _timeSinceLeftGround = float.MaxValue;
        }
#endif

        /// <summary>
        /// 接地状態を毎フレーム通知する。接地中は _timeSinceLeftGround を 0 に保ち、
        /// 離れた後は deltaTime ごとに加算することで Coyote Time 判定用の経過時間を維持する。
        /// 呼び出し側は FixedUpdate 等で TryStartJump の前に必ず呼ぶこと。
        /// </summary>
        public void UpdateGroundedState(bool isGrounded, float deltaTime)
        {
            if (isGrounded)
            {
                _timeSinceLeftGround = 0f;
                return;
            }

            if (_timeSinceLeftGround == float.MaxValue)
            {
                return;
            }

            _timeSinceLeftGround += deltaTime;
        }

        /// <summary>
        /// 現在 Coyote Time 窓内か (定数フォールバック版)。既存テスト互換用に残置。
        /// 本体 (TryStartJump) はデータ駆動版 <see cref="IsInCoyoteWindowFor(MoveParams)"/> を使用しており、
        /// 本プロパティはプロダクションコードから呼ばれていない。
        /// 既存テスト (CoyoteTimeTests) が全て IsInCoyoteWindowFor 側へ移行した段階で削除予定。
        /// </summary>
        public bool IsInCoyoteWindow => _timeSinceLeftGround < k_CoyoteTimeSeconds;

        /// <summary>
        /// 指定の MoveParams で Coyote Time 窓内か判定する (データ駆動版)。
        /// moveParams.coyoteTime &gt; 0 ならその値、それ以外は定数 <see cref="k_CoyoteTimeSeconds"/> にフォールバック。
        /// </summary>
        public bool IsInCoyoteWindowFor(MoveParams moveParams)
        {
            return _timeSinceLeftGround < GetEffectiveCoyoteTime(moveParams);
        }

        /// <summary>
        /// MoveParams.coyoteTime の有効値を返す。
        /// 0 以下 (未設定) の場合は定数 <see cref="k_CoyoteTimeSeconds"/> にフォールバックし、
        /// 既存 CharacterInfo アセットで coyoteTime が未設定でも従来通りの挙動を保つ。
        /// </summary>
        private static float GetEffectiveCoyoteTime(MoveParams p)
        {
            return p.coyoteTime > 0f ? p.coyoteTime : k_CoyoteTimeSeconds;
        }

        /// <summary>水平移動速度を計算する。facingDir は回避時の方向（1=右, -1=左）。</summary>
        public float CalculateHorizontalSpeed(float inputX, MoveParams moveParams, float facingDir = 1f)
        {
            if (_isDodging)
            {
                // 入力方向が無い場合は向いている方向に回避
                float dodgeDir = Mathf.Abs(inputX) > 0.1f ? Mathf.Sign(inputX) : facingDir;

                // dashSpeed が設定されていればそれを使い、それが 0 の場合は
                // moveSpeed * dodgeSpeedMultiplier (キャラ別データ駆動、未設定なら定数フォールバック) を使う。
                if (moveParams.dashSpeed > 0f)
                {
                    return dodgeDir * moveParams.dashSpeed;
                }
                return dodgeDir * moveParams.moveSpeed * GetEffectiveDodgeSpeedMultiplier(moveParams);
            }

            float speed = _isSprinting
                ? moveParams.moveSpeed * GetEffectiveSprintSpeedMultiplier(moveParams)
                : moveParams.moveSpeed;
            return inputX * speed;
        }

        /// <summary>MoveParams.dodgeDuration の有効値 (0 以下なら定数フォールバック)。</summary>
        private static float GetEffectiveDodgeDuration(MoveParams p)
        {
            return p.dodgeDuration > 0f ? p.dodgeDuration : k_DodgeDuration;
        }

        /// <summary>MoveParams.dodgeSpeedMultiplier の有効値 (0 以下なら定数フォールバック)。</summary>
        private static float GetEffectiveDodgeSpeedMultiplier(MoveParams p)
        {
            return p.dodgeSpeedMultiplier > 0f ? p.dodgeSpeedMultiplier : k_DodgeSpeedMultiplier;
        }

        /// <summary>MoveParams.sprintSpeedMultiplier の有効値 (0 以下なら定数フォールバック)。</summary>
        private static float GetEffectiveSprintSpeedMultiplier(MoveParams p)
        {
            return p.sprintSpeedMultiplier > 0f ? p.sprintSpeedMultiplier : k_SprintSpeedMultiplier;
        }

        /// <summary>
        /// ジャンプ開始判定。スタミナ消費あり。
        /// 接地中、または接地を離れてから k_CoyoteTimeSeconds 秒以内なら許容する (Coyote Time)。
        /// ジャンプ成立時に Coyote 窓を消費する (空中で 2 回目のジャンプは不可)。
        /// </summary>
        public float TryStartJump(bool jumpPressed, bool isGrounded, MoveParams moveParams,
            ref float currentStamina)
        {
            if (!jumpPressed)
            {
                return 0f;
            }

            bool allowed = isGrounded || IsInCoyoteWindowFor(moveParams);
            if (!allowed)
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
            // Coyote 窓を消費して空中連打を防ぐ
            _timeSinceLeftGround = float.MaxValue;
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

            if (_dodgeTimer >= GetEffectiveDodgeDuration(moveParams))
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
