using System;
using System.Collections.Generic;
using R3;

namespace Game.Core
{
    /// <summary>Runtimeの確定時点でSoAと戦闘履歴を観測へ投影する接続層。</summary>
    public sealed class AIObservationCoordinator : IDisposable
    {
        private readonly SoACharaDataDic _data;
        private readonly ActorRegistry _actors;
        private readonly Dictionary<ActorRef, DamageScoreTracker> _scores = new Dictionary<ActorRef, DamageScoreTracker>();
        private readonly Dictionary<ActorRef, HashSet<ActorRef>> _sources = new Dictionary<ActorRef, HashSet<ActorRef>>();
        private readonly Dictionary<ActorRef, ActorRef> _lastAttackers = new Dictionary<ActorRef, ActorRef>();
        private readonly List<int> _hashes = new List<int>();
        private readonly List<ActorRef> _sourceRemoval = new List<ActorRef>();
        private readonly List<ActorRef> _removed = new List<ActorRef>();
        private readonly IDisposable _registeredSubscription;
        private readonly IDisposable _removedSubscription;
        private readonly IDisposable _deathSubscription;
        private readonly IDisposable _damageSubscription;
        private float _currentTime;

        public AIObservationContext Observation { get; } = new AIObservationContext();
        public AITargetRelations Targets { get; } = new AITargetRelations();

        public AIObservationCoordinator(SoACharaDataDic data, ActorRegistry actors, GameEvents events)
        {
            _data = data;
            _actors = actors;
            _registeredSubscription = events.OnCharacterRegistered.Subscribe(RegisterHash);
            _removedSubscription = events.OnCharacterRemoved.Subscribe(RemoveHash);
            _deathSubscription = events.OnCharacterDeath.Subscribe(death => RemoveHash(death.deadHash));
            _damageSubscription = events.OnDamageDealt.Subscribe(damage =>
            {
                if (damage.result.totalDamage <= 0
                    || !Resolve(damage.result.attackerRef, damage.attackerHash, out ActorRef source)
                    || !Resolve(damage.result.defenderRef, damage.defenderHash, out ActorRef owner)) { return; }
                RegisterActor(owner);
                _scores[owner].AddDamage(source.Hash, damage.result.totalDamage, _currentTime);
                _sources[owner].Add(source);
                _lastAttackers[owner] = source;
            });
        }

        public void BeginTick(float currentTime) => _currentTime = currentTime;

        public void Capture(long tick, float currentTime)
        {
            _currentTime = currentTime;
            Observation.BeginSnapshot(tick, currentTime);
            _hashes.Clear();
            _data.GetAllHashes(_hashes);
            for (int i = 0; i < _hashes.Count; i++)
            {
                int hash = _hashes[i];
                if (!_actors.TryGet(hash, out ActorRef actor) || _data.GetVitals(hash).currentHp <= 0
                    || _data.GetFlags(hash).ActState == ActState.Dead) { RemoveHash(hash); continue; }
                if (_data.GetFlags(hash).IsVanished) { Targets.RemoveHash(hash); continue; }
                RegisterActor(actor);
                Observation.AddActor(new AIActorObservation
                {
                    actor = actor, vitals = _data.GetVitals(hash),
                    combat = _data.GetCombatStats(hash), flags = _data.GetFlags(hash),
                });
            }
            Observation.SetDamageScoresAvailable();
            foreach (KeyValuePair<ActorRef, DamageScoreTracker> owner in _scores)
            {
                if (!Observation.Contains(owner.Key)) { continue; }
                foreach (ActorRef source in _sources[owner.Key])
                {
                    Observation.RecordDamageScore(owner.Key, source, owner.Value.GetScore(source.Hash, currentTime));
                }
                if (_lastAttackers.TryGetValue(owner.Key, out ActorRef lastAttacker))
                {
                    Observation.RecordLastAttacker(owner.Key, lastAttacker);
                }
            }
            Targets.CopyTo(Observation);
        }

        private bool Resolve(ActorRef reference, int hash, out ActorRef current)
        {
            current = reference;
            if (reference.IsValid) { return _actors.IsCurrent(reference); }
            return _actors.TryGet(hash, out current);
        }

        private void RegisterHash(int hash)
        {
            RemoveHash(hash);
            if (_actors.TryGet(hash, out ActorRef actor)) { RegisterActor(actor); }
        }

        private void RegisterActor(ActorRef actor)
        {
            Targets.Register(actor);
            if (!_scores.ContainsKey(actor))
            {
                _scores.Add(actor, new DamageScoreTracker());
                _sources.Add(actor, new HashSet<ActorRef>());
            }
        }

        private void RemoveHash(int hash)
        {
            if (hash == 0) { return; }
            Targets.RemoveHash(hash);
            _removed.Clear();
            foreach (KeyValuePair<ActorRef, DamageScoreTracker> owner in _scores)
            {
                if (owner.Key.Hash == hash) { _removed.Add(owner.Key); }
                owner.Value.RemoveAttacker(hash);
                _sourceRemoval.Clear();
                foreach (ActorRef source in _sources[owner.Key])
                {
                    if (source.Hash == hash) { _sourceRemoval.Add(source); }
                }
                for (int i = 0; i < _sourceRemoval.Count; i++) { _sources[owner.Key].Remove(_sourceRemoval[i]); }
                if (_lastAttackers.TryGetValue(owner.Key, out ActorRef lastAttacker) && lastAttacker.Hash == hash)
                {
                    _lastAttackers.Remove(owner.Key);
                }
            }
            for (int i = 0; i < _removed.Count; i++)
            {
                _scores[_removed[i]].Dispose();
                _scores.Remove(_removed[i]);
                _sources.Remove(_removed[i]);
                _lastAttackers.Remove(_removed[i]);
            }
        }

        public void Dispose()
        {
            _registeredSubscription.Dispose(); _removedSubscription.Dispose();
            _deathSubscription.Dispose(); _damageSubscription.Dispose();
            foreach (DamageScoreTracker score in _scores.Values) { score.Dispose(); }
            _scores.Clear(); _sources.Clear(); _lastAttackers.Clear();
        }
    }
}
