using System;
using UnityEngine;

namespace Game.Core
{
    [Serializable]
    public struct BulletProfile
    {
        public BulletMoveType moveType;
        public float speed;
        public float acceleration;
        public float angle;
        public float spreadAngle;
        public float lifeTime;
        public int hitLimit;
        public float emitInterval;
        public BulletFeature features;
        public float explodeRadius;
        public Vector2 knockbackForce;
    }
}
