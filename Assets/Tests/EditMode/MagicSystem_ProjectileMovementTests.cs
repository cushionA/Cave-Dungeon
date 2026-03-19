using NUnit.Framework;
using UnityEngine;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class MagicSystem_ProjectileMovementTests
    {
        [Test]
        public void ProjectileMovement_Straight_MaintainsDirection()
        {
            Projectile p = new Projectile();
            BulletProfile profile = new BulletProfile { moveType = BulletMoveType.Straight, speed = 10f };
            p.Initialize(1, profile, Vector2.zero, Vector2.right);

            Vector2 velocity = ProjectileMovement.CalculateVelocity(p, 0.1f, Vector2.zero);

            Assert.Greater(velocity.x, 0f);
            Assert.AreEqual(0f, velocity.y, 0.01f);
        }

        [Test]
        public void ProjectileMovement_Homing_TurnsTowardTarget()
        {
            Projectile p = new Projectile();
            BulletProfile profile = new BulletProfile { moveType = BulletMoveType.Homing, speed = 10f };
            p.Initialize(1, profile, Vector2.zero, Vector2.right);
            Vector2 target = new Vector2(0f, 10f);

            Vector2 velocity = ProjectileMovement.CalculateVelocity(p, 0.5f, target);

            Assert.Greater(velocity.y, 0f);
        }

        [Test]
        public void ProjectileMovement_Rain_AppliesGravity()
        {
            Projectile p = new Projectile();
            BulletProfile profile = new BulletProfile { moveType = BulletMoveType.Rain, speed = 5f };
            p.Initialize(1, profile, Vector2.zero, Vector2.right);

            Vector2 velocity = ProjectileMovement.CalculateVelocity(p, 0.5f, Vector2.zero);

            Assert.Less(velocity.y, 0f);
        }

        [Test]
        public void ProjectileMovement_Set_ReturnsZeroVelocity()
        {
            Projectile p = new Projectile();
            BulletProfile profile = new BulletProfile { moveType = BulletMoveType.Set, speed = 10f };
            p.Initialize(1, profile, Vector2.zero, Vector2.right);

            Vector2 velocity = ProjectileMovement.CalculateVelocity(p, 0.1f, Vector2.zero);

            Assert.AreEqual(Vector2.zero, velocity);
        }

        [Test]
        public void ProjectileMovement_UpdateAll_MovesProjectiles()
        {
            ProjectilePool pool = new ProjectilePool(4);
            Projectile p = pool.Get();
            p.Initialize(1, new BulletProfile { moveType = BulletMoveType.Straight, speed = 10f, hitLimit = 1, lifeTime = 5f }, Vector2.zero, Vector2.right);

            ProjectileMovement.UpdateAll(pool, 0.1f, Vector2.zero);

            Assert.Greater(p.Position.x, 0f);

            pool.Clear();
        }
    }
}
