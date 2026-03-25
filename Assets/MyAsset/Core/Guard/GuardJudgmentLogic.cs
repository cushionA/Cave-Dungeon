using UnityEngine;

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

        /// <summary>ジャストガード時のスタミナ回復量</summary>
        public const float k_JustGuardStaminaRecovery = 15f;

        /// <summary>ジャストガード時のアーマー回復量</summary>
        public const float k_JustGuardArmorRecovery = 10f;

        private const float k_ReductionGuarded = 0.7f;
        private const float k_ReductionJustGuard = 1.0f;
        private const float k_ReductionEnhancedGuard = 0.9f;
        private const float k_ReductionNone = 0f;

        /// <summary>
        /// ガード結果を判定する。
        /// 判定優先順:
        ///   1. isGuarding=false => NoGuard
        ///   2. Unparriable => NoGuard
        ///   3. ガード方向不一致 => NoGuard
        ///   4. guardTimeSinceStart &lt;= EnhancedGuardWindow &amp;&amp; !JustGuardImmune => EnhancedGuard
        ///   5. guardTimeSinceStart &lt;= JustGuardWindow &amp;&amp; !JustGuardImmune => JustGuard
        ///   6. GuardAttack効果中 => ガードブレイクをGuardedに昇格
        ///   7. attackPower > guardStrength => GuardBreak（GuardAttack中はGuarded）
        ///   8. else => Guarded
        /// </summary>
        public static GuardResult Judge(
            bool isGuarding,
            float guardTimeSinceStart,
            float guardStrength,
            float attackPower,
            AttackFeature attackFeature,
            GuardDirection guardDirection,
            bool isAttackFromFront,
            bool hasGuardAttackEffect)
        {
            if (!isGuarding)
            {
                return GuardResult.NoGuard;
            }

            if ((attackFeature & AttackFeature.Unparriable) != 0)
            {
                return GuardResult.NoGuard;
            }

            // ガード方向チェック
            if (!IsGuardDirectionValid(guardDirection, isAttackFromFront))
            {
                return GuardResult.NoGuard;
            }

            bool isJustGuardImmune = (attackFeature & AttackFeature.JustGuardImmune) != 0;

            // EnhancedGuard (0~0.05s) を先に判定。JustGuard (0~0.1s) より狭い窓なので先にチェックする。
            // JustGuardImmuneはJustGuardのみ無効化し、EnhancedGuardには影響しない。
            if (guardTimeSinceStart < k_EnhancedGuardWindow)
            {
                return GuardResult.EnhancedGuard;
            }

            if (guardTimeSinceStart <= k_JustGuardWindow && !isJustGuardImmune)
            {
                return GuardResult.JustGuard;
            }

            if (attackPower > guardStrength)
            {
                // GuardAttack効果中はブレイクしない
                if (hasGuardAttackEffect)
                {
                    return GuardResult.Guarded;
                }
                return GuardResult.GuardBreak;
            }

            return GuardResult.Guarded;
        }

        /// <summary>
        /// 旧シグネチャ互換（方向チェック・GuardAttack なし）。
        /// </summary>
        public static GuardResult Judge(
            bool isGuarding,
            float guardTimeSinceStart,
            float guardStrength,
            float attackPower,
            AttackFeature attackFeature)
        {
            return Judge(isGuarding, guardTimeSinceStart, guardStrength, attackPower,
                attackFeature, GuardDirection.Front, true, false);
        }

        /// <summary>
        /// ガード方向が攻撃方向に対して有効か判定する。
        /// </summary>
        public static bool IsGuardDirectionValid(GuardDirection guardDirection, bool isAttackFromFront)
        {
            switch (guardDirection)
            {
                case GuardDirection.Front:
                    return isAttackFromFront;
                case GuardDirection.Back:
                    return !isAttackFromFront;
                case GuardDirection.Both:
                    return true;
                default:
                    return false;
            }
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

        /// <summary>
        /// ガードが成功したかどうかを判定する（Guarded / JustGuard / EnhancedGuard）。
        /// NoGuard と GuardBreak は false を返す。
        /// </summary>
        public static bool IsGuardSucceeded(GuardResult result)
        {
            return result == GuardResult.Guarded
                || result == GuardResult.JustGuard
                || result == GuardResult.EnhancedGuard;
        }

        /// <summary>
        /// ジャストガード成功時のスタミナ・アーマー回復を適用する。
        /// </summary>
        public static void ApplyJustGuardRecovery(
            ref float currentStamina, float maxStamina,
            ref float currentArmor, float maxArmor)
        {
            currentStamina = Mathf.Min(maxStamina, currentStamina + k_JustGuardStaminaRecovery);
            currentArmor = Mathf.Min(maxArmor, currentArmor + k_JustGuardArmorRecovery);
        }
    }
}
