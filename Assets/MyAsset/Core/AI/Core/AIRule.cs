using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Game.Core
{

    [Serializable]
    public struct AIRule
    {
        public AICondition[] conditions;
        public int actionIndex;
        public byte probability;
    }
}
