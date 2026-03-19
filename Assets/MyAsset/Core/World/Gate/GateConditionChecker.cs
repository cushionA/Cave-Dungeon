using System;

namespace Game.Core
{
    [Serializable]
    public struct GateDefinition
    {
        public string gateId;
        public GateType gateType;
        public AbilityFlag requiredAbility;
        public int requiredItemId;
        public string requiredClearFlag;
        public bool isPermanent;
    }

    public static class GateConditionChecker
    {
        public static bool Evaluate(GateDefinition definition, CharacterFlags playerFlags,
            Func<int, bool> hasItem, Func<string, bool> hasFlag)
        {
            switch (definition.gateType)
            {
                case GateType.Clear:
                    return hasFlag != null && hasFlag(definition.requiredClearFlag);

                case GateType.Ability:
                    return (playerFlags.AbilityFlags & definition.requiredAbility) != 0;

                case GateType.Key:
                    return hasItem != null && hasItem(definition.requiredItemId);

                default:
                    return false;
            }
        }
    }
}
