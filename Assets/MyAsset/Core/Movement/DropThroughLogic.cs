namespace Game.Core
{
    /// <summary>
    /// 一方通行プラットフォーム (drop-through) の判定ロジック。
    /// MonoBehaviour 非依存のピュアロジック。static メソッドのみ。
    ///
    /// 下入力 + ジャンプ入力 + 接地中 → drop-through 発動。
    /// 実際のレイヤー切替・時間管理は呼び出し側 (PlayerCharacter) が担当する。
    /// </summary>
    public static class DropThroughLogic
    {
        /// <summary>
        /// drop-through 無敵 (すり抜け) 状態を維持する時間 (秒)。
        /// プレイヤーがプラットフォームの下側まで確実に抜けられる長さ。
        /// </summary>
        public const float k_DropThroughDurationSeconds = 0.3f;

        /// <summary>
        /// 下入力を drop-through 発動条件とみなす閾値 (Y軸 input)。
        /// </summary>
        public const float k_DownInputThreshold = 0.5f;

        /// <summary>
        /// drop-through を発動できるか判定する。
        /// 条件: 下方向入力 + ジャンプ入力 + 接地中。
        /// </summary>
        /// <param name="downPressed">下方向入力があるか</param>
        /// <param name="jumpPressed">ジャンプ入力があるか</param>
        /// <param name="isGrounded">現在接地中か</param>
        /// <param name="duration">発動時の持続時間 (秒)。false 時は 0。</param>
        /// <returns>発動可ならtrue</returns>
        public static bool TryDropThrough(bool downPressed, bool jumpPressed, bool isGrounded, out float duration)
        {
            if (!downPressed || !jumpPressed || !isGrounded)
            {
                duration = 0f;
                return false;
            }

            duration = k_DropThroughDurationSeconds;
            return true;
        }

        /// <summary>
        /// Y軸入力値から「下入力中」を判定するヘルパー。
        /// </summary>
        public static bool IsDownInput(float inputY)
        {
            return inputY <= -k_DownInputThreshold;
        }
    }
}
