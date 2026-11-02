using System;
using NUnit.Framework;
using UnityEngine;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class ProjectileLeaseTests
    {
        [Test]
        public void ProjectilePool_OldLeaseReturn_DoesNotReleaseTheReusedProjectile()
        {
            ProjectilePool pool = new ProjectilePool(0);
            Projectile projectile = pool.Get();
            ulong oldLease = projectile.LeaseId;
            Assert.IsTrue(pool.TryReturn(projectile, oldLease));
            Projectile reused = pool.Get();
            reused.Initialize(1, CreateProfile(), Vector2.zero, Vector2.right);

            Assert.AreSame(projectile, reused);
            Assert.AreNotEqual(oldLease, reused.LeaseId);
            Assert.IsFalse(pool.TryReturn(projectile, oldLease));
            Assert.IsFalse(pool.IsActive(projectile, oldLease));
            Assert.IsTrue(pool.IsActive(reused, reused.LeaseId));
            Assert.IsTrue(reused.IsAlive);
            Assert.AreEqual(1, pool.ActiveCount);
        }

        [Test]
        public void ProjectilePool_ReturnMiddleThenMovedLast_KeepsActiveOwnershipConsistent()
        {
            ProjectilePool pool = new ProjectilePool(0);
            Projectile first = pool.Get();
            Projectile middle = pool.Get();
            Projectile last = pool.Get();
            ulong firstLease = first.LeaseId;
            ulong lastLease = last.LeaseId;

            Assert.IsTrue(pool.TryReturn(middle, middle.LeaseId));
            Assert.IsTrue(pool.IsActive(first, firstLease));
            Assert.IsTrue(pool.IsActive(last, lastLease));
            Assert.IsTrue(pool.TryReturn(last, lastLease));
            Assert.IsTrue(pool.TryReturn(first, firstLease));
            Assert.AreEqual(0, pool.ActiveCount);
        }

        [Test]
        public void ProjectileSimulation_InvalidDelta_RejectsBeforeMutatingState()
        {
            Projectile projectile = new Projectile();
            projectile.Initialize(1, CreateProfile(), Vector2.zero, Vector2.right);

            Assert.Throws<ArgumentOutOfRangeException>(() => ProjectileMovement.UpdateProjectile(projectile, -1f, Vector2.zero));
            Assert.Throws<ArgumentOutOfRangeException>(() => ProjectileMovement.UpdateProjectile(projectile, float.NaN, Vector2.zero));
            Assert.AreEqual(Vector2.zero, projectile.Position);
            Assert.AreEqual(0f, projectile.ElapsedTime);
        }

        [Test]
        public void ProjectileSimulation_DeferredLifetime_ResolvesLastSegmentBeforeDeath()
        {
            BulletProfile profile = CreateProfile();
            profile.lifeTime = 0.1f;
            profile.hitLimit = 2;
            Projectile projectile = new Projectile();
            projectile.Initialize(1, profile, Vector2.zero, Vector2.right);

            ProjectileMovement.UpdateProjectile(projectile, 0.25f, Vector2.zero, true);

            Assert.IsTrue(projectile.IsAlive, "The lifetime segment must remain available for hit resolution.");
            Assert.AreEqual(1f, projectile.Position.x, 0.0001f);
            Assert.IsTrue(projectile.RegisterHit());
            Assert.IsTrue(projectile.IsAlive);
            projectile.CompleteSimulationStep();
            Assert.IsFalse(projectile.IsAlive);
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
