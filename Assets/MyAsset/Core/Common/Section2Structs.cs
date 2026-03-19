using System;

namespace Game.Core
{
    [Serializable]
    public struct ActionSlot
    {
        public ActionExecType execType;
        public int paramId;
        public float paramValue;
        public string displayName;
    }

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

    [Serializable]
    public struct AITargetSelect
    {
        public TargetSortKey sortKey;
        public Element elementFilter;
        public bool isDescending;
        public TargetFilter filter;
    }

    [Serializable]
    public struct TargetFilter
    {
        /// <summary>各フィルターのAND/OR判定切替</summary>
        public FilterBitFlag filterFlags;

        /// <summary>陣営（複数指定可）</summary>
        public CharacterBelong belong;

        /// <summary>特徴（複数指定可）</summary>
        public CharacterFeature feature;

        /// <summary>弱点属性（複数指定可）</summary>
        public Element weakPoint;

        /// <summary>距離範囲 x=min, y=max（0,0で無制限）</summary>
        public UnityEngine.Vector2 distanceRange;

        /// <summary>自分を対象に含めるか</summary>
        public bool includeSelf;
    }

    [Flags]
    public enum FilterBitFlag : byte
    {
        None         = 0,
        BelongAnd    = 1 << 0,
        FeatureAnd   = 1 << 1,
        WeakPointAnd = 1 << 2,
        IsSelf       = 1 << 3,
        IsPlayer     = 1 << 4,
    }

    [Serializable]
    public struct AIRule
    {
        public AICondition[] conditions;
        public int actionIndex;
        public byte probability;
    }

    [Serializable]
    public struct AIMode
    {
        public string modeName;
        public AIRule[] targetRules;
        public AIRule[] actionRules;
        public AITargetSelect[] targetSelects;
        public ActionSlot[] actions;
        public int defaultActionIndex;
        public UnityEngine.Vector2 judgeInterval;
    }

    [Serializable]
    public struct DamageScoreEntry
    {
        public int attackerHash;
        public float score;
        public float lastUpdateTime;
    }

    [Serializable]
    public struct ReactionTrigger
    {
        public AIConditionType triggerCondition;
        public CompareOp compareOp;
        public float threshold;
        public int reactionActionIndex;
    }
}
