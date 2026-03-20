namespace Game.Core
{
    /// <summary>
    /// 状況ダメージボーナス判定ロジック。
    /// カウンター・背面攻撃・怯み中ヒットのボーナス倍率を判定する。
    /// 重複なし: 複数条件を満たす場合は最大倍率のもののみ適用。
    /// </summary>
    public static class SituationalBonusLogic
    {
        /// <summary>カウンターボーナス倍率（攻撃中の敵にヒット）</summary>
        public const float k_CounterBonusMult = 1.3f;

        /// <summary>背面攻撃ボーナス倍率</summary>
        public const float k_BackstabBonusMult = 1.25f;

        /// <summary>怯み中ヒットボーナス倍率</summary>
        public const float k_StaggerHitBonusMult = 1.2f;

        /// <summary>
        /// 状況ボーナスを評価する。重複なし（最大値のみ適用）。
        /// 優先順: Counter(1.3) > Backstab(1.25) > StaggerHit(1.2)
        /// </summary>
        /// <param name="isTargetAttacking">対象が攻撃状態か</param>
        /// <param name="isAttackFromBehind">背面からの攻撃か</param>
        /// <param name="isTargetInHitstun">対象が怯み/スタン中か</param>
        /// <returns>(倍率, ボーナス種別)</returns>
        public static (float multiplier, SituationalBonus bonus) Evaluate(
            bool isTargetAttacking,
            bool isAttackFromBehind,
            bool isTargetInHitstun)
        {
            float bestMult = 1.0f;
            SituationalBonus bestBonus = SituationalBonus.None;

            if (isTargetAttacking && k_CounterBonusMult > bestMult)
            {
                bestMult = k_CounterBonusMult;
                bestBonus = SituationalBonus.Counter;
            }

            if (isAttackFromBehind && k_BackstabBonusMult > bestMult)
            {
                bestMult = k_BackstabBonusMult;
                bestBonus = SituationalBonus.Backstab;
            }

            if (isTargetInHitstun && k_StaggerHitBonusMult > bestMult)
            {
                bestMult = k_StaggerHitBonusMult;
                bestBonus = SituationalBonus.StaggerHit;
            }

            return (bestMult, bestBonus);
        }

        /// <summary>
        /// ActStateが攻撃中（カウンター対象）かどうか判定する。
        /// AttackPrep, Attacking, AttackRecovery がカウンター対象。
        /// </summary>
        public static bool IsTargetAttacking(ActState state)
        {
            return state == ActState.AttackPrep
                || state == ActState.Attacking
                || state == ActState.AttackRecovery;
        }
    }
}
