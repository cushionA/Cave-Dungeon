using UnityEngine;

namespace Game.Core
{
    public class Projectile
    {
        public int CasterHash { get; private set; }
        public int RemainingHits { get; private set; }
        public BulletProfile Profile { get; private set; }
        public float ElapsedTime { get; private set; }
        public bool IsAlive { get; private set; }
        public int TargetHash { get; set; }
        public Vector2 Position { get; set; }
        public Vector2 Velocity { get; set; }
        public Vector2 TargetPosition { get; set; }

        public void Initialize(int casterHash, BulletProfile profile, Vector2 position, Vector2 direction)
        {
            CasterHash = casterHash;
            Profile = profile;
            RemainingHits = profile.hitLimit > 0 ? profile.hitLimit : 1;
            ElapsedTime = 0f;
            IsAlive = true;
            Position = position;
            Velocity = direction.normalized * profile.speed;
        }

        public void Tick(float deltaTime)
        {
            if (!IsAlive)
            {
                return;
            }

            ElapsedTime += deltaTime;
            if (Profile.lifeTime > 0f && ElapsedTime >= Profile.lifeTime)
            {
                Kill();
                return;
            }
        }

        public bool RegisterHit()
        {
            if (!IsAlive)
            {
                return false;
            }

            RemainingHits--;
            bool hasPierce = (Profile.features & BulletFeature.Pierce) != 0;

            if (RemainingHits <= 0 && !hasPierce)
            {
                Kill();
            }

            return true;
        }

        public void Kill()
        {
            IsAlive = false;
        }

        public void Reset()
        {
            IsAlive = false;
            CasterHash = 0;
            TargetHash = 0;
            RemainingHits = 0;
            ElapsedTime = 0f;
            Position = Vector2.zero;
            Velocity = Vector2.zero;
            TargetPosition = Vector2.zero;
        }
    }
}
