using System;
using NUnit.Framework;
using UnityEngine;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class ProjectileSimulationTests
    {
        [Test]
        public void ProjectilePool_DoubleReturn_DoesNotLeaseTheSameInstanceTwice()
        {
            ProjectilePool pool = new ProjectilePool(0);
            Projectile original = pool.Get();
            pool.Return(original);
            pool.Return(original);

            Projectile first = pool.Get();
            Projectile second = pool.Get();

            Assert.AreNotSame(first, second);
            Assert.AreEqual(2, pool.ActiveCount);
        }

        [Test]
        public void ProjectilePool_ForeignReturn_DoesNotResetOrAdoptAnotherPoolsProjectile()
        {
            ProjectilePool owner = new ProjectilePool(0);
            ProjectilePool other = new ProjectilePool(0);
            Projectile projectile = owner.Get();
            projectile.Initialize(1, CreateProfile(), Vector2.zero, Vector2.right);

            other.Return(projectile);

            Assert.IsTrue(projectile.IsAlive);
            Assert.AreEqual(1, owner.ActiveCount);
            Assert.AreEqual(0, other.ActiveCount);
            Assert.AreNotSame(projectile, other.Get());
        }

        [Test]
        public void ProjectileSimulation_CrossesSpawnDelay_UsesTheRemainingStepForMotionAndLifetime()
        {
            BulletProfile profile = CreateProfile();
            profile.spawnDelay = 0.1f;
            ProjectilePool pool = new ProjectilePool(0);
            Projectile projectile = pool.Get();
            projectile.Initialize(1, profile, Vector2.zero, Vector2.right);

            ProjectileMovement.UpdateAll(pool, 0.25f, Vector2.zero);

            Assert.IsFalse(projectile.IsSpawnDelayed);
            Assert.AreEqual(0.15f, projectile.ElapsedTime, 0.0001f);
            Assert.AreEqual(1.5f, projectile.Position.x, 0.0001f);
        }

        [Test]
        public void ProjectileTick_CrossesSpawnDelay_UsesTheSameActiveClockAsSimulation()
        {
            BulletProfile profile = CreateProfile();
            profile.spawnDelay = 0.1f;
            Projectile projectile = new Projectile();
            projectile.Initialize(1, profile, Vector2.zero, Vector2.right);

            projectile.Tick(0.25f);

            Assert.AreEqual(0.15f, projectile.ElapsedTime, 0.0001f);
        }

        [Test]
        public void ProjectileSimulation_CrossesLifetime_StopsAtItsLifetimePosition()
        {
            BulletProfile profile = CreateProfile();
            profile.lifeTime = 0.1f;
            ProjectilePool pool = new ProjectilePool(0);
            Projectile projectile = pool.Get();
            projectile.Initialize(1, profile, Vector2.zero, Vector2.right);

            ProjectileMovement.UpdateAll(pool, 0.25f, Vector2.zero);

            Assert.IsFalse(projectile.IsAlive);
            Assert.AreEqual(0.1f, projectile.ElapsedTime, 0.0001f);
            Assert.AreEqual(1f, projectile.Position.x, 0.0001f);
        }

        [Test]
        public void ProjectileSimulation_GravityFeature_AffectsMotionInTheSameStep()
        {
            BulletProfile profile = CreateProfile();
            profile.features = BulletFeature.Gravity;
            ProjectilePool pool = new ProjectilePool(0);
            Projectile projectile = pool.Get();
            projectile.Initialize(1, profile, Vector2.zero, Vector2.right);

            ProjectileMovement.UpdateAll(pool, 0.1f, Vector2.zero);

            Assert.AreEqual(-0.981f, projectile.Velocity.y, 0.0001f);
            Assert.AreEqual(-0.0981f, projectile.Position.y, 0.0001f);
        }

        [Test]
        public void ProjectileSimulation_RainWithGravity_AppliesGravityOnce()
        {
            BulletProfile profile = CreateProfile();
            profile.moveType = BulletMoveType.Rain;
            profile.features = BulletFeature.Gravity;
            ProjectilePool pool = new ProjectilePool(0);
            Projectile projectile = pool.Get();
            projectile.Initialize(1, profile, Vector2.zero, Vector2.right);

            ProjectileMovement.UpdateAll(pool, 0.1f, Vector2.zero);

            Assert.AreEqual(-0.981f, projectile.Velocity.y, 0.0001f);
            Assert.AreEqual(-0.0981f, projectile.Position.y, 0.0001f);
        }

        [Test]
        public void ProjectileMovement_HomingTargetDirectlyBehind_StartsTurningWithoutStalling()
        {
            BulletProfile profile = CreateProfile();
            profile.moveType = BulletMoveType.Homing;
            Projectile projectile = new Projectile();
            projectile.Initialize(1, profile, Vector2.zero, Vector2.right);

            Vector2 velocity = ProjectileMovement.CalculateVelocity(projectile, 0.02f, Vector2.left);

            Assert.Greater(Mathf.Abs(velocity.y), 0.01f);
            Assert.AreEqual(10f, velocity.magnitude, 0.0001f);
        }

        [TestCase(BulletMoveType.Homing)]
        [TestCase(BulletMoveType.Angle)]
        public void ProjectileMovement_DeceleratesPastZero_DoesNotReverse(BulletMoveType moveType)
        {
            BulletProfile profile = CreateProfile();
            profile.moveType = moveType;
            profile.speed = 1f;
            profile.acceleration = -1f;
            Projectile projectile = new Projectile();
            projectile.Initialize(1, profile, Vector2.zero, Vector2.right);
            projectile.Tick(2f);

            Vector2 velocity = ProjectileMovement.CalculateVelocity(projectile, 0.1f, Vector2.right);

            Assert.AreEqual(Vector2.zero, velocity);
        }

        private static BulletProfile CreateProfile()
        {
            return new BulletProfile
            {
                moveType = BulletMoveType.Straight,
                speed = 10f,
                hitLimit = 1,
                lifeTime = 10f
            };
        }
    }
}
