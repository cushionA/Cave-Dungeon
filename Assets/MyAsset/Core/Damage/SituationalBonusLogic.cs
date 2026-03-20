using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// 状況ダメージボーナスの設定値。ScriptableObjectから注入可能。
    /// </summary>
    [Serializable]
    public struct SituationalBonusConfig
    {
        [LabelText("カウンター倍率"), MinValue(1f)]
        public float counterMultiplier;

        [LabelText("背面攻撃倍率"), MinValue(1f)]
        public float backstabMultiplier;

        [LabelText("怯み中ヒット倍率"), MinValue(1f)]
        public float staggerHitMultiplier;

        /// <summary>デフォルト設定を返す。</summary>
        public static SituationalBonusConfig Default => new SituationalBonusConfig
        {
            counterMultiplier = 1.3f,
            backstabMultiplier = 1.25f,
            staggerHitMultiplier = 1.2f,
        };
    }

    /// <summary>
    /// 状況ダメージボーナス判定ロジック。
    /// カウンター・背面攻撃・怯み中ヒットのボーナス倍率を判定する。
    /// 重複なし: 複数条件を満たす場合は最大倍率のもののみ適用。
    /// </summary>
    public static class SituationalBonusLogic
    {
        /// <summary>
        /// 状況ボーナスを評価する。重複なし（最大値のみ適用）。
        /// </summary>
        public static (float multiplier, SituationalBonus bonus) Evaluate(
            bool isTargetAttacking,
            bool isAttackFromBehind,
            bool isTargetInHitstun,
            in SituationalBonusConfig config)
        {
            float bestMult = 1.0f;
            SituationalBonus bestBonus = SituationalBonus.None;

            if (isTargetAttacking && config.counterMultiplier > bestMult)
            {
                bestMult = config.counterMultiplier;
                bestBonus = SituationalBonus.Counter;
            }

            if (isAttackFromBehind && config.backstabMultiplier > bestMult)
            {
                bestMult = config.backstabMultiplier;
                bestBonus = SituationalBonus.Backstab;
            }

            if (isTargetInHitstun && config.staggerHitMultiplier > bestMult)
            {
                bestMult = config.staggerHitMultiplier;
                bestBonus = SituationalBonus.StaggerHit;
            }

            return (bestMult, bestBonus);
        }

        /// <summary>
        /// デフォルト設定で評価する（旧シグネチャ互換）。
        /// </summary>
        public static (float multiplier, SituationalBonus bonus) Evaluate(
            bool isTargetAttacking,
            bool isAttackFromBehind,
            bool isTargetInHitstun)
        {
            return Evaluate(isTargetAttacking, isAttackFromBehind, isTargetInHitstun,
                SituationalBonusConfig.Default);
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
