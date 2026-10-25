using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Game.Core
{
    public enum StatusEffectApplicationMode : byte
    {
        Accumulate,
        Immediate,
    }

    public enum StatusEffectTarget : byte
    {
        HitTarget,
        Source,
    }

    public enum StatusEffectGuardRule : byte
    {
        Block,
        Ignore,
    }

    [Serializable]
    public struct StatusEffectApplication
    {
        public StatusEffectDefinition definition;
        public StatusEffectApplicationMode mode;
        public StatusEffectTarget target;
        public StatusEffectGuardRule guardRule;
        [ShowIf(nameof(mode), StatusEffectApplicationMode.Accumulate)]
        [Min(0f)] public float accumulateValue;
    }

    [Serializable]
    public struct StatusEffectApplicationData
    {
        public StatusEffectInfo info;
        public StatusEffectApplicationMode mode;
        public StatusEffectTarget target;
        public StatusEffectGuardRule guardRule;
    }

    public interface IStatusEffectWriter
    {
        bool Apply(int hash, StatusEffectInfo info, int sourceHash = 0);
        bool Accumulate(int hash, StatusEffectInfo info, float statusCut, int sourceHash = 0);
    }

    public static class StatusEffectApplicationLogic
    {
        public static StatusEffectApplicationData[] Resolve(StatusEffectApplication[] applications)
        {
            if (applications == null || applications.Length == 0)
            {
                return Array.Empty<StatusEffectApplicationData>();
            }

            StatusEffectApplicationData[] resolved = new StatusEffectApplicationData[applications.Length];
            for (int i = 0; i < applications.Length; i++)
            {
                StatusEffectApplication application = applications[i];
                if (application.definition == null)
                {
                    throw new ArgumentException($"Effect application {i} has no definition.", nameof(applications));
                }
                StatusEffectInfo info = application.definition.CreateInfo(application.accumulateValue);
                if (!CharacterStatusEffects.IsValidId(info.effect) || !IsPositiveFinite(info.duration)
                    || !Enum.IsDefined(typeof(StatusEffectApplicationMode), application.mode)
                    || !Enum.IsDefined(typeof(StatusEffectTarget), application.target)
                    || !Enum.IsDefined(typeof(StatusEffectGuardRule), application.guardRule)
                    || (application.mode == StatusEffectApplicationMode.Accumulate && !IsPositiveFinite(info.accumulateValue))
                    || !IsNonNegativeFinite(info.tickDamage) || !IsNonNegativeFinite(info.tickInterval)
                    || (info.tickDamage > 0f && info.tickInterval <= 0f)
                    || !IsNonNegativeFinite(info.modifier) || info.modifier > 1f)
                {
                    throw new ArgumentException($"Effect application {i} has invalid settings.", nameof(applications));
                }
                resolved[i] = new StatusEffectApplicationData
                {
                    info = info, mode = application.mode, target = application.target,
                    guardRule = application.guardRule,
                };
            }
            return resolved;
        }

        public static StatusEffectId Apply(StatusEffectApplicationData[] applications,
            int sourceHash, int hitTargetHash, GuardResult guardResult, float statusCut, IStatusEffectWriter writer,
            StatusEffectApplicationData[] equipmentApplications = null)
        {
            StatusEffectId first = ApplyGroup(applications, sourceHash, hitTargetHash, guardResult, statusCut, writer);
            StatusEffectId equipmentFirst = ApplyGroup(equipmentApplications, sourceHash, hitTargetHash,
                guardResult, statusCut, writer);
            return first != StatusEffectId.None ? first : equipmentFirst;
        }

        private static StatusEffectId ApplyGroup(StatusEffectApplicationData[] applications,
            int sourceHash, int hitTargetHash, GuardResult guardResult, float statusCut, IStatusEffectWriter writer)
        {
            StatusEffectId firstApplied = StatusEffectId.None;
            if (applications == null)
            {
                return firstApplied;
            }
            bool guardSucceeded = GuardJudgmentLogic.IsGuardSucceeded(guardResult);
            for (int i = 0; i < applications.Length; i++)
            {
                StatusEffectApplicationData application = applications[i];
                if (guardSucceeded && application.guardRule == StatusEffectGuardRule.Block)
                {
                    continue;
                }
                bool targetsSource = application.target == StatusEffectTarget.Source;
                int targetHash = targetsSource ? sourceHash : hitTargetHash;
                bool applied = application.mode == StatusEffectApplicationMode.Immediate
                    ? writer.Apply(targetHash, application.info, sourceHash)
                    : writer.Accumulate(targetHash, application.info, targetsSource ? 0f : statusCut, sourceHash);
                if (applied && firstApplied == StatusEffectId.None)
                {
                    firstApplied = application.info.effect;
                }
            }
            return firstApplied;
        }

        private static bool IsPositiveFinite(float value) => value > 0f && !float.IsInfinity(value);
        private static bool IsNonNegativeFinite(float value) => value >= 0f && !float.IsInfinity(value);
    }
}
