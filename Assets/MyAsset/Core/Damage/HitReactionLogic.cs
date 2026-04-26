namespace Game.Core
{
    /// <summary>
    /// 被弾リアクション判定ロジック。
    /// アーマー状態、特殊効果、攻撃属性からリアクション種別を決定するpureロジック。
    /// </summary>
    public static class HitReactionLogic
    {
        /// <summary>ガードブレイク硬直時間（秒）</summary>
        public const float k_GuardBreakStunDuration = 1.0f;

        /// <summary>怯み硬直時間（秒）</summary>
        public const float k_FlinchDuration = 0.3f;

        /// <summary>
        /// 被弾時のリアクションを判定する。
        /// 判定優先順:
        ///   1. ガードブレイク → HitReaction.GuardBreak
        ///   2. ガード成功 → HitReaction.None
        ///   3. SuperArmor → HitReaction.None
        ///   4. ヒットスタン中（Flinch/GuardBroken/Stunned）+ 吹き飛ばし → Knockback（KnockbackImmunity時はFlinch）
        ///   5. アーマー > 0 → HitReaction.None
        ///   6. 吹き飛ばし力あり → Knockback（KnockbackImmunity時はFlinch）
        ///   7. それ以外 → Flinch
        /// </summary>
        public static HitReaction Determine(
            bool hasSuperArmor,
            bool hasKnockbackImmunity,
            float totalArmorBefore,
            bool hasKnockbackForce,
            GuardResult guardResult,
            ActState currentState)
        {
            // 1. ガードブレイク
            if (guardResult == GuardResult.GuardBreak)
            {
                return HitReaction.GuardBreak;
            }

            // 2. ガード成功（Guarded/JustGuard）
            if (guardResult == GuardResult.Guarded
                || guardResult == GuardResult.JustGuard)
            {
                return HitReaction.None;
            }

            // 3. SuperArmor
            if (hasSuperArmor)
            {
                return HitReaction.None;
            }

            // 4. ヒットスタン中に吹き飛ばし攻撃 → アーマー無視でKnockback
            bool isInHitstun = currentState == ActState.Flinch
                || currentState == ActState.GuardBroken
                || currentState == ActState.Stunned;

            if (isInHitstun && hasKnockbackForce)
            {
                if (hasKnockbackImmunity)
                {
                    return HitReaction.Flinch;
                }
                return HitReaction.Knockback;
            }

            // 5. アーマーが残っている → 怯まない
            if (totalArmorBefore > 0f)
            {
                return HitReaction.None;
            }

            // 6. 吹き飛ばし力あり
            if (hasKnockbackForce)
            {
                if (hasKnockbackImmunity)
                {
                    return HitReaction.Flinch;
                }
                return HitReaction.Knockback;
            }

            // 7. 通常怯み
            return HitReaction.Flinch;
        }

        /// <summary>
        /// ヒットスタン状態（Flinch/GuardBroken/Stunned）かどうか。
        /// この状態中は吹き飛ばし攻撃でKnockbackに遷移可能。
        /// </summary>
        public static bool IsInHitstun(ActState state)
        {
            return state == ActState.Flinch
                || state == ActState.GuardBroken
                || state == ActState.Stunned;
        }

        /// <summary>
        /// Flinch 解除瞬間 (前フレームが Flinch、現フレームが非 Flinch) かを判定する。
        /// true の場合、呼び出し側はキャラクタの currentArmor を maxArmor へ満タン復帰させる。
        /// Flinch 中は <see cref="DamageReceiver"/> が currentArmor=0 を強制しているため、
        /// 解除瞬間に元の最大値へ戻すことで「Flinch=完全無防備、解除後は再び armor 持ち」の挙動になる。
        /// </summary>
        public static bool ShouldResetArmorOnFlinchExit(ActState previousState, ActState currentState)
        {
            return previousState == ActState.Flinch && currentState != ActState.Flinch;
        }
    }
}
