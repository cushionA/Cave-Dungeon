using Game.Core;
using UnityEngine;

namespace Game.Runtime
{
    /// <summary>遭遇内の一体が現在目撃している敵と、その足元のワールド座標。</summary>
    internal readonly struct EncounterSighting3D
    {
        public ActorRef Observer { get; }
        public ActorRef Target { get; }
        public long ObservationTick { get; }
        public Vector3 Position { get; }
        public Vector3 TargetCenter { get; }

        public EncounterSighting3D(ActorRef observer, ActorRef target, long observationTick, Vector3 position, Vector3 targetCenter)
        {
            Observer = observer;
            Target = target;
            ObservationTick = observationTick;
            Position = position;
            TargetCenter = targetCenter;
        }
    }
}
