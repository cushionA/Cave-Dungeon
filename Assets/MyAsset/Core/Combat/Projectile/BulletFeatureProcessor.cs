using System.Collections.Generic;
using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// Processes BulletFeature flags on projectiles.
    /// Provides gravity, pierce, explode, reflect, knockback queries and effects.
    /// </summary>
    public static class BulletFeatureProcessor
    {
        public static void ProcessFeatures(Projectile projectile, float deltaTime)
        {
            BulletFeature features = projectile.Profile.features;

            if ((features & BulletFeature.Gravity) != 0)
            {
                projectile.Velocity += new Vector2(0f, -9.81f) * deltaTime;
            }
        }

        public static bool ShouldPierce(BulletFeature features)
        {
            return (features & BulletFeature.Pierce) != 0;
        }

        public static bool ShouldExplode(BulletFeature features)
        {
            return (features & BulletFeature.Explode) != 0;
        }

        /// <summary>
        /// Finds all character hashes within explosion radius from center position.
        /// </summary>
        public static List<int> GetExplosionTargets(Vector2 center, float radius,
            List<int> allHashes, SoACharaDataDic data)
        {
            List<int> targets = new List<int>();
            for (int i = 0; i < allHashes.Count; i++)
            {
                int hash = allHashes[i];
                if (!data.TryGetValue(hash, out int _))
                {
                    continue;
                }
                ref CharacterVitals v = ref data.GetVitals(hash);
                float dist = Vector2.Distance(center, v.position);
                if (dist <= radius)
                {
                    targets.Add(hash);
                }
            }
            return targets;
        }

        public static bool ShouldReflect(BulletFeature features)
        {
            return (features & BulletFeature.Reflect) != 0;
        }

        /// <summary>
        /// Reverses the projectile's horizontal velocity for reflection.
        /// </summary>
        public static void Reflect(Projectile projectile)
        {
            projectile.Velocity = new Vector2(-projectile.Velocity.x, projectile.Velocity.y);
        }

        public static bool HasKnockback(BulletFeature features)
        {
            return (features & BulletFeature.Knockback) != 0;
        }
    }
}
