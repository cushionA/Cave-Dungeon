using UnityEngine;

namespace Game.Core
{
    [CreateAssetMenu(fileName = "StatusEffect", menuName = "Game/Status Effect Definition")]
    public sealed class StatusEffectDefinition : ScriptableObject
    {
        public StatusEffectId effect;
        [Min(0f)] public float duration = 5f;
        [Min(0f)] public float tickDamage;
        [Min(0f)] public float tickInterval = 1f;
        [Range(0f, 1f)] public float modifier;
        public byte maxStack = 1;

        public StatusEffectInfo CreateInfo(float accumulateValue = 0f)
        {
            return new StatusEffectInfo
            {
                effect = effect,
                accumulateValue = accumulateValue,
                duration = duration,
                tickDamage = tickDamage,
                tickInterval = tickInterval,
                modifier = modifier,
                maxStack = maxStack,
            };
        }
    }
}
