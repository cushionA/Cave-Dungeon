using System;
using UnityEngine;

namespace Game.Core
{

    [Serializable]
    public struct CharacterGrowth
    {
        public int level;
        public StatModifier allocatedStats;
    }
}
