using System;
using System.Collections.Generic;

namespace Game.Core
{
    public sealed partial class ActionExecutionSystem
    {
        private sealed class ExecutionGroup
        {
            public long id;
            public int ownerHash;
            public int actorVersion;
            public ActorRef ownerRef;
            public readonly HashSet<long> members = new HashSet<long>();
            public int pending;
            public bool cancelRequested;
            public bool wasCancelled;
        }

        private readonly struct GroupMutation : IDisposable
        {
            private readonly ActionExecutionSystem _system;

            public GroupMutation(ActionExecutionSystem system)
            {
                _system = system;
                system._groupMutationDepth++;
            }

            public void Dispose()
            {
                _system._groupMutationDepth--;
                _system.SettleGroups();
            }
        }

        private readonly Dictionary<long, ExecutionGroup> _groups = new Dictionary<long, ExecutionGroup>();
        private readonly Dictionary<long, long> _completedSourceGroups = new Dictionary<long, long>();
        private readonly Dictionary<long, long> _completionBarriers = new Dictionary<long, long>();
        private readonly ActorRegistry _actorRegistry;
        private long _nextGroupId = 1;
        private int _groupMutationDepth;
        private bool _settlingGroups;

        /// <summary>必須の実行と待機要求がなくなってから一度だけ通知する。</summary>
        public event Action<ActionGroupEvent> GroupChanged;

        public int ActiveGroupCount => _groups.Count;

        public bool IsGroupActive(long groupId) => _groups.ContainsKey(groupId);

        /// <summary>受理済み実行の効果を現在個体へ適用できるか。消滅中でも正当な既受理実行は継続する。</summary>
        public bool CanApplyExecutionEffects(long instanceId) =>
            _instances.TryGetValue(instanceId, out Instance state)
            && _groups.TryGetValue(state.execution.groupId, out ExecutionGroup group)
            && !group.cancelRequested
            && _actors.TryGetValue(state.execution.ownerHash, out int actorVersion)
            && group.actorVersion == actorVersion
            && IsCurrentExecutionOwner(state.execution);

        public bool TryCompleteAndContinue(long sourceInstanceId, ActionRequest continuation, out long instanceId) =>
            TryCompleteAndContinue(sourceInstanceId, continuation, out instanceId, out _);

        /// <summary>
        /// 元実行を正常完了し、同じグループの後続を同期開始する。拒否時は元実行も資源も変えない。
        /// 元のCompleted通知が要求する次の行動は、この後続が完了するまで待機する。
        /// </summary>
        public bool TryCompleteAndContinue(long sourceInstanceId, ActionRequest continuation,
            out long instanceId, out long groupId)
        {
            using GroupMutation mutation = new GroupMutation(this);
            instanceId = groupId = 0;
            if (!_instances.TryGetValue(sourceInstanceId, out Instance source) || continuation.detached
                || (continuation.sourceInstanceId != 0 && continuation.sourceInstanceId != sourceInstanceId)
                || (continuation.groupId != 0 && continuation.groupId != source.execution.groupId)) { return false; }
            if (continuation.chargeMultiplier == 0f) { continuation.chargeMultiplier = source.execution.chargeMultiplier; }
            if (!PrepareRequestReferences(ref continuation) || continuation.ownerHash != source.execution.ownerHash) { return false; }
            continuation.sourceInstanceId = sourceInstanceId;
            continuation.groupId = source.execution.groupId;
            return TryStart(continuation, out instanceId, out groupId, false, sourceInstanceId);
        }

        public bool TryGetGroupId(long instanceId, out long groupId)
        {
            if (_instances.TryGetValue(instanceId, out Instance state))
            {
                groupId = state.execution.groupId;
                return true;
            }
            return _completedSourceGroups.TryGetValue(instanceId, out groupId);
        }

        public void CopyGroupActive(long groupId, List<ActionExecution> output)
        {
            output.Clear();
            if (!_groups.TryGetValue(groupId, out ExecutionGroup group)) { return; }
            foreach (long id in group.members)
            {
                if (_instances.TryGetValue(id, out Instance state)) { output.Add(CurrentSnapshot(state)); }
            }
        }

        public bool GetGroupPrimary(long groupId, out ActionExecution execution)
        {
            execution = default;
            bool found = false;
            if (!_groups.TryGetValue(groupId, out ExecutionGroup group)) { return false; }
            foreach (long id in group.members)
            {
                if (!_instances.TryGetValue(id, out Instance state)) { continue; }
                ActionExecution candidate = CurrentSnapshot(state);
                if (!found || HigherPrimary(candidate, execution))
                {
                    execution = candidate;
                    found = true;
                }
            }
            return found;
        }

        /// <summary>同じグループの全実行・待機要求を取り消す。Detachedな別グループは対象外。</summary>
        public bool CancelGroup(long groupId)
        {
            using GroupMutation mutation = new GroupMutation(this);
            if (!_groups.TryGetValue(groupId, out ExecutionGroup group) || group.cancelRequested) { return false; }
            group.cancelRequested = true;
            group.wasCancelled = true;
            RemovePendingRequests(queued => queued.request.groupId == groupId);
            List<long> members = new List<long>(group.members);
            for (int i = 0; i < members.Count; i++) { End(members[i], ActionSignalKind.Interrupted); }
            PruneCompletedSources();
            return true;
        }

        private bool PrepareRequestReferences(ref ActionRequest request)
        {
            if ((request.ownerRef != default && !request.ownerRef.IsValid)
                || (request.targetRef != default && !request.targetRef.IsValid)) { return false; }
            if (request.chargeMultiplier == 0f) { request.chargeMultiplier = 1f; }
            if (request.chargeMultiplier <= 0f || float.IsNaN(request.chargeMultiplier)
                || float.IsInfinity(request.chargeMultiplier)) { return false; }
            if (_actorRegistry == null) { return !request.ownerRef.IsValid && !request.targetRef.IsValid; }
            if (!request.ownerRef.IsValid && !_actorRegistry.TryGet(request.ownerHash, out request.ownerRef)) { return false; }
            if (request.ownerHash == 0) { request.ownerHash = request.ownerRef.Hash; }
            if (request.ownerRef.Hash != request.ownerHash || !_actorRegistry.IsCurrent(request.ownerRef)) { return false; }
            if (request.targetRef.IsValid)
            {
                if (request.targetHash == 0) { request.targetHash = request.targetRef.Hash; }
                return request.targetRef.Hash == request.targetHash && _actorRegistry.IsCurrent(request.targetRef);
            }
            return request.targetHash == 0 || _actorRegistry.TryGet(request.targetHash, out request.targetRef);
        }

        private bool IsCurrentExecutionOwner(in ActionExecution execution) =>
            !execution.ownerRef.IsValid || (_actorRegistry != null && _actorRegistry.IsCurrent(execution.ownerRef));

        private bool IsCostFreeRequiredContinuation(in ActionRequest request, in ActionDefinitionSnapshot definition)
        {
            if (request.detached || request.sourceInstanceId == 0 || definition.staminaCost != 0f
                || definition.mpCost != 0 || definition.staminaPerSecond != 0f
                || !TryGetGroupId(request.sourceInstanceId, out long sourceGroup)
                || (request.groupId != 0 && request.groupId != sourceGroup)
                || !_groups.TryGetValue(sourceGroup, out ExecutionGroup group)
                || group.ownerHash != request.ownerHash || group.cancelRequested
                || !_actors.TryGetValue(request.ownerHash, out int actorVersion)
                || group.actorVersion != actorVersion)
            {
                return false;
            }
            return _actorRegistry == null || (group.ownerRef == request.ownerRef && _actorRegistry.IsCurrent(group.ownerRef));
        }

        private bool CollectGroupReplacement(in ActionRequest request, in ActionDefinitionSnapshot definition,
            ref List<long> conflicts, out ExecutionGroup replaced)
        {
            replaced = null;
            if (request.replaceGroupId == 0) { return true; }
            if (!_groups.TryGetValue(request.replaceGroupId, out replaced) || replaced.cancelRequested
                || replaced.ownerHash != request.ownerHash
                || !_actors.TryGetValue(request.ownerHash, out int actorVersion) || replaced.actorVersion != actorVersion
                || request.groupId == request.replaceGroupId
                || (request.sourceInstanceId != 0 && TryGetGroupId(request.sourceInstanceId, out long sourceGroup)
                    && sourceGroup == request.replaceGroupId)
                || (_actorRegistry != null && (replaced.ownerRef != request.ownerRef || !_actorRegistry.IsCurrent(replaced.ownerRef))))
            {
                return false;
            }
            foreach (long id in replaced.members)
            {
                if (!_instances.TryGetValue(id, out Instance previous)) { continue; }
                // 全体置換では資源が非競合でも、既存各部品の割込権限を確認する。
                if (!CanReplace(request, definition, CurrentSnapshot(previous))) { return false; }
                conflicts ??= new List<long>();
                if (!conflicts.Contains(id)) { conflicts.Add(id); }
            }
            return true;
        }

        private bool ResolveRequestGroup(ref ActionRequest request, out ExecutionGroup group)
        {
            group = null;
            if (request.detached)
            {
                // Detachedな行動は受付時に独立rootへ変換する。発射された弾の寿命とは別の契約。
                request.groupId = 0;
                request.sourceInstanceId = 0;
                request.detached = false;
            }
            long groupId = request.groupId;
            if (request.sourceInstanceId != 0)
            {
                if (!TryGetGroupId(request.sourceInstanceId, out long sourceGroup)) { return false; }
                if (groupId != 0 && groupId != sourceGroup) { return false; }
                groupId = sourceGroup;
            }
            if (!_actors.TryGetValue(request.ownerHash, out int actorVersion)) { return false; }
            if (groupId != 0)
            {
                if (!_groups.TryGetValue(groupId, out group) || group.ownerHash != request.ownerHash
                    || group.actorVersion != actorVersion || group.cancelRequested) { return false; }
                if (_actorRegistry != null && (group.ownerRef != request.ownerRef
                    || !_actorRegistry.IsCurrent(group.ownerRef))) { return false; }
            }
            else
            {
                group = new ExecutionGroup
                {
                    id = _nextGroupId++, ownerHash = request.ownerHash,
                    actorVersion = actorVersion, ownerRef = request.ownerRef,
                };
                _groups.Add(group.id, group);
            }
            request.groupId = group.id;
            return true;
        }

        private void DetachGroupMember(Instance state, bool cancelled)
        {
            if (_groups.TryGetValue(state.execution.groupId, out ExecutionGroup group))
            {
                group.members.Remove(state.execution.instanceId);
                group.wasCancelled |= cancelled;
            }
        }

        private void ReleaseQueuedGroup(in QueuedRequest request, bool cancelled)
        {
            if (_groups.TryGetValue(request.request.groupId, out ExecutionGroup group))
            {
                group.pending--;
                group.wasCancelled |= cancelled;
            }
        }

        private void CancelActorGroups(int ownerHash)
        {
            foreach (ExecutionGroup group in _groups.Values)
            {
                if (group.ownerHash == ownerHash)
                {
                    group.cancelRequested = true;
                    group.wasCancelled = true;
                }
            }
        }

        private void SettleGroups()
        {
            if (_groupMutationDepth != 0 || _settlingGroups) { return; }
            _settlingGroups = true;
            try
            {
                while (true)
                {
                    ExecutionGroup completed = null;
                    foreach (ExecutionGroup group in _groups.Values)
                    {
                        if (group.members.Count == 0 && group.pending == 0) { completed = group; break; }
                    }
                    if (completed == null) { return; }
                    _groups.Remove(completed.id);
                    GroupChanged?.Invoke(new ActionGroupEvent
                    {
                        groupId = completed.id, ownerHash = completed.ownerHash, ownerRef = completed.ownerRef,
                        kind = completed.wasCancelled ? ActionGroupSignalKind.Cancelled : ActionGroupSignalKind.Completed,
                    });
                }
            }
            finally { _settlingGroups = false; }
        }
    }
}
