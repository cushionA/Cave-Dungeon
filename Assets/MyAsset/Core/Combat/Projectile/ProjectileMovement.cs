using UnityEngine;

namespace Game.Core
{
    public static class ProjectileMovement
    {
        public static Vector2 CalculateVelocity(Projectile projectile, float deltaTime,
            Vector2 targetPosition)
        {
            BulletProfile profile = projectile.Profile;
            Vector2 currentVelocity = projectile.Velocity;

            switch (profile.moveType)
            {
                case BulletMoveType.Straight:
                    return ApplyAcceleration(currentVelocity, profile.acceleration, deltaTime);

                case BulletMoveType.Homing:
                {
                    Vector2 toTarget = (targetPosition - projectile.Position).normalized;
                    float homingStrength = 5f;
                    Vector2 desired = toTarget * (currentVelocity.magnitude + profile.acceleration * deltaTime);
                    Vector2 blended = Vector2.Lerp(currentVelocity, desired, homingStrength * deltaTime);
                    float speed = profile.speed + profile.acceleration * projectile.ElapsedTime;
                    return blended.normalized * speed;
                }

                case BulletMoveType.Angle:
                {
                    float rad = profile.angle * Mathf.Deg2Rad;
                    Vector2 direction = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
                    float speed = profile.speed + profile.acceleration * projectile.ElapsedTime;
                    return direction * speed;
                }

                case BulletMoveType.Rain:
                {
                    Vector2 gravity = new Vector2(0f, -9.81f);
                    return currentVelocity + gravity * deltaTime;
                }

                case BulletMoveType.Set:
                    return Vector2.zero;

                case BulletMoveType.Stop:
                {
                    float decel = profile.acceleration;
                    float speed = currentVelocity.magnitude - Mathf.Abs(decel) * deltaTime;
                    if (speed <= 0f)
                    {
                        return Vector2.zero;
                    }
                    return currentVelocity.normalized * speed;
                }

                default:
                    return currentVelocity;
            }
        }

        public static void UpdateAll(ProjectilePool pool, float deltaTime, Vector2 defaultTarget)
        {
            for (int i = 0; i < pool.ActiveProjectiles.Count; i++)
            {
                Projectile p = pool.ActiveProjectiles[i];
                if (!p.IsAlive)
                {
                    continue;
                }

                Vector2 newVelocity = CalculateVelocity(p, deltaTime, defaultTarget);
                p.Velocity = newVelocity;
                p.Position += newVelocity * deltaTime;
                p.Tick(deltaTime);
            }
        }

        private static Vector2 ApplyAcceleration(Vector2 velocity, float acceleration, float deltaTime)
        {
            if (Mathf.Approximately(acceleration, 0f))
            {
                return velocity;
            }

            float speed = velocity.magnitude + acceleration * deltaTime;
            if (speed < 0f)
            {
                speed = 0f;
            }
            return velocity.normalized * speed;
        }
    }
}
