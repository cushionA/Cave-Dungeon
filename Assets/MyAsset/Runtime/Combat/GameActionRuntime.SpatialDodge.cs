using Game.Core;
using UnityEngine;

namespace Game.Runtime
{
    public sealed partial class GameActionRuntime
    {
        internal bool TryGetDodge3DMovement(ActorRef owner, long instance, out Vector3 velocity)
        {
            velocity = default;
            if (!IsCurrentDodge(owner, instance) || !Execution.TryGetMovement(owner.Hash, true, out Vector2 planar)) { return false; }
            velocity = new Vector3(planar.x, 0f, planar.y);
            return true;
        }

        internal void CancelDodge3D(ActorRef owner, long instance)
        {
            if (!IsCurrentDodge(owner, instance)) { return; }
            _held.Remove((owner.Hash, Builtin.Dodge));
            Execution.Cancel(instance, force: true);
        }

        private bool IsCurrentDodge(ActorRef owner, long instance) => owner.IsValid
            && _held.TryGetValue((owner.Hash, Builtin.Dodge), out long active) && active == instance
            && Execution.TryGetInstance(instance, out ActionExecution current) && current.ownerRef == owner;
    }
}
