using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.Runtime
{
    internal readonly struct ProjectileLockWarning
    {
        public readonly ulong LeaseId;
        public readonly Vector3 FromTarget;

        public ProjectileLockWarning(ulong leaseId, Vector3 fromTarget)
        {
            LeaseId = leaseId;
            FromTarget = fromTarget;
        }
    }

    public partial class ProjectileManager
    {
        private readonly ProjectileSpatialIndex<ProjectileStep, PhysicsScene2D> _flightIndex = new(SampleFlight, isPlanar: true);
        private readonly List<ProjectileStep> _nearbyFlights = new();

        private static ProjectileSpatialSample<PhysicsScene2D> SampleFlight(ProjectileStep step) =>
            step.core != null && step.core.IsAlive && !step.core.IsSpawnDelayed
                ? new ProjectileSpatialSample<PhysicsScene2D>(step.world, step.core.Position)
                : default;

        /// <summary>同じ2D物理空間で、観測者に作用し得る近くの敵対弾を数える。</summary>
        public int CountNearbyHostileProjectiles(BaseCharacter observer, float radius)
        {
            GameManager manager = GameManager.Instance;
            if (observer == null || manager == null || !manager.TryGetRegisteredActor(observer, out ActorRef target)
                || !TryCollectNearbyFlights(target, radius, out _)) { return 0; }

            CharacterBelong faction = GameManager.Data.GetFlags(target.Hash).Belong;
            int count = 0;
            foreach (ProjectileStep step in _nearbyFlights)
            {
                if (!IsAvailableAttack(step, target)) { continue; }
                AttackContext source = step.controller.CapturedMagic.attackContext;
                if (AttackTargetRules.CanAffect(source.Owner, source.Belong, target, faction, AttackTargetRelation.Hostile))
                {
                    count++;
                }
            }
            _nearbyFlights.Clear();
            return count;
        }

        internal void CollectLockWarnings(ActorRef target, List<ProjectileLockWarning> warnings,
            float radius = ProjectileSpatialIndex.DefaultQueryRadius)
        {
            warnings.Clear();
            if (!TryCollectNearbyFlights(target, radius, out Vector2 targetPosition)) { return; }
            foreach (ProjectileStep step in _nearbyFlights)
            {
                Projectile projectile = step.core;
                if (projectile == null || projectile.ExplicitTargetRef != target || !IsAvailableAttack(step, target)) { continue; }

                Vector2 fromTarget = projectile.Position - targetPosition;
                if (Vector2.Dot(fromTarget, projectile.Velocity) < 0f)
                {
                    warnings.Add(new ProjectileLockWarning(step.lease, fromTarget));
                }
            }
            _nearbyFlights.Clear();
        }

        private bool TryCollectNearbyFlights(ActorRef target, float radius, out Vector2 position)
        {
            position = default;
            SoACharaDataDic data = GameManager.Data;
            if (_activeProjectiles == null || !(radius > 0f) || !float.IsFinite(radius) || GameManager.Actors == null
                || !GameManager.Actors.IsCurrent(target) || !GameManager.IsCharacterValid(target.Hash)
                || data.Uses3DPhysics(target.Hash) || data.GetVitals(target.Hash).currentHp <= 0)
            {
                return false;
            }

            BaseCharacter body = data.GetManaged(target.Hash) as BaseCharacter;
            position = body != null ? body.TargetingCenter : data.GetVitals(target.Hash).position;
            _flightIndex.Collect(_activeProjectiles, GameManager.PhysicsSceneOf(body), position, radius, _nearbyFlights,
                canCache: !_isTicking && !_isClearing);
            return true;
        }

        private bool IsAvailableAttack(in ProjectileStep step, ActorRef target)
        {
            Projectile projectile = step.core;
            return projectile != null && projectile.IsAlive && !projectile.IsSpawnDelayed
                && IsCurrent(step.controller, projectile, step.lease)
                && step.controller.CapturedMagic.magicType == MagicType.Attack
                && step.controller.CanAffectTarget(projectile, target);
        }
    }
}
