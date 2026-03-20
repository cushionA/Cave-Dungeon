using System.Collections.Generic;
using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// AIInfo ScriptableObject (旧設計) から
    /// ランタイムAI構造体 (AIMode[], ModeTransitionRule[]) への変換アダプター。
    /// 旧フィールド (CharacterModeData[], ActData[]) を新設計に橋渡しする。
    /// </summary>
    public static class AIInfoConverter
    {
        private const float k_DefaultTargetJudgeInterval = 0.5f;
        private const float k_DefaultActionJudgeInterval = 0.3f;

        /// <summary>
        /// AIInfo の modes と actDataList を AIMode[] に変換する。
        /// </summary>
        public static AIMode[] ConvertModes(AIInfo info)
        {
            if (info == null || info.modes == null || info.modes.Length == 0)
            {
                return new AIMode[0];
            }

            AIMode[] result = new AIMode[info.modes.Length];
            for (int i = 0; i < info.modes.Length; i++)
            {
                result[i] = ConvertSingleMode(info.modes[i], info.actDataList);
            }
            return result;
        }

        /// <summary>
        /// AIInfo の modes 内の TransitionCondition[] を ModeTransitionRule[] に変換する。
        /// モードのインデックスは modes 配列内の CharacterMode enum 値で解決する。
        /// </summary>
        public static ModeTransitionRule[] ConvertTransitions(AIInfo info)
        {
            if (info == null || info.modes == null || info.modes.Length == 0)
            {
                return new ModeTransitionRule[0];
            }

            // Build mode enum -> index map
            Dictionary<CharacterMode, int> modeIndexMap = new Dictionary<CharacterMode, int>();
            for (int i = 0; i < info.modes.Length; i++)
            {
                modeIndexMap[info.modes[i].mode] = i;
            }

            List<ModeTransitionRule> rules = new List<ModeTransitionRule>();
            for (int i = 0; i < info.modes.Length; i++)
            {
                CharacterModeData modeData = info.modes[i];
                if (modeData.transitions == null)
                {
                    continue;
                }

                for (int j = 0; j < modeData.transitions.Length; j++)
                {
                    TransitionCondition tc = modeData.transitions[j];
                    if (!modeIndexMap.TryGetValue(tc.targetMode, out int targetIdx))
                    {
                        continue;
                    }

                    ModeTransitionRule rule = new ModeTransitionRule
                    {
                        targetModeIndex = targetIdx,
                        conditions = new AICondition[] { ConvertTriggerToCondition(tc) },
                    };
                    rules.Add(rule);
                }
            }

            return rules.ToArray();
        }

        private static AIMode ConvertSingleMode(CharacterModeData modeData, ActData[] actDataList)
        {
            // Collect valid actions
            List<ActionSlot> actions = new List<ActionSlot>();
            List<int> validOriginalIndices = new List<int>();

            if (modeData.availableActIndices != null && actDataList != null)
            {
                for (int i = 0; i < modeData.availableActIndices.Length; i++)
                {
                    int idx = modeData.availableActIndices[i];
                    if (idx >= 0 && idx < actDataList.Length)
                    {
                        actions.Add(ConvertActToSlot(actDataList[idx]));
                        validOriginalIndices.Add(idx);
                    }
                }
            }

            // Build action rules from act data weights and conditions
            List<AIRule> actionRules = new List<AIRule>();
            for (int i = 0; i < validOriginalIndices.Count; i++)
            {
                ActData act = actDataList[validOriginalIndices[i]];
                List<AICondition> conditions = BuildActConditions(act);
                AIRule rule = new AIRule
                {
                    conditions = conditions.Count > 0 ? conditions.ToArray() : new AICondition[0],
                    actionIndex = i,
                    probability = (byte)Mathf.Clamp(Mathf.RoundToInt(act.weight), 0, 255),
                };
                actionRules.Add(rule);
            }

            // Sort by weight descending (higher priority first)
            actionRules.Sort((a, b) => b.probability.CompareTo(a.probability));

            // Build target rules from detection/combat range
            List<AIRule> targetRules = new List<AIRule>();
            List<AITargetSelect> targetSelects = new List<AITargetSelect>();

            if (modeData.detectionRange > 0f || modeData.combatRange > 0f)
            {
                // Primary target select: nearest enemy in detection range
                AITargetSelect nearestEnemy = new AITargetSelect
                {
                    sortKey = TargetSortKey.Distance,
                    isDescending = false,
                    filter = new TargetFilter
                    {
                        belong = CharacterBelong.Enemy,
                        distanceRange = new Vector2(0f, modeData.detectionRange > 0f ? modeData.detectionRange : modeData.combatRange),
                        includeSelf = false,
                    },
                };
                targetSelects.Add(nearestEnemy);

                AIRule targetRule = new AIRule
                {
                    conditions = new AICondition[0], // Always evaluate
                    actionIndex = 0,
                    probability = 100,
                };
                targetRules.Add(targetRule);
            }

            // Default action: find Wait type or lowest weight action
            int defaultIdx = FindDefaultActionIndex(actions);

            return new AIMode
            {
                modeName = modeData.mode.ToString(),
                targetRules = targetRules.ToArray(),
                actionRules = actionRules.ToArray(),
                targetSelects = targetSelects.ToArray(),
                actions = actions.ToArray(),
                defaultActionIndex = defaultIdx,
                judgeInterval = new Vector2(k_DefaultTargetJudgeInterval, k_DefaultActionJudgeInterval),
            };
        }

        private static ActionSlot ConvertActToSlot(ActData act)
        {
            return new ActionSlot
            {
                execType = MapActType(act.actType),
                paramId = act.attackInfoIndex,
                paramValue = 0f,
                displayName = act.actName,
            };
        }

        private static ActionExecType MapActType(ActType actType)
        {
            switch (actType)
            {
                case ActType.Attack:      return ActionExecType.Attack;
                case ActType.Guard:       return ActionExecType.Sustained;
                case ActType.Dodge:       return ActionExecType.Instant;
                case ActType.Heal:        return ActionExecType.Cast;
                case ActType.Buff:        return ActionExecType.Broadcast;
                case ActType.Wait:        return ActionExecType.Sustained;
                case ActType.Flee:        return ActionExecType.Instant;
                case ActType.Move:        return ActionExecType.Sustained;
                case ActType.FeintCancel: return ActionExecType.Instant;
                default:                  return ActionExecType.Attack;
            }
        }

        private static List<AICondition> BuildActConditions(ActData act)
        {
            List<AICondition> conditions = new List<AICondition>();

            // Convert TriggerJudge
            if (act.triggerJudge.condition != TriggerConditionType.Always)
            {
                AICondition triggerCond = ConvertTriggerJudge(act.triggerJudge);
                if (triggerCond.conditionType != AIConditionType.None)
                {
                    conditions.Add(triggerCond);
                }
            }

            // Convert ActJudge: stamina requirement
            if (act.actJudge.requiredStamina > 0f)
            {
                conditions.Add(new AICondition
                {
                    conditionType = AIConditionType.StaminaRatio,
                    compareOp = CompareOp.GreaterEqual,
                    operandA = Mathf.RoundToInt(act.actJudge.requiredStamina),
                    filter = new TargetFilter { includeSelf = true },
                });
            }

            // Convert ActJudge: MP requirement
            if (act.actJudge.requiredMp > 0f)
            {
                conditions.Add(new AICondition
                {
                    conditionType = AIConditionType.MpRatio,
                    compareOp = CompareOp.GreaterEqual,
                    operandA = Mathf.RoundToInt(act.actJudge.requiredMp),
                    filter = new TargetFilter { includeSelf = true },
                });
            }

            // Convert ActJudge: HP range
            if (act.actJudge.minHpRatio > 0f || (act.actJudge.maxHpRatio > 0f && act.actJudge.maxHpRatio < 1f))
            {
                int minHp = Mathf.RoundToInt(act.actJudge.minHpRatio * 100f);
                int maxHp = Mathf.RoundToInt(act.actJudge.maxHpRatio * 100f);
                if (maxHp == 0)
                {
                    maxHp = 100;
                }

                conditions.Add(new AICondition
                {
                    conditionType = AIConditionType.HpRatio,
                    compareOp = CompareOp.InRange,
                    operandA = minHp,
                    operandB = maxHp,
                    filter = new TargetFilter { includeSelf = true },
                });
            }

            return conditions;
        }

        private static AICondition ConvertTriggerJudge(TriggerJudgeData trigger)
        {
            switch (trigger.condition)
            {
                case TriggerConditionType.EnemyInRange:
                    return new AICondition
                    {
                        conditionType = AIConditionType.Distance,
                        compareOp = MapComparison(trigger.comparison),
                        operandA = Mathf.RoundToInt(trigger.value),
                        filter = new TargetFilter { includeSelf = false },
                    };

                case TriggerConditionType.HpBelow:
                    return new AICondition
                    {
                        conditionType = AIConditionType.HpRatio,
                        compareOp = CompareOp.Less,
                        operandA = Mathf.RoundToInt(trigger.value),
                        filter = new TargetFilter { includeSelf = true },
                    };

                case TriggerConditionType.MpAbove:
                    return new AICondition
                    {
                        conditionType = AIConditionType.MpRatio,
                        compareOp = CompareOp.GreaterEqual,
                        operandA = Mathf.RoundToInt(trigger.value),
                        filter = new TargetFilter { includeSelf = true },
                    };

                case TriggerConditionType.AllyHpBelow:
                    return new AICondition
                    {
                        conditionType = AIConditionType.HpRatio,
                        compareOp = CompareOp.Less,
                        operandA = Mathf.RoundToInt(trigger.value),
                        filter = new TargetFilter
                        {
                            includeSelf = false,
                            belong = CharacterBelong.Ally,
                        },
                    };

                default:
                    return default;
            }
        }

        private static AICondition ConvertTriggerToCondition(TransitionCondition tc)
        {
            switch (tc.trigger)
            {
                case TriggerType.EnemyDetected:
                    return new AICondition
                    {
                        conditionType = AIConditionType.Distance,
                        compareOp = CompareOp.LessEqual,
                        operandA = Mathf.RoundToInt(tc.threshold),
                        filter = new TargetFilter { includeSelf = false },
                    };

                case TriggerType.EnemyLost:
                    return new AICondition
                    {
                        conditionType = AIConditionType.Distance,
                        compareOp = CompareOp.Greater,
                        operandA = Mathf.RoundToInt(tc.threshold),
                        filter = new TargetFilter { includeSelf = false },
                    };

                case TriggerType.HpBelowThreshold:
                    return new AICondition
                    {
                        conditionType = AIConditionType.HpRatio,
                        compareOp = CompareOp.Less,
                        operandA = Mathf.RoundToInt(tc.threshold),
                        filter = new TargetFilter { includeSelf = true },
                    };

                case TriggerType.HpAboveThreshold:
                    return new AICondition
                    {
                        conditionType = AIConditionType.HpRatio,
                        compareOp = CompareOp.GreaterEqual,
                        operandA = Mathf.RoundToInt(tc.threshold),
                        filter = new TargetFilter { includeSelf = true },
                    };

                case TriggerType.AllyInDanger:
                    return new AICondition
                    {
                        conditionType = AIConditionType.HpRatio,
                        compareOp = CompareOp.Less,
                        operandA = Mathf.RoundToInt(tc.threshold),
                        filter = new TargetFilter
                        {
                            includeSelf = false,
                            belong = CharacterBelong.Ally,
                        },
                    };

                default:
                    return default;
            }
        }

        private static CompareOp MapComparison(ComparisonOperator op)
        {
            switch (op)
            {
                case ComparisonOperator.LessThan:       return CompareOp.Less;
                case ComparisonOperator.LessOrEqual:    return CompareOp.LessEqual;
                case ComparisonOperator.Equal:          return CompareOp.Equal;
                case ComparisonOperator.GreaterOrEqual: return CompareOp.GreaterEqual;
                case ComparisonOperator.GreaterThan:    return CompareOp.Greater;
                default:                                return CompareOp.Less;
            }
        }

        private static int FindDefaultActionIndex(List<ActionSlot> actions)
        {
            // Prefer Wait-type (Sustained with no paramId) as default
            int lowestWeightIdx = -1;
            for (int i = 0; i < actions.Count; i++)
            {
                if (actions[i].execType == ActionExecType.Sustained)
                {
                    return i;
                }
                if (lowestWeightIdx < 0)
                {
                    lowestWeightIdx = i;
                }
            }
            return lowestWeightIdx >= 0 ? lowestWeightIdx : (actions.Count > 0 ? 0 : -1);
        }
    }
}
