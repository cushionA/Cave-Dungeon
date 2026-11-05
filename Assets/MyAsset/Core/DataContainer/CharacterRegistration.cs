using System;
using UnityEngine;

namespace Game.Core
{

    public struct CharacterRegistration
    {
        public CharacterBaseStats baseStats;
        public CharacterGrowth growth;
        public CharacterFlags flags;
        public CharacterDefenseState defenseState;
    }
}
