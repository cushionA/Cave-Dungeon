using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Game.Core
{

    [Serializable]
    public struct AICondition
    {
        /// <summary>条件の種類</summary>
        public AIConditionType conditionType;

        /// <summary>比較演算子</summary>
        public CompareOp compareOp;

        /// <summary>
        /// 比較値A（閾値下限、ビットフラグ等）。
        /// conditionTypeに応じて意味が変わる:
        /// - HpRatio/MpRatio等: 数値閾値
        /// - ProjectileNear: 旧データの距離[m]（現在は評価しない）
        /// - NearbyHostileProjectileCount: 固定半径の敵対飛翔体の数
        /// - ObjectNearby: RecognizeObjectTypeのビットフラグ
        /// - EventFired: BrainEventFlagTypeのビットフラグ
        /// </summary>
        public int operandA;

        /// <summary>
        /// 比較値B（InRangeの上限値）。
        /// InRange以外では未使用。
        /// </summary>
        public int operandB;

        /// <summary>チェック対象を絞り込むフィルター</summary>
        public TargetFilter filter;
    }
}
