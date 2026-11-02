using UnityEngine;

namespace Game.Core
{
    public static class ProjectileMovement
    {
        private static readonly Vector2 s_Gravity = new Vector2(0f, -9.81f);

        public static Vector2 CalculateVelocity(Projectile projectile, float deltaTime,
            Vector2 targetPosition)
        {
            ref readonly BulletProfile profile = ref projectile.CapturedProfile;
            Vector2 currentVelocity = projectile.Velocity;

            switch (profile.moveType)
            {
                case BulletMoveType.Straight:
                    return ApplyAcceleration(currentVelocity, profile.acceleration, deltaTime);

                case BulletMoveType.Homing:
                {
                    Vector2 toTarget = (targetPosition - projectile.Position).normalized;
                    Vector2 direction = currentVelocity.sqrMagnitude > 0f
                        ? currentVelocity.normalized : projectile.InitialDirection;
                    if (direction.sqrMagnitude == 0f) { direction = toTarget; }
                    float speed = Mathf.Max(0f, profile.speed + profile.acceleration * projectile.ElapsedTime);
                    if (toTarget.sqrMagnitude == 0f) { return direction * speed; }

                    // 速度の線形補間では真後ろの目標へ曲がれず、相殺時は永久停止する。
                    // 方向の最短角を補間し、減速による速度は 0 で止める。
                    float cross = direction.x * toTarget.y - direction.y * toTarget.x;
                    float dot = direction.x * toTarget.x + direction.y * toTarget.y;
                    float turn = Mathf.Atan2(cross, dot)
                        * Mathf.Clamp01(projectile.GetCurrentHomingStrength() * deltaTime);
                    float angle = Mathf.Atan2(direction.y, direction.x) + turn;
                    return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed;
                }

                case BulletMoveType.Angle:
                {
                    float rad = profile.angle * Mathf.Deg2Rad;
                    Vector2 direction = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
                    float speed = Mathf.Max(0f, profile.speed + profile.acceleration * projectile.ElapsedTime);
                    return direction * speed;
                }

                case BulletMoveType.Rain:
                {
                    return currentVelocity + s_Gravity * deltaTime;
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
                UpdateProjectile(pool.ActiveProjectiles[i], deltaTime, defaultTarget);
            }
        }

        /// <summary>
        /// 時間・移動・重力を一度だけ進める。Runtime は最後の区間を sweep してから
        /// CompleteSimulationStep を呼べるよう寿命終了の確定を遅らせられる。
        /// </summary>
        public static void UpdateProjectile(Projectile projectile, float deltaTime, Vector2 defaultTarget,
            bool deferLifetimeEnd = false)
        {
            float activeTime = projectile.AdvanceTime(deltaTime);
            if (activeTime > 0f)
            {
                Vector2 target = projectile.TargetPosition.sqrMagnitude > 0.001f
                    ? projectile.TargetPosition : defaultTarget;
                Vector2 velocity = CalculateVelocity(projectile, activeTime, target);
                projectile.Velocity = ApplyGravity(in projectile.CapturedProfile, velocity, activeTime);
                projectile.Position += projectile.Velocity * activeTime;
            }
            if (!deferLifetimeEnd) { projectile.CompleteSimulationStep(); }
        }

        internal static Vector2 ApplyGravity(in BulletProfile profile, Vector2 velocity, float deltaTime)
        {
            // Rain 自身が重力を持つ。Set は静止弾なのでどちらも追加しない。
            if ((profile.features & BulletFeature.Gravity) == 0
                || profile.moveType == BulletMoveType.Rain || profile.moveType == BulletMoveType.Set)
            { return velocity; }
            return velocity + s_Gravity * deltaTime;
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
