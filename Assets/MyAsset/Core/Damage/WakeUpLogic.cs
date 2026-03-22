namespace Game.Core
{
    /// <summary>
    /// 起き上がり無敵ロジック。
    /// Knockbacked+着地→WakeUp遷移、持続時間経過→Neutral遷移+無敵解除。
    /// </summary>
    public static class WakeUpLogic
    {
        /// <summary>起き上がりデフォルト持続時間（秒）</summary>
        public const float k_DefaultWakeUpDuration = 0.5f;

        public struct Result
        {
            public ActState newState;
            public bool isInvincible;
        }

        /// <summary>
        /// 起き上がり遷移を評価する。
        /// </summary>
        /// <param name="currentState">現在のActState。</param>
        /// <param name="isGrounded">接地しているか。</param>
        /// <param name="wakeUpElapsed">WakeUp状態の経過時間。</param>
        /// <param name="wakeUpDuration">WakeUp状態の持続時間。</param>
        /// <returns>新しい状態と無敵かどうか。</returns>
        public static Result Evaluate(
            ActState currentState, bool isGrounded,
            float wakeUpElapsed, float wakeUpDuration)
        {
            // Knockbacked + 着地 → WakeUp遷移
            if (currentState == ActState.Knockbacked && isGrounded)
            {
                return new Result
                {
                    newState = ActState.WakeUp,
                    isInvincible = true
                };
            }

            // WakeUp中: 持続時間チェック
            if (currentState == ActState.WakeUp)
            {
                if (wakeUpElapsed >= wakeUpDuration)
                {
                    // 持続時間経過 → Neutral遷移 + 無敵解除
                    return new Result
                    {
                        newState = ActState.Neutral,
                        isInvincible = false
                    };
                }

                // まだ起き上がり中 → 無敵維持
                return new Result
                {
                    newState = ActState.WakeUp,
                    isInvincible = true
                };
            }

            // 非関連状態はパススルー
            // Note: Knockbacked && !isGrounded のケースもここに来る。
            // 吹き飛ばし空中中は追撃可能（仕様）。着地するまでWakeUpに遷移しない。
            return new Result
            {
                newState = currentState,
                isInvincible = false
            };
        }

        /// <summary>
        /// 指定状態がWakeUpかどうか。
        /// </summary>
        public static bool IsWakeUpState(ActState state)
        {
            return state == ActState.WakeUp;
        }
    }
}
