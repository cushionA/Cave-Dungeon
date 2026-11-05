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
