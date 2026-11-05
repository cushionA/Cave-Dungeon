using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Game.Core
{

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
}
