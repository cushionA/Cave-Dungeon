using System;
using Game.Core;
using UnityEngine;

namespace Game.Runtime
{
    public sealed partial class GameActionRuntime
    {
        private readonly struct StartingExplosiveShot
        {
            public readonly ActorRef owner;
            public readonly int definitionId;
            public readonly ActorRef target;
            public readonly EncounterExplosiveAim3D.Selection? selection;

            public StartingExplosiveShot(ActorRef owner, int definitionId, ActorRef target,
                EncounterExplosiveAim3D.Selection? selection)
            {
                this.owner = owner;
                this.definitionId = definitionId;
                this.target = target;
                this.selection = selection;
            }
        }

        private StartingExplosiveShot? _startingExplosiveShot;

        internal bool CanAimExplosiveShot3D(CharacterActor3D owner, AttackInfo attack,
            in EncounterExplosiveAim3D.Selection selection)
            => TryGetExplosiveShot3D(owner, attack, selection, out _);

        internal bool TryExecuteExplosiveShot3D(CharacterActor3D owner, AttackInfo attack,
            in EncounterExplosiveAim3D.Selection selection, out long instanceId)
        {
            instanceId = 0;
            if (owner == null || attack == null) { return false; }
            Physics.SyncTransforms();
            if (!TryGetExplosiveShot3D(owner, attack, selection, out int definitionId)) { return false; }
            // 攻撃枠と行動の世代検証は観測した敵を使い、弾が向かう爆発物は別に捕捉する。
            return ExecuteRestrictedShot3D(owner.Actor, definitionId, selection.Enemy, default, selection,
                out instanceId);
        }

        private bool ExecuteRestrictedShot3D(ActorRef actor, int definitionId, ActorRef target,
            ActionTargetSelection selected, EncounterExplosiveAim3D.Selection? selection, out long instanceId)
        {
            instanceId = 0;
            if (_startingExplosiveShot.HasValue) { return false; }
            _startingExplosiveShot = new StartingExplosiveShot(actor, definitionId, target, selection);
            try
            {
                return ExecuteSpatialRequest(actor, definitionId, target, selected, out instanceId);
            }
            finally { _startingExplosiveShot = null; }
        }

        private bool TryGetExplosiveShot3D(CharacterActor3D owner, AttackInfo attack,
            in EncounterExplosiveAim3D.Selection selection, out int definitionId)
        {
            definitionId = 0;
            if (owner == null || attack == null || !owner.IsAlive || owner.Body == null || !owner.Body.enabled
                || GameManager.Projectiles3D == null) { return false; }
            ActorRef actor = owner.Actor;
            CharacterFlags flags = _data.GetFlags(actor.Hash);
            if (flags.IsVanished) { return false; }

            try { definitionId = _catalog.Register(attack); }
            catch (ArgumentException) { return false; }
            if (!_catalog.TryGet(definitionId, out ActionCatalog.Entry entry)
                || entry.attack3D.shape != AttackShape3D.Projectile || entry.multiLockRange > 0f) { return false; }
            MagicDefinition magic = entry.CreateMagic();
            AttackTargetRelation relations = magic.targetPolicy.Resolve(magic.magicType);
            if (magic.bulletCount != 1 || magic.groundImpactRange > 0f || magic.bulletProfile.explodeRadius > 0f
                || magic.bulletProfile.moveType != BulletMoveType.Straight
                || (relations & AttackTargetRelation.Hostile) == 0) { return false; }

            // ここでは弾を生成せず、同じ衝突クエリに必要な陣営・対象条件だけを渡す。
            AttackContext context = AttackContext.Capture(actor, default, 1f, flags.Belong, null, targetRelations: relations);
            return selection.TryGetLaunchDirection(owner, entry.attack3D, magic.bulletProfile, context, out _);
        }

        private StartingExplosiveShot? TakeStartingExplosiveShot(in ActionExecution current)
        {
            if (_startingExplosiveShot is not StartingExplosiveShot starting || current.forced
                || starting.owner != current.ownerRef || starting.definitionId != current.definitionId
                || starting.target != current.targetRef) { return null; }

            _startingExplosiveShot = null;
            return starting;
        }

        private void ReleaseExplosiveShot3D(CharacterActor3D owner, in ActionExecution current, CombatExecution combat,
            in AttackContext context, in EncounterExplosiveAim3D.Selection aim)
        {
            ProjectileManager3D manager = GameManager.Projectiles3D;
            if (manager == null || !AttackContext.CanFinishAcceptedCast(context.Owner, current)
                || !Execution.CanApplyExecutionEffects(current.instanceId)) { return; }
            Physics.SyncTransforms();
            if (!aim.TryGetLaunchDirection(owner, combat.attack3D, combat.magic.bulletProfile, context,
                out Vector3 direction)) { return; }
            manager.Spawn(owner.gameObject.scene, MultiLockTargeting3D.Origin(owner, combat.attack3D),
                direction, combat.attack3D, combat.magic, combat.attack, context, default, combat.explosiveContact3D);
        }
    }
}
