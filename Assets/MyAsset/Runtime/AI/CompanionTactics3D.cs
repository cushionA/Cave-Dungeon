using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Runtime
{
    /// <summary>3Dで見えている個体を既存の戦術評価に渡し、その行動を3Dで実行する。</summary>
    internal sealed class CompanionTactics3D : IActionCommandBackend, IDisposable
    {
        private readonly CharacterActor3D _owner;
        private readonly ActorRef _ownerRef;
        private readonly CharacterActor3D _knight;
        private readonly GameActionRuntime _actions;
        private readonly AttackInfo[] _attacks;
        private readonly string[] _attackIds;
        private readonly float _sightRange;
        private readonly LayerMask _obstructionLayers;
        private readonly float _followDistance;
        private readonly AIObservationContext _observation = new AIObservationContext();
        private readonly List<int> _hashes = new List<int>();
        private readonly List<ActorRef> _previous = new List<ActorRef>();
        private readonly AITargetRelations _targets = new AITargetRelations();
        private readonly JudgmentLoop _judgment;
        private readonly ModeController _modes;
        private bool _hasModes;
        private long _tick;
        private long _instance;
        private ActionCommandKind _motion;
        private ActorRef _motionTarget;
        private float _motionEnd;

        public int ActiveModeIndex => _modes.CurrentModeIndex;
        public bool IsExecuting => _instance != 0 && _actions.Execution.TryGetInstance(_instance, out _);
        public event Action OnActionCompleted;

        internal CompanionTactics3D(CharacterActor3D owner, CharacterActor3D knight, GameActionRuntime actions,
            AttackInfo[] attacks, float sightRange, LayerMask obstructionLayers, float followDistance)
        {
            _owner = owner;
            _ownerRef = owner.Actor;
            _knight = knight;
            _actions = actions;
            _attacks = attacks ?? Array.Empty<AttackInfo>();
            _attackIds = Array.ConvertAll(_attacks, attack => attack != null ? attack.PersistentId : null);
            _sightRange = sightRange;
            _obstructionLayers = obstructionLayers;
            _followDistance = followDistance;
            ActionExecutor executor = new ActionExecutor();
            executor.SetExecutionBackend(this);
            _judgment = new JudgmentLoop(executor, GameManager.Data, _ownerRef.Hash);
            _modes = new ModeController(_judgment);
        }

        internal void SetConfig(CompanionAIConfig config)
        {
            _hasModes = config.modes?.Length > 0;
            _modes.SetModes(config.modes, config.modeTransitionRules);
            _judgment.ForceEvaluate();
        }

        void IActionExecutionBackend.Tick(float deltaTime)
        {
            if (_instance != 0 && !IsExecuting)
            {
                _instance = 0;
                OnActionCompleted?.Invoke();
            }
        }

        internal void Evaluate(float deltaTime)
        {
            if (!_hasModes) { return; }
            Capture();
            if (!_judgment.UseObservation(_observation, _targets)) { return; }
            _modes.EvaluateTransitions(_ownerRef.Hash, _judgment.CurrentTargetHash, _observation);
            _judgment.Tick(deltaTime, null, Time.fixedTime);
        }

        private void Capture()
        {
            _observation.BeginSnapshot(++_tick, Time.fixedTime);
            PhysicsScene world = _owner.gameObject.scene.GetPhysicsScene();
            SoACharaDataDic data = GameManager.Data;
            data.GetAllHashes(_hashes);
            Physics.SyncTransforms();
            foreach (int hash in _hashes)
            {
                if (!GameManager.Actors.TryGet(hash, out ActorRef reference)
                    || !MultiLockTargeting3D.TryGetActor(reference, out CharacterActor3D actor)
                    || actor.gameObject.scene.GetPhysicsScene() != world
                    || (actor != _owner && !CanSee(_owner, actor, _sightRange, _obstructionLayers))) { continue; }

                CharacterVitals vitals = data.GetVitals(hash);
                Vector3 position = actor.Motor.FeetPosition;
                // 平面距離を使う既存戦術には床面方向を渡し、観測と実際の当たり判定は3Dで行う。
                vitals.position = new Vector2(position.x, position.z);
                _observation.AddActor(AIActorObservation.Capture(reference, vitals, data.GetCombatStats(hash), data.GetFlags(hash)));
                _targets.Register(reference);
            }
            foreach (ActorRef previous in _previous)
            {
                if (!_observation.Contains(previous)) { _targets.Remove(previous); }
            }
            _previous.Clear();
            foreach (int hash in _observation.ActorHashes)
            {
                if (_observation.TryGetActor(hash, out AIActorObservation actor)) { _previous.Add(actor.actor); }
            }
            if (_knight != null) { _observation.SetProtectedPlayer(_knight.Actor); }
        }

        internal static bool CanSee(CharacterActor3D source, CharacterActor3D target, float range, LayerMask obstructions)
        {
            Vector3 origin = source.Body.bounds.center;
            Vector3 ray = target.Body.bounds.center - origin;
            float distance = ray.magnitude;
            return distance <= range && (distance <= 0f
                || !source.gameObject.scene.GetPhysicsScene().Raycast(origin, ray / distance, out RaycastHit hit,
                    distance, obstructions, QueryTriggerInteraction.Ignore) || hit.collider == target.Body);
        }

        internal static Vector3 FollowDirection(CharacterActor3D source, CharacterActor3D target, float distance)
        {
            if (target == null || !target.IsAlive || !target.Body.enabled
                || source.gameObject.scene.GetPhysicsScene() != target.gameObject.scene.GetPhysicsScene()) { return Vector3.zero; }
            Vector3 offset = target.Motor.FeetPosition - source.Motor.FeetPosition;
            offset.y = 0f;
            return offset.sqrMagnitude > distance * distance ? offset.normalized : Vector3.zero;
        }

        internal bool TryGetMovement(out Vector3 direction)
        {
            direction = Vector3.zero;
            if (_motion == ActionCommandKind.Unsupported || Time.fixedTime >= _motionEnd) { return false; }
            MultiLockTargeting3D.TryGetActor(_motionTarget, out CharacterActor3D target);
            switch (_motion)
            {
                case ActionCommandKind.Follow: direction = FollowDirection(_owner, target != null ? target : _knight, _followDistance); break;
                case ActionCommandKind.MoveToTarget: direction = FollowDirection(_owner, target, 1f); break;
                case ActionCommandKind.Flee:
                case ActionCommandKind.Retreat: direction = -FollowDirection(_owner, target, 0f); break;
            }
            return true;
        }

        internal static bool Supports(ActionCommandKind kind) => kind is ActionCommandKind.Attack or ActionCommandKind.Cast
            or ActionCommandKind.Follow or ActionCommandKind.MoveToTarget or ActionCommandKind.Retreat
            or ActionCommandKind.Flee or ActionCommandKind.Guard or ActionCommandKind.Idle;

        bool IActionCommandBackend.Supports(ActionCommandKind kind) => Supports(kind);

        public bool Execute(int ownerHash, int targetHash, ActionSlot slot) => ExecuteCommand(ownerHash, targetHash, ActionCommand.FromLegacySlot(slot)).Accepted;

        public ActionDispatchResult ExecuteCommand(int ownerHash, int targetHash, in ActionCommand command)
        {
            if (_owner.Actor != _ownerRef || ownerHash != _ownerRef.Hash) { return new ActionDispatchResult(ActionDispatchStatus.InvalidOwner); }
            if (!Supports(command.kind)) { return new ActionDispatchResult(ActionDispatchStatus.Unsupported); }
            if (IsExecuting) { return new ActionDispatchResult(ActionDispatchStatus.Rejected); }
            ActorRef target = _observation.TryGetActor(targetHash, out AIActorObservation captured) ? captured.actor : default;
            if (command.kind is ActionCommandKind.Attack or ActionCommandKind.Cast)
            {
                ActionDispatchStatus reference = ActionDefinitionReference.Resolve(command.attack.definitionId,
                    command.attack.legacyIndex, _attackIds, out int index);
                if (reference != ActionDispatchStatus.Accepted) { return new ActionDispatchResult(reference); }
                AttackInfo attack = _attacks[index];
                if (attack == null || !MultiLockTargeting3D.TryGetActor(target, out CharacterActor3D actor)
                    || !AttackTargetRules.CanAffect(_ownerRef, GameManager.Data.GetFlags(ownerHash).Belong,
                        target, GameManager.Data.GetFlags(target.Hash).Belong, AttackTargetRelation.Hostile))
                {
                    return new ActionDispatchResult(ActionDispatchStatus.InvalidTarget);
                }
                Vector3 direction = actor.Motor.FeetPosition - _owner.Motor.FeetPosition;
                float range = attack.aiEvaluation.effectiveRangeMax > 0f ? attack.aiEvaluation.effectiveRangeMax
                    : attack.attack3D.shape == AttackShape3D.Projectile ? _sightRange : 2.3f;
                if (direction.sqrMagnitude > range * range) { return new ActionDispatchResult(ActionDispatchStatus.InvalidTarget); }
                direction.y = 0f;
                if (direction.sqrMagnitude > 0.001f) { _owner.transform.rotation = Quaternion.LookRotation(direction); }
                if (!_actions.Execute3D(_owner, attack, target, out _instance)) { return new ActionDispatchResult(ActionDispatchStatus.Rejected); }
            }
            else
            {
                float duration = command.sustained.duration > 0f ? command.sustained.duration : 0.3f;
                if (command.kind == ActionCommandKind.Guard && !_actions.TrySetGuarding(ownerHash, true, out _instance, duration))
                {
                    return new ActionDispatchResult(ActionDispatchStatus.Rejected);
                }
                _motion = command.kind;
                _motionTarget = target;
                _motionEnd = Time.fixedTime + duration;
            }
            return new ActionDispatchResult(ActionDispatchStatus.Accepted, _instance, targetRef: target);
        }

        public void CancelCurrent()
        {
            if (_instance != 0) { _actions.Execution.Cancel(_instance, force: true); }
            _instance = 0;
            _motion = ActionCommandKind.Unsupported;
            _motionTarget = default;
        }

        internal void Stop() => _judgment.ForceEvaluate();

        public void Dispose()
        {
            _judgment.Dispose();
            _judgment.Executor.SetExecutionBackend(null);
        }
    }
}
