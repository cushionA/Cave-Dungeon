using System;
using System.Collections.Generic;
using R3;

namespace Game.Core
{
    public sealed class StatusEffectSystem : IDisposable, IStatusEffectWriter
    {
        private readonly SoACharaDataDic _data;
        private readonly GameEvents _events;
        private readonly List<StatusEffectTick> _ticks = new List<StatusEffectTick>();
        private readonly IDisposable _deathSubscription;

        public StatusEffectStore Store { get; } = new StatusEffectStore();

        public StatusEffectSystem(SoACharaDataDic data, GameEvents events)
        {
            _data = data;
            _events = events;
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
            return CanApply(hash) && Store.Apply(hash, info, sourceHash);
        }

        public bool Accumulate(int hash, StatusEffectInfo info, float statusCut, int sourceHash = 0)
        {
            return CanApply(hash) && Store.Accumulate(hash, info, statusCut, sourceHash);
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
                if (!_data.TryGetValue(tick.targetHash, out _))
                {
                    continue;
                }
                int hp = _data.GetVitals(tick.targetHash).currentHp;
                if (hp <= 0)
                {
                    continue;
                }

                int actualDamage = Math.Min(hp, tick.damage);
                _data.GetVitals(tick.targetHash).currentHp = hp - actualDamage;
                bool isKill = actualDamage == hp;
                if (isKill)
                {
                    _data.GetFlags(tick.targetHash).ActState = ActState.Dead;
                    ClearAll(tick.targetHash);
                }
                _events.FireDamageDealt(new DamageResult
                {
                    totalDamage = actualDamage,
                    isKill = isKill,
                    appliedEffect = tick.effectId,
                }, tick.sourceHash, tick.targetHash);
                if (isKill)
                {
                    _events.FireCharacterDeath(tick.targetHash, tick.sourceHash);
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
