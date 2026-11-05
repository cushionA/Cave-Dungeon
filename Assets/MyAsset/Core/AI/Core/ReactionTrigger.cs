using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Game.Core
{

    [Serializable]
    public struct ReactionTrigger
    {
        public AIConditionType triggerCondition;
        public CompareOp compareOp;
        public float threshold;
        public int reactionActionIndex;
    }
}
