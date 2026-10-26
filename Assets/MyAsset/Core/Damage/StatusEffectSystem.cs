using System;
using System.Collections.Generic;
using R3;

namespace Game.Core
{
    public sealed class StatusEffectSystem : IDisposable, IStatusEffectWriter
    {
        private readonly SoACharaDataDic _data;
        private readonly GameEvents _events;
        private readonly ActorRegistry _actors;
        private readonly List<StatusEffectTick> _ticks = new List<StatusEffectTick>();
        private readonly IDisposable _deathSubscription;

        public StatusEffectStore Store { get; } = new StatusEffectStore();

        public StatusEffectSystem(SoACharaDataDic data, GameEvents events, ActorRegistry actors = null)
        {
            _data = data;
            _events = events;
            _actors = actors;
            Store.Changed += PublishChange;
            _deathSubscription = events.OnCharacterDeath.Subscribe(death => ClearAll(death.deadHash));
        }

        public void Register(int hash)
        {
            _data.GetStatusEffects(hash) = Store.GetSummary(hash);
        }

        public static MovementInfo RestrictInput(MovementInfo input, in CharacterStatusEffects effects)
        {
            if (effects.BlocksMovement)
            {
                input.moveDirection = default;
                input.jumpPressed = false;
                input.jumpHeld = false;
                input.dodgePressed = false;
                input.sprintHeld = false;
            }
            if (effects.BlocksActions)
            {
                input.attackInput = null;
                input.guardHeld = false;
                input.cooperationPressed = false;
                input.interactPressed = false;
            }
            return input;
        }

        public void Unregister(int hash)
        {
            ClearAll(hash);
        }

        public bool Apply(int hash, StatusEffectInfo info, int sourceHash = 0)
        {
            if (_actors != null && _actors.TryGet(hash, out ActorRef target))
            {
                _actors.TryGet(sourceHash, out ActorRef source);
                return CanApply(hash) && Store.Apply(target, info, source);
            }
            return CanApply(hash) && Store.Apply(hash, info, sourceHash);
        }

        public bool Accumulate(int hash, StatusEffectInfo info, float statusCut, int sourceHash = 0)
        {
            if (_actors != null && _actors.TryGet(hash, out ActorRef target))
            {
                _actors.TryGet(sourceHash, out ActorRef source);
                return CanApply(hash) && Store.Accumulate(target, info, statusCut, source);
            }
            return CanApply(hash) && Store.Accumulate(hash, info, statusCut, sourceHash);
        }

        private readonly struct CapturedSourceWriter : IStatusEffectWriter
        {
            private readonly StatusEffectSystem _system;
            private readonly ActorRef _source;
            private readonly ActorRef _target;
            public CapturedSourceWriter(StatusEffectSystem system, ActorRef source, ActorRef target)
            { _system = system; _source = source; _target = target; }
            private ActorRef Resolve(int hash) => hash == _target.Hash ? _target : hash == _source.Hash ? _source : default;
            public bool Apply(int hash, StatusEffectInfo info, int sourceHash = 0)
            {
                ActorRef target = Resolve(hash);
                return _system._actors.IsCurrent(target) && _system.CanApply(hash)
                    && _system.Store.Apply(target, info, _source);
            }
            public bool Accumulate(int hash, StatusEffectInfo info, float cut, int sourceHash = 0)
            {
                ActorRef target = Resolve(hash);
                return _system._actors.IsCurrent(target) && _system.CanApply(hash)
                    && _system.Store.Accumulate(target, info, cut, _source);
            }
        }

        public StatusEffectId ApplyApplications(StatusEffectApplicationData[] applications, ActorRef source,
            int hitTargetHash, GuardResult guardResult, float statusCut,
            StatusEffectApplicationData[] equipmentApplications = null)
        {
            if (_actors == null) { throw new InvalidOperationException("Captured sources require an actor registry."); }
            if (!_actors.TryGet(hitTargetHash, out ActorRef target)) { return StatusEffectId.None; }
            int liveSourceHash = _actors.IsCurrent(source) ? source.Hash : 0;
            return StatusEffectApplicationLogic.Apply(applications, liveSourceHash, hitTargetHash,
                guardResult, statusCut, new CapturedSourceWriter(this, source, target), equipmentApplications);
        }

        public StatusEffectId ApplyApplications(in AttackContext context, int hitTargetHash,
            GuardResult guardResult, float statusCut)
        {
            if (_actors == null) { throw new InvalidOperationException("Captured sources require an actor registry."); }
            if (!_actors.TryGet(hitTargetHash, out ActorRef target)) { return StatusEffectId.None; }
            return context.ApplyStatusEffects(context.GetLiveSourceHash(_actors), hitTargetHash,
                guardResult, statusCut, new CapturedSourceWriter(this, context.Owner, target));
        }

        public StatusEffectId ApplyApplications(StatusEffectApplicationData[] applications,
            int sourceHash, int hitTargetHash, GuardResult guardResult, float statusCut,
            StatusEffectApplicationData[] equipmentApplications = null)
        {
            return StatusEffectApplicationLogic.Apply(applications, sourceHash, hitTargetHash,
                guardResult, statusCut, this, equipmentApplications);
        }

        public bool Remove(int hash, StatusEffectId id)
        {
            return _data.TryGetValue(hash, out _) && Store.Remove(hash, id);
        }

        public void ClearAll(int hash)
        {
            Store.ClearAll(hash);
        }

        public void Tick(float deltaTime)
        {
            Store.TickAll(deltaTime, _ticks);
            for (int i = 0; i < _ticks.Count; i++)
            {
                StatusEffectTick tick = _ticks[i];
                if (tick.targetRef.IsValid && (_actors == null || !_actors.IsCurrent(tick.targetRef))) { continue; }
                if (!_data.TryGetValue(tick.targetHash, out _))
                {
                    continue;
                }
                ref CharacterVitals vitals = ref _data.GetVitals(tick.targetHash);
                int hp = vitals.currentHp;
                if (hp <= 0)
                {
                    continue;
                }

                int actualDamage = Math.Min(hp, tick.damage);
                vitals.currentHp = hp - actualDamage;
                bool isKill = actualDamage == hp;
                if (isKill)
                {
                    _data.GetFlags(tick.targetHash).ActState = ActState.Dead;
                    ClearAll(tick.targetHash);
                }
                _events.FireDamageDealt(new DamageResult
                {
                    attackerRef = tick.sourceRef,
                    defenderRef = tick.targetRef,
                    totalDamage = actualDamage,
                    isKill = isKill,
                    appliedEffect = tick.effectId,
                }, tick.sourceHash, tick.targetHash);
                if (isKill)
                {
                    if (tick.targetRef.IsValid) { _events.FireCharacterDeath(tick.targetRef, tick.sourceRef); }
                    else { _events.FireCharacterDeath(tick.targetHash, tick.sourceHash); }
                }
            }
        }

        public void Dispose()
        {
            _deathSubscription.Dispose();
            Store.Changed -= PublishChange;
            _ticks.Clear();
        }

        private bool CanApply(int hash)
        {
            return _data.TryGetValue(hash, out _) && _data.GetVitals(hash).currentHp > 0;
        }

        private void PublishChange(int hash, StatusEffectId id, StatusEffectChangeKind kind)
        {
            if (_data.TryGetValue(hash, out _))
            {
                _data.GetStatusEffects(hash) = Store.GetSummary(hash);
            }
            _events.FireStatusEffectChanged(hash, id, kind);
            if (kind == StatusEffectChangeKind.Applied)
            {
                _events.FireStatusEffectApplied(hash, id);
            }
        }
    }
}
