using NUnit.Framework;
using UnityEngine;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class MagicSystem_ProjectileCoreTests
    {
        [Test]
        public void Projectile_Initialize_SetsProperties()
        {
            Projectile p = new Projectile();
            BulletProfile profile = new BulletProfile { speed = 10f, hitLimit = 3, lifeTime = 5f };

            p.Initialize(100, profile, Vector2.zero, Vector2.right);

            Assert.AreEqual(100, p.CasterHash);
            Assert.AreEqual(3, p.RemainingHits);
            Assert.IsTrue(p.IsAlive);
        }

        [Test]
        public void Projectile_Tick_DiesOnLifetimeExpiry()
        {
            Projectile p = new Projectile();
            BulletProfile profile = new BulletProfile { speed = 10f, hitLimit = 1, lifeTime = 1f };
            p.Initialize(100, profile, Vector2.zero, Vector2.right);

            p.Tick(1.1f);

            Assert.IsFalse(p.IsAlive);
        }

        [Test]
        public void Projectile_RegisterHit_DecreasesHits()
        {
            Projectile p = new Projectile();
            BulletProfile profile = new BulletProfile { speed = 10f, hitLimit = 2 };
            p.Initialize(100, profile, Vector2.zero, Vector2.right);

            p.RegisterHit();
            Assert.AreEqual(1, p.RemainingHits);
            Assert.IsTrue(p.IsAlive);

            p.RegisterHit();
            Assert.IsFalse(p.IsAlive);
        }

        [Test]
        public void ProjectilePool_GetAndReturn_Recycles()
        {
            ProjectilePool pool = new ProjectilePool(4);

            Projectile p = pool.Get();
            Assert.AreEqual(1, pool.ActiveCount);

            pool.Return(p);
            Assert.AreEqual(0, pool.ActiveCount);
        }

        [Test]
        public void ProjectilePool_ReturnAllDead_CleansUp()
        {
            ProjectilePool pool = new ProjectilePool(4);
            Projectile p1 = pool.Get();
            Projectile p2 = pool.Get();
            p1.Initialize(1, new BulletProfile { hitLimit = 1, speed = 1f }, Vector2.zero, Vector2.right);
            p2.Initialize(2, new BulletProfile { hitLimit = 1, speed = 1f }, Vector2.zero, Vector2.right);

            p1.Kill();
            pool.ReturnAllDead();

            Assert.AreEqual(1, pool.ActiveCount);
        }
    }
}
