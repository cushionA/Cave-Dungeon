using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Game.Core
{

    [Serializable]
    public struct AITargetSelect
    {
        public TargetSortKey sortKey;
        public Element elementFilter;
        public bool isDescending;
        public TargetFilter filter;
        [Tooltip("候補がない場合は次の標的ルールを試す")]
        public bool continueIfEmpty;

        /// <summary>旧設定の属性指定はORで読み、明示的な編集ではフィルタ側に一本化する。</summary>
        public TargetFilter EffectiveFilter
        {
            readonly get
            {
                TargetFilter effective = filter;
                if (effective.weakPoint == 0 && elementFilter != 0)
                {
                    effective.weakPoint = elementFilter;
                    effective.filterFlags &= ~FilterBitFlag.WeakPointAnd;
                }
                return effective;
            }
            set
            {
                filter = value;
                elementFilter = 0;
            }
        }
    }
}
