using UnityEngine;

namespace Game.Core
{
    /// <summary>同じtickで確定した装置の登録世代と、操作・効果それぞれの範囲。</summary>
    public struct AISceneGimmick
    {
        public const float k_LureHeightTolerance = 0.25f;
        public const float k_MaxLureRun = 12f;
        public const float k_MaxApproachDistance = 12f;

        public int id;
        public int generation;
        public int objectMask;
        public PhysicsScene2D physicsScene;
        public Vector2 operationPosition;
        public Vector2 effectPosition;
        public float operationRadius;
        public float approachDistance;
        public float effectRadius;
        public float effectDelay;
        public bool requiresClearOperation;
        public bool requiresLure;
        public bool usesProjectile;
        public Vector2 lureExit;
        public float lurePlateX;

        public readonly bool IsValid => id != 0 && generation > 0
            && objectMask > 0 && objectMask <= 0xfff
            && IsFinite(operationPosition.x) && IsFinite(operationPosition.y)
            && IsFinite(effectPosition.x) && IsFinite(effectPosition.y)
            && IsFinite(operationRadius) && operationRadius >= 0f
            && IsFinite(approachDistance) && approachDistance >= 0f && approachDistance <= k_MaxApproachDistance
            && IsFinite(effectRadius) && effectRadius >= 0f
            && IsFinite(effectDelay) && effectDelay >= 0f
            && (!requiresLure || effectDelay == 0f && IsFinite(lureExit.x) && IsFinite(lureExit.y) && IsFinite(lurePlateX));

        public readonly bool CanOperateFrom(Vector2 position) =>
            (position - operationPosition).sqrMagnitude <= operationRadius * operationRadius;

        /// <summary>操作圏の少し内側にある、同じ高さの立ち位置を求める。床と遮蔽は観測側で確認する。</summary>
        public readonly bool TryGetApproachDestination(Vector2 position, out Vector2 destination)
        {
            destination = default;
            if (approachDistance <= 0f || requiresLure || CanOperateFrom(position)) { return false; }
            float vertical = position.y - operationPosition.y;
            float widthSquared = operationRadius * operationRadius - vertical * vertical;
            if (!(widthSquared > 0f) || !IsFinite(widthSquared)) { return false; }
            float width = Mathf.Sqrt(widthSquared);
            float direction = Mathf.Sign(operationPosition.x - position.x);
            float inset = Mathf.Min(width * 0.2f, 0.05f);
            destination = new Vector2(operationPosition.x - direction * (width - inset), position.y);
            float distance = Mathf.Abs(destination.x - position.x);
            return distance > 0f && distance <= approachDistance;
        }

        public readonly bool ContainsEffect(Vector2 position) =>
            (position - effectPosition).sqrMagnitude <= effectRadius * effectRadius;

        public readonly bool WillAffectTarget(Vector2 position, Vector2 velocity) =>
            ContainsEffect(effectDelay == 0f ? position : position + velocity * effectDelay);

        /// <summary>追跡者、誘い役、板、退避点の順に並ぶ平坦な経路だけを使う。</summary>
        public readonly bool CanLureFrom(Vector2 owner, Vector2 target)
        {
            if (!requiresLure || !IsFinite(owner.x) || !IsFinite(owner.y) || !IsFinite(target.x) || !IsFinite(target.y)
                || Mathf.Abs(owner.y - lureExit.y) > k_LureHeightTolerance
                || Mathf.Abs(target.y - owner.y) > k_LureHeightTolerance
                || ContainsEffect(owner) || ContainsEffect(target) || ContainsEffect(lureExit)) { return false; }

            float direction = lureExit.x > lurePlateX ? 1f : -1f;
            return (owner.x - target.x) * direction > 0f && (lurePlateX - owner.x) * direction > 0f
                && (lureExit.x - lurePlateX) * direction >= GameConstants.k_DefaultAttackRange + 0.25f
                && (lureExit.x - target.x) * direction <= k_MaxLureRun;
        }

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
