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
    }
}
