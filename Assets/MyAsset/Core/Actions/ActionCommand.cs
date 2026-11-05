using System;
using System.Collections.Generic;

namespace Game.Core
{
    public enum ActionCommandKind : byte
    {
        Unsupported,
        Attack,
        Cast,
        Dodge,
        Guard,
        Idle,
        MoveToTarget,
        Follow,
        Retreat,
        Flee,
    }

    public enum ActionVariant : byte
    {
        Default,
        Aerial,
        Diving,
    }

    public enum ActionDispatchStatus : byte
    {
        Rejected,
        Accepted,
        Unsupported,
        InvalidOwner,
        InvalidTarget,
        InvalidParameters,
        UnknownDefinition,
        AmbiguousDefinition,
        InvalidLegacyReference,
    }

    public struct AttackActionParameters
    {
        public string definitionId;
        public int legacyIndex;
        public float chargeMultiplier;
        public ActionVariant variant;
    }

    public struct SustainedActionParameters
    {
        public float duration;
    }

    /// <summary>実行入口の意味付き要求。旧ActionSlotの汎用パラメータは境界で一度だけ変換する。</summary>
    public struct ActionCommand
    {
        public ActionCommandKind kind;
        public AttackActionParameters attack;
        public SustainedActionParameters sustained;

        public static ActionCommand FromLegacySlot(in ActionSlot slot)
        {
            ActionCommand command = default;
            if (slot.execType == ActionExecType.Attack || slot.execType == ActionExecType.Cast)
            {
                command.kind = slot.execType == ActionExecType.Cast ? ActionCommandKind.Cast : ActionCommandKind.Attack;
                command.attack = new AttackActionParameters
                {
                    definitionId = slot.actionDefinitionId, legacyIndex = slot.paramId,
                    chargeMultiplier = slot.paramValue == 0f ? 1f : slot.paramValue,
                };
                return command;
            }
            if (slot.execType == ActionExecType.Instant)
            {
                command.kind = slot.paramId == (int)InstantAction.Dodge ? ActionCommandKind.Dodge : ActionCommandKind.Unsupported;
                return command;
            }
            if (slot.execType != ActionExecType.Sustained) { return command; }
            command.sustained.duration = slot.paramValue;
            switch ((SustainedAction)slot.paramId)
            {
                case SustainedAction.Guard: command.kind = ActionCommandKind.Guard; break;
                case SustainedAction.Idle: command.kind = ActionCommandKind.Idle; break;
                case SustainedAction.MoveToTarget: command.kind = ActionCommandKind.MoveToTarget; break;
                case SustainedAction.Follow: command.kind = ActionCommandKind.Follow; break;
                case SustainedAction.Retreat: command.kind = ActionCommandKind.Retreat; break;
                case SustainedAction.Flee: command.kind = ActionCommandKind.Flee; break;
            }
            return command;
        }
    }

    public readonly struct ActionDispatchResult
    {
        public ActionDispatchStatus Status { get; }
        public long InstanceId { get; }
        public long GroupId { get; }
        public ActorRef TargetRef { get; }
        public bool Accepted => Status == ActionDispatchStatus.Accepted;

        public ActionDispatchResult(ActionDispatchStatus status, long instanceId = 0, long groupId = 0, ActorRef targetRef = default)
        {
            Status = status;
            InstanceId = instanceId;
            GroupId = groupId;
            TargetRef = targetRef;
        }
    }

    /// <summary>永続参照が指定された要求は配列位置へ代替しない。旧保存のみindex解決を許可する。</summary>
    public static class ActionDefinitionReference
    {
        public static ActionDispatchStatus Resolve(string persistentId, int legacyIndex,
            IReadOnlyList<string> definitionIds, out int index)
        {
            index = -1;
            if (string.IsNullOrEmpty(persistentId))
            {
                if (definitionIds == null || legacyIndex < 0 || legacyIndex >= definitionIds.Count)
                {
                    return ActionDispatchStatus.InvalidLegacyReference;
                }
                index = legacyIndex;
                return ActionDispatchStatus.Accepted;
            }
            if (definitionIds != null)
            {
                for (int i = 0; i < definitionIds.Count; i++)
                {
                    if (!string.Equals(persistentId, definitionIds[i], StringComparison.Ordinal)) { continue; }
                    if (index >= 0) { index = -1; return ActionDispatchStatus.AmbiguousDefinition; }
                    index = i;
                }
            }
            return index >= 0 ? ActionDispatchStatus.Accepted : ActionDispatchStatus.UnknownDefinition;
        }
    }

    public interface IActionCommandBackend : IActionExecutionBackend
    {
        ActionDispatchResult ExecuteCommand(int ownerHash, int targetHash, in ActionCommand command);
        bool Supports(ActionCommandKind kind);
    }
}
