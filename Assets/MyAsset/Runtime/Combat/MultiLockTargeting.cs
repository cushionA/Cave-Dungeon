using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.Runtime
{
    /// <summary>プレイヤーの印とAIの攻撃で使う、世代・陣営・射程・射線の判定。</summary>
    public sealed class MultiLockTargeting
    {
        public const int MaxTargets = ActionTargetSelection.MaxTargets;
        private readonly List<RaycastHit2D> _hits = new List<RaycastHit2D>(8);
        private readonly List<int> _hashes = new List<int>(32);
        private readonly List<(ActorRef target, float distance)> _candidates = new List<(ActorRef, float)>(16);

        public static float GetRange(ActionDefinition definition)
        {
            if (definition == null || !(definition is AttackInfo attack)) { return 0f; }
            ProjectileInfo projectile = attack.projectileInfo;
            return (!projectile.hasProjectile || (SupportsTrajectory(projectile.trajectory)
                    && projectile.groundImpactRange == 0f && projectile.speed > 0f))
                && attack.multiLockRange > 0f && !float.IsInfinity(attack.multiLockRange)
                && (attack.targetPolicy.Resolve(attack.magicType) & AttackTargetRelation.Hostile) != 0
                ? attack.multiLockRange : 0f;
        }

        private static bool SupportsTrajectory(BulletMoveType trajectory) => trajectory == BulletMoveType.Straight
            || trajectory == BulletMoveType.Homing || trajectory == BulletMoveType.Rain || trajectory == BulletMoveType.Stop;

        public bool TryGetCandidate(ActorRef owner, ActorRef target, float range, out Vector2 position)
        {
            position = default;
            if (!(range > 0f) || float.IsInfinity(range) || !TryGetActor(owner, out BaseCharacter caster)
                || !TryGetActor(target, out BaseCharacter actor)
                || !AttackTargetRules.CanAffect(owner, GameManager.Data.GetFlags(owner.Hash).Belong,
                    target, GameManager.Data.GetFlags(target.Hash).Belong, AttackTargetRelation.Hostile))
            {
                return false;
            }

            PhysicsScene2D scene = GameManager.PhysicsSceneOf(caster);
            if (scene != GameManager.PhysicsSceneOf(actor)) { return false; }

            position = actor.TargetingCenter;
            Vector2 origin = caster.TargetingCenter;
            Vector2 direction = position - origin;
            if (direction.sqrMagnitude > range * range) { return false; }
            if (direction.sqrMagnitude <= Mathf.Epsilon) { return true; }

            ContactFilter2D filter = new ContactFilter2D { useTriggers = false };
            scene.Raycast(origin, direction.normalized, direction.magnitude, filter, _hits);
            foreach (RaycastHit2D hit in _hits)
            {
                // Solid terrain can hide a mark. Actors remain independent candidates in a crowd.
                if (hit.collider.GetComponentInParent<BaseCharacter>() == null) { return false; }
            }
            return true;
        }

        internal ActionTargetSelection SelectForAI(in ActionExecution cast, float range)
        {
            ActionTargetSelection selected = default;
            if (!TryGetActor(cast.ownerRef, out BaseCharacter caster)) { return selected; }

            Physics2D.SyncTransforms();
            Vector2 origin = caster.TargetingCenter;
            Vector2 forward = cast.direction;
            ActorRef primary = cast.targetRef;
            if (!primary.IsValid && GameManager.TargetRelations != null)
            {
                GameManager.TargetRelations.TryGetTarget(cast.ownerRef, out primary);
            }
            if (TryGetActor(primary, out BaseCharacter primaryActor))
            {
                Vector2 offset = primaryActor.TargetingCenter - origin;
                if (offset != Vector2.zero) { forward = offset; }
            }
            if (TryGetCandidate(cast.ownerRef, primary, range, out _)) { selected.TryAdd(primary); }

            _candidates.Clear();
            _hashes.Clear();
            GameManager.Data.GetAllHashes(_hashes);
            foreach (int hash in _hashes)
            {
                if (hash == primary.Hash || !GameManager.Actors.TryGet(hash, out ActorRef candidate)
                    || !TryGetCandidate(cast.ownerRef, candidate, range, out Vector2 position)) { continue; }
                Vector2 offset = position - origin;
                if (Vector2.Dot(forward, offset) >= 0f) { _candidates.Add((candidate, offset.sqrMagnitude)); }
            }
            _candidates.Sort(CompareCandidates);
            for (int i = 0; i < _candidates.Count && selected.Count < MaxTargets; i++)
            {
                selected.TryAdd(_candidates[i].target);
            }
            return selected;
        }

        internal ActionTargetSelection Revalidate(ActorRef owner, in ActionTargetSelection selected, float range)
        {
            ActionTargetSelection current = default;
            if (selected.Count == 0) { return current; }

            Physics2D.SyncTransforms();
            for (int i = 0; i < selected.Count; i++)
            {
                ActorRef target = selected[i];
                if (TryGetCandidate(owner, target, range, out _)) { current.TryAdd(target); }
            }
            return current;
        }

        internal static bool TryGetActor(ActorRef actor, out BaseCharacter character)
        {
            character = null;
            if (GameManager.Actors == null || !GameManager.Actors.IsCurrent(actor)
                || !GameManager.IsCharacterValid(actor.Hash)) { return false; }
            CharacterFlags flags = GameManager.Data.GetFlags(actor.Hash);
            if (flags.IsVanished || flags.ActState == ActState.Dead
                || GameManager.Data.GetVitals(actor.Hash).currentHp <= 0) { return false; }
            character = GameManager.Data.GetManaged(actor.Hash) as BaseCharacter;
            return character != null && character.gameObject.activeInHierarchy;
        }

        private static int CompareCandidates((ActorRef target, float distance) first,
            (ActorRef target, float distance) second)
        {
            int distance = first.distance.CompareTo(second.distance);
            return distance != 0 ? distance : first.target.Hash.CompareTo(second.target.Hash);
        }
    }
}
