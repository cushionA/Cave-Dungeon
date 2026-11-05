using System;

namespace Game.Core
{
    /// <summary>登録をまたいで再利用されるhashと、その個体の登録世代。</summary>
    public readonly struct ActorRef : IEquatable<ActorRef>
    {
        public int Hash { get; }
        public long Generation { get; }
        public bool IsValid => Hash != 0 && Generation > 0;

        public ActorRef(int hash, long generation)
        {
            Hash = hash;
            Generation = generation;
        }

        public bool Equals(ActorRef other) => Hash == other.Hash && Generation == other.Generation;
        public override bool Equals(object obj) => obj is ActorRef other && Equals(other);
        public override int GetHashCode() => unchecked(Hash * 397 ^ Generation.GetHashCode());
        public static bool operator ==(ActorRef first, ActorRef second) => first.Equals(second);
        public static bool operator !=(ActorRef first, ActorRef second) => !first.Equals(second);
    }
}
