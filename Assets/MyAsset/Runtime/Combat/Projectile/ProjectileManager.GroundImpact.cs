using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.Runtime
{
    public partial class ProjectileManager
    {
        private readonly struct GroundImpactShot
        {
            public readonly Vector2 position;
            public readonly GameObject warning;

            public GroundImpactShot(Vector2 position, GameObject warning)
            {
                this.position = position;
                this.warning = warning;
            }
        }

        private readonly Dictionary<ulong, GroundImpactShot> _groundImpactShots = new();

        internal void SpawnGroundImpactFromExecution(in ActionExecution source, MagicDefinition magic,
            Vector2 origin, Vector2 impactPosition, GameObject warning)
        {
            bool transferredWarning = false;
            try
            {
                if (!TryCaptureExecutionShot(source, ref magic)) { return; }

                // Ground impact is committed at windup. Travel straight there without detonating on a bystander.
                Vector2 direction = (impactPosition - origin).normalized;
                magic.bulletProfile.moveType = BulletMoveType.Straight;
                ProjectileController controller = SpawnCapturedProjectile(source.ownerRef, magic, origin, direction,
                    source.targetRef, explicitTarget: true, groundImpact: new GroundImpactShot(impactPosition, warning));
                transferredWarning = controller != null;
            }
            finally
            {
                if (!transferredWarning) { AttackWarningPresentation.HideGroundImpact(warning); }
            }
        }

        private bool TryStepGroundImpact(in ProjectileStep step, float deltaTime)
        {
            if (!_groundImpactShots.TryGetValue(step.lease, out GroundImpactShot impact)) { return false; }
            Vector2 previousPosition = step.core.Position;
            Vector2 toImpact = impact.position - previousPosition;
            ProjectileMovement.UpdateProjectile(step.core, deltaTime, Vector2.zero, true);
            Vector2 travel = step.core.Position - previousPosition;
            if (!step.core.IsSpawnDelayed && toImpact.sqrMagnitude <= travel.sqrMagnitude)
            {
                step.core.Position = impact.position;
                // The lock describes the incoming flight. The blast affects current occupants, including respawns.
                step.core.TrySetTarget(default, GameManager.Actors);
                step.controller.SyncTransform();
                AttackWarningPresentation.HideGroundImpact(impact.warning);
                try
                {
                    ProcessExplosion(step.core, step.controller.CapturedMagic);
                }
                finally
                {
                    // Damage listeners can clear the pool and reuse the same controller.
                    if (IsCurrent(step.controller, step.core, step.lease)) { ReturnProjectile(step.controller); }
                }
            }
            else
            {
                step.core.CompleteSimulationStep();
                step.controller.SyncTransform();
            }
            return true;
        }

        private void RemoveGroundImpact(ulong lease)
        {
            if (_groundImpactShots.Remove(lease, out GroundImpactShot impact))
            {
                AttackWarningPresentation.HideGroundImpact(impact.warning);
            }
        }
    }
}
