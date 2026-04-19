namespace Game.Core
{
    /// <summary>
    /// ガード結果に応じたフィードバックデータ。
    /// エフェクト種別、ヒットストップ時間、カメラシェイク強度を保持する。
    /// </summary>
    public struct GuardFeedbackData
    {
        public GuardResult result;
        public float hitStopDuration;
        public float cameraShakeIntensity;
        public bool playGuardEffect;
        public bool playBreakEffect;
    }

    /// <summary>
    /// GuardResultからGuardFeedbackDataを生成する純粋ロジック。
    /// 実際のエフェクト再生やカメラ処理はMonoBehaviour側で行う。
    /// </summary>
    public static class GuardFeedbackLogic
    {
        public const float k_NormalHitStop = 0.05f;
        public const float k_JustGuardHitStop = 0.15f;
        public const float k_GuardBreakHitStop = 0.2f;
        public const float k_NormalShake = 0.1f;
        public const float k_JustGuardShake = 0.3f;
        public const float k_GuardBreakShake = 0.5f;

        /// <summary>GuardResultからフィードバックデータを生成</summary>
        public static GuardFeedbackData CreateFeedback(GuardResult result)
        {
            switch (result)
            {
                case GuardResult.Guarded:
                    return new GuardFeedbackData
                    {
                        result = GuardResult.Guarded,
                        hitStopDuration = k_NormalHitStop,
                        cameraShakeIntensity = k_NormalShake,
                        playGuardEffect = true,
                        playBreakEffect = false,
                    };

                case GuardResult.JustGuard:
                    return new GuardFeedbackData
                    {
                        result = GuardResult.JustGuard,
                        hitStopDuration = k_JustGuardHitStop,
                        cameraShakeIntensity = k_JustGuardShake,
                        playGuardEffect = true,
                        playBreakEffect = false,
                    };

                case GuardResult.GuardBreak:
                    return new GuardFeedbackData
                    {
                        result = GuardResult.GuardBreak,
                        hitStopDuration = k_GuardBreakHitStop,
                        cameraShakeIntensity = k_GuardBreakShake,
                        playGuardEffect = false,
                        playBreakEffect = true,
                    };

                case GuardResult.NoGuard:
                default:
                    return new GuardFeedbackData
                    {
                        result = GuardResult.NoGuard,
                        hitStopDuration = 0f,
                        cameraShakeIntensity = 0f,
                        playGuardEffect = false,
                        playBreakEffect = false,
                    };
            }
        }
    }
}
