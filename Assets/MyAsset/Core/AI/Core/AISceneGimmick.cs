using UnityEngine;

namespace Game.Core
{
    /// <summary>同じtickで確定した装置の登録世代と、操作・効果それぞれの範囲。</summary>
    public struct AISceneGimmick
    {
        public int id;
        public int generation;
        public int objectMask;
        public PhysicsScene2D physicsScene;
        public Vector2 operationPosition;
        public Vector2 effectPosition;
        public float operationRadius;
        public float effectRadius;
        public float effectDelay;
        public bool requiresClearOperation;

        public readonly bool IsValid => id != 0 && generation > 0
            && objectMask > 0 && objectMask <= 0xfff
            && IsFinite(operationPosition.x) && IsFinite(operationPosition.y)
            && IsFinite(effectPosition.x) && IsFinite(effectPosition.y)
            && IsFinite(operationRadius) && operationRadius >= 0f
            && IsFinite(effectRadius) && effectRadius >= 0f
            && IsFinite(effectDelay) && effectDelay >= 0f;

        public readonly bool CanOperateFrom(Vector2 position) =>
            (position - operationPosition).sqrMagnitude <= operationRadius * operationRadius;

        public readonly bool ContainsEffect(Vector2 position) =>
            (position - effectPosition).sqrMagnitude <= effectRadius * effectRadius;

        public readonly bool WillAffectTarget(Vector2 position, Vector2 velocity) =>
            ContainsEffect(effectDelay == 0f ? position : position + velocity * effectDelay);

        /// <summary>味方は着弾時の一点に加えて、そこまでに効果範囲を横切る場合も保護する。</summary>
        public readonly bool ThreatensFriendly(Vector2 position, Vector2 velocity)
        {
            if (effectDelay == 0f) { return ContainsEffect(position); }
            Vector2 travel = velocity * effectDelay;
            float lengthSquared = travel.sqrMagnitude;
            if (!IsFinite(lengthSquared) || !IsFinite(position.x) || !IsFinite(position.y)) { return true; }
            if (lengthSquared == 0f) { return ContainsEffect(position); }
            float fraction = Mathf.Clamp01(Vector2.Dot(effectPosition - position, travel) / lengthSquared);
            return ContainsEffect(position + fraction * travel);
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
