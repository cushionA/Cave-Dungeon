using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Profiling;
using UnityEngine;

namespace Game.Core
{
    public sealed class AIDecisionSystem : IDisposable
    {
        private static readonly ProfilerMarker s_CompleteMarker = new ProfilerMarker("AI.Decision.Complete");
        private static readonly ProfilerMarker s_ApplyMarker = new ProfilerMarker("AI.Decision.Apply");
        private sealed class ProgramEntry
        {
            public JudgmentLoop loop;
            public AICompiledMode[] modes;
            public int index;
            public bool active = true;
        }

        private struct PendingDecision
        {
            public ProgramEntry entry;
            public int revision;
            public int modeIndex;
            public int targetHash;
            public int candidateStart;
            public int candidateCount;
            public bool selectTarget;
            public bool selectAction;
            public long tick;
        }

        private struct Request
        {
            public int programIndex;
            public int ownerIndex;
            public int targetIndex;
            public int ownerHash;
            public int targetHash;
            public int candidateStart;
            public int candidateCount;
            public bool selectTarget;
            public bool selectAction;
            public long tick;
            public int revision;
        }

        private struct Ratios
        {
            public float hpPercent;
            public float hpFraction;
            public float mpPercent;
            public float staminaPercent;
            public float armorPercent;
        }

        private readonly ActorRegistry _actors;
        private AIObservationContext _observation;
        private long _snapshotTick;
        private int _snapshotCount;
        private readonly List<KeyValuePair<(ActorRef owner, ActorRef source), float>> _scoreSamples
            = new List<KeyValuePair<(ActorRef owner, ActorRef source), float>>();
        private readonly Dictionary<JudgmentLoop, ProgramEntry> _programs = new Dictionary<JudgmentLoop, ProgramEntry>();
        private readonly List<ProgramEntry> _entries = new List<ProgramEntry>();
        private readonly List<PendingDecision> _pending = new List<PendingDecision>();
        private readonly List<int> _candidateHashes = new List<int>();
        private readonly HashSet<int> _invalidatedHashes = new HashSet<int>();
        private NativeParallelHashMap<int, int> _indices;
        private NativeArray<int> _snapshotHashes;
        private NativeArray<ActorRef> _actorRefs;
        private NativeArray<ActorRef> _lastAttackers;
        private NativeArray<int> _targetCounts;
        private NativeParallelHashMap<ulong, float> _scores;
        private NativeArray<Vector2> _positions;
        private NativeArray<Ratios> _ratios;
        private NativeArray<int> _hp;
        private NativeArray<CharacterFlags> _flags;
        private NativeArray<AICombatValues> _combat;
        private NativeArray<AIProgramRanges> _programRanges;
        private NativeArray<AIRuleRange> _rules;
        private NativeArray<AICondition> _conditions;
        private NativeArray<AITargetSelect> _selects;
        private NativeArray<int> _candidateIndices;
        private NativeArray<Request> _requests;
        private NativeArray<AIDecisionResult> _results;
        private JobHandle _handle;
        private bool _programsDirty;
        private bool _scheduled;
        private bool _disposed;

        internal AIObservationContext Observation => _observation;

        public AIDecisionSystem(ActorRegistry actors)
        {
            _actors = actors;
            _indices = new NativeParallelHashMap<int, int>(16, Allocator.Persistent);
            _scores = new NativeParallelHashMap<ulong, float>(16, Allocator.Persistent);
        }

        public void BeginTick(AIObservationContext observation)
        {
            ThrowIfScheduled();
            _observation = observation ?? throw new ArgumentNullException(nameof(observation));
            _snapshotTick = observation.Tick;
            _pending.Clear();
            _candidateHashes.Clear();
            _invalidatedHashes.Clear();
        }

        internal void Queue(JudgmentLoop loop, List<int> candidates, float time,
            bool selectTarget, bool selectAction)
        {
            ThrowIfScheduled();
            if (!_programs.TryGetValue(loop, out ProgramEntry entry))
            {
                entry = new ProgramEntry { loop = loop, modes = loop.CompiledModes };
                _programs.Add(loop, entry);
                _entries.Add(entry);
                _programsDirty = true;
            }
            else if (!ReferenceEquals(entry.modes, loop.CompiledModes))
            {
                entry.modes = loop.CompiledModes;
                _programsDirty = true;
            }
            _pending.Add(new PendingDecision
            {
                entry = entry,
                revision = loop.Revision,
                modeIndex = loop.CompiledModeIndex,
                targetHash = loop.CurrentTargetHash,
                candidateStart = _candidateHashes.Count,
                candidateCount = candidates == null ? 0 : candidates.Count,
                selectTarget = selectTarget,
                selectAction = selectAction,
                tick = _snapshotTick
            });
            if (candidates != null)
            {
                _candidateHashes.AddRange(candidates);
            }
            else
            {
                IReadOnlyList<int> hashes = _observation.ActorHashes;
                PendingDecision pending = _pending[_pending.Count - 1];
                pending.candidateCount = hashes.Count;
                _pending[_pending.Count - 1] = pending;
                for (int i = 0; i < hashes.Count; i++) { _candidateHashes.Add(hashes[i]); }
            }
        }

        public void Schedule()
        {
            ThrowIfScheduled();
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                PendingDecision pending = _pending[i];
                if (!pending.entry.active || pending.revision != pending.entry.loop.Revision
                    || pending.tick != _observation.Tick)
                {
                    _pending.RemoveAt(i);
                }
            }
            if (_pending.Count == 0)
            {
                _handle = default;
                _scheduled = true;
                return;
            }
            if (_programsDirty)
            {
                RebuildPrograms();
            }
            CaptureWorld();
            EnsureCapacity(ref _candidateIndices, _candidateHashes.Count);
            for (int i = 0; i < _candidateHashes.Count; i++)
            {
                int hash = _candidateHashes[i];
                _candidateIndices[i] = !_invalidatedHashes.Contains(hash)
                    && _indices.TryGetValue(hash, out int index) ? index : -1;
            }
            EnsureCapacity(ref _requests, _pending.Count);
            EnsureCapacity(ref _results, _pending.Count);
            for (int i = 0; i < _pending.Count; i++)
            {
                PendingDecision pending = _pending[i];
                _requests[i] = new Request
                {
                    programIndex = pending.entry.index + pending.modeIndex,
                    ownerHash = pending.entry.loop.OwnerHash,
                    targetHash = pending.targetHash,
                    ownerIndex = _indices.TryGetValue(pending.entry.loop.OwnerHash, out int ownerIndex) ? ownerIndex : -1,
                    targetIndex = _indices.TryGetValue(pending.targetHash, out int targetIndex) ? targetIndex : -1,
                    candidateStart = pending.candidateStart,
                    candidateCount = pending.candidateCount,
                    selectTarget = pending.selectTarget,
                    selectAction = pending.selectAction,
                    tick = pending.tick,
                    revision = pending.revision
                };
            }
            DecisionJob job = new DecisionJob
            {
                indices = _indices,
                hashes = _snapshotHashes,
                actors = _actorRefs,
                lastAttackers = _lastAttackers,
                targetCounts = _targetCounts,
                scores = _scores,
                scoresAvailable = _observation.DamageScoresAvailable,
                targetsAvailable = _observation.TargetRelationsAvailable,
                actorCount = _snapshotCount,
                positions = _positions,
                ratios = _ratios,
                hp = _hp,
                flags = _flags,
                combat = _combat,
                programs = _programRanges,
                rules = _rules,
                conditions = _conditions,
                selects = _selects,
                candidates = _candidateIndices,
                requests = _requests,
                results = _results
            };
            _handle = job.Schedule(_pending.Count, 8);
            _scheduled = true;
        }

        public void CompleteAndApply(Func<int, bool> canApply = null)
        {
            if (!_scheduled)
            {
                return;
            }
            try
            {
                using (s_CompleteMarker.Auto())
                {
                    _handle.Complete();
                }
            }
            finally
            {
                _scheduled = false;
            }
            using (s_ApplyMarker.Auto())
            {
                for (int i = 0; i < _pending.Count; i++)
                {
                    PendingDecision pending = _pending[i];
                    int owner = pending.entry.loop.OwnerHash;
                    AIDecisionResult result = _results[i];
                    if (!pending.entry.active || pending.revision != pending.entry.loop.Revision
                        || result.observationTick != _observation.Tick || !_actors.IsCurrent(result.owner)
                        || canApply != null && !canApply(owner))
                    {
                        continue;
                    }
                    if (result.target.IsValid && (!_actors.IsCurrent(result.target)
                        || _invalidatedHashes.Contains(result.target.Hash)))
                    {
                        pending.entry.loop.InvalidateTarget();
                        continue;
                    }
                    pending.entry.loop.ApplyDecision(_results[i], pending.revision);
                }
            }
            _pending.Clear();
        }

        public void RemoveActor(int hash)
        {
            _invalidatedHashes.Add(hash);
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                ProgramEntry entry = _entries[i];
                if (entry.loop.OwnerHash == hash)
                {
                    entry.active = false;
                    entry.loop.Detach(this);
                    _programs.Remove(entry.loop);
                    _entries.RemoveAt(i);
                    _programsDirty = true;
                }
            }
        }

        internal void Unregister(JudgmentLoop loop)
        {
            if (_programs.TryGetValue(loop, out ProgramEntry entry))
            {
                entry.active = false;
                _programs.Remove(loop);
                _entries.Remove(entry);
                _programsDirty = true;
            }
            loop.Detach(this);
        }

        private void CaptureWorld()
        {
            IReadOnlyList<int> hashes = _observation.ActorHashes;
            int count = hashes.Count;
            _snapshotCount = count;
            EnsureCapacity(ref _snapshotHashes, count);
            EnsureCapacity(ref _actorRefs, count);
            EnsureCapacity(ref _lastAttackers, count);
            EnsureCapacity(ref _targetCounts, count);
            EnsureCapacity(ref _positions, count);
            EnsureCapacity(ref _ratios, count);
            EnsureCapacity(ref _hp, count);
            EnsureCapacity(ref _flags, count);
            EnsureCapacity(ref _combat, count);
            _indices.Clear();
            if (_indices.Capacity < count) { _indices.Capacity = count; }
            for (int i = 0; i < count; i++)
            {
                int hash = hashes[i];
                _observation.TryGetActor(hash, out AIActorObservation actor);
                _indices.Add(hash, i);
                _snapshotHashes[i] = hash;
                _actorRefs[i] = actor.actor;
                _observation.TryGetLastAttacker(hash, out ActorRef attacker);
                _lastAttackers[i] = attacker;
                _observation.TryGetTargetingCount(hash, out int targetingCount);
                _targetCounts[i] = targetingCount;
                _positions[i] = actor.position;
                _ratios[i] = new Ratios
                {
                    hpPercent = actor.hpPercent, hpFraction = actor.hpFraction, mpPercent = actor.mpPercent,
                    staminaPercent = actor.staminaPercent, armorPercent = actor.armorPercent
                };
                _hp[i] = actor.currentHp;
                _flags[i] = actor.flags;
                _combat[i] = new AICombatValues
                {
                    attack = actor.attackPower, defense = actor.defensePower, weaknesses = actor.weakPoints
                };
            }
            _scores.Clear();
            _observation.CopyDamageScores(_scoreSamples);
            if (_scores.Capacity < _scoreSamples.Count) { _scores.Capacity = _scoreSamples.Count; }
            for (int i = 0; i < _scoreSamples.Count; i++)
            {
                KeyValuePair<(ActorRef owner, ActorRef source), float> score = _scoreSamples[i];
                if (_indices.TryGetValue(score.Key.owner.Hash, out int owner)
                    && _indices.TryGetValue(score.Key.source.Hash, out int source)
                    && _actorRefs[owner] == score.Key.owner && _actorRefs[source] == score.Key.source)
                {
                    _scores.Add(ScoreKey(owner, source), score.Value);
                }
            }
        }

        private static ulong ScoreKey(int owner, int source) => ((ulong)(uint)owner << 32) | (uint)source;

        private void RebuildPrograms()
        {
            int ruleCount = 0;
            int conditionCount = 0;
            int selectCount = 0;
            int programCount = 0;
            for (int i = 0; i < _entries.Count; i++)
            {
                AICompiledMode[] modes = _entries[i].modes;
                programCount += modes.Length;
                for (int m = 0; m < modes.Length; m++)
                {
                    ruleCount += modes[m].rules.Length;
                    conditionCount += modes[m].conditions.Length;
                    selectCount += modes[m].selects.Length;
                }
            }
            EnsureCapacity(ref _programRanges, programCount);
            EnsureCapacity(ref _rules, ruleCount);
            EnsureCapacity(ref _conditions, conditionCount);
            EnsureCapacity(ref _selects, selectCount);
            int ruleStart = 0;
            int conditionStart = 0;
            int selectStart = 0;
            int programIndex = 0;
            for (int i = 0; i < _entries.Count; i++)
            {
                ProgramEntry entry = _entries[i];
                entry.index = programIndex;
                for (int m = 0; m < entry.modes.Length; m++)
                {
                    AICompiledMode mode = entry.modes[m];
                    AIProgramRanges ranges = mode.ranges;
                    ranges.targetRuleStart += ruleStart;
                    ranges.actionRuleStart += ruleStart;
                    ranges.selectStart = selectStart;
                    _programRanges[programIndex++] = ranges;
                    for (int r = 0; r < mode.rules.Length; r++)
                    {
                        AIRuleRange rule = mode.rules[r];
                        rule.conditionStart += conditionStart;
                        _rules[ruleStart + r] = rule;
                    }
                    for (int c = 0; c < mode.conditions.Length; c++)
                    {
                        _conditions[conditionStart + c] = mode.conditions[c];
                    }
                    for (int s = 0; s < mode.selects.Length; s++)
                    {
                        _selects[selectStart + s] = mode.selects[s];
                    }
                    ruleStart += mode.rules.Length;
                    conditionStart += mode.conditions.Length;
                    selectStart += mode.selects.Length;
                }
            }
            _programsDirty = false;
        }

        private void ThrowIfScheduled()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(AIDecisionSystem));
            }
            if (_scheduled)
            {
                throw new InvalidOperationException("Complete the previous AI decisions before preparing another tick.");
            }
        }

        private static void EnsureCapacity<T>(ref NativeArray<T> array, int count) where T : struct
        {
            if (array.IsCreated && array.Length >= count)
            {
                return;
            }
            int capacity = 16;
            while (capacity < count)
            {
                capacity *= 2;
            }
            if (array.IsCreated)
            {
                array.Dispose();
            }
            array = new NativeArray<T>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }

        private static void DisposeArray<T>(ref NativeArray<T> array) where T : struct
        {
            if (array.IsCreated)
            {
                array.Dispose();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            try
            {
                if (_scheduled)
                {
                    _handle.Complete();
                }
            }
            finally
            {
                _scheduled = false;
                _disposed = true;
                _indices.Dispose();
                _scores.Dispose();
                DisposeArray(ref _actorRefs);
                DisposeArray(ref _lastAttackers);
                DisposeArray(ref _targetCounts);
                DisposeArray(ref _snapshotHashes);
                DisposeArray(ref _positions);
                DisposeArray(ref _ratios);
                DisposeArray(ref _hp);
                DisposeArray(ref _flags);
                DisposeArray(ref _combat);
                DisposeArray(ref _programRanges);
                DisposeArray(ref _rules);
                DisposeArray(ref _conditions);
                DisposeArray(ref _selects);
                DisposeArray(ref _candidateIndices);
                DisposeArray(ref _requests);
                DisposeArray(ref _results);
                for (int i = 0; i < _entries.Count; i++)
                {
                    _entries[i].loop.Detach(this);
                }
                _programs.Clear();
                _entries.Clear();
                _pending.Clear();
                _candidateHashes.Clear();
                _scoreSamples.Clear();
                _invalidatedHashes.Clear();
            }
        }

        private struct NativeWorld : IAIReadWorld
        {
            public NativeParallelHashMap<int, int> indices;
            public NativeParallelHashMap<ulong, float> scores;
            public NativeArray<ActorRef> actors;
            public NativeArray<ActorRef> lastAttackers;
            public NativeArray<int> targetCounts;
            public NativeArray<int> hashes;
            public NativeArray<Vector2> positions;
            public NativeArray<Ratios> ratios;
            public NativeArray<int> hp;
            public NativeArray<CharacterFlags> flags;
            public NativeArray<AICombatValues> combat;
            public bool scoresAvailable;
            public bool targetsAvailable;
            public int actorCount;
            public int ownerHash;
            public int ownerIndex;
            public int targetHash;
            public int targetIndex;
            private int _cachedHash;
            private int _cachedIndex;
            private bool _hasCachedIndex;

            private bool TryGetIndex(int hash, out int index)
            {
                if (hash == ownerHash) { index = ownerIndex; return index >= 0; }
                if (hash == targetHash) { index = targetIndex; return index >= 0; }
                if (_hasCachedIndex && hash == _cachedHash) { index = _cachedIndex; return index >= 0; }
                bool found = indices.TryGetValue(hash, out index);
                _cachedHash = hash; _cachedIndex = found ? index : -1; _hasCachedIndex = true;
                return found;
            }

            public int ActorCount => actorCount;
            public int HashAt(int index) => hashes[index];

            public bool TryGetActor(int hash, out AIActorObservation actor)
            {
                actor = default;
                if (!TryGetIndex(hash, out int index)) { return false; }
                Ratios ratio = ratios[index];
                AICombatValues stats = combat[index];
                actor = new AIActorObservation(actors[index], positions[index], flags[index], hp[index],
                    ratio.hpPercent, ratio.hpFraction, ratio.mpPercent, ratio.staminaPercent, ratio.armorPercent,
                    stats.attack, stats.defense, stats.weaknesses);
                return true;
            }

            public bool TryGetDamageScore(int owner, int source, out float value)
            {
                value = 0f;
                if (!scoresAvailable || !TryGetIndex(owner, out int ownerIndex)
                    || !TryGetIndex(source, out int sourceIndex)) { return false; }
                scores.TryGetValue(ScoreKey(ownerIndex, sourceIndex), out value);
                return true;
            }

            public bool TryGetTargetingCount(int target, out int count)
            {
                count = 0;
                if (!targetsAvailable || !TryGetIndex(target, out int index)) { return false; }
                count = targetCounts[index];
                return true;
            }

            public bool TryGetLastAttacker(int owner, out ActorRef attacker)
            {
                attacker = default;
                if (!scoresAvailable || !TryGetIndex(owner, out int index)) { return false; }
                attacker = lastAttackers[index];
                return attacker.IsValid;
            }
        }

        private struct NativeProgram : IAIProgram
        {
            public AIProgramRanges ranges;
            public NativeArray<AIRuleRange> rules;
            public NativeArray<AICondition> conditions;
            public NativeArray<AITargetSelect> selects;
            public AIProgramRanges Ranges => ranges;
            public AIRuleRange GetRule(int index) => rules[index];
            public AICondition GetCondition(int index) => conditions[index];
            public AITargetSelect GetTargetSelect(int index) => selects[index];
        }

        private struct NativeCandidates : IAICandidates
        {
            public NativeArray<int> indices;
            public NativeArray<int> hashes;
            public int start;
            public int count;
            public int Count => count;
            public bool TryGetHash(int index, out int hash)
            {
                int snapshotIndex = indices[start + index];
                hash = snapshotIndex >= 0 ? hashes[snapshotIndex] : 0;
                return snapshotIndex >= 0;
            }
        }

        [BurstCompile]
        private struct DecisionJob : IJobParallelFor
        {
            [ReadOnly] public NativeParallelHashMap<int, int> indices;
            [ReadOnly] public NativeArray<int> hashes;
            [ReadOnly] public NativeArray<ActorRef> actors;
            [ReadOnly] public NativeArray<ActorRef> lastAttackers;
            [ReadOnly] public NativeArray<int> targetCounts;
            [ReadOnly] public NativeParallelHashMap<ulong, float> scores;
            public bool scoresAvailable;
            public bool targetsAvailable;
            public int actorCount;
            [ReadOnly] public NativeArray<Vector2> positions;
            [ReadOnly] public NativeArray<Ratios> ratios;
            [ReadOnly] public NativeArray<int> hp;
            [ReadOnly] public NativeArray<CharacterFlags> flags;
            [ReadOnly] public NativeArray<AICombatValues> combat;
            [ReadOnly] public NativeArray<AIProgramRanges> programs;
            [ReadOnly] public NativeArray<AIRuleRange> rules;
            [ReadOnly] public NativeArray<AICondition> conditions;
            [ReadOnly] public NativeArray<AITargetSelect> selects;
            [ReadOnly] public NativeArray<int> candidates;
            [ReadOnly] public NativeArray<Request> requests;
            [WriteOnly] public NativeArray<AIDecisionResult> results;

            public void Execute(int index)
            {
                Request request = requests[index];
                NativeWorld world = new NativeWorld
                {
                    indices = indices, positions = positions, ratios = ratios, hp = hp,
                    scores = scores, actors = actors, lastAttackers = lastAttackers, hashes = hashes,
                    targetCounts = targetCounts, scoresAvailable = scoresAvailable,
                    targetsAvailable = targetsAvailable, actorCount = actorCount,
                    flags = flags, combat = combat, ownerHash = request.ownerHash,
                    ownerIndex = request.ownerIndex, targetHash = request.targetHash, targetIndex = request.targetIndex
                };
                NativeProgram program = new NativeProgram
                {
                    ranges = programs[request.programIndex], rules = rules, conditions = conditions, selects = selects
                };
                NativeCandidates input = new NativeCandidates
                {
                    indices = candidates, hashes = hashes, start = request.candidateStart, count = request.candidateCount
                };
                AIDecisionResult result = AIQueryKernel.Decide(request.ownerHash, request.targetHash,
                    request.selectTarget, request.selectAction, ref world, ref program, ref input);
                result.owner = request.ownerIndex >= 0 ? actors[request.ownerIndex] : default;
                result.target = world.TryGetActor(result.targetHash, out AIActorObservation target) ? target.actor : default;
                result.observationTick = request.tick;
                result.revision = request.revision;
                results[index] = result;
            }
        }
    }
}
