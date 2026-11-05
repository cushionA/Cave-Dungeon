using System;
using System.Collections.Generic;

namespace Game.Core
{
    internal struct AIRuleRange
    {
        public int conditionStart;
        public int conditionCount;
        public int actionIndex;
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
        public bool hasTargetRules;
        public bool hasActionRules;
    }

    internal struct AIDecisionResult
    {
        public int targetHash;
        public int actionIndex;
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

        public AICompiledMode(AIMode mode)
        {
            List<AIRuleRange> flatRules = new List<AIRuleRange>();
            List<AICondition> flatConditions = new List<AICondition>();
            AppendRules(mode.targetRules, flatRules, flatConditions);
            int targetCount = flatRules.Count;
            AppendRules(mode.actionRules, flatRules, flatConditions);
            rules = flatRules.ToArray();
            conditions = flatConditions.ToArray();
            selects = mode.targetSelects == null ? Array.Empty<AITargetSelect>() : (AITargetSelect[])mode.targetSelects.Clone();
            actions = mode.actions == null ? Array.Empty<ActionSlot>() : (ActionSlot[])mode.actions.Clone();
            ranges = new AIProgramRanges
            {
                targetRuleStart = 0,
                targetRuleCount = targetCount,
                actionRuleStart = targetCount,
                actionRuleCount = flatRules.Count - targetCount,
                selectCount = selects.Length,
                actionCount = actions.Length,
                defaultActionIndex = mode.defaultActionIndex,
                hasTargetRules = mode.targetRules != null && mode.targetSelects != null,
                hasActionRules = mode.actionRules != null && mode.actions != null
            };
        }

        private static void AppendRules(AIRule[] source, List<AIRuleRange> rules, List<AICondition> conditions)
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
