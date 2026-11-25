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
        internal void CollectLockWarnings(ActorRef target, List<ProjectileLockWarning> warnings)
        {
            warnings.Clear();
            if (_activeProjectiles == null || GameManager.Actors == null
                || !GameManager.Actors.IsCurrent(target) || !GameManager.IsCharacterValid(target.Hash))
            {
                return;
            }

            ref CharacterVitals targetVitals = ref GameManager.Data.GetVitals(target.Hash);
            if (targetVitals.currentHp <= 0) { return; }

            BaseCharacter body = GameManager.Data.GetManaged(target.Hash) as BaseCharacter;
            Vector2 targetPosition = body != null ? body.TargetingCenter : targetVitals.position;
            foreach (ProjectileStep step in _activeProjectiles)
            {
                Projectile projectile = step.core;
                if (projectile == null || !projectile.IsAlive || projectile.IsSpawnDelayed
                    || projectile.ExplicitTargetRef != target || !IsCurrent(step.controller, projectile, step.lease)
                    || step.controller.CapturedMagic.magicType != MagicType.Attack
                    || !step.controller.CanAffectTarget(projectile, target))
                {
                    continue;
                }

                Vector2 fromTarget = projectile.Position - targetPosition;
                if (Vector2.Dot(fromTarget, projectile.Velocity) < 0f)
                {
                    warnings.Add(new ProjectileLockWarning(step.lease, fromTarget));
                }
            }
        }
    }
}
