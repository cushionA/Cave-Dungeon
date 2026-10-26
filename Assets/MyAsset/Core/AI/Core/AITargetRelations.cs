using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>標的関係の正本。登録世代の変更・削除は両方向の関係を破棄する。</summary>
    public sealed class AITargetRelations
    {
        private readonly Dictionary<int, ActorRef> _actors = new Dictionary<int, ActorRef>();
        private readonly Dictionary<ActorRef, ActorRef> _targets = new Dictionary<ActorRef, ActorRef>();
        private readonly List<ActorRef> _remove = new List<ActorRef>();

        public void Register(ActorRef actor)
        {
            if (!actor.IsValid) { return; }
            if (_actors.TryGetValue(actor.Hash, out ActorRef previous))
            {
                if (previous.Equals(actor)) { return; }
                Remove(previous);
            }
            _actors[actor.Hash] = actor;
        }

        public bool IsCurrent(ActorRef actor) => actor.IsValid && _actors.TryGetValue(actor.Hash, out ActorRef current) && current.Equals(actor);

        public bool SetTarget(ActorRef owner, ActorRef target)
        {
            if (!IsCurrent(owner)) { return false; }
            if (!target.IsValid) { _targets.Remove(owner); return true; }
            if (!IsCurrent(target)) { return false; }
            _targets[owner] = target;
            return true;
        }

        public bool TryGetTarget(ActorRef owner, out ActorRef target)
        {
            target = default;
            return IsCurrent(owner) && _targets.TryGetValue(owner, out target) && IsCurrent(target);
        }

        public void Remove(ActorRef actor)
        {
            if (!_actors.TryGetValue(actor.Hash, out ActorRef current) || !current.Equals(actor)) { return; }
            _actors.Remove(actor.Hash);
            _targets.Remove(actor);
            _remove.Clear();
            foreach (KeyValuePair<ActorRef, ActorRef> pair in _targets)
            {
                if (pair.Value.Equals(actor)) { _remove.Add(pair.Key); }
            }
            for (int i = 0; i < _remove.Count; i++) { _targets.Remove(_remove[i]); }
        }

        public void RemoveHash(int hash)
        {
            if (_actors.TryGetValue(hash, out ActorRef actor)) { Remove(actor); }
        }

        public void CopyTo(AIObservationContext observation)
        {
            observation.SetTargetRelationsAvailable();
            foreach (KeyValuePair<ActorRef, ActorRef> pair in _targets)
            {
                observation.RecordTarget(pair.Key, pair.Value);
            }
        }
    }
}
