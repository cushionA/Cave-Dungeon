using System;
using System.Collections.Generic;
using Game.Core;
using R3;
using UnityEngine;

namespace Game.Runtime
{
    /// <summary>戦場で観測できた現役の個体だけに号令を配送し、受信した世代ごとに保持する。</summary>
    internal sealed class RuntimeFieldOrders : IDisposable
    {
        // The current prototype uses the 2D Ground layer as its solid sight blocker.
        private const int GroundMask = 1 << 6;
        private readonly float _rangeSquared;
        private readonly Dictionary<ActorRef, (FieldOrder order, PhysicsScene2D world)> _orders = new();
        private readonly List<ActorRef> _removed = new();
        private readonly List<ActionExecution> _actions = new();
        private readonly IDisposable _death;
        private readonly GameEvents _events;

        internal RuntimeFieldOrders(GameEvents events, float range)
        {
            _rangeSquared = range * range;
            _events = events;
            _death = events.OnCharacterDeathResolved.Subscribe(death => Forget(death.Dead));
            events.SetFieldOrderReceiver(ResolveIssuer, Deliver);
        }

        private static FieldOrderIssuer ResolveIssuer(ActorRef actor)
        {
            if (!TryActor(actor, out BaseCharacter character, out AIActorObservation observed)) { return FieldOrderIssuer.None; }
            if (character is PlayerCharacter && CharacterRegistry.PlayerHash == actor.Hash
                && observed.flags.Belong == CharacterBelong.Ally) { return FieldOrderIssuer.Player; }
            if (character is EnemyCharacter enemy && enemy.HasCommanderOrders
                && observed.flags.Belong == CharacterBelong.Enemy) { return FieldOrderIssuer.EnemyCommander; }
            if (character is CompanionCharacter && character.FieldOrderJudgment != null
                && observed.flags.Belong == CharacterBelong.Ally) { return FieldOrderIssuer.CompanionAI; }
            return FieldOrderIssuer.None;
        }

        internal bool HasAssignments => _orders.Count != 0;

        private static bool TryActor(ActorRef actor, out BaseCharacter character, out AIActorObservation observed)
        {
            character = null;
            observed = default;
            if (!GameManager.Actors.IsCurrent(actor) || !GameManager.Observation.TryGetActor(actor.Hash, out observed)
                || observed.actor != actor || !GameManager.Data.TryGetValue(actor.Hash, out _)) { return false; }
            character = GameManager.Data.GetManaged(actor.Hash) as BaseCharacter;
            if (character == null || !character.isActiveAndEnabled || !character.IsAlive) { return false; }
            CharacterFlags live = GameManager.Data.GetFlags(actor.Hash);
            return !live.IsVanished && live.ActState != ActState.Dead && live.Belong == observed.flags.Belong
                && GameManager.PhysicsSceneOf(character) == observed.physicsScene;
        }

        private static bool HasSight(in AIActorObservation observer, in AIActorObservation other)
        {
            Vector2 offset = other.position - observer.position;
            float distance = offset.magnitude;
            if (distance == 0f) { return true; }
            ContactFilter2D filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(GroundMask);
            return !observer.physicsScene.Raycast(observer.position, offset / distance, distance, filter);
        }

        private bool Deliver(FieldOrder order)
        {
            if (!TryActor(order.Source, out BaseCharacter leader, out AIActorObservation source)) { return false; }
            bool taunt = order.Action == BroadcastAction.Taunt;
            if (FieldOrderCatalog.RequiresTarget(order.Action))
            {
                if (!TryActor(order.Target, out _, out AIActorObservation target)
                    || target.physicsScene != source.physicsScene
                    || (target.position - source.position).sqrMagnitude > _rangeSquared
                    || !AttackTargetRules.CanAffect(source.actor, source.flags.Belong, target.actor, target.flags.Belong,
                        AttackTargetRelation.Hostile)) { return false; }
                Physics2D.SyncTransforms();
                if (!HasSight(source, target)) { return false; }
            }
            else if (order.Action == BroadcastAction.DefendPoint)
            {
                if ((order.Position.Value - source.position).sqrMagnitude > _rangeSquared) { return false; }
            }
            else if (taunt) { Physics2D.SyncTransforms(); }
            else if (order.Action != BroadcastAction.Rally && order.Action != BroadcastAction.ReleaseOrder) { return false; }

            bool accepted = false;
            AIObservationContext observation = GameManager.Observation;
            for (int i = 0; i < observation.ActorHashes.Count; i++)
            {
                observation.TryGetActor(observation.ActorHashes[i], out AIActorObservation candidate);
                if (!TryActor(candidate.actor, out BaseCharacter receiver, out _) || receiver.FieldOrderJudgment == null
                    || !InRangeAndRelated(source, candidate, taunt) || (taunt && !HasSight(candidate, source))) { continue; }
                if (!OnOrderLevel(order, source.position.y, candidate.position.y, leader, receiver)) { continue; }
                if (order.Action == BroadcastAction.DesignateTarget && (_orders.ContainsKey(candidate.actor)
                    || GameManager.TargetRelations.TryGetTarget(candidate.actor, out _))) { continue; }
                if (order.Action == BroadcastAction.ReleaseOrder)
                {
                    accepted |= _orders.Remove(candidate.actor);
                    continue;
                }
                _orders[candidate.actor] = (order, source.physicsScene);
                accepted = true;
            }
            return accepted;
        }

        private static bool OnOrderLevel(in FieldOrder order, float sourceY, float recipientY,
            BaseCharacter leader, BaseCharacter receiver)
        {
            if (order.Action == BroadcastAction.Rally)
            {
                return Mathf.Abs(sourceY - recipientY) <= receiver.FieldOrderTolerance(leader).y;
            }
            return order.Action != BroadcastAction.DefendPoint
                || Mathf.Abs(order.Position.Value.y - recipientY) <= receiver.FieldOrderTolerance(null).y;
        }

        private bool InRangeAndRelated(in AIActorObservation source, in AIActorObservation recipient, bool taunt) =>
            source.physicsScene == recipient.physicsScene && (source.position - recipient.position).sqrMagnitude <= _rangeSquared
            && AttackTargetRules.CanAffect(source.actor, source.flags.Belong, recipient.actor, recipient.flags.Belong,
                taunt ? AttackTargetRelation.Hostile : AttackTargetRelation.Friendly);

        internal bool TryRead(ActorRef recipient, out FieldOrder order)
        {
            order = default;
            if (!_orders.TryGetValue(recipient, out (FieldOrder order, PhysicsScene2D world) assignment)) { return false; }
            FieldOrder requested = assignment.order;
            if (TryActor(recipient, out BaseCharacter character, out AIActorObservation receiver)
                && TryActor(requested.Source, out BaseCharacter leader, out AIActorObservation source) && assignment.world == source.physicsScene
                && (requested.Issuer == FieldOrderIssuer.EnemyCommander ? source.flags.Belong == CharacterBelong.Enemy
                    : source.flags.Belong == CharacterBelong.Ally)
                && (requested.Issuer != FieldOrderIssuer.Player || CharacterRegistry.PlayerHash == requested.Source.Hash)
                && OnOrderLevel(requested, source.position.y, receiver.position.y, leader, character)
                && InRangeAndRelated(source, receiver, requested.Action == BroadcastAction.Taunt)
                && (!requested.Target.IsValid || (TryActor(requested.Target, out _, out AIActorObservation target)
                    && target.physicsScene == source.physicsScene && AttackTargetRules.CanAffect(receiver.actor, receiver.flags.Belong,
                        target.actor, target.flags.Belong, AttackTargetRelation.Hostile))))
            {
                order = requested;
                return true;
            }
            _orders.Remove(recipient);
            return false;
        }

        internal bool CanSwitch(int owner)
        {
            if (GameManager.Data.GetStatusEffects(owner).BlocksActions
                || ActionExecutionSystem.IsActionBlockingReaction(GameManager.Data.GetFlags(owner).ActState)) { return false; }
            GameManager.Actions.Execution.CopyActive(owner, _actions);
            for (int i = 0; i < _actions.Count; i++)
            {
                if (_actions[i].forced || !_actions[i].canCancel) { return false; }
            }
            return true;
        }

        internal void Complete(ActorRef recipient) => _orders.Remove(recipient);

        internal void Forget(ActorRef actor)
        {
            if (!actor.IsValid) { return; }
            _removed.Clear();
            foreach (KeyValuePair<ActorRef, (FieldOrder order, PhysicsScene2D world)> pair in _orders)
            {
                FieldOrder order = pair.Value.order;
                if (pair.Key == actor || order.Source == actor || order.Target == actor) { _removed.Add(pair.Key); }
            }
            for (int i = 0; i < _removed.Count; i++) { _orders.Remove(_removed[i]); }
        }

        public void Dispose()
        {
            _death.Dispose();
            _events.SetFieldOrderReceiver(null, null);
            _orders.Clear();
        }
    }
}
