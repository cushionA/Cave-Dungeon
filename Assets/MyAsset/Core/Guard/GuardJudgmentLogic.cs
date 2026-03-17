namespace Game.Core
{
    /// <summary>
    /// ガード判定ロジック。
    /// タイミングベースのガード判定、ジャストガード、強化ガード、ガードブレイクを処理する。
    /// </summary>
    public static class GuardJudgmentLogic
    {
        /// <summary>ジャストガード受付時間（秒）</summary>
        public const float k_JustGuardWindow = 0.1f;

        /// <summary>強化ガード受付時間（秒）。JustGuardWindowより短い。</summary>
        public const float k_EnhancedGuardWindow = 0.05f;

        private const float k_ReductionGuarded = 0.7f;
        private const float k_ReductionJustGuard = 1.0f;
        private const float k_ReductionEnhancedGuard = 0.9f;
        private const float k_ReductionNone = 0f;

        /// <summary>
        /// ガード結果を判定する。
        /// 判定優先順:
        ///   1. isGuarding=false => NoGuard
        ///   2. Unparriable => NoGuard
        ///   3. guardTimeSinceStart &lt;= JustGuardWindow &amp;&amp; !JustGuardImmune => JustGuard
        ///   4. guardTimeSinceStart &lt;= EnhancedGuardWindow => EnhancedGuard
        ///   5. attackPower > guardStrength => GuardBreak
        ///   6. else => Guarded
        /// </summary>
        public static GuardResult Judge(
            bool isGuarding,
            float guardTimeSinceStart,
            float guardStrength,
            float attackPower,
            AttackFeature attackFeature)
        {
            if (!isGuarding)
            {
                return GuardResult.NoGuard;
            }

            if ((attackFeature & AttackFeature.Unparriable) != 0)
            {
                return GuardResult.NoGuard;
            }

            bool isJustGuardImmune = (attackFeature & AttackFeature.JustGuardImmune) != 0;

            if (guardTimeSinceStart <= k_JustGuardWindow && !isJustGuardImmune)
            {
                return GuardResult.JustGuard;
            }

            if (guardTimeSinceStart <= k_EnhancedGuardWindow)
            {
                return GuardResult.EnhancedGuard;
            }

            if (attackPower > guardStrength)
            {
                return GuardResult.GuardBreak;
            }

            return GuardResult.Guarded;
        }

        /// <summary>
        /// ガードによるダメージ軽減率を取得する。
        /// NoGuard => 0, Guarded => 0.7, JustGuard => 1.0, GuardBreak => 0, EnhancedGuard => 0.9
        /// </summary>
        public static float GetDamageReduction(GuardResult result)
        {
            switch (result)
            {
                case GuardResult.Guarded:
                    return k_ReductionGuarded;
                case GuardResult.JustGuard:
                    return k_ReductionJustGuard;
                case GuardResult.EnhancedGuard:
                    return k_ReductionEnhancedGuard;
                case GuardResult.NoGuard:
                case GuardResult.GuardBreak:
                default:
                    return k_ReductionNone;
            }
        }
    }
}
