using System;

namespace Game.Core
{
    [Serializable]
    public struct CharacterDefenseState
    {
        public bool isGuarding;
        public float guardElapsedTime;
        public float continuousJustGuardUntil;
        public float actionElapsedTime;
        public float actionArmorConsumed;
        public float armorRecoveryTimer;
        public float armorRecoveryDelay;
        public float armorRecoveryRate;
        public float maxArmor;
        public ActState previousActState;
    }
}
