using System.Collections.Generic;
using Game.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Runtime
{
    public sealed partial class ProjectileManager3D
    {
        private const float k_LandingOverlapTolerance = 0.02f;

        private readonly List<int> _impactHashes = new List<int>(32);
        private readonly List<ActorRef> _impactTargets = new List<ActorRef>(16);

        /// <summary>予備動作の時点でも、実際の飛翔体と同じ半径・レイヤーで着弾面まで調べる。</summary>
        internal bool CanReachGroundImpact(Scene scene, Vector3 origin, in AttackProfile3D shape,
            in MagicDefinition magic, in GroundAttack3D impact)
        {
            BulletProfile profile = magic.bulletProfile;
            if (_data == null || !isActiveAndEnabled || _isClearing || !scene.isLoaded
                || !IsFinite(origin) || !PositiveFinite(shape.projectileRadius)
                || !PositiveFinite(profile.speed) || !PositiveFinite(profile.lifeTime)) { return false; }

            PhysicsScene world = scene.GetPhysicsScene();
            if (!impact.BelongsTo(world)) { return false; }
            Vector3 offset = FlightDestination(impact, shape) - origin;
            float distance = offset.magnitude;
            float reach = profile.speed * profile.lifeTime;
            if (!float.IsFinite(distance)
                || (profile.acceleration == 0f && distance > reach && !Mathf.Approximately(distance, reach))) { return false; }

            Physics.SyncTransforms();
            Vector3 direction = distance > 0f ? offset / distance : -impact.Normal;
            return !IsGroundFlightBlocked(world, origin, direction, distance, shape, impact);
        }

        /// <summary>発動時に床の予告を受け取り、弾の終了時に消去する。</summary>
        internal bool SpawnGroundImpact(Scene scene, Vector3 position, in AttackProfile3D shape,
            in MagicDefinition magic, in AttackMotionData attack, in AttackContext context, ActorRef explicitTarget,
            in GroundAttack3D impact, GameObject warning)
        {
            try
            {
                if (!CanReachGroundImpact(scene, position, shape, magic, impact)) { return false; }
                Vector3 offset = FlightDestination(impact, shape) - position;
                float distance = offset.magnitude;
                Vector3 direction = distance > 0f ? offset / distance : -impact.Normal;
                Shot shot = CreateShot(scene, position, direction, shape, magic, attack, context, explicitTarget, impact);
                return shot != null && ActivateShot(shot, magic.projectilePrefab, ref warning);
            }
            finally
            {
                AttackWarningPresentation.HideGroundImpact(warning);
            }
        }

        private void AdvanceGroundImpact(Shot shot, in GroundAttack3D impact, float duration)
        {
            Vector3 center = impact.Center;
            Vector3 offset = FlightDestination(impact, shot.shape) - shot.position;
            float distance = offset.magnitude;
            float maximumTravel = shot.Speed * duration;
            if (!float.IsFinite(distance) || !float.IsFinite(maximumTravel))
            {
                Retire(shot);
                return;
            }

            float travel = maximumTravel >= distance || Mathf.Approximately(maximumTravel, distance)
                ? distance : maximumTravel;
            if (distance > 0f) { shot.direction = offset / distance; }
            if (IsGroundFlightBlocked(shot.world, shot.position, shot.direction, travel, shot.shape, impact))
            {
                Retire(shot);
                return;
            }

            if (travel < distance)
            {
                MoveShot(shot, shot.position + shot.direction * travel);
                return;
            }

            // 発射時の印から被弾者を決めない。着弾時点で、確定した床の内側にいる世代を記録する。
            CollectGroundImpactTargets(shot, impact);
            Retire(shot);
            try
            {
                foreach (ActorRef actor in _impactTargets)
                {
                    ApplyDamage(shot, GetEligibleActor(shot, actor), actor, center);
                }
            }
            finally
            {
                _impactTargets.Clear();
            }
        }

        // SphereCastの中心を床から半径分離し、球の接触点を予告の中心と一致させる。
        private static Vector3 FlightDestination(in GroundAttack3D impact, in AttackProfile3D shape)
            => impact.Center + impact.Normal * shape.projectileRadius;

        private void CollectGroundImpactTargets(Shot shot, in GroundAttack3D impact)
        {
            _impactHashes.Clear();
            _impactTargets.Clear();
            ActorRegistry actors = GameManager.Actors;
            if (_data == null || actors == null) { return; }

            _data.GetAllHashes(_impactHashes);
            foreach (int hash in _impactHashes)
            {
                if (actors.TryGet(hash, out ActorRef actor) && impact.Contains(GetEligibleActor(shot, actor)))
                {
                    _impactTargets.Add(actor);
                }
            }
        }

        private bool IsGroundFlightBlocked(PhysicsScene world, Vector3 origin, Vector3 direction, float distance,
            in AttackProfile3D shape, in GroundAttack3D impact)
        {
            int mask = shape.obstructionLayers.value;
            float radius = shape.projectileRadius;
            if (distance > 0f && (mask & (1 << impact.Surface.gameObject.layer)) != 0
                && impact.Surface.Raycast(new Ray(origin, direction), out RaycastHit surfaceHit, distance)
                && !impact.IsLandingContact(surfaceHit))
            {
                return true;
            }

            bool blocked = false;
            int count = QueryOverlaps(world, origin, radius, mask);
            for (int i = 0; i < count; i++)
            {
                Collider collider = _overlaps[i];
                _overlaps[i] = null;
                blocked |= IsGroundObstruction(collider) && !IsLandingOverlap(collider, origin, radius, impact);
            }
            if (blocked || distance <= 0f) { return blocked; }

            count = QuerySweep(world, origin, radius, direction, distance, mask);
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _sweepHits[i];
                if (IsGroundObstruction(hit.collider) && !impact.IsLandingContact(hit)
                    && (hit.distance != 0f || !IsLandingOverlap(hit.collider, origin, radius, impact)))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsGroundObstruction(Collider collider) =>
            collider != null && collider.GetComponentInParent<CharacterActor3D>() == null;

        private static bool IsLandingOverlap(Collider collider, Vector3 origin, float radius, in GroundAttack3D impact)
        {
            if (collider != impact.Surface) { return false; }
            Vector3 normal = impact.Normal;
            float height = Vector3.Dot(origin - impact.Center, normal);
            float probeDistance = radius + k_LandingOverlapTolerance;
            if (height < -k_LandingOverlapTolerance || height > probeDistance) { return false; }

            Ray probe = new Ray(origin + normal * probeDistance, -normal);
            return collider.Raycast(probe, out RaycastHit hit, 2f * probeDistance) && impact.IsLandingContact(hit);
        }
    }
}
