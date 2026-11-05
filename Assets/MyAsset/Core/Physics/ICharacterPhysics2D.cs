using System;
using UnityEngine;

namespace Game.Core
{
    /// <summary>キャラクター制御が必要とする物理の境界。UnityのBodyやColliderを外へ渡さない。</summary>
    public interface ICharacterPhysics2D : IDisposable
    {
        Vector2 Position { get; }
        Vector2 Velocity { get; }
        float GravityScale { get; }
        int CollisionLayer { get; }
        bool IsSuspended { get; }
        bool BodyColliderEnabled { get; }

        void SetVelocity(Vector2 velocity);
        void ResetMotion();
        void SetGravityScale(float gravityScale);
        void ApplyImpulse(Vector2 impulse);
        void SetPosition(Vector2 position);
        void SetCollisionLayer(int layer);
        void SetBodySuspended(bool suspended);
        GroundInfo QueryGround(int layerMask);
        bool QueryWall(float facingDirection, int layerMask, out int colliderId);
        bool TryGetDropThroughDuration(int layerMask, out float duration);
        void BeginDropThrough();
        void EndDropThrough();
    }
}
