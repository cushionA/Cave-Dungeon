using System;

namespace Game.Core
{
    public enum StatusEffectChangeKind : byte
    {
        Applied,
        Refreshed,
        Removed,
        Expired,
        Cleared,
    }

    public struct ActiveEffect
    {
        public ActorRef targetRef;
        public ActorRef sourceRef;
        public int instanceId;
        public int targetHash;
        public int sourceHash;
        public StatusEffectId id;
        public float accumulation;
        public float remainTime;
        public float duration;
        public float tickTimer;
        public float tickInterval;
        public int tickDamage;
        public float modifier;
        public int stackCount;
        public int maxStack;
    }

    public class StatusEffectManager
    {
        public const float k_DefaultThreshold = 100f;

        private readonly int _targetHash;

        public StatusEffectStore Store { get; }
        public int ActiveCount => Store.GetActiveCount(_targetHash);
        public float MoveSpeedMultiplier => GetSummary().MoveSpeedMultiplier;
        public float AttackMultiplier => GetSummary().AttackMultiplier;
        public float DefenseMultiplier => GetSummary().DefenseMultiplier;
        public float DamageTakenMultiplier => GetSummary().DamageTakenMultiplier;

        public StatusEffectManager() : this(new StatusEffectStore(), 0)
        {
        }

        public StatusEffectManager(StatusEffectStore store, int targetHash)
        {
            Store = store ?? throw new ArgumentNullException(nameof(store));
            _targetHash = targetHash;
        }

        public bool Accumulate(StatusEffectInfo info, float statusCut, int sourceHash = 0)
        {
            return Store.Accumulate(_targetHash, info, statusCut, sourceHash);
        }

        public bool Accumulate(StatusEffectId id, float value, float statusCut)
        {
            return Accumulate(new StatusEffectInfo
            {
                effect = id,
                accumulateValue = value,
                duration = 10f,
                tickDamage = 5f,
                tickInterval = 2f,
            }, statusCut);
        }

        public bool Apply(StatusEffectInfo info, int sourceHash = 0)
        {
            return Store.Apply(_targetHash, info, sourceHash);
        }

        public int Tick(float deltaTime) => Store.Tick(_targetHash, deltaTime);
        public bool Remove(StatusEffectId id) => Store.Remove(_targetHash, id);
        public ActiveEffect GetActiveEffect(int index) => Store.GetActiveEffect(_targetHash, index);
        public bool TryGetEffect(StatusEffectId id, out ActiveEffect effect) => Store.TryGetEffect(_targetHash, id, out effect);
        public float GetAccumulation(StatusEffectId id) => Store.GetAccumulation(_targetHash, id);
        public float GetRemainingTime(StatusEffectId id) => Store.GetRemainingTime(_targetHash, id);
        public bool IsActive(StatusEffectId id) => Store.IsActive(_targetHash, id);
        public CharacterStatusEffects GetSummary() => Store.GetSummary(_targetHash);
        public void ClearAll() => Store.ClearAll(_targetHash);
    }
}
