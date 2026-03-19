using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class MagicSystem_BulletFeaturesTests
    {
        [Test]
        public void BulletFeatureProcessor_Pierce_ReturnsTrue()
        {
            Assert.IsTrue(BulletFeatureProcessor.ShouldPierce(BulletFeature.Pierce));
            Assert.IsFalse(BulletFeatureProcessor.ShouldPierce(BulletFeature.None));
        }

        [Test]
        public void BulletFeatureProcessor_Explode_ReturnsTrue()
        {
            Assert.IsTrue(BulletFeatureProcessor.ShouldExplode(BulletFeature.Explode));
            Assert.IsTrue(BulletFeatureProcessor.ShouldExplode(BulletFeature.Pierce | BulletFeature.Explode));
        }

        [Test]
        public void BulletFeatureProcessor_Reflect_ReversesX()
        {
            Projectile p = new Projectile();
            p.Initialize(1, new BulletProfile { speed = 10f, hitLimit = 1 }, Vector2.zero, Vector2.right);
            float originalX = p.Velocity.x;

            BulletFeatureProcessor.Reflect(p);

            Assert.AreEqual(-originalX, p.Velocity.x, 0.01f);
        }

        [Test]
        public void BulletFeatureProcessor_ExplosionTargets_FindsInRadius()
        {
            SoACharaDataDic data = new SoACharaDataDic();
            data.Add(1, new CharacterVitals { position = Vector2.zero }, default, default, default);
            data.Add(2, new CharacterVitals { position = new Vector2(3f, 0f) }, default, default, default);
            data.Add(3, new CharacterVitals { position = new Vector2(20f, 0f) }, default, default, default);

            List<int> targets = BulletFeatureProcessor.GetExplosionTargets(
                Vector2.zero, 5f, new List<int> { 1, 2, 3 }, data);

            Assert.AreEqual(2, targets.Count);
            Assert.IsTrue(targets.Contains(1));
            Assert.IsTrue(targets.Contains(2));
            data.Dispose();
        }

        [Test]
        public void BulletFeatureProcessor_CombinedFlags_WorkCorrectly()
        {
            BulletFeature combined = BulletFeature.Pierce | BulletFeature.Knockback | BulletFeature.Gravity;
            Assert.IsTrue(BulletFeatureProcessor.ShouldPierce(combined));
            Assert.IsTrue(BulletFeatureProcessor.HasKnockback(combined));
            Assert.IsFalse(BulletFeatureProcessor.ShouldExplode(combined));
        }
    }
}
