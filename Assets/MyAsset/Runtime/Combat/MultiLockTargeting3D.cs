using System.Collections.Generic;
using Game.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Runtime
{
    /// <summary>3Dの入力と発射で共通の、現行世代・陣営・距離・射線の検査。</summary>
    internal sealed class MultiLockTargeting3D
    {
        private readonly List<int> _hashes = new List<int>(32);
        private readonly List<(ActorRef actor, float distance)> _candidates = new List<(ActorRef, float)>(16);

        internal static float GetRange(AttackInfo definition)
        {
            return definition != null && SupportsLock(definition.attack3D.shape)
                ? MultiLockTargeting.GetRange(definition) : 0f;
        }

        internal static bool SupportsLock(AttackShape3D shape) =>
            shape == AttackShape3D.Projectile || shape == AttackShape3D.Melee;

        internal bool TryGetCandidate(CharacterActor3D owner, ActorRef target, AttackInfo definition, out Vector3 position)
        {
            position = default;
            float range = GetRange(definition);
            return range > 0f && TryGetCandidate(owner, target, definition.attack3D,
                definition.targetPolicy.Resolve(definition.magicType) & AttackTargetRelation.Hostile, range, out position);
        }

        internal bool TryGetCandidate(CharacterActor3D owner, ActorRef target, in AttackProfile3D shape,
            AttackTargetRelation relations, float range, out Vector3 position)
        {
            position = default;
            if (owner == null || !owner.IsAlive || target == owner.Actor || !TryGetActor(target, out CharacterActor3D actor)
                || ((1 << actor.Body.gameObject.layer) & shape.targetLayers.value) == 0)
            {
                return false;
            }

            PhysicsScene world = owner.gameObject.scene.GetPhysicsScene();
            if (actor.gameObject.scene.GetPhysicsScene() != world
                || !AttackTargetRules.CanAffect(owner.Actor, GameManager.Data.GetFlags(owner.ObjectHash).Belong,
                    target, GameManager.Data.GetFlags(actor.ObjectHash).Belong, relations))
            {
                return false;
            }

            position = actor.Body.bounds.center;
            Vector3 origin = Origin(owner, shape);
            Vector3 offset = position - origin;
            float distance = offset.magnitude;
            return distance <= range && (distance <= 0f || !world.Raycast(origin,
                offset / distance, distance, shape.obstructionLayers, QueryTriggerInteraction.Ignore));
        }

        internal ActionTargetSelection SelectForAI(CharacterActor3D owner, ActorRef primary,
            in AttackProfile3D shape, float range)
        {
            const AttackTargetRelation hostile = AttackTargetRelation.Hostile;
            ActionTargetSelection result = default;
            Vector3 origin = Origin(owner, shape);
            Vector3 forward = owner.Motor.Facing;
            if (TryGetCandidate(owner, primary, shape, hostile, range, out Vector3 first))
            {
                result.TryAdd(primary);
                Vector3 towardPrimary = first - origin;
                if (towardPrimary.sqrMagnitude > 0f) { forward = towardPrimary; }
            }

            _hashes.Clear();
            _candidates.Clear();
            GameManager.Data.GetAllHashes(_hashes);
            foreach (int hash in _hashes)
            {
                if (hash == primary.Hash || !GameManager.Actors.TryGet(hash, out ActorRef candidate)
                    || !TryGetCandidate(owner, candidate, shape, hostile, range, out Vector3 position))
                {
                    continue;
                }
                Vector3 offset = position - origin;
                if (Vector3.Dot(forward, offset) >= 0f)
                {
                    _candidates.Add((candidate, offset.sqrMagnitude));
                }
            }

            _candidates.Sort(CompareCandidates);
            for (int i = 0; i < _candidates.Count && result.Count < ActionTargetSelection.MaxTargets; i++)
            {
                result.TryAdd(_candidates[i].actor);
            }
            return result;
        }

        internal static bool TryGetActor(ActorRef target, out CharacterActor3D actor)
        {
            actor = null;
            if (GameManager.Actors == null || !GameManager.Actors.IsCurrent(target)) { return false; }
            actor = GameManager.Data.GetManaged(target.Hash) as CharacterActor3D;
            return actor != null && actor.Body.enabled && actor.IsAlive && actor.Actor == target
                && !GameManager.Data.GetFlags(target.Hash).IsVanished;
        }

        internal static Vector3 Origin(CharacterActor3D owner, in AttackProfile3D shape) =>
            shape.shape == AttackShape3D.Projectile
                ? owner.transform.TransformPoint(shape.localCenter) : owner.Body.bounds.center;

        private static int CompareCandidates((ActorRef actor, float distance) left, (ActorRef actor, float distance) right)
        {
            int distance = left.distance.CompareTo(right.distance);
            return distance != 0 ? distance : left.actor.Hash.CompareTo(right.actor.Hash);
        }
    }
}
