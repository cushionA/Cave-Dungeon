using Game.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Runtime
{
    public sealed partial class GameActionRuntime
    {
        private void CaptureGroundProjectile3D(CharacterActor3D owner, ActorRef primary, CombatExecution combat)
        {
            ProjectileManager3D manager = GameManager.Projectiles3D;
            if (manager == null) { return; }

            float range = combat.magic.groundImpactRange;
            float radius = combat.magic.bulletProfile.explodeRadius;
            GroundAttack3D impact;
            if (primary.IsValid)
            {
                if (!_spatialTargeting.TryGetCandidate(owner, primary, combat.attack3D, combat.attack.targetPolicy.Resolve(),
                        float.PositiveInfinity, out _)
                    || !MultiLockTargeting3D.TryGetActor(primary, out CharacterActor3D target)
                    || !GroundAttack3D.TryCreate(target, radius, out impact))
                {
                    return;
                }

                Vector3 offset = impact.Center - owner.Motor.FeetPosition;
                offset.y = 0f;
                if (offset.sqrMagnitude > range * range) { return; }
            }
            else if (!GroundAttack3D.TryCreateAhead(owner, range, radius, out impact))
            {
                return;
            }

            Scene scene = owner.gameObject.scene;
            Vector3 origin = MultiLockTargeting3D.Origin(owner, combat.attack3D);
            if (!manager.CanReachGroundImpact(scene, origin, combat.attack3D, combat.magic, impact))
            {
                return;
            }

            combat.groundAttack3D = impact;
            combat.groundImpactWarning = impact.ShowWarning();
        }

        private static void ReleaseGroundProjectile3D(CharacterActor3D owner, ActorRef primary, CombatExecution combat,
            in AttackContext context)
        {
            ProjectileManager3D manager = GameManager.Projectiles3D;
            if (manager == null || !combat.groundAttack3D.IsValid)
            {
                ClearGroundWarning(combat);
                return;
            }

            GameObject warning = combat.groundImpactWarning;
            combat.groundImpactWarning = null;
            Scene scene = owner.gameObject.scene;
            Vector3 origin = MultiLockTargeting3D.Origin(owner, combat.attack3D);
            manager.SpawnGroundImpact(scene, origin, combat.attack3D, combat.magic, combat.attack,
                context, primary, combat.groundAttack3D, warning);
        }
    }
}
