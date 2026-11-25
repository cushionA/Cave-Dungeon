using Game.Core;
using UnityEngine;

namespace Game.Runtime
{
    /// <summary>遭遇内の一体が現在目撃している敵と、その足元のワールド座標。</summary>
    internal readonly struct EncounterSighting3D
    {
        public ActorRef Observer { get; }
        public ActorRef Target { get; }
        public Vector3 Position { get; }

        public EncounterSighting3D(ActorRef observer, ActorRef target, Vector3 position)
        {
            Observer = observer;
            Target = target;
            Position = position;
        }
    }
}
