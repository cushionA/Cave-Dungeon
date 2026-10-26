using System;
using Game.Core;
using UnityEngine;

namespace Game.Runtime
{
    /// <summary>既存ステージと同じPhysics2Dで動くキャラクター物理実装。</summary>
    public sealed class RigidbodyCharacterPhysics2D : ICharacterPhysics2D
    {
        private const float k_GroundCheckDistance = 0.1f;
        private const float k_GroundCheckWidthRatio = 0.9f;
        private const float k_WallCheckExtend = 0.05f;
        private const float k_WallCheckHeightRatio = 0.9f;
        private const float k_DropThroughCastDistance = 0.2f;

        private readonly Rigidbody2D _body;
        private readonly BoxCollider2D _collider;
        private bool _suspended;
        private bool _colliderBeforeSuspend;
        private RigidbodyConstraints2D _constraintsBeforeSuspend;
        private float _gravityBeforeSuspend;
        private bool _droppingThrough;
        private int _baseCollisionLayer;

        public RigidbodyCharacterPhysics2D(Rigidbody2D body, BoxCollider2D collider)
        {
            _body = body != null ? body : throw new ArgumentNullException(nameof(body));
            _collider = collider != null ? collider : throw new ArgumentNullException(nameof(collider));
            _baseCollisionLayer = body.gameObject.layer;
            _body.gravityScale = GameConstants.k_GravityScale;
            _body.constraints = RigidbodyConstraints2D.FreezeRotation;
            _body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }

        public Vector2 Position => _body != null ? _body.position : default;
        public Vector2 Velocity => _body != null ? _body.linearVelocity : default;
        public float GravityScale => _body != null ? _body.gravityScale : 0f;
        public int CollisionLayer => _body != null ? _body.gameObject.layer : _baseCollisionLayer;
        public bool IsSuspended => _suspended;
        public bool BodyColliderEnabled => _collider != null && _collider.enabled;

        public void SetVelocity(Vector2 velocity)
        {
            if (_body != null) { _body.linearVelocity = _suspended ? Vector2.zero : velocity; }
        }

        public void SetGravityScale(float gravityScale)
        {
            if (_suspended) { _gravityBeforeSuspend = gravityScale; }
            else if (_body != null) { _body.gravityScale = gravityScale; }
        }

        public void ResetMotion()
        {
            SetVelocity(Vector2.zero);
            if (_body != null) { _body.angularVelocity = 0f; }
        }

        public void ApplyImpulse(Vector2 impulse)
        {
            if (_body != null && !_suspended) { _body.AddForce(impulse, ForceMode2D.Impulse); }
        }

        public void SetPosition(Vector2 position)
        {
            if (_body != null) { _body.position = position; }
        }

        public void SetCollisionLayer(int layer)
        {
            _baseCollisionLayer = layer;
            ApplyCollisionLayer();
        }

        public void SetBodySuspended(bool suspended)
        {
            if (_suspended == suspended) { return; }
            _suspended = suspended;
            if (suspended)
            {
                _colliderBeforeSuspend = _collider != null && _collider.enabled;
                _constraintsBeforeSuspend = _body != null ? _body.constraints : RigidbodyConstraints2D.None;
                _gravityBeforeSuspend = _body != null ? _body.gravityScale : 0f;
                if (_collider != null) { _collider.enabled = false; }
                if (_body != null)
                {
                    _body.constraints = RigidbodyConstraints2D.FreezeAll;
                    _body.gravityScale = 0f;
                    _body.linearVelocity = Vector2.zero;
                    _body.angularVelocity = 0f;
                }
            }
            else
            {
                if (_collider != null) { _collider.enabled = _colliderBeforeSuspend; }
                if (_body != null)
                {
                    _body.constraints = _constraintsBeforeSuspend;
                    _body.gravityScale = _gravityBeforeSuspend;
                }
            }
        }

        public GroundInfo QueryGround(int layerMask)
        {
            if (_collider == null) { return GroundInfo.NotGrounded; }
            Vector2 origin = (Vector2)_collider.transform.position + _collider.offset;
            origin.y -= _collider.size.y * 0.5f;
            Vector2 size = new Vector2(_collider.size.x * k_GroundCheckWidthRatio, 0.05f);
            RaycastHit2D hit = Physics2D.BoxCast(origin, size, 0f, Vector2.down, k_GroundCheckDistance, layerMask);
            return hit.collider != null
                ? new GroundInfo { isGrounded = true, normal = hit.normal } : GroundInfo.NotGrounded;
        }

        public bool QueryWall(float facingDirection, int layerMask, out int colliderId)
        {
            colliderId = AdvancedMovementLogic.k_NoWallId;
            if (_collider == null) { return false; }
            Bounds bounds = _collider.bounds;
            Vector2 origin = new Vector2(bounds.center.x + facingDirection * (bounds.extents.x + k_WallCheckExtend), bounds.center.y);
            Vector2 size = new Vector2(k_WallCheckExtend, bounds.size.y * k_WallCheckHeightRatio);
            Collider2D hit = Physics2D.OverlapBox(origin, size, 0f, layerMask);
            if (hit == null) { return false; }
            colliderId = hit.GetHashCode();
            return true;
        }

        public bool TryGetDropThroughDuration(int layerMask, out float duration)
        {
            duration = 0f;
            if (_collider == null) { return false; }
            Bounds bounds = _collider.bounds;
            Vector2 origin = new Vector2(bounds.center.x, bounds.min.y);
            Vector2 size = new Vector2(bounds.size.x * k_GroundCheckWidthRatio, 0.05f);
            RaycastHit2D hit = Physics2D.BoxCast(origin, size, 0f, Vector2.down, k_DropThroughCastDistance, layerMask);
            if (hit.collider == null) { return false; }
            DropThroughPlatform platform = hit.collider.GetComponent<DropThroughPlatform>();
            if (platform == null) { return false; }
            duration = platform.DropThroughDurationOverride;
            return true;
        }

        public void BeginDropThrough()
        {
            if (_droppingThrough) { return; }
            _baseCollisionLayer = CollisionLayer;
            _droppingThrough = true;
            ApplyCollisionLayer();
        }

        public void EndDropThrough()
        {
            if (!_droppingThrough) { return; }
            _droppingThrough = false;
            ApplyCollisionLayer();
        }

        private void ApplyCollisionLayer()
        {
            if (_body != null)
            {
                _body.gameObject.layer = _droppingThrough ? GameConstants.k_LayerCharaInvincible : _baseCollisionLayer;
            }
        }

        public void Dispose()
        {
            EndDropThrough();
            SetBodySuspended(false);
        }
    }
}
