using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Core
{
    [Serializable]
    public struct HitRepeatSettings
    {
        [Tooltip("同じ対象への命中上限。未設定は1回。")]
        public int maxHitsPerTarget;

        [Tooltip("同じ対象に再び命中するまでの間隔（秒）。命中上限が2回以上で、間隔が正の場合に再ヒットする。")]
        public float interval;

        public bool IsEnabled => maxHitsPerTarget > 1 && float.IsFinite(interval) && interval > 0f;
        public int EffectiveMaxHitsPerTarget => IsEnabled ? maxHitsPerTarget : 1;
    }

    /// <summary>一つの攻撃や飛翔体で命中できる対象数と、対象ごとの命中間隔を管理する。</summary>
    public sealed class AttackHitTracker
    {
        private const double k_FloatRelativePrecision = 1.1920928955078125e-7;

        private struct TargetHits
        {
            public int Count;
            public double LastTime;
        }

        private readonly Dictionary<ActorRef, TargetHits> _targets = new Dictionary<ActorRef, TargetHits>();
        private int _maxTargets;
        private int _maxHitsPerTarget;
        private int _completedTargets;
        private double _minimumInterval;

        public AttackHitTracker(int maxTargets, HitRepeatSettings repeatHits = default) => Reset(maxTargets, repeatHits);

        public int HitCount { get; private set; }
        public bool IsExhausted => _completedTargets >= _maxTargets;

        public void Reset(int maxTargets, HitRepeatSettings repeatHits = default)
        {
            _maxTargets = maxTargets;
            _maxHitsPerTarget = repeatHits.EffectiveMaxHitsPerTarget;
            // 秒の設定値と固定刻みはfloat由来なので、境界で最大1 float相当の差を許容する。
            _minimumInterval = _maxHitsPerTarget > 1 ? repeatHits.interval * (1.0 - k_FloatRelativePrecision) : 0.0;
            _completedTargets = 0;
            HitCount = 0;
            _targets.Clear();
        }

        public bool CanRegisterHit(ActorRef target, double time = 0)
        {
            _targets.TryGetValue(target, out TargetHits hits);
            return CanRegister(hits, time);
        }

        public bool TryRegisterHit(ActorRef target, double time = 0)
        {
            _targets.TryGetValue(target, out TargetHits hits);
            if (!CanRegister(hits, time)) { return false; }

            hits.Count++;
            hits.LastTime = time;
            _targets[target] = hits;
            if (hits.Count == _maxHitsPerTarget) { _completedTargets++; }
            if (HitCount < int.MaxValue) { HitCount++; }
            return true;
        }

        private bool CanRegister(in TargetHits hits, double time)
        {
            if (_maxHitsPerTarget > 1 && !double.IsFinite(time)) { return false; }
            if (hits.Count == 0) { return _targets.Count < _maxTargets; }
            if (hits.Count >= _maxHitsPerTarget || time <= hits.LastTime) { return false; }
            return time - hits.LastTime >= _minimumInterval;
        }
    }
}
