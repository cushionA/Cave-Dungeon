using System;
using Game.Core;
using UnityEngine;

namespace Game.Runtime
{
    public partial class BaseCharacter
    {
        private ICharacterPhysics2D _physicsBackend;
        private bool _hasPendingPhysicsLayer;
        private int _pendingPhysicsLayer;
        private bool _hasSimulationVelocity;

        public ICharacterPhysics2D PhysicsBackend => _physicsBackend;
        public Vector2 PhysicsPosition => _physicsBackend != null ? _physicsBackend.Position : default;
        public Vector2 PhysicsVelocity => _physicsBackend != null ? _physicsBackend.Velocity : default;
        public float PhysicsGravityScale => _physicsBackend != null ? _physicsBackend.GravityScale : 0f;
        public int PhysicsCollisionLayer => _physicsBackend != null ? _physicsBackend.CollisionLayer : _pendingPhysicsLayer;
        public bool PhysicsBodySuspended => _physicsBackend != null && _physicsBackend.IsSuspended;
        public bool PhysicsBodyColliderEnabled => _physicsBackend != null && _physicsBackend.BodyColliderEnabled;

        private void InitializeCharacterPhysics()
        {
            if (_physicsBackend == null)
            {
                _physicsBackend = CreateCharacterPhysics() ?? throw new InvalidOperationException("A character requires a physics backend.");
            }
            if (_hasPendingPhysicsLayer)
            {
                _physicsBackend.SetCollisionLayer(_pendingPhysicsLayer);
                _hasPendingPhysicsLayer = false;
            }
        }

        protected virtual ICharacterPhysics2D CreateCharacterPhysics() => new RigidbodyCharacterPhysics2D(_rb, _collider);

        public void ResetPhysicsMotion()
        {
            _hasSimulationVelocity = true;
            _physicsBackend?.ResetMotion();
        }

        public void SetPhysicsVelocity(Vector2 velocity)
        {
            _hasSimulationVelocity = true;
            _physicsBackend?.SetVelocity(velocity);
        }
        public void SetPhysicsGravityScale(float gravityScale) => _physicsBackend?.SetGravityScale(gravityScale);
        public void ApplyPhysicsImpulse(Vector2 impulse) => _physicsBackend?.ApplyImpulse(impulse);
        public void SetPhysicsPosition(Vector2 position) => _physicsBackend?.SetPosition(position);
        public void SetPhysicsBodySuspended(bool suspended) => _physicsBackend?.SetBodySuspended(suspended);
        public void BeginPhysicsDropThrough() => _physicsBackend?.BeginDropThrough();
        public void EndPhysicsDropThrough() => _physicsBackend?.EndDropThrough();

        public void SetPhysicsCollisionLayer(int layer)
        {
            if (_physicsBackend != null) { _physicsBackend.SetCollisionLayer(layer); }
            else
            {
                // 他コンポーネントのOnEnableがBaseのAwakeより先でも、要求を落とさない。
                _pendingPhysicsLayer = layer;
                _hasPendingPhysicsLayer = true;
            }
        }

        public bool TryGetDropThroughDuration(out float duration)
        {
            duration = 0f;
            return _physicsBackend != null && _physicsBackend.TryGetDropThroughDuration(_groundLayer, out duration);
        }

        private void ResetPhysicsTransientState()
        {
            EndPhysicsDropThrough();
            SetPhysicsBodySuspended(false);
        }

#if UNITY_INCLUDE_TESTS
        public void SetPhysicsBackendForTest(ICharacterPhysics2D backend)
        {
            if (backend == null) { throw new ArgumentNullException(nameof(backend)); }
            _physicsBackend?.Dispose();
            _physicsBackend = backend;
        }
#endif
    }
}
