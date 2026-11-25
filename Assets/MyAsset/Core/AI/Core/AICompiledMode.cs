using System;
using System.Collections.Generic;

namespace Game.Core
{
    internal struct AIRuleRange
    {
        public int conditionStart;
        public int conditionCount;
        public int actionIndex;
        public bool interactsWithGimmick;
    }

    internal struct AIProgramRanges
    {
        public int targetRuleStart;
        public int targetRuleCount;
        public int actionRuleStart;
        public int actionRuleCount;
        public int selectStart;
        public int selectCount;
        public int actionCount;
        public int defaultActionIndex;
        public bool defaultInteractsWithGimmick;
        public bool hasTargetRules;
        public bool hasActionRules;
    }

    internal struct AIDecisionResult
    {
        public int targetHash;
        public int actionIndex;
        public AISceneGimmick gimmick;
        public ActorRef owner;
        public ActorRef target;
        public long observationTick;
        public int revision;
    }

    internal sealed class AICompiledMode
    {
        public readonly AIRuleRange[] rules;
        public readonly AICondition[] conditions;
        public readonly AITargetSelect[] selects;
        public readonly ActionSlot[] actions;
        public readonly AIProgramRanges ranges;
        public readonly bool requiresTargetLineOfSight;
        public readonly bool requiresNearbyHostileProjectileCount;

        public AICompiledMode(AIMode mode)
        {
            List<AIRuleRange> flatRules = new List<AIRuleRange>();
            List<AICondition> flatConditions = new List<AICondition>();
            AppendRules(mode.targetRules, null, flatRules, flatConditions);
            int targetCount = flatRules.Count;
            AppendRules(mode.actionRules, mode.actions, flatRules, flatConditions);
            rules = flatRules.ToArray();
            conditions = flatConditions.ToArray();
            requiresTargetLineOfSight = Array.Exists(conditions, IsLineOfSightCondition);
            requiresNearbyHostileProjectileCount = Array.Exists(conditions, IsNearbyHostileProjectileCountCondition);
            selects = mode.targetSelects == null ? Array.Empty<AITargetSelect>() : (AITargetSelect[])mode.targetSelects.Clone();
            actions = mode.actions == null ? Array.Empty<ActionSlot>() : (ActionSlot[])mode.actions.Clone();
            int fallback = mode.defaultActionIndex;
            if (requiresTargetLineOfSight && mode.actionRules != null
                && IsOnlyLineOfSightGated(mode.actionRules, fallback))
            {
                fallback = -1;
            }
            ranges = new AIProgramRanges
            {
                targetRuleStart = 0,
                targetRuleCount = targetCount,
                actionRuleStart = targetCount,
                actionRuleCount = flatRules.Count - targetCount,
                selectCount = selects.Length,
                actionCount = actions.Length,
                defaultActionIndex = fallback,
                defaultInteractsWithGimmick = IsInteraction(mode.actions, fallback),
                hasTargetRules = mode.targetRules != null && mode.targetSelects != null,
                hasActionRules = mode.actionRules != null && mode.actions != null
            };
        }

        internal static bool IsLineOfSightCondition(AICondition condition) =>
            condition.conditionType == AIConditionType.TargetLineOfSight;

        internal static bool IsNearbyHostileProjectileCountCondition(AICondition condition) =>
            condition.conditionType == AIConditionType.NearbyHostileProjectileCount;

        private static bool IsOnlyLineOfSightGated(AIRule[] rules, int actionIndex)
        {
            bool hasMatchingRule = false;
            foreach (AIRule rule in rules)
            {
                if (rule.actionIndex != actionIndex)
                {
                    continue;
                }
                if (rule.conditions == null || !Array.Exists(rule.conditions, IsLineOfSightCondition))
                {
                    return false;
                }
                hasMatchingRule = true;
            }
            return hasMatchingRule;
        }

        private static bool IsInteraction(ActionSlot[] actions, int index)
        {
            return actions != null && index >= 0 && index < actions.Length
                && actions[index].execType == ActionExecType.Instant && actions[index].paramId == (int)InstantAction.InteractObject;
        }

        private static void AppendRules(AIRule[] source, ActionSlot[] actions,
            List<AIRuleRange> rules, List<AICondition> conditions)
        {
            if (source == null)
            {
                return;
            }
            for (int i = 0; i < source.Length; i++)
            {
                AIRule rule = source[i];
                rules.Add(new AIRuleRange
                {
                    conditionStart = conditions.Count,
                    conditionCount = rule.conditions == null ? 0 : rule.conditions.Length,
                    interactsWithGimmick = IsInteraction(actions, rule.actionIndex),
                    actionIndex = rule.actionIndex
                });
                if (rule.conditions != null)
                {
                    conditions.AddRange(rule.conditions);
                }
            }
        }
    }

    internal struct AIManagedProgram : IAIProgram
    {
        public AICompiledMode mode;
        public AIProgramRanges Ranges => mode.ranges;
        public AIRuleRange GetRule(int index) => mode.rules[index];
        public AICondition GetCondition(int index) => mode.conditions[index];
        public AITargetSelect GetTargetSelect(int index) => mode.selects[index];
    }
}
