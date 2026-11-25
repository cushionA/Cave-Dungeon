using System;
using System.Collections.Generic;
using Game.Core;
using R3;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Runtime
{
    /// <summary>同じ標的に向かう作戦行動を、受理された実行グループの寿命に合わせて調整する。</summary>
    internal sealed class MissionAttackSlots3D : IDisposable
    {
        private sealed class Reservation
        {
            public readonly ActorRef Owner;
            public readonly ActorRef Target;
            public long GroupId;
            public bool Invalidated;

            public Reservation(ActorRef owner, ActorRef target)
            {
                Owner = owner;
                Target = target;
            }
        }

        private readonly GameActionRuntime _actions;
        private readonly ActionExecutionSystem _execution;
        private readonly Dictionary<ActorRef, Reservation> _reservations = new();
        private readonly IDisposable _removal;
        private readonly IDisposable _death;
        private bool _refreshing;
        private bool _disposed;

        internal MissionAttackSlots3D(GameActionRuntime actions, GameEvents events)
        {
            _actions = actions ?? throw new ArgumentNullException(nameof(actions));
            if (events == null) { throw new ArgumentNullException(nameof(events)); }

            _execution = actions.Execution;
            _execution.GroupChanged += ReleaseFinishedGroup;
            _removal = events.OnCharacterRemoved.Subscribe(_ => Refresh());
            _death = events.OnCharacterDeathResolved.Subscribe(death => Invalidate(death.Dead));
        }

        internal int ActiveCount => _reservations.Count;

        internal bool TryExecute(CharacterActor3D owner, ActorRef target, AttackInfo attack, out long instanceId)
        {
            instanceId = 0;
            if (_disposed || !ReferenceEquals(_actions, GameManager.Actions) || owner == null || attack == null)
            {
                return false;
            }
            Refresh();

            ActorRef attacker = owner.Actor;
            if (_disposed || _reservations.ContainsKey(target) || !CanShareWorldAndFight(attacker, target)
                || (attack.targetPolicy.Resolve(attack.magicType) & AttackTargetRelation.Hostile) == 0)
            {
                return false;
            }

            foreach (Reservation existing in _reservations.Values)
            {
                if (existing.Owner == attacker) { return false; }
            }

            // 行動開始の通知は同期なので、その通知から再度要求されても枠を奪えないよう先に確保する。
            Reservation pending = new Reservation(attacker, target);
            _reservations.Add(target, pending);
            bool accepted = false;
            try
            {
                accepted = _actions.Execute3D(owner, attack, target, out instanceId);
                return accepted;
            }
            finally
            {
                bool active = _execution.TryGetGroupId(instanceId, out pending.GroupId)
                    && _execution.IsGroupActive(pending.GroupId);
                if (!active || !accepted || _disposed || pending.Invalidated || !CanShareWorldAndFight(attacker, target))
                {
                    Release(pending, cancelExecution: active);
                }
            }
        }

        internal void Refresh()
        {
            if (_disposed || _refreshing) { return; }
            _refreshing = true;
            try
            {
                while (!_disposed)
                {
                    Reservation invalid = null;
                    foreach (Reservation reservation in _reservations.Values)
                    {
                        if (reservation.GroupId != 0 && (reservation.Invalidated
                            || !_execution.IsGroupActive(reservation.GroupId)
                            || !CanShareWorldAndFight(reservation.Owner, reservation.Target)))
                        {
                            invalid = reservation;
                            break;
                        }
                    }

                    if (invalid == null) { return; }
                    Release(invalid, cancelExecution: true);
                }
            }
            finally
            {
                _refreshing = false;
            }
        }

        internal void Invalidate(ActorRef actor)
        {
            if (!actor.IsValid) { return; }
            foreach (Reservation reservation in _reservations.Values)
            {
                if (reservation.Owner == actor || reservation.Target == actor) { reservation.Invalidated = true; }
            }
            Refresh();
        }

        private void ReleaseFinishedGroup(ActionGroupEvent notification)
        {
            Reservation finished = null;
            foreach (Reservation reservation in _reservations.Values)
            {
                if (reservation.GroupId == notification.groupId && reservation.Owner == notification.ownerRef)
                {
                    finished = reservation;
                    break;
                }
            }
            if (finished != null) { Release(finished, cancelExecution: false); }
        }

        private void Release(Reservation reservation, bool cancelExecution)
        {
            if (_reservations.TryGetValue(reservation.Target, out Reservation current) && current == reservation)
            {
                _reservations.Remove(reservation.Target);
            }
            if (cancelExecution && reservation.GroupId != 0) { _execution.CancelGroup(reservation.GroupId); }
        }

        private static bool CanShareWorldAndFight(ActorRef attacker, ActorRef target)
        {
            if (!MultiLockTargeting3D.TryGetActor(attacker, out CharacterActor3D owner)
                || !MultiLockTargeting3D.TryGetActor(target, out CharacterActor3D defender)
                || owner.gameObject.scene.GetPhysicsScene() != defender.gameObject.scene.GetPhysicsScene())
            {
                return false;
            }

            CharacterFlags sourceFlags = GameManager.Data.GetFlags(attacker.Hash);
            CharacterFlags targetFlags = GameManager.Data.GetFlags(target.Hash);
            return sourceFlags.ActState != ActState.Dead && targetFlags.ActState != ActState.Dead
                && AttackTargetRules.CanAffect(attacker, sourceFlags.Belong,
                    target, targetFlags.Belong, AttackTargetRelation.Hostile);
        }

        public void Dispose()
        {
            if (_disposed) { return; }
            _disposed = true;
            _execution.GroupChanged -= ReleaseFinishedGroup;
            _death.Dispose();
            _removal.Dispose();

            List<Reservation> current = new List<Reservation>(_reservations.Values);
            _reservations.Clear();
            foreach (Reservation reservation in current)
            {
                if (reservation.GroupId != 0) { _execution.CancelGroup(reservation.GroupId); }
            }
        }
    }
}
