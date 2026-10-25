using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Core
{
    public sealed class ActionExecutionSystem : IDisposable
    {
        private sealed class Instance
        {
            public ActionExecution execution;
            public float[] armorConsumed;
            public bool[] triggeredTransitions;
            public bool[] pendingTransitions;
            public bool hitConfirmed;
        }

        private struct QueuedRequest
        {
            public ActionRequest request;
            public int actorVersion;
            public int definitionVersion;
            public bool allowInterruptedSource;
        }

        private readonly IExecutionWorld _world;
        private readonly Dictionary<int, ActionDefinitionData> _definitions = new Dictionary<int, ActionDefinitionData>();
        private readonly Dictionary<int, int> _definitionVersions = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _actors = new Dictionary<int, int>();
        private readonly Dictionary<long, Instance> _instances = new Dictionary<long, Instance>();
        private readonly Dictionary<long, int> _completedSources = new Dictionary<long, int>();
        private readonly HashSet<long> _interruptedSources = new HashSet<long>();
        private readonly HashSet<long> _pendingSourceNotifications = new HashSet<long>();
        private readonly Queue<QueuedRequest> _requests = new Queue<QueuedRequest>();
        private readonly List<QueuedRequest> _tickRequests = new List<QueuedRequest>();
        private readonly List<long> _tickInstances = new List<long>();
        private int _nextDefinitionId = 1;
        private int _nextDefinitionVersion = 1;
        private int _nextActorVersion = 1;
        private int _tickRequestIndex;
        private long _nextInstanceId = 1;
        private int _notificationDepth;
        private bool _disposed;

        public event Action<ActionExecutionEvent> Changed;

        public int DefinitionCount => _definitions.Count;
        public int ActiveCount => _instances.Count;
        public int PendingCount => _requests.Count + _tickRequests.Count - _tickRequestIndex;
        public int RetainedSourceCount => _completedSources.Count;

        public ActionExecutionSystem(IExecutionWorld world)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
        }

        public void RegisterActor(int ownerHash)
        {
            if (!_disposed && !_actors.ContainsKey(ownerHash))
            {
                _actors.Add(ownerHash, _nextActorVersion++);
            }
        }

        public void RemoveActor(int ownerHash)
        {
            if (!_actors.Remove(ownerHash))
            {
                return;
            }
            RemovePendingRequests(request => request.request.ownerHash == ownerHash);
            List<Instance> removed = new List<Instance>();
            foreach (KeyValuePair<long, Instance> pair in _instances)
            {
                if (pair.Value.execution.ownerHash == ownerHash)
                {
                    removed.Add(pair.Value);
                }
            }
            bool hadGameplay = false;
            bool hadStatefulAction = false;
            for (int i = 0; i < removed.Count; i++)
            {
                _instances.Remove(removed[i].execution.instanceId);
                hadGameplay |= !removed[i].execution.definition.motionOnly;
                hadStatefulAction |= AffectsActState(removed[i].execution.definition);
            }
            List<long> sources = new List<long>();
            foreach (KeyValuePair<long, int> pair in _completedSources)
            {
                if (pair.Value == ownerHash)
                {
                    sources.Add(pair.Key);
                }
            }
            for (int i = 0; i < sources.Count; i++)
            {
                _completedSources.Remove(sources[i]);
                _interruptedSources.Remove(sources[i]);
                _pendingSourceNotifications.Remove(sources[i]);
            }
            if (hadGameplay && _world.TryGetActor(ownerHash, out ActionActorState actor))
            {
                actor.flags.ActState = DeriveState(ownerHash, actor.flags.ActState, actor.vitals.currentHp, !hadStatefulAction);
                _world.CommitActor(ownerHash, actor);
            }
            for (int i = 0; i < removed.Count; i++)
            {
                Notify(removed[i], ActionSignalKind.Interrupted);
            }
            PruneCompletedSources();
        }

        public int RegisterDefinition(ActionDefinitionData data)
        {
            if (data.id < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(data));
            }
            int id = data.id;
            if (id == 0)
            {
                while (_definitions.ContainsKey(_nextDefinitionId))
                {
                    _nextDefinitionId++;
                }
                id = _nextDefinitionId++;
            }
            if (_definitions.ContainsKey(id))
            {
                throw new ArgumentException($"Action definition {id} is already registered.", nameof(data));
            }
            data.id = id;
            _definitions.Add(id, SnapshotDefinition(data));
            _definitionVersions.Add(id, _nextDefinitionVersion++);
            return id;
        }

        public bool UnregisterDefinition(int id)
        {
            if (!_definitions.Remove(id))
            {
                return false;
            }
            _definitionVersions.Remove(id);
            RemovePendingRequests(request => request.request.definitionId == id);
            PruneCompletedSources();
            return true;
        }

        public void UpdateDefinition(int id, ActionDefinitionData data)
        {
            if (!_definitions.ContainsKey(id))
            {
                throw new KeyNotFoundException($"Action definition {id} is not registered.");
            }
            if (data.id != 0 && data.id != id)
            {
                throw new ArgumentException("The action definition ID cannot change.", nameof(data));
            }
            data.id = id;
            _definitions[id] = SnapshotDefinition(data);
        }

        public bool TryStart(ActionRequest request, out long instanceId)
        {
            return TryStart(request, out instanceId, false);
        }

        private bool TryStart(ActionRequest request, out long instanceId, bool allowInterruptedSource)
        {
            instanceId = 0;
            if (_disposed || (_notificationDepth > 0 && !request.forced) || !_actors.ContainsKey(request.ownerHash)
                || !_definitions.TryGetValue(request.definitionId, out ActionDefinitionData definition)
                || !_world.TryGetActor(request.ownerHash, out ActionActorState actor)
                || !ValidSource(request.ownerHash, request.sourceInstanceId, allowInterruptedSource))
            {
                return false;
            }
            if (!definition.motionOnly)
            {
                bool dead = actor.vitals.currentHp <= 0 || actor.flags.ActState == ActState.Dead;
                if (dead && !(request.forced && definition.actState == ActState.Dead))
                {
                    return false;
                }
                if (!request.forced && (Blocked(definition, actor) || IsReaction(actor.flags.ActState)))
                {
                    return false;
                }
                if (!request.forced && (actor.vitals.currentStamina < definition.staminaCost
                    || actor.vitals.currentMp < definition.mpCost))
                {
                    return false;
                }
            }

            List<long> conflicts = null;
            foreach (KeyValuePair<long, Instance> pair in _instances)
            {
                ActionExecution existing = CurrentSnapshot(pair.Value);
                if (existing.ownerHash != request.ownerHash || (existing.definition.resources & definition.resources) == 0)
                {
                    continue;
                }
                if (!CanReplace(request, definition, existing))
                {
                    return false;
                }
                conflicts ??= new List<long>();
                conflicts.Add(pair.Key);
            }

            List<Instance> ended = null;
            if (conflicts != null)
            {
                ended = new List<Instance>(conflicts.Count);
                for (int i = 0; i < conflicts.Count; i++)
                {
                    long id = conflicts[i];
                    Instance previous = _instances[id];
                    _instances.Remove(id);
                    _completedSources[id] = request.ownerHash;
                    _interruptedSources.Add(id);
                    _pendingSourceNotifications.Add(id);
                    ended.Add(previous);
                }
            }

            instanceId = _nextInstanceId++;
            Instance state = new Instance
            {
                execution = new ActionExecution
                {
                    instanceId = instanceId, ownerHash = request.ownerHash, definitionId = definition.id,
                    targetHash = request.targetHash, direction = request.direction,
                    targetPosition = request.targetPosition, speedMultiplier = request.speedMultiplier == 0f ? 1f : request.speedMultiplier,
                    forced = request.forced, sourceInstanceId = request.sourceInstanceId, definition = definition,
                    phase = definition.anticipationDuration > 0f ? AnimationPhase.Anticipation : AnimationPhase.Active,
                },
                armorConsumed = definition.effects.Length > 0 ? new float[definition.effects.Length] : Array.Empty<float>(),
                triggeredTransitions = definition.transitions.Length > 0 ? new bool[definition.transitions.Length] : Array.Empty<bool>(),
                pendingTransitions = definition.transitions.Length > 0 ? new bool[definition.transitions.Length] : Array.Empty<bool>(),
            };
            _instances.Add(instanceId, state);
            if (!definition.motionOnly)
            {
                actor.vitals.currentStamina = Math.Max(0f, actor.vitals.currentStamina - definition.staminaCost);
                actor.vitals.currentMp = Math.Max(0, actor.vitals.currentMp - definition.mpCost);
                actor.flags.ActState = DeriveState(request.ownerHash, actor.flags.ActState, actor.vitals.currentHp,
                    !AffectsActState(definition));
                _world.CommitActor(request.ownerHash, actor);
            }
            if (ended != null)
            {
                for (int i = 0; i < ended.Count; i++)
                {
                    Notify(ended[i], ActionSignalKind.Interrupted);
                }
            }
            if (!_instances.ContainsKey(instanceId))
            {
                return true;
            }
            Notify(state, ActionSignalKind.Started);
            if (!_instances.ContainsKey(instanceId))
            {
                return true;
            }
            Notify(state, ActionSignalKind.PhaseChanged);
            Advance(instanceId, 0f);
            return true;
        }

        public void Queue(ActionRequest request)
        {
            Queue(request, false);
        }

        private void Queue(ActionRequest request, bool allowInterruptedSource)
        {
            if (!_disposed && _actors.TryGetValue(request.ownerHash, out int version)
                && _definitionVersions.TryGetValue(request.definitionId, out int definitionVersion)
                && ValidSource(request.ownerHash, request.sourceInstanceId, allowInterruptedSource))
            {
                _requests.Enqueue(new QueuedRequest
                {
                    request = request, actorVersion = version, definitionVersion = definitionVersion,
                    allowInterruptedSource = allowInterruptedSource,
                });
            }
        }

        public bool HasPendingFrom(long sourceId)
        {
            if (sourceId <= 0)
            {
                return false;
            }
            foreach (QueuedRequest request in _requests)
            {
                if (request.request.sourceInstanceId == sourceId)
                {
                    return true;
                }
            }
            for (int i = _tickRequestIndex; i < _tickRequests.Count; i++)
            {
                if (_tickRequests[i].request.sourceInstanceId == sourceId)
                {
                    return true;
                }
            }
            return false;
        }

        public int CancelPendingFrom(long sourceId)
        {
            if (sourceId <= 0)
            {
                return 0;
            }
            int removed = RemovePendingRequests(request => request.request.sourceInstanceId == sourceId);
            PruneCompletedSources();
            return removed;
        }

        public void Tick(float deltaTime)
        {
            if (_disposed || deltaTime < 0f || _notificationDepth > 0)
            {
                return;
            }
            while (_requests.Count > 0)
            {
                _tickRequests.Add(_requests.Dequeue());
            }
            while (_tickRequestIndex < _tickRequests.Count)
            {
                QueuedRequest queued = _tickRequests[_tickRequestIndex++];
                if (_actors.TryGetValue(queued.request.ownerHash, out int version) && version == queued.actorVersion
                    && _definitionVersions.TryGetValue(queued.request.definitionId, out int definitionVersion)
                    && definitionVersion == queued.definitionVersion)
                {
                    TryStart(queued.request, out _, queued.allowInterruptedSource);
                }
            }
            _tickRequests.Clear();
            _tickRequestIndex = 0;
            _tickInstances.Clear();
            foreach (long id in _instances.Keys)
            {
                _tickInstances.Add(id);
            }
            for (int i = 0; i < _tickInstances.Count; i++)
            {
                Advance(_tickInstances[i], deltaTime);
            }
            PruneCompletedSources();
        }

        public bool Release(long instanceId)
        {
            if (!_instances.TryGetValue(instanceId, out Instance state) || state.execution.released)
            {
                return false;
            }
            state.execution.released = true;
            Notify(state, ActionSignalKind.Released);
            if (_instances.ContainsKey(instanceId) && state.execution.definition.endMode == ActionEndMode.Held)
            {
                ChangePhase(state, AnimationPhase.Recovery, 0f);
                Advance(instanceId, 0f);
            }
            return true;
        }

        public bool Complete(long instanceId) => End(instanceId, ActionSignalKind.Completed);

        public bool Cancel(long instanceId, bool force = false)
        {
            if (!_instances.TryGetValue(instanceId, out Instance state) || (!force && !CurrentSnapshot(state).canCancel))
            {
                return false;
            }
            return End(instanceId, ActionSignalKind.Interrupted);
        }

        public bool Signal(long instanceId, ActionSignalKind kind, int signalId = 0)
        {
            if (!_instances.TryGetValue(instanceId, out Instance state))
            {
                return false;
            }
            if (kind == ActionSignalKind.HitConfirmed)
            {
                if (!_world.TryGetActor(state.execution.ownerHash, out ActionActorState actor)
                    || actor.vitals.currentHp <= 0 || actor.flags.ActState == ActState.Dead)
                {
                    return false;
                }
                state.hitConfirmed = true;
            }
            Notify(state, kind, signalId);
            return true;
        }

        public bool UpdateInput(long instanceId, Vector2 direction, float speedMultiplier = 1f)
        {
            if (!_instances.TryGetValue(instanceId, out Instance state)
                || !state.execution.definition.movementUsesInput)
            {
                return false;
            }
            state.execution.direction = direction;
            state.execution.speedMultiplier = speedMultiplier;
            return true;
        }

        public bool TryGetInstance(long instanceId, out ActionExecution execution)
        {
            if (_instances.TryGetValue(instanceId, out Instance state))
            {
                execution = CurrentSnapshot(state);
                return true;
            }
            execution = default;
            return false;
        }

        public void CopyActive(int ownerHash, List<ActionExecution> output)
        {
            output.Clear();
            foreach (Instance state in _instances.Values)
            {
                if (state.execution.ownerHash == ownerHash)
                {
                    output.Add(CurrentSnapshot(state));
                }
            }
        }

        public bool IsGameplayActive(int ownerHash)
        {
            foreach (Instance state in _instances.Values)
            {
                if (state.execution.ownerHash == ownerHash && !state.execution.definition.motionOnly)
                {
                    return true;
                }
            }
            return false;
        }

        public bool IsResourceOccupied(int ownerHash, ActionResources resource)
        {
            foreach (Instance state in _instances.Values)
            {
                if (state.execution.ownerHash == ownerHash && (state.execution.definition.resources & resource) != 0)
                {
                    return true;
                }
            }
            return false;
        }

        public bool GetPrimary(int ownerHash, out ActionExecution execution)
        {
            Instance selected = null;
            foreach (Instance state in _instances.Values)
            {
                if (state.execution.ownerHash == ownerHash && AffectsActState(state.execution.definition)
                    && (selected == null || HigherPrimary(state.execution, selected.execution)))
                {
                    selected = state;
                }
            }
            execution = selected != null ? CurrentSnapshot(selected) : default;
            return selected != null;
        }

        public bool TryGetMovement(int ownerHash, bool horizontal, out Vector2 velocity)
        {
            ActionResources resource = horizontal ? ActionResources.HorizontalMovement : ActionResources.VerticalMovement;
            Instance selected = null;
            foreach (Instance state in _instances.Values)
            {
                ActionExecution current = state.execution;
                if (current.ownerHash == ownerHash && !current.definition.motionOnly
                    && (current.definition.resources & resource) != 0
                    && (selected == null || HigherPriority(current, selected.execution)))
                {
                    selected = state;
                }
            }
            velocity = default;
            if (selected == null)
            {
                return false;
            }
            ActionExecution active = selected.execution;
            ActionDefinitionData definition = active.definition;
            if ((definition.gameplay & ActionGameplayFlags.Move) != 0 && active.elapsed >= definition.moveStartTime
                && (definition.moveDuration <= 0f || active.elapsed < definition.moveStartTime + definition.moveDuration))
            {
                velocity = active.direction * (definition.moveSpeed * active.speedMultiplier);
            }
            return true;
        }

        public ActionEffectProcessor.EffectState EvaluateEffects(int ownerHash)
        {
            ActionEffectProcessor.EffectState combined = default;
            foreach (Instance state in _instances.Values)
            {
                if (state.execution.ownerHash != ownerHash || state.execution.definition.motionOnly)
                {
                    continue;
                }
                ActionEffectProcessor.EffectState value = ActionEffectProcessor.Evaluate(state.execution.definition.effects, state.execution.elapsed);
                combined.isInvincible |= value.isInvincible;
                combined.hasSuperArmor |= value.hasSuperArmor;
                combined.hasGuardPoint |= value.hasGuardPoint;
                combined.hasGuardAttackEffect |= value.hasGuardAttackEffect;
                combined.hasKnockbackImmunity |= value.hasKnockbackImmunity;
                combined.damageReduction = Math.Max(combined.damageReduction, value.damageReduction);
                ActionEffect[] effects = state.execution.definition.effects;
                for (int i = 0; i < effects.Length; i++)
                {
                    if (effects[i].type == ActionEffectType.Armor && effects[i].IsActive(state.execution.elapsed))
                    {
                        combined.actionArmorValue += Math.Max(0f, effects[i].value - state.armorConsumed[i]);
                    }
                }
            }
            return combined;
        }

        public void ConsumeArmor(int ownerHash, float damage)
        {
            if (damage <= 0f)
            {
                return;
            }
            foreach (Instance state in _instances.Values)
            {
                if (state.execution.ownerHash != ownerHash || state.execution.definition.motionOnly)
                {
                    continue;
                }
                ActionEffect[] effects = state.execution.definition.effects;
                for (int i = 0; i < effects.Length; i++)
                {
                    if (effects[i].type == ActionEffectType.Armor && effects[i].IsActive(state.execution.elapsed))
                    {
                        float consumed = Math.Min(damage, Math.Max(0f, effects[i].value - state.armorConsumed[i]));
                        state.armorConsumed[i] += consumed;
                        damage -= consumed;
                        if (damage <= 0f)
                        {
                            return;
                        }
                    }
                }
            }
        }

        public float GetConsumedArmor(int ownerHash)
        {
            float consumed = 0f;
            foreach (Instance state in _instances.Values)
            {
                if (state.execution.ownerHash != ownerHash || state.execution.definition.motionOnly)
                {
                    continue;
                }
                ActionEffect[] effects = state.execution.definition.effects;
                for (int i = 0; i < effects.Length; i++)
                {
                    if (effects[i].type == ActionEffectType.Armor && effects[i].IsActive(state.execution.elapsed))
                    {
                        consumed += state.armorConsumed[i];
                    }
                }
            }
            return consumed;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            List<long> active = new List<long>(_instances.Keys);
            for (int i = 0; i < active.Count; i++)
            {
                End(active[i], ActionSignalKind.Interrupted);
            }
            _requests.Clear();
            _completedSources.Clear();
            _interruptedSources.Clear();
            _pendingSourceNotifications.Clear();
            _actors.Clear();
            _definitions.Clear();
            _definitionVersions.Clear();
            _tickRequests.Clear();
            _tickRequestIndex = 0;
            _tickInstances.Clear();
        }

        private void Advance(long id, float deltaTime)
        {
            if (!_instances.TryGetValue(id, out Instance state))
            {
                return;
            }
            ActionExecution active = state.execution;
            if (!active.definition.motionOnly)
            {
                if (!_world.TryGetActor(active.ownerHash, out ActionActorState actor)
                    || ((actor.vitals.currentHp <= 0 || actor.flags.ActState == ActState.Dead)
                        && active.definition.actState != ActState.Dead)
                    || (!active.forced && Blocked(active.definition, actor)))
                {
                    End(id, ActionSignalKind.Interrupted);
                    return;
                }
                if (active.definition.staminaPerSecond > 0f)
                {
                    float duration = deltaTime;
                    if (active.definition.endMode == ActionEndMode.Timed)
                    {
                        float total = active.definition.anticipationDuration + active.definition.activeDuration + active.definition.recoveryDuration;
                        if (active.definition.durationMode == ActionDurationMode.Fixed)
                        {
                            total = active.definition.duration;
                        }
                        duration = Math.Min(duration, Math.Max(0f, total - active.elapsed));
                    }
                    float cost = active.definition.staminaPerSecond * duration;
                    bool exhausted = actor.vitals.currentStamina <= cost;
                    actor.vitals.currentStamina = Math.Max(0f, actor.vitals.currentStamina - cost);
                    _world.CommitActor(active.ownerHash, actor);
                    if (exhausted && cost > 0f)
                    {
                        End(id, ActionSignalKind.Interrupted);
                        return;
                    }
                }
            }
            if (active.definition.endMode == ActionEndMode.Timed && active.definition.durationMode == ActionDurationMode.Fixed)
            {
                deltaTime = Math.Min(deltaTime, Math.Max(0f, active.definition.duration - active.elapsed));
            }
            state.execution.elapsed += deltaTime;
            state.execution.phaseElapsed += deltaTime;
            while (_instances.TryGetValue(id, out state))
            {
                active = state.execution;
                float duration;
                switch (active.phase)
                {
                    case AnimationPhase.Anticipation:
                        duration = active.definition.anticipationDuration;
                        if (active.phaseElapsed < duration)
                        {
                            CompleteFixedIfExpired(state);
                            return;
                        }
                        ChangePhase(state, AnimationPhase.Active, active.phaseElapsed - duration);
                        break;
                    case AnimationPhase.Active:
                        if (active.definition.endMode != ActionEndMode.Timed)
                        {
                            return;
                        }
                        if (active.definition.durationMode == ActionDurationMode.Fixed
                            && active.definition.anticipationDuration + active.definition.activeDuration + active.definition.recoveryDuration <= 0f)
                        {
                            CompleteFixedIfExpired(state);
                            return;
                        }
                        duration = active.definition.activeDuration;
                        if (active.phaseElapsed < duration)
                        {
                            CompleteFixedIfExpired(state);
                            return;
                        }
                        ChangePhase(state, AnimationPhase.Recovery, active.phaseElapsed - duration);
                        break;
                    case AnimationPhase.Recovery:
                        if (active.definition.endMode == ActionEndMode.Timed
                            && active.definition.durationMode == ActionDurationMode.Fixed)
                        {
                            CompleteFixedIfExpired(state);
                            return;
                        }
                        if (active.phaseElapsed < active.definition.recoveryDuration)
                        {
                            return;
                        }
                        End(id, ActionSignalKind.Completed);
                        return;
                    default:
                        return;
                }
            }
        }

        private void CompleteFixedIfExpired(Instance state)
        {
            ActionDefinitionData definition = state.execution.definition;
            if (definition.endMode == ActionEndMode.Timed && definition.durationMode == ActionDurationMode.Fixed
                && state.execution.elapsed >= definition.duration)
            {
                End(state.execution.instanceId, ActionSignalKind.Completed);
            }
        }

        private void ChangePhase(Instance state, AnimationPhase phase, float elapsed)
        {
            state.execution.phase = phase;
            state.execution.phaseElapsed = elapsed;
            if (!state.execution.definition.motionOnly)
            {
                CommitDerivedState(state.execution.ownerHash, !AffectsActState(state.execution.definition));
            }
            Notify(state, ActionSignalKind.PhaseChanged);
        }

        private bool End(long id, ActionSignalKind kind)
        {
            if (!_instances.TryGetValue(id, out Instance state))
            {
                return false;
            }
            _instances.Remove(id);
            if (_actors.ContainsKey(state.execution.ownerHash))
            {
                _completedSources[id] = state.execution.ownerHash;
                _pendingSourceNotifications.Add(id);
                if (kind == ActionSignalKind.Interrupted)
                {
                    _interruptedSources.Add(id);
                }
            }
            if (!state.execution.definition.motionOnly)
            {
                CommitDerivedState(state.execution.ownerHash, !AffectsActState(state.execution.definition));
            }
            Notify(state, kind);
            return true;
        }

        private void Notify(Instance state, ActionSignalKind kind, int signalId = 0)
        {
            try
            {
                ProcessTransitions(state, kind, signalId);
                Emit(new ActionExecutionEvent { kind = kind, execution = CurrentSnapshot(state), signalId = signalId });
            }
            finally
            {
                if (kind == ActionSignalKind.Completed || kind == ActionSignalKind.Interrupted)
                {
                    _pendingSourceNotifications.Remove(state.execution.instanceId);
                    PruneCompletedSources();
                }
            }
        }

        private void Emit(ActionExecutionEvent change)
        {
            _notificationDepth++;
            try
            {
                Changed?.Invoke(change);
            }
            finally
            {
                _notificationDepth--;
            }
        }

        private void ProcessTransitions(Instance state, ActionSignalKind kind, int signalId)
        {
            ActionTransition[] transitions = state.execution.definition.transitions;
            for (int i = 0; i < transitions.Length; i++)
            {
                if (state.triggeredTransitions[i])
                {
                    continue;
                }
                ActionTransition transition = transitions[i];
                bool matches = transition.trigger == kind && (transition.signalId == 0 || transition.signalId == signalId)
                    && state.execution.elapsed >= transition.minElapsed && (!transition.requiresHit || state.hitConfirmed);
                if (matches && transition.onCompletion && kind != ActionSignalKind.Completed)
                {
                    state.pendingTransitions[i] = true;
                    continue;
                }
                if ((!matches && !(kind == ActionSignalKind.Completed && state.pendingTransitions[i]))
                    || transition.nextDefinitionId <= 0)
                {
                    continue;
                }
                state.triggeredTransitions[i] = true;
                Queue(new ActionRequest
                {
                    ownerHash = state.execution.ownerHash, definitionId = transition.nextDefinitionId,
                    targetHash = state.execution.targetHash, targetPosition = state.execution.targetPosition,
                    direction = state.execution.direction, speedMultiplier = state.execution.speedMultiplier,
                    sourceInstanceId = state.execution.instanceId,
                }, kind == ActionSignalKind.Interrupted);
                return;
            }
        }

        private bool ValidSource(int ownerHash, long source, bool allowInterruptedSource = false)
        {
            return source == 0 || (_instances.TryGetValue(source, out Instance active) && active.execution.ownerHash == ownerHash)
                || (_completedSources.TryGetValue(source, out int owner) && owner == ownerHash
                    && (allowInterruptedSource || !_interruptedSources.Contains(source)));
        }

        private static bool CanReplace(ActionRequest request, ActionDefinitionData definition, ActionExecution existing)
        {
            if (definition.motionOnly && !existing.definition.motionOnly)
            {
                return false;
            }
            if (!definition.motionOnly && existing.definition.motionOnly)
            {
                return true;
            }
            if (request.forced || request.sourceInstanceId == existing.instanceId)
            {
                return true;
            }
            return !existing.forced && definition.priority >= existing.definition.priority && existing.canCancel;
        }

        private static bool Blocked(ActionDefinitionData definition, ActionActorState actor)
        {
            return actor.statusEffects.BlocksActions
                || ((definition.gameplay & ActionGameplayFlags.Cast) != 0 && actor.statusEffects.BlocksCasting)
                || ((definition.gameplay & ActionGameplayFlags.Move) != 0 && actor.statusEffects.BlocksMovement);
        }

        private static bool IsReaction(ActState state)
        {
            return HitReactionLogic.IsArmorResetStun(state) || state == ActState.WakeUp;
        }

        private static bool HigherPriority(ActionExecution candidate, ActionExecution current)
        {
            if (candidate.forced != current.forced)
            {
                return candidate.forced;
            }
            return candidate.definition.priority != current.definition.priority
                ? candidate.definition.priority > current.definition.priority : candidate.instanceId > current.instanceId;
        }

        private static bool HigherPrimary(ActionExecution candidate, ActionExecution current)
        {
            if (candidate.definition.motionOnly != current.definition.motionOnly)
            {
                return !candidate.definition.motionOnly;
            }
            if (candidate.forced != current.forced)
            {
                return candidate.forced;
            }
            bool candidateMove = (candidate.definition.gameplay & ActionGameplayFlags.Move) != 0;
            bool currentMove = (current.definition.gameplay & ActionGameplayFlags.Move) != 0;
            if (candidateMove != currentMove)
            {
                return !candidateMove;
            }
            return HigherPriority(candidate, current);
        }

        private static bool AffectsActState(ActionDefinitionData definition)
        {
            return !definition.motionOnly
                && (definition.resources != ActionResources.None || definition.actState != ActState.Neutral);
        }

        private ActState DeriveState(int ownerHash, ActState current, int hp, bool preserveWhenNoPrimary = false)
        {
            if (current == ActState.Dead || hp <= 0)
            {
                return ActState.Dead;
            }
            if (!GetPrimary(ownerHash, out ActionExecution primary) || primary.definition.motionOnly)
            {
                return preserveWhenNoPrimary ? current : ActState.Neutral;
            }
            ActState state = primary.definition.actState;
            if (state == ActState.Attacking || state == ActState.AttackPrep || state == ActState.AttackRecovery)
            {
                return primary.phase == AnimationPhase.Anticipation ? ActState.AttackPrep
                    : primary.phase == AnimationPhase.Recovery ? ActState.AttackRecovery : ActState.Attacking;
            }
            return state;
        }

        private void CommitDerivedState(int ownerHash, bool preserveWhenNoPrimary = false)
        {
            if (_actors.ContainsKey(ownerHash) && _world.TryGetActor(ownerHash, out ActionActorState actor))
            {
                actor.flags.ActState = DeriveState(ownerHash, actor.flags.ActState, actor.vitals.currentHp, preserveWhenNoPrimary);
                _world.CommitActor(ownerHash, actor);
            }
        }

        private static ActionExecution CurrentSnapshot(Instance state)
        {
            ActionExecution execution = state.execution;
            float duration = execution.phase == AnimationPhase.Anticipation ? execution.definition.anticipationDuration
                : execution.phase == AnimationPhase.Recovery ? execution.definition.recoveryDuration : execution.definition.activeDuration;
            execution.normalizedTime = duration > 0f ? Math.Min(1f, execution.phaseElapsed / duration) : 0f;
            execution.canCancel = execution.definition.cancelPoint >= 0f
                ? execution.phase == AnimationPhase.Recovery && execution.normalizedTime >= execution.definition.cancelPoint
                : execution.definition.cancelAfter >= 0f && execution.elapsed >= execution.definition.cancelAfter;
            return execution;
        }

        private void PruneCompletedSources()
        {
            List<long> removed = null;
            foreach (long source in _completedSources.Keys)
            {
                if (_pendingSourceNotifications.Contains(source))
                {
                    continue;
                }
                bool queued = false;
                foreach (QueuedRequest request in _requests)
                {
                    if (request.request.sourceInstanceId == source)
                    {
                        queued = true;
                        break;
                    }
                }
                for (int i = _tickRequestIndex; !queued && i < _tickRequests.Count; i++)
                {
                    queued = _tickRequests[i].request.sourceInstanceId == source;
                }
                if (!queued)
                {
                    removed ??= new List<long>();
                    removed.Add(source);
                }
            }
            if (removed != null)
            {
                for (int i = 0; i < removed.Count; i++)
                {
                    _completedSources.Remove(removed[i]);
                    _interruptedSources.Remove(removed[i]);
                }
            }
        }

        private int RemovePendingRequests(Predicate<QueuedRequest> matches)
        {
            int removed = 0;
            int count = _requests.Count;
            for (int i = 0; i < count; i++)
            {
                QueuedRequest request = _requests.Dequeue();
                if (!matches(request))
                {
                    _requests.Enqueue(request);
                }
                else
                {
                    removed++;
                }
            }
            for (int i = _tickRequests.Count - 1; i >= _tickRequestIndex; i--)
            {
                if (matches(_tickRequests[i]))
                {
                    _tickRequests.RemoveAt(i);
                    removed++;
                }
            }
            return removed;
        }

        private static ActionDefinitionData SnapshotDefinition(ActionDefinitionData data)
        {
            if (!FiniteNonNegative(data.staminaCost) || data.mpCost < 0 || !FiniteNonNegative(data.staminaPerSecond)
                || !FiniteNonNegative(data.anticipationDuration) || !FiniteNonNegative(data.activeDuration)
                || !FiniteNonNegative(data.recoveryDuration) || !FiniteNonNegative(data.moveStartTime)
                || !FiniteNonNegative(data.moveDuration) || !FiniteNonNegative(data.moveSpeed)
                || !FiniteNonNegative(data.duration)
                || float.IsNaN(data.cancelAfter) || float.IsInfinity(data.cancelAfter)
                || float.IsNaN(data.cancelPoint) || float.IsInfinity(data.cancelPoint) || data.cancelPoint > 1f
                || !Enum.IsDefined(typeof(ActionEndMode), data.endMode)
                || !Enum.IsDefined(typeof(ActionDurationMode), data.durationMode))
            {
                throw new ArgumentException("Invalid action definition.", nameof(data));
            }
            data.effects = data.effects != null ? (ActionEffect[])data.effects.Clone() : Array.Empty<ActionEffect>();
            data.transitions = data.transitions != null ? (ActionTransition[])data.transitions.Clone() : Array.Empty<ActionTransition>();
            for (int i = 0; i < data.effects.Length; i++)
            {
                if (!FiniteNonNegative(data.effects[i].startTime) || !FiniteNonNegative(data.effects[i].duration)
                    || !FiniteNonNegative(data.effects[i].value))
                {
                    throw new ArgumentException("Invalid timed action effect.", nameof(data));
                }
            }
            return data;
        }

        private static bool FiniteNonNegative(float value) => value >= 0f && !float.IsInfinity(value);
    }
}
