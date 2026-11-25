using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Runtime
{
    public sealed partial class GameActionRuntime
    {
        private Collider[] _spatialOverlaps = new Collider[16];
        private readonly MultiLockTargeting3D _spatialTargeting = new MultiLockTargeting3D();

        /// <summary>3Dの個体と攻撃形状を共通の行動実行へ渡す。</summary>
        public bool Execute3D(CharacterActor3D owner, AttackInfo definition, out long instanceId)
            => Execute3D(owner, definition, default, default, out instanceId);

        /// <summary>AIや単体指定から、主標的付きの3D攻撃を受け付ける。</summary>
        public bool Execute3D(CharacterActor3D owner, AttackInfo definition, ActorRef primaryTarget, out long instanceId)
            => Execute3D(owner, definition, primaryTarget, default, out instanceId);

        /// <summary>長押し中に選んだ世代付きの順序を、この攻撃の要求へコピーする。</summary>
        public bool Execute3D(CharacterActor3D owner, AttackInfo definition, IReadOnlyList<ActorRef> targets, out long instanceId)
        {
            instanceId = 0;
            if (targets == null) { return false; }
            ActionTargetSelection selected = default;
            for (int i = 0; i < targets.Count && selected.Count < ActionTargetSelection.MaxTargets; i++)
            {
                selected.TryAdd(targets[i]);
            }
            return selected.Count > 0 && Execute3D(owner, definition, default, selected, out instanceId);
        }

        private bool Execute3D(CharacterActor3D owner, AttackInfo definition, ActorRef primaryTarget,
            ActionTargetSelection selected, out long instanceId)
        {
            instanceId = 0;
            if (owner == null || definition == null || !owner.IsAlive)
            {
                return false;
            }

            ActorRef actor = owner.Actor;
            int definitionId = _catalog.Register(definition);
            if (!_catalog.TryGet(definitionId, out ActionCatalog.Entry entry)
                || entry.attack3D.shape == AttackShape3D.None
                || (entry.attack3D.shape == AttackShape3D.Projectile && GameManager.Projectiles3D == null)
                || (selected.Count > 0 && (entry.multiLockRange <= 0f || !MultiLockTargeting3D.SupportsLock(entry.attack3D.shape)))
                || (entry.attack3D.shape == AttackShape3D.GroundArea && !owner.Motor.IsGrounded))
            {
                return false;
            }

            return Execution.TryStart(new ActionRequest
            {
                ownerHash = actor.Hash,
                ownerRef = actor,
                targetHash = primaryTarget.Hash,
                targetRef = primaryTarget,
                selectedTargets = selected,
                definitionId = definitionId,
                speedMultiplier = 1f,
                chargeMultiplier = 1f,
            }, out instanceId, out _);
        }

        private void HandleSpatialExecution(in ActionExecutionEvent notification, CharacterActor3D owner)
        {
            ref readonly ActionExecution current = ref notification.execution;
            if (notification.kind == ActionSignalKind.Completed || notification.kind == ActionSignalKind.Interrupted)
            {
                RemoveCombat(current.instanceId);
                _movementOutputs.Remove(current.instanceId);
                return;
            }
            if (!owner.IsAlive || owner.Actor != current.ownerRef)
            {
                return;
            }
            _consumableUses.TryGetValue(current.instanceId, out ConsumableUse consumable);

            if (notification.kind == ActionSignalKind.Started
                && _catalog.TryGet(current.definitionId, out ActionCatalog.Entry entry)
                && entry.attack3D.shape != AttackShape3D.None)
            {
                AttackMotionData attack = entry.CreateAttack();
                attack.actionInstanceId = current.instanceId;
                attack.motionValue = consumable != null ? 1f : attack.motionValue * current.chargeMultiplier;
                attack.equipmentStatusEffects = consumable != null ? null
                    : GameManager.Equipment.GetOnHitEffects(owner.ObjectHash, entry.equipmentSource);
                CombatExecution capture = new CombatExecution
                {
                    attack = attack,
                    attack3D = entry.attack3D,
                    multiLockRange = MultiLockTargeting3D.SupportsLock(entry.attack3D.shape) ? entry.multiLockRange : 0f,
                    multiLockTargets = current.selectedTargets,
                };
                if (capture.multiLockRange > 0f && capture.multiLockTargets.Count == 0)
                {
                    capture.multiLockTargets = _spatialTargeting.SelectForAI(owner, current.targetRef,
                        capture.attack3D, capture.multiLockRange);
                }
                if (capture.attack3D.shape == AttackShape3D.Projectile)
                {
                    capture.magic = entry.CreateMagic();
                    if (consumable != null) { capture.magic.motionValue = 1f; }
                    if (capture.magic.groundImpactRange > 0f)
                    {
                        CaptureGroundProjectile3D(owner, current.targetRef, capture);
                    }
                }
                if (capture.attack3D.shape == AttackShape3D.GroundArea
                    && GroundAttack3D.TryCreate(owner, capture.attack3D.radius, out capture.groundAttack3D))
                {
                    capture.groundImpactWarning = capture.groundAttack3D.ShowWarning();
                }
                _combat.Add(current.instanceId, capture);
                if (entry.attack3D.shape == AttackShape3D.Melee && current.phase == AnimationPhase.Anticipation
                    && (attack.feature & AttackFeature.JustGuardImmune) != 0
                    && WarnsPlayer3D(owner, attack.targetPolicy.Resolve()))
                {
                    _attackWarnings.PlayDangerousMelee(entry.attackSettings.GuardWarningSound);
                }
            }

            if (notification.kind != ActionSignalKind.PhaseChanged) { return; }
            _combat.TryGetValue(current.instanceId, out CombatExecution combat);
            if (current.phase != AnimationPhase.Active)
            {
                PresentConsumablePhase(consumable, current);
                if (current.phase == AnimationPhase.Recovery && combat?.repeat3D != null)
                {
                    EndSpatialRepeat(current.instanceId, combat);
                }
                return;
            }

            AttackContext context;
            if (consumable != null)
            {
                if (!TryCommitConsumable(consumable, owner, current, combat, out context))
                {
                    if (combat != null) { ClearGroundWarning(combat); }
                    return;
                }
                PresentConsumablePhase(consumable, current);
                if (combat == null)
                {
                    ShowConsumableEffect(consumable, owner, null);
                    return;
                }
            }
            else if (combat == null || !CombatDataHelper.TryCaptureAttack(owner.ObjectHash, combat.attack, _data,
                GameManager.Actors, out context) || context.Owner != current.ownerRef)
            {
                if (combat != null) { ClearGroundWarning(combat); }
                return;
            }

            if (combat.magic.groundImpactRange > 0f)
            {
                ReleaseGroundProjectile3D(owner, current.targetRef, combat, context);
                if (consumable != null) { ShowConsumableEffect(consumable, owner, combat); }
                return;
            }

            if (combat.attack3D.shape == AttackShape3D.Projectile)
            {
                ClearGroundWarning(combat);
                ReleaseProjectiles3D(owner, current.targetRef, combat, context);
                if (consumable != null) { ShowConsumableEffect(consumable, owner, combat); }
                return;
            }

            BeginSpatialRepeat(owner, current, combat, context);
            if (combat.repeat3D == null) { ClearGroundWarning(combat); }
            if (combat.attack3D.shape == AttackShape3D.GroundArea)
            {
                ResolveGroundAttack3D(owner, combat, context, current.phaseElapsed);
            }
            else
            {
                ResolveMelee3D(owner, combat, context, current.phaseElapsed);
            }
            if (consumable != null) { ShowConsumableEffect(consumable, owner, combat); }
        }

        private bool WarnsPlayer3D(CharacterActor3D owner, AttackTargetRelation targets)
        {
            if ((targets & AttackTargetRelation.Hostile) == 0) { return false; }

            PhysicsScene world = owner.gameObject.scene.GetPhysicsScene();
            CharacterBelong belong = _data.GetFlags(owner.ObjectHash).Belong;
            foreach (int hash in _owners)
            {
                if (hash == owner.ObjectHash || !_data.TryGetValue(hash, out _)) { continue; }

                ref readonly CharacterFlags flags = ref _data.GetFlags(hash);
                if ((flags.Feature & CharacterFeature.Player) == 0 || flags.IsVanished
                    || AttackTargetRules.GetRelation(belong, flags.Belong, false) != AttackTargetRelation.Hostile)
                {
                    continue;
                }

                if (_data.GetManaged(hash) is CharacterActor3D player && player != null && player.IsAlive
                    && player.gameObject.scene.GetPhysicsScene() == world)
                {
                    return true;
                }
            }

            return false;
        }

        private static void ClearGroundWarning(CombatExecution combat)
        {
            AttackWarningPresentation.HideGroundImpact(combat.groundImpactWarning);
            combat.groundImpactWarning = null;
        }

        private void ReleaseProjectiles3D(CharacterActor3D owner, ActorRef primary, CombatExecution combat,
            in AttackContext context)
        {
            ProjectileManager3D manager = GameManager.Projectiles3D;
            if (manager == null) { return; }
            Scene scene = owner.gameObject.scene;
            Vector3 origin = MultiLockTargeting3D.Origin(owner, combat.attack3D);
            Vector3 forward = owner.Motor.Facing;
            if (combat.multiLockRange <= 0f && !primary.IsValid)
            {
                manager.Spawn(scene, origin, forward, combat.attack3D, combat.magic, combat.attack, context, default);
                return;
            }

            bool multiLock = combat.multiLockRange > 0f;
            float range = multiLock ? combat.multiLockRange
                : combat.magic.bulletProfile.speed * combat.magic.bulletProfile.lifeTime;
            AttackTargetRelation relations = multiLock
                ? context.TargetRelations & AttackTargetRelation.Hostile : context.TargetRelations;
            int count = multiLock ? combat.multiLockTargets.Count : 1;
            for (int i = 0; i < count; i++)
            {
                if (owner == null || owner.Actor != context.Owner) { break; }
                ActorRef target = multiLock ? combat.multiLockTargets[i] : primary;
                if (!_spatialTargeting.TryGetCandidate(owner, target, combat.attack3D, relations, range, out Vector3 position))
                {
                    continue;
                }
                Vector3 offset = position - origin;
                manager.Spawn(scene, origin, offset.sqrMagnitude > 0f ? offset.normalized : forward,
                    combat.attack3D, combat.magic, combat.attack, context, target);
            }
        }

        private void ResolveMelee3D(CharacterActor3D owner, CombatExecution combat, in AttackContext context, float phaseTime)
        {
            AttackProfile3D profile = combat.attack3D;
            Vector3 origin = owner.Body.bounds.center;
            Vector3 center = owner.transform.TransformPoint(profile.localCenter);
            Quaternion orientation = owner.transform.rotation;
            PhysicsScene world = owner.gameObject.scene.GetPhysicsScene();
            int count;
            while (true)
            {
                count = world.OverlapBox(center, profile.halfExtents, _spatialOverlaps, orientation,
                    profile.targetLayers, QueryTriggerInteraction.Ignore);
                if (count < _spatialOverlaps.Length) { break; }
                Array.Resize(ref _spatialOverlaps, _spatialOverlaps.Length * 2);
            }

            CharacterActor3D nearest = null;
            float nearestDistance = float.PositiveInfinity;
            bool multiLock = combat.multiLockRange > 0f;
            ActionTargetSelection hits = default;
            for (int i = 0; i < count; i++)
            {
                Collider hit = _spatialOverlaps[i];
                _spatialOverlaps[i] = null;
                CharacterActor3D target = hit.GetComponentInParent<CharacterActor3D>();
                if (!CanHit3D(target, world, profile.targetLayers, context)
                    || !CanRegisterSpatialHit(combat, target.Actor, phaseTime)) { continue; }
                if (multiLock)
                {
                    if (combat.multiLockTargets.Contains(target.Actor)
                        && _spatialTargeting.TryGetCandidate(owner, target.Actor, profile, AttackTargetRelation.Hostile,
                            combat.multiLockRange, out _))
                    {
                        hits.TryAdd(target.Actor);
                    }
                    continue;
                }

                Vector3 offset = target.Body.bounds.center - origin;
                float distance = offset.sqrMagnitude;
                if (nearest != null && (distance > nearestDistance
                    || (distance == nearestDistance && target.Actor.Hash >= nearest.Actor.Hash))) { continue; }
                if (distance > 0f && world.Raycast(origin, offset.normalized, Mathf.Sqrt(distance),
                    profile.obstructionLayers, QueryTriggerInteraction.Ignore)) { continue; }

                nearest = target;
                nearestDistance = distance;
            }
            if (nearest != null)
            {
                ApplySpatialHit(owner, nearest, combat, context, origin, phaseTime);
            }
            if (!multiLock) { return; }
            for (int i = 0; i < combat.multiLockTargets.Count; i++)
            {
                if (combat.repeat3D != null && !CanContinueSpatialRepeat(owner, combat)) { break; }
                ActorRef mark = combat.multiLockTargets[i];
                if (hits.Contains(mark) && MultiLockTargeting3D.TryGetActor(mark, out CharacterActor3D target))
                {
                    ApplySpatialHit(owner, target, combat, context, origin, phaseTime);
                }
            }
        }

        private void ResolveGroundAttack3D(CharacterActor3D owner, CombatExecution combat, in AttackContext context, float phaseTime)
        {
            PhysicsScene world = owner.gameObject.scene.GetPhysicsScene();
            List<int> hashes = combat.repeat3D?.hashes ?? new List<int>();
            hashes.Clear();
            _data.GetAllHashes(hashes);
            List<ActorRef> eligible = combat.repeat3D?.eligible ?? new List<ActorRef>();
            eligible.Clear();
            for (int i = 0; i < hashes.Count; i++)
            {
                if (_data.GetManaged(hashes[i]) is CharacterActor3D target
                    && CanHit3D(target, world, combat.attack3D.targetLayers, context)
                    && combat.groundAttack3D.Contains(target)
                    && CanRegisterSpatialHit(combat, target.Actor, phaseTime))
                {
                    eligible.Add(target.Actor);
                }
            }

            foreach (ActorRef actor in eligible)
            {
                if (combat.repeat3D != null && !CanContinueSpatialRepeat(owner, combat)) { break; }
                if (GameManager.Actors.IsCurrent(actor) && _data.GetManaged(actor.Hash) is CharacterActor3D target
                    && CanHit3D(target, world, combat.attack3D.targetLayers, context)
                    && combat.groundAttack3D.Contains(target))
                {
                    ApplySpatialHit(owner, target, combat, context, combat.groundAttack3D.Center, phaseTime);
                }
            }
        }

        private bool CanHit3D(CharacterActor3D target, PhysicsScene world, LayerMask layers, in AttackContext context)
        {
            return target != null && target.IsAlive && target.Body.enabled
                && (layers.value & (1 << target.Body.gameObject.layer)) != 0
                && target.gameObject.scene.GetPhysicsScene() == world
                && CombatDataHelper.CanAffectTarget(context, target.Actor, _data, GameManager.Actors);
        }

        private static void ApplyHit3D(CharacterActor3D target, in AttackMotionData attack, in AttackContext context, Vector3 origin)
        {
            if (!(target.Damageable is DamageReceiver receiver)) { return; }
            DamageData damage = CombatDataHelper.BuildDamageData(context.Owner.Hash, target.Actor.Hash, attack, context.AttackStats);
            context.ApplyTo(ref damage, target.Actor);
            receiver.ReceiveDamage3D(damage, origin);
        }
    }
}
