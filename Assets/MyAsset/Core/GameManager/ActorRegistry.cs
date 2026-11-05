using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>現在登録されている個体だけを解決する。解除した世代は再発行しない。</summary>
    public sealed class ActorRegistry
    {
        private readonly Dictionary<int, ActorRef> _active = new Dictionary<int, ActorRef>();
        private long _nextGeneration = 1;

        public ActorRef Register(int hash)
        {
            if (hash == 0) { throw new ArgumentOutOfRangeException(nameof(hash), "Zero is reserved for no actor."); }
            if (_active.ContainsKey(hash)) { throw new ArgumentException("Actor is already registered.", nameof(hash)); }
            ActorRef actor = new ActorRef(hash, _nextGeneration++);
            _active.Add(hash, actor);
            return actor;
        }

        public bool Unregister(int hash) => _active.Remove(hash);
        public bool TryGet(int hash, out ActorRef actor) => _active.TryGetValue(hash, out actor);
        public bool IsCurrent(ActorRef actor) => actor.IsValid
            && _active.TryGetValue(actor.Hash, out ActorRef current) && current == actor;
        public void Clear() => _active.Clear();
    }
}
