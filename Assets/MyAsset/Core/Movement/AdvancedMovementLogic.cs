using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// 高度な移動ロジック（壁蹴り、壁張り付き、落下攻撃、重量ペナルティ）。
    /// MonoBehaviour非依存のピュアロジック。static メソッドのみ。
    /// </summary>
    public class AdvancedMovementLogic
    {
        public const float k_WallClingSlideSpeed = 1.0f;
        public const float k_WallKickForceX = 8.0f;
        public const float k_WallKickForceY = 10.0f;
        public const float k_DropAttackMinHeight = 2.0f;
        public const float k_WeightPenaltyThreshold = 0.7f;
        private const float k_WeightPenaltyMinimum = 0.5f;

        /// <summary>壁IDなし（未接触 or 不明）を表す定数。</summary>
        public const int k_NoWallId = 0;

        /// <summary>
        /// 壁蹴りを試みる（左右交互制約あり）。
        /// 前回と同一壁ID（currentWallColliderId == lastKickedWallId）の場合は失敗する。
        /// 成功時は newLastKickedWallId に currentWallColliderId をセットする。
        /// 着地時は lastKickedWallId を k_NoWallId にリセットすることで再蹴りを許可する。
        /// </summary>
        public static Vector2 TryWallKickWithAlternation(
            AbilityFlag abilityFlags,
            bool isTouchingWall,
            bool jumpPressed,
            bool isFacingRight,
            int currentWallColliderId,
            int lastKickedWallId,
            out int newLastKickedWallId)
        {
            newLastKickedWallId = lastKickedWallId;

            bool hasWallKick = (abilityFlags & AbilityFlag.WallKick) != 0;
            if (!hasWallKick || !isTouchingWall || !jumpPressed)
            {
                return Vector2.zero;
            }

            // 同一壁への連続蹴り禁止（ID 不明の場合は制約なし）
            if (currentWallColliderId != k_NoWallId && currentWallColliderId == lastKickedWallId)
            {
                return Vector2.zero;
            }

            newLastKickedWallId = currentWallColliderId;
            float directionX = isFacingRight ? k_WallKickForceX : -k_WallKickForceX;
            return new Vector2(directionX, k_WallKickForceY);
        }

        /// <summary>
        /// WallKickフラグ所持+壁に接触中+ジャンプ押下 → (横方向, 上方向)のキック力を返す。
        /// 条件未達ならVector2.zeroを返す。
        /// 横方向はisFacingRightに応じて符号反転（壁から離れる方向）。
        /// </summary>
        public static Vector2 TryWallKick(AbilityFlag abilityFlags, bool isTouchingWall, bool jumpPressed, bool isFacingRight)
        {
            bool hasWallKick = (abilityFlags & AbilityFlag.WallKick) != 0;

            if (!hasWallKick || !isTouchingWall || !jumpPressed)
            {
                return Vector2.zero;
            }

            float directionX = isFacingRight ? k_WallKickForceX : -k_WallKickForceX;
            return new Vector2(directionX, k_WallKickForceY);
        }

        /// <summary>
        /// WallClingフラグ所持+壁に接触中 → 滑り速度(k_WallClingSlideSpeed)を返す。
        /// 条件未達なら0を返す。
        /// </summary>
        public static float GetWallClingSlideSpeed(AbilityFlag abilityFlags, bool isTouchingWall)
        {
            bool hasWallCling = (abilityFlags & AbilityFlag.WallCling) != 0;

            if (!hasWallCling || !isTouchingWall)
            {
                return 0f;
            }

            return k_WallClingSlideSpeed;
        }

        /// <summary>
        /// 空中(非接地) かつ 高さがMinHeight以上 かつ 下入力中 → trueで落下攻撃遷移可能。
        /// </summary>
        public static bool CanStartDropAttack(bool isGrounded, float heightAboveGround, bool downInputHeld)
        {
            if (isGrounded)
            {
                return false;
            }

            if (heightAboveGround < k_DropAttackMinHeight)
            {
                return false;
            }

            return downInputHeld;
        }

        /// <summary>
        /// weightRatio > threshold の場合、速度に減衰係数を返す。
        /// 0.7以下→1.0、0.7~1.0→線形補間で1.0→0.5。
        /// EquipmentStatCalculator.CalculatePerformanceMultiplierと同一ロジック。
        /// </summary>
        public static float CalculateWeightPenalty(float weightRatio)
        {
            return EquipmentStatCalculator.CalculatePerformanceMultiplier(weightRatio);
        }
    }
}
