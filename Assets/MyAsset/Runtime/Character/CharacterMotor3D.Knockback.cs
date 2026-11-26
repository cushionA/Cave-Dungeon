using Game.Core;
using UnityEngine;

namespace Game.Runtime
{
    public sealed partial class CharacterMotor3D
    {
        private struct KnockbackMotion
        {
            public CharacterActor3D owner;
            public ActorRef actor;
            public GameActionRuntime actions;
            public long instance;
            public Vector3 planarVelocity;
            public bool landed;
        }

        private KnockbackMotion _knockback;

        internal bool CanReceiveKnockback => isActiveAndEnabled && _controller != null && _controller.enabled;

        internal void BeginKnockback(CharacterActor3D owner, ActorRef actor, long instance, Vector3 impulse)
        {
            if (!CanReceiveKnockback) { return; }
            _knockback = new KnockbackMotion
            {
                owner = owner, actor = actor, actions = GameManager.Actions, instance = instance,
                planarVelocity = new Vector3(impulse.x, 0f, impulse.z),
            };
            _detachedJump = default;
            _verticalSpeed += impulse.y;
            if (_verticalSpeed > 0f) { Ground = default; }
        }

        internal bool TryGetKnockbackLanding(long instance, out bool landed)
        {
            landed = _knockback.landed;
            return instance == _knockback.instance && HasCurrentKnockback();
        }

        internal void EndKnockback(long instance)
        {
            if (_knockback.instance != instance) { return; }
            bool ownerChanged = _knockback.owner == null || _knockback.owner.Actor != _knockback.actor;
            ClearKnockback(resetVelocity: ownerChanged);
        }

        private bool HasCurrentKnockback()
        {
            if (_knockback.instance == 0) { return false; }
            if (CanReceiveKnockback && _knockback.owner != null && _knockback.owner.Actor == _knockback.actor
                && _knockback.owner.IsAlive && ReferenceEquals(_knockback.actions, GameManager.Actions))
            {
                return true;
            }
            ClearKnockback(resetVelocity: true);
            return false;
        }

        private void ClearKnockback(bool resetVelocity)
        {
            _knockback = default;
            if (!resetVelocity) { return; }
            _verticalSpeed = 0f;
            Velocity = Vector3.zero;
        }
    }
}
