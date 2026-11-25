using System;

namespace Game.Core
{
    /// <summary>一度の攻撃に割り当てる世代付きの印。値コピーで受付時の順番を保つ。</summary>
    public struct ActionTargetSelection
    {
        public const int MaxTargets = 3;
        private ActorRef _first;
        private ActorRef _second;
        private ActorRef _third;

        public int Count { get; private set; }

        public readonly ActorRef this[int index] => index switch
        {
            0 when Count > 0 => _first,
            1 when Count > 1 => _second,
            2 when Count > 2 => _third,
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };

        public readonly bool Contains(ActorRef target) => target.IsValid
            && (target == _first || target == _second || target == _third);

        public bool TryAdd(ActorRef target)
        {
            if (!target.IsValid || Count == MaxTargets || target.Hash == _first.Hash
                || target.Hash == _second.Hash || target.Hash == _third.Hash)
            {
                return false;
            }

            switch (Count)
            {
                case 0: _first = target; break;
                case 1: _second = target; break;
                case 2: _third = target; break;
            }
            Count++;
            return true;
        }
    }
}
