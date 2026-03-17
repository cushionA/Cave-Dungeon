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
        /// weightRatio > threshold の場合、速度に (1 - (weightRatio - threshold)) を乗算する係数を返す。
        /// 最小0.5。threshold以下なら1.0を返す。
        /// </summary>
        public static float CalculateWeightPenalty(float weightRatio)
        {
            if (weightRatio <= k_WeightPenaltyThreshold)
            {
                return 1.0f;
            }

            float penalty = 1.0f - (weightRatio - k_WeightPenaltyThreshold);
            return Mathf.Max(penalty, k_WeightPenaltyMinimum);
        }
    }
}
