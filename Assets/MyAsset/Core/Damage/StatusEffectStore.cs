using System;
using System.Collections.Generic;

namespace Game.Core
{
    public struct StatusEffectTick
    {
        public int instanceId;
        public int targetHash;
        public int sourceHash;
        public StatusEffectId effectId;
        public int damage;
    }

    public sealed class StatusEffectStore : IStatusEffectWriter
    {
        private readonly Dictionary<int, int> _indices = new Dictionary<int, int>();
        private readonly List<(int targetHash, StatusEffectId effectId)> _expired = new List<(int, StatusEffectId)>();
        private int _count;
        private int _nextInstanceId = 1;
        private int[] _instanceIds;
        private int[] _targetHashes;
        private int[] _sourceHashes;
        private StatusEffectId[] _effectIds;
        private float[] _accumulations;
        private float[] _remainingTimes;
        private float[] _durations;
        private float[] _tickTimers;
        private float[] _tickIntervals;
        private float[] _modifiers;
        private int[] _tickDamages;
        private int[] _stackCounts;
        private int[] _maxStacks;

        public event Action<int, StatusEffectId, StatusEffectChangeKind> Changed;

        public int Count => _count;
        public int ActiveCount => GetActiveCount();

        public StatusEffectStore(int capacity = 32)
        {
            int length = Math.Max(1, capacity);
            _instanceIds = new int[length];
            _targetHashes = new int[length];
            _sourceHashes = new int[length];
            _effectIds = new StatusEffectId[length];
            _accumulations = new float[length];
            _remainingTimes = new float[length];
            _durations = new float[length];
            _tickTimers = new float[length];
            _tickIntervals = new float[length];
            _modifiers = new float[length];
            _tickDamages = new int[length];
            _stackCounts = new int[length];
            _maxStacks = new int[length];
        }

        public int GetActiveCount(int targetHash)
        {
            int count = 0;
            for (int i = 0; i < _count; i++)
            {
                if (_targetHashes[i] == targetHash && _stackCounts[i] > 0)
                {
                    count++;
                }
            }
            return count;
        }

        public ActiveEffect GetActiveEffect(int index)
        {
            return GetActiveEffect(index, 0, false);
        }

        public ActiveEffect GetActiveEffect(int targetHash, int index)
        {
            return GetActiveEffect(index, targetHash, true);
        }

        public void CopyActiveEffects(List<ActiveEffect> output)
        {
            CopyActiveEffects(0, output, false);
        }

        public void CopyActiveEffects(int targetHash, List<ActiveEffect> output)
        {
            CopyActiveEffects(targetHash, output, true);
        }

        public bool TryGetEffect(int targetHash, StatusEffectId id, out ActiveEffect effect)
        {
            int selected = -1;
            for (int i = 0; i < _count; i++)
            {
                if (_targetHashes[i] == targetHash && _effectIds[i] == id && _stackCounts[i] > 0
                    && (selected < 0 || _remainingTimes[i] > _remainingTimes[selected]))
                {
                    selected = i;
                }
            }
            effect = selected >= 0 ? Snapshot(selected) : default;
            return selected >= 0;
        }

        public bool TryGetInstance(int instanceId, out ActiveEffect effect)
        {
            if (_indices.TryGetValue(instanceId, out int index))
            {
                effect = Snapshot(index);
                return true;
            }
            effect = default;
            return false;
        }

        public bool IsActive(int targetHash, StatusEffectId id)
        {
            return TryGetEffect(targetHash, id, out _);
        }

        public float GetRemainingTime(int targetHash, StatusEffectId id)
        {
            return TryGetEffect(targetHash, id, out ActiveEffect effect) ? effect.remainTime : 0f;
        }

        public float GetAccumulation(int targetHash, StatusEffectId id)
        {
            float accumulation = 0f;
            for (int i = 0; i < _count; i++)
            {
                if (_targetHashes[i] == targetHash && _effectIds[i] == id)
                {
                    accumulation += _accumulations[i];
                }
            }
            return accumulation;
        }

        public CharacterStatusEffects GetSummary(int targetHash)
        {
            CharacterStatusEffects summary = default;
            for (int i = 0; i < _count; i++)
            {
                if (_targetHashes[i] == targetHash && _stackCounts[i] > 0)
                {
                    summary.Include(Snapshot(i));
                }
            }
            return summary;
        }

        public bool Accumulate(int targetHash, StatusEffectInfo info, float statusCut, int sourceHash = 0)
        {
            if (!CharacterStatusEffects.IsValidId(info.effect) || info.duration <= 0f)
            {
                return false;
            }
            float resistance = Math.Min(1f, Math.Max(0f, statusCut));
            float amount = Math.Max(0f, info.accumulateValue) * (1f - resistance);
            if (amount <= 0f)
            {
                return false;
            }
            int index = FindRow(targetHash, info.effect, sourceHash);
            if (index < 0)
            {
                index = AddRow(targetHash, info.effect, sourceHash);
            }
            _accumulations[index] += amount;
            if (GetAccumulation(targetHash, info.effect) < StatusEffectManager.k_DefaultThreshold)
            {
                return false;
            }
            for (int i = _count - 1; i >= 0; i--)
            {
                if (_targetHashes[i] == targetHash && _effectIds[i] == info.effect)
                {
                    _accumulations[i] = 0f;
                    if (_stackCounts[i] <= 0)
                    {
                        RemoveRow(i);
                    }
                }
            }
            return Apply(targetHash, info, sourceHash);
        }

        public bool Apply(int targetHash, StatusEffectInfo info, int sourceHash = 0)
        {
            if (!CharacterStatusEffects.IsValidId(info.effect) || info.duration <= 0f)
            {
                return false;
            }
            int index = FindRow(targetHash, info.effect, sourceHash);
            if (index < 0)
            {
                index = AddRow(targetHash, info.effect, sourceHash);
            }
            bool wasActive = _stackCounts[index] > 0;
            int maxStack = Math.Max(1, (int)info.maxStack);
            float interval = info.tickInterval > 0f ? info.tickInterval : 1f;
            _tickTimers[index] = wasActive ? _tickTimers[index] / _tickIntervals[index] * interval : 0f;
            _tickIntervals[index] = interval;
            _remainingTimes[index] = info.duration;
            _durations[index] = info.duration;
            _tickDamages[index] = (int)Math.Max(0f, info.tickDamage);
            _modifiers[index] = Math.Min(1f, Math.Max(0f, info.modifier));
            _stackCounts[index] = wasActive ? Math.Min(_stackCounts[index] + 1, maxStack) : 1;
            _maxStacks[index] = maxStack;
            _accumulations[index] = 0f;
            Changed?.Invoke(targetHash, info.effect, wasActive ? StatusEffectChangeKind.Refreshed : StatusEffectChangeKind.Applied);
            return true;
        }

        public int Tick(int targetHash, float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return 0;
            }
            int totalDamage = 0;
            ulong expired = 0;
            for (int i = _count - 1; i >= 0; i--)
            {
                if (_targetHashes[i] != targetHash || _stackCounts[i] <= 0)
                {
                    continue;
                }
                _tickTimers[i] += Math.Min(deltaTime, _remainingTimes[i]);
                int tickCount = (int)(_tickTimers[i] / _tickIntervals[i]);
                totalDamage += tickCount * _tickDamages[i] * _stackCounts[i];
                _tickTimers[i] -= tickCount * _tickIntervals[i];
                _remainingTimes[i] = Math.Max(0f, _remainingTimes[i] - deltaTime);
                if (_remainingTimes[i] <= 0f)
                {
                    expired |= 1UL << (int)_effectIds[i];
                    RemoveRow(i);
                }
            }
            PublishChanges(targetHash, expired, StatusEffectChangeKind.Expired);
            return totalDamage;
        }

        public void TickAll(float deltaTime, List<StatusEffectTick> results)
        {
            results.Clear();
            if (deltaTime <= 0f)
            {
                return;
            }
            _expired.Clear();
            for (int i = _count - 1; i >= 0; i--)
            {
                if (_stackCounts[i] <= 0)
                {
                    continue;
                }
                _tickTimers[i] += Math.Min(deltaTime, _remainingTimes[i]);
                int tickCount = (int)(_tickTimers[i] / _tickIntervals[i]);
                int damage = tickCount * _tickDamages[i] * _stackCounts[i];
                if (damage > 0)
                {
                    results.Add(new StatusEffectTick
                    {
                        instanceId = _instanceIds[i],
                        targetHash = _targetHashes[i],
                        sourceHash = _sourceHashes[i],
                        effectId = _effectIds[i],
                        damage = damage,
                    });
                }
                _tickTimers[i] -= tickCount * _tickIntervals[i];
                _remainingTimes[i] = Math.Max(0f, _remainingTimes[i] - deltaTime);
                if (_remainingTimes[i] <= 0f)
                {
                    _expired.Add((_targetHashes[i], _effectIds[i]));
                    RemoveRow(i);
                }
            }
            for (int i = 0; i < _expired.Count; i++)
            {
                (int targetHash, StatusEffectId effectId) = _expired[i];
                Changed?.Invoke(targetHash, effectId, StatusEffectChangeKind.Expired);
            }
        }

        public bool Remove(int targetHash, StatusEffectId id)
        {
            bool removed = false;
            bool wasActive = false;
            for (int i = _count - 1; i >= 0; i--)
            {
                if (_targetHashes[i] == targetHash && _effectIds[i] == id)
                {
                    removed = true;
                    wasActive |= _stackCounts[i] > 0;
                    RemoveRow(i);
                }
            }
            if (wasActive)
            {
                Changed?.Invoke(targetHash, id, StatusEffectChangeKind.Removed);
            }
            return removed;
        }

        public bool RemoveInstance(int instanceId)
        {
            if (!_indices.TryGetValue(instanceId, out int index))
            {
                return false;
            }
            int targetHash = _targetHashes[index];
            StatusEffectId id = _effectIds[index];
            bool wasActive = _stackCounts[index] > 0;
            RemoveRow(index);
            if (wasActive)
            {
                Changed?.Invoke(targetHash, id, StatusEffectChangeKind.Removed);
            }
            return true;
        }

        public void ClearAll(int targetHash)
        {
            ulong active = 0;
            for (int i = _count - 1; i >= 0; i--)
            {
                if (_targetHashes[i] == targetHash)
                {
                    if (_stackCounts[i] > 0)
                    {
                        active |= 1UL << (int)_effectIds[i];
                    }
                    RemoveRow(i);
                }
            }
            PublishChanges(targetHash, active, StatusEffectChangeKind.Cleared);
        }

        private int GetActiveCount()
        {
            int count = 0;
            for (int i = 0; i < _count; i++)
            {
                if (_stackCounts[i] > 0)
                {
                    count++;
                }
            }
            return count;
        }

        private void CopyActiveEffects(int targetHash, List<ActiveEffect> output, bool filterTarget)
        {
            output.Clear();
            for (int i = 0; i < _count; i++)
            {
                if (_stackCounts[i] > 0 && (!filterTarget || _targetHashes[i] == targetHash))
                {
                    output.Add(Snapshot(i));
                }
            }
        }

        private ActiveEffect GetActiveEffect(int index, int targetHash, bool filterTarget)
        {
            if (index < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            for (int i = 0; i < _count; i++)
            {
                if (_stackCounts[i] <= 0 || (filterTarget && _targetHashes[i] != targetHash))
                {
                    continue;
                }
                if (index == 0)
                {
                    return Snapshot(i);
                }
                index--;
            }
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        private ActiveEffect Snapshot(int index)
        {
            return new ActiveEffect
            {
                instanceId = _instanceIds[index],
                targetHash = _targetHashes[index],
                sourceHash = _sourceHashes[index],
                id = _effectIds[index],
                accumulation = _accumulations[index],
                remainTime = _remainingTimes[index],
                duration = _durations[index],
                tickTimer = _tickTimers[index],
                tickInterval = _tickIntervals[index],
                tickDamage = _tickDamages[index],
                modifier = _modifiers[index],
                stackCount = _stackCounts[index],
                maxStack = _maxStacks[index],
            };
        }

        private int FindRow(int targetHash, StatusEffectId id, int sourceHash)
        {
            for (int i = 0; i < _count; i++)
            {
                if (_targetHashes[i] == targetHash && _effectIds[i] == id && _sourceHashes[i] == sourceHash)
                {
                    return i;
                }
            }
            return -1;
        }

        private int AddRow(int targetHash, StatusEffectId id, int sourceHash)
        {
            if (_count == _instanceIds.Length)
            {
                Resize(_count * 2);
            }
            int index = _count++;
            _instanceIds[index] = _nextInstanceId++;
            _targetHashes[index] = targetHash;
            _sourceHashes[index] = sourceHash;
            _effectIds[index] = id;
            _accumulations[index] = 0f;
            _remainingTimes[index] = 0f;
            _durations[index] = 0f;
            _tickTimers[index] = 0f;
            _tickIntervals[index] = 0f;
            _modifiers[index] = 0f;
            _tickDamages[index] = 0;
            _stackCounts[index] = 0;
            _maxStacks[index] = 0;
            _indices.Add(_instanceIds[index], index);
            return index;
        }

        private void RemoveRow(int index)
        {
            _indices.Remove(_instanceIds[index]);
            int last = --_count;
            if (index == last)
            {
                return;
            }
            _instanceIds[index] = _instanceIds[last];
            _targetHashes[index] = _targetHashes[last];
            _sourceHashes[index] = _sourceHashes[last];
            _effectIds[index] = _effectIds[last];
            _accumulations[index] = _accumulations[last];
            _remainingTimes[index] = _remainingTimes[last];
            _durations[index] = _durations[last];
            _tickTimers[index] = _tickTimers[last];
            _tickIntervals[index] = _tickIntervals[last];
            _modifiers[index] = _modifiers[last];
            _tickDamages[index] = _tickDamages[last];
            _stackCounts[index] = _stackCounts[last];
            _maxStacks[index] = _maxStacks[last];
            _indices[_instanceIds[index]] = index;
        }

        private void Resize(int capacity)
        {
            Array.Resize(ref _instanceIds, capacity);
            Array.Resize(ref _targetHashes, capacity);
            Array.Resize(ref _sourceHashes, capacity);
            Array.Resize(ref _effectIds, capacity);
            Array.Resize(ref _accumulations, capacity);
            Array.Resize(ref _remainingTimes, capacity);
            Array.Resize(ref _durations, capacity);
            Array.Resize(ref _tickTimers, capacity);
            Array.Resize(ref _tickIntervals, capacity);
            Array.Resize(ref _modifiers, capacity);
            Array.Resize(ref _tickDamages, capacity);
            Array.Resize(ref _stackCounts, capacity);
            Array.Resize(ref _maxStacks, capacity);
        }

        private void PublishChanges(int targetHash, ulong changed, StatusEffectChangeKind kind)
        {
            for (int i = 1; i < (int)StatusEffectId.Count; i++)
            {
                if ((changed & (1UL << i)) != 0)
                {
                    Changed?.Invoke(targetHash, (StatusEffectId)i, kind);
                }
            }
        }
    }
}
