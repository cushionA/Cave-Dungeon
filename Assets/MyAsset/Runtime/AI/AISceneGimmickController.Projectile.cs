using System.Collections.Generic;
using Game.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Runtime
{
    public sealed partial class AISceneGimmickController
    {
        [Tooltip("起動イベントの代わりに、同じオブジェクトの爆発物を水平に撃つ。")]
        [SerializeField] private bool shootExplosive;
        [SerializeField] private ExplosiveProp2D projectileProp;
        [Tooltip("予備動作のない単発・直進・非貫通の飛翔体攻撃。")]
        [SerializeField] private AttackInfo projectileAttack;

        private readonly List<RaycastHit2D> _shotHits = new();
        private ulong _nextShotToken;
        private ulong _shotToken;
        private double _shotExpiresAt;

        internal readonly struct ShotReservation
        {
            private readonly AISceneGimmickController _source;
            private readonly ulong _token;
            internal readonly AttackInfo Attack;
            internal readonly Vector2 Direction;

            internal ShotReservation(AISceneGimmickController source, ulong token, Vector2 direction)
            {
                _source = source;
                _token = token;
                Attack = source.projectileAttack;
                Direction = direction;
            }

            internal bool IsAssigned => _token != 0;

            internal void Release()
            {
                if (_source != null && _source._shotToken == _token) { _source._shotToken = 0; }
            }
        }

        private Collider2D ProjectileHitCollider
        {
            get
            {
                if (requireTargetContact || projectileProp == null || projectileProp.gameObject != gameObject) { return null; }
                Collider2D hit = projectileProp.HitCollider;
                return ExplosiveProp2D.FindLiveContact(hit) == projectileProp ? hit : null;
            }
        }

        private bool IsProjectileOperationAvailable => projectileAttack != null && ProjectileHitCollider != null
            && (_shotToken == 0 || Time.fixedTimeAsDouble >= _shotExpiresAt);

        private AISceneGimmick WithProjectileOperation(AISceneGimmick value)
        {
            if (!shootExplosive) { return value; }
            Collider2D hit = ProjectileHitCollider;
            if (hit == null || !projectileProp.TryGetBlast(out Vector2 center, out float radiusSquared)) { return default; }

            value.operationPosition = hit.bounds.center;
            value.approachDistance = 0f;
            value.requiresClearOperation = true;
            value.effectPosition = center;
            value.effectRadius = Mathf.Sqrt(radiusSquared);
            value.effectDelay = 0f;
            value.usesProjectile = true;
            return value;
        }

        private bool TryAimProjectile(PhysicsScene2D world, Vector2 position, out Vector2 direction, out float lifetime)
        {
            direction = default;
            lifetime = 0f;
            ProjectileManager projectiles = GameManager.Projectiles;
            Collider2D hit = ProjectileHitCollider;
            if (hit == null || !world.IsValid() || projectiles == null || !projectiles.isActiveAndEnabled
                || projectiles.gameObject.scene.GetPhysicsScene2D() != world
                || GameManager.Actions == null
                || !GameManager.Actions.TryGetPropShot(projectileAttack, out BulletProfile bullet, out GameObject prefab))
            {
                return false;
            }

            int collisionMask = projectiles.GetCollisionMask(prefab);
            if ((collisionMask & (1 << hit.gameObject.layer)) == 0) { return false; }
            float dx = hit.bounds.center.x - position.x;
            float distance = Mathf.Min(Mathf.Abs(dx), bullet.speed * bullet.lifeTime);
            if (!(distance > 0f) || !float.IsFinite(position.y)) { return false; }

            Vector2 aim = Vector2.right * Mathf.Sign(dx);
            ContactFilter2D filter = new ContactFilter2D { useTriggers = true };
            filter.SetLayerMask(operationBlockers.value | collisionMask);
            world.Raycast(position, aim, distance, filter, _shotHits);
            bool reachesProp = false;
            foreach (RaycastHit2D contact in _shotHits)
            {
                if (contact.collider == hit)
                {
                    reachesProp = true;
                    break;
                }
                Collider2D obstruction = contact.collider;
                int layer = 1 << obstruction.gameObject.layer;
                if (!obstruction.isTrigger && (operationBlockers.value & layer) != 0
                    || (collisionMask & layer) != 0 && ExplosiveProp2D.FindLiveContact(obstruction) != null)
                {
                    break;
                }
            }
            _shotHits.Clear();
            if (!reachesProp) { return false; }

            direction = aim;
            lifetime = bullet.lifeTime;
            return true;
        }

        private bool ReserveShot(PhysicsScene2D world, Vector2 spawnPosition, out ShotReservation shot)
        {
            shot = default;
            if (!TryAimProjectile(world, spawnPosition, out Vector2 direction, out float lifetime)) { return false; }
            _shotToken = checked(++_nextShotToken);
            _shotExpiresAt = Time.fixedTimeAsDouble + lifetime;
            shot = new ShotReservation(this, _shotToken, direction);
            return true;
        }

#if UNITY_INCLUDE_TESTS
        internal void SetProjectileOperationForTest(ExplosiveProp2D prop, AttackInfo attack)
        {
            shootExplosive = true;
            projectileProp = prop;
            projectileAttack = attack;
            generation = checked(generation + 1);
            _shotToken = 0;
        }
#endif
    }
}
