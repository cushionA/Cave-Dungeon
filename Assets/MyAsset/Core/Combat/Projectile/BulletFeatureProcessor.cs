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
        private static readonly Vector2 s_Gravity = new Vector2(0f, -9.81f);
        private static List<int> s_ExplosionBuffer = new List<int>();

        public static void ProcessFeatures(Projectile projectile, float deltaTime)
        {
            BulletFeature features = projectile.Profile.features;

            if ((features & BulletFeature.Gravity) != 0)
            {
                projectile.Velocity += s_Gravity * deltaTime;
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
        /// <summary>
        /// 爆発範囲内の全キャラクターハッシュを返す。
        /// 返却リストは内部バッファのため次回呼び出しで上書きされる。
        /// </summary>
        public static List<int> GetExplosionTargets(Vector2 center, float radius,
            List<int> allHashes, SoACharaDataDic data)
        {
            s_ExplosionBuffer.Clear();
            float sqrRadius = radius * radius;
            for (int i = 0; i < allHashes.Count; i++)
            {
                int hash = allHashes[i];
                if (!data.TryGetValue(hash, out int _))
                {
                    continue;
                }
                ref CharacterVitals v = ref data.GetVitals(hash);
                float sqrDist = (center - v.position).sqrMagnitude;
                if (sqrDist <= sqrRadius)
                {
                    s_ExplosionBuffer.Add(hash);
                }
            }
            return s_ExplosionBuffer;
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
