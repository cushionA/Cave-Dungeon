using System.Collections.Generic;
using Game.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Runtime
{
    /// <summary>実際に目撃した敵への攻撃を、味方から離れた爆発物への直撃弾に切り替える。</summary>
    internal sealed class EncounterExplosiveAim3D
    {
        private const int k_MaxProps = 12;

        internal readonly struct Selection
        {
            private readonly EncounterExplosiveAim3D _aim;

            internal ActorRef Owner { get; }
            internal ActorRef Enemy { get; }
            internal ExplosiveProp3D Prop { get; }
            internal Collider HitCollider { get; }
            internal Scene Scene { get; }
            internal float SightRange { get; }
            internal LayerMask ObservationBlockers { get; }

            internal Selection(EncounterExplosiveAim3D aim, CharacterActor3D owner, ActorRef enemy,
                ExplosiveProp3D prop, float sightRange, LayerMask observationBlockers)
            {
                _aim = aim;
                Owner = owner.Actor;
                Enemy = enemy;
                Prop = prop;
                HitCollider = prop.HitCollider;
                Scene = owner.gameObject.scene;
                SightRange = sightRange;
                ObservationBlockers = observationBlockers;
            }

            internal bool TryGetLaunchDirection(CharacterActor3D owner, in AttackProfile3D shape, in BulletProfile profile,
                in AttackContext context, out Vector3 direction)
            {
                direction = default;
                return _aim != null && _aim.TryGetLaunchDirection(this, owner, shape, profile, context, out direction);
            }
        }

        private readonly List<int> _actorHashes = new();

        internal bool TrySelect(CharacterActor3D owner, in EncounterSighting3D sighting, AttackInfo attack,
            ExplosiveProp3D[] props, float sightRange, LayerMask blockers, GameActionRuntime actions, out Selection selection)
        {
            selection = default;
            float closest = float.PositiveInfinity;
            Vector3 ownerCenter = owner.Body.bounds.center;
            for (int i = 0; i < Mathf.Min(props.Length, k_MaxProps); i++)
            {
                ExplosiveProp3D prop = props[i];
                if (prop == null || prop.gameObject.scene != owner.gameObject.scene
                    || !prop.TryGetBlast(out Vector3 center, out float radiusSquared)
                    || (sighting.TargetCenter - center).sqrMagnitude > radiusSquared) { continue; }

                Selection candidate = new(this, owner, sighting.Target, prop, sightRange, blockers);
                float distance = (candidate.HitCollider.bounds.center - ownerCenter).sqrMagnitude;
                if (!float.IsFinite(distance) || distance > closest
                    || (distance == closest && selection.HitCollider != null
                        && ExplosiveProp3D.CompareContactOrder(candidate.HitCollider, selection.HitCollider) >= 0)
                    || !actions.CanAimExplosiveShot3D(owner, attack, candidate)) { continue; }

                closest = distance;
                selection = candidate;
            }
            return selection.Prop != null;
        }

        private bool TryGetLaunchDirection(in Selection selection, CharacterActor3D owner, in AttackProfile3D shape,
            in BulletProfile profile, in AttackContext context, out Vector3 direction)
        {
            direction = default;
            ExplosiveProp3D prop = selection.Prop;
            if (owner == null || owner.Actor != selection.Owner || context.Owner != selection.Owner || !owner.IsAlive
                || owner.gameObject.scene != selection.Scene || !selection.Scene.isLoaded || prop == null
                || prop.gameObject.scene != selection.Scene || prop.HitCollider != selection.HitCollider
                || !prop.TryGetBlast(out Vector3 center, out float radiusSquared)
                || !MultiLockTargeting3D.TryGetActor(selection.Enemy, out CharacterActor3D enemy)
                || enemy.gameObject.scene != selection.Scene
                || !AttackTargetRules.CanAffect(selection.Owner, context.Belong, selection.Enemy,
                    GameManager.Data.GetFlags(selection.Enemy.Hash).Belong, AttackTargetRelation.Hostile)
                || (enemy.Body.bounds.center - center).sqrMagnitude > radiusSquared) { return false; }

            PhysicsScene world = selection.Scene.GetPhysicsScene();
            if (!world.IsValid() || !CanStillSee(owner, enemy, world, selection.SightRange, selection.ObservationBlockers))
            {
                return false;
            }

            ProjectileManager3D projectiles = GameManager.Projectiles3D;
            Vector3 origin = MultiLockTargeting3D.Origin(owner, shape);
            Vector3 offset = selection.HitCollider.bounds.center - origin;
            float length = offset.magnitude;
            float distance = Mathf.Min(length, profile.speed * profile.lifeTime);
            if (projectiles == null || !(distance > 0f) || !float.IsFinite(length)
                || !projectiles.CanHitExplosiveFirst(owner, prop, origin, offset / length, distance, shape, context)
                || HasAllyInBlast(context, world, center, radiusSquared)) { return false; }

            direction = offset / length;
            return true;
        }

        private static bool CanStillSee(CharacterActor3D owner, CharacterActor3D enemy, PhysicsScene world,
            float range, LayerMask blockers)
        {
            // 受理した詠唱は、消滅で自分のコライダーが無効になっても同じ姿勢から照会できる。
            Vector3 origin = owner.Body.transform.TransformPoint(owner.Body.center);
            Vector3 offset = enemy.Body.bounds.center - origin;
            float distance = offset.magnitude;
            return distance <= range && (distance <= 0f || !world.Raycast(origin, offset / distance, out RaycastHit hit,
                distance, blockers, QueryTriggerInteraction.Ignore) || hit.collider == enemy.Body);
        }

        private bool HasAllyInBlast(in AttackContext shooter, PhysicsScene world, Vector3 center, float radiusSquared)
        {
            SoACharaDataDic data = GameManager.Data;
            ActorRegistry actors = GameManager.Actors;
            _actorHashes.Clear();
            data.GetAllHashes(_actorHashes);
            foreach (int hash in _actorHashes)
            {
                if (!actors.TryGet(hash, out ActorRef candidate)
                    || !AttackTargetRules.CanAffect(shooter.Owner, shooter.Belong, candidate, data.GetFlags(hash).Belong,
                        AttackTargetRelation.Friendly | AttackTargetRelation.Self)
                    || !MultiLockTargeting3D.TryGetActor(candidate, out CharacterActor3D ally)
                    || ally.gameObject.scene.GetPhysicsScene() != world) { continue; }

                if ((ally.Body.bounds.center - center).sqrMagnitude <= radiusSquared) { return true; }
            }
            return false;
        }
    }
}
