using NUnit.Framework;
using UnityEngine;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class AIInfoConverterTests
    {
        [Test]
        public void ConvertModes_EmptyModes_ReturnsEmptyArray()
        {
            AIInfo info = ScriptableObject.CreateInstance<AIInfo>();
            info.modes = new CharacterModeData[0];
            info.actDataList = new ActData[0];

            AIMode[] result = AIInfoConverter.ConvertModes(info);

            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Length);

            Object.DestroyImmediate(info);
        }

        [Test]
        public void ConvertModes_SingleMode_SetsModeName()
        {
            AIInfo info = CreateSingleModeInfo(CharacterMode.Combat);

            AIMode[] result = AIInfoConverter.ConvertModes(info);

            Assert.AreEqual(1, result.Length);
            Assert.AreEqual("Combat", result[0].modeName);

            Object.DestroyImmediate(info);
        }

        [Test]
        public void ConvertModes_WithAvailableActs_MapsActionsCorrectly()
        {
            AIInfo info = ScriptableObject.CreateInstance<AIInfo>();
            info.actDataList = new ActData[]
            {
                new ActData
                {
                    actName = "Slash",
                    actType = ActType.Attack,
                    attackInfoIndex = 0,
                    weight = 70f,
                    coolTime = 1f,
                },
                new ActData
                {
                    actName = "Heal",
                    actType = ActType.Heal,
                    attackInfoIndex = -1,
                    weight = 30f,
                    coolTime = 5f,
                }
            };
            info.modes = new CharacterModeData[]
            {
                new CharacterModeData
                {
                    mode = CharacterMode.Combat,
                    detectionRange = 10f,
                    combatRange = 3f,
                    retreatHpThreshold = 0.2f,
                    availableActIndices = new int[] { 0, 1 },
                    transitions = new TransitionCondition[0],
                }
            };

            AIMode[] result = AIInfoConverter.ConvertModes(info);

            Assert.AreEqual(1, result.Length);
            Assert.AreEqual(2, result[0].actions.Length);
            Assert.AreEqual("Slash", result[0].actions[0].displayName);
            Assert.AreEqual(ActionExecType.Attack, result[0].actions[0].execType);
            Assert.AreEqual(0, result[0].actions[0].paramId);

            Object.DestroyImmediate(info);
        }

        [Test]
        public void ConvertModes_ActTypeMapping_MapsAllTypes()
        {
            AIInfo info = ScriptableObject.CreateInstance<AIInfo>();
            info.actDataList = new ActData[]
            {
                new ActData { actName = "A", actType = ActType.Attack, weight = 10f },
                new ActData { actName = "G", actType = ActType.Guard, weight = 10f },
                new ActData { actName = "D", actType = ActType.Dodge, weight = 10f },
                new ActData { actName = "H", actType = ActType.Heal, weight = 10f },
                new ActData { actName = "B", actType = ActType.Buff, weight = 10f },
                new ActData { actName = "W", actType = ActType.Wait, weight = 10f },
            };
            info.modes = new CharacterModeData[]
            {
                new CharacterModeData
                {
                    mode = CharacterMode.Combat,
                    availableActIndices = new int[] { 0, 1, 2, 3, 4, 5 },
                    transitions = new TransitionCondition[0],
                }
            };

            AIMode[] result = AIInfoConverter.ConvertModes(info);

            Assert.AreEqual(ActionExecType.Attack, result[0].actions[0].execType);
            Assert.AreEqual(ActionExecType.Sustained, result[0].actions[1].execType);  // Guard -> Sustained
            Assert.AreEqual(ActionExecType.Instant, result[0].actions[2].execType);    // Dodge -> Instant
            Assert.AreEqual(ActionExecType.Cast, result[0].actions[3].execType);       // Heal -> Cast
            Assert.AreEqual(ActionExecType.Broadcast, result[0].actions[4].execType);  // Buff -> Broadcast
            Assert.AreEqual(ActionExecType.Sustained, result[0].actions[5].execType);  // Wait -> Sustained

            Object.DestroyImmediate(info);
        }

        [Test]
        public void ConvertModes_ActionRules_GeneratedFromWeights()
        {
            AIInfo info = ScriptableObject.CreateInstance<AIInfo>();
            info.actDataList = new ActData[]
            {
                new ActData
                {
                    actName = "Slash",
                    actType = ActType.Attack,
                    weight = 80f,
                    triggerJudge = new TriggerJudgeData
                    {
                        condition = TriggerConditionType.EnemyInRange,
                        value = 5f,
                        comparison = ComparisonOperator.LessOrEqual,
                    },
                    actJudge = new ActJudgeData { requiredStamina = 10f },
                }
            };
            info.modes = new CharacterModeData[]
            {
                new CharacterModeData
                {
                    mode = CharacterMode.Combat,
                    availableActIndices = new int[] { 0 },
                    transitions = new TransitionCondition[0],
                }
            };

            AIMode[] result = AIInfoConverter.ConvertModes(info);

            Assert.IsNotNull(result[0].actionRules);
            Assert.AreEqual(1, result[0].actionRules.Length);
            Assert.AreEqual(0, result[0].actionRules[0].actionIndex);

            Object.DestroyImmediate(info);
        }

        [Test]
        public void ConvertModes_TriggerCondition_MapsToAICondition()
        {
            AIInfo info = ScriptableObject.CreateInstance<AIInfo>();
            info.actDataList = new ActData[]
            {
                new ActData
                {
                    actName = "RangedAttack",
                    actType = ActType.Attack,
                    weight = 50f,
                    triggerJudge = new TriggerJudgeData
                    {
                        condition = TriggerConditionType.EnemyInRange,
                        value = 8f,
                        comparison = ComparisonOperator.LessOrEqual,
                    },
                }
            };
            info.modes = new CharacterModeData[]
            {
                new CharacterModeData
                {
                    mode = CharacterMode.Combat,
                    availableActIndices = new int[] { 0 },
                    transitions = new TransitionCondition[0],
                }
            };

            AIMode[] result = AIInfoConverter.ConvertModes(info);

            AICondition cond = result[0].actionRules[0].conditions[0];
            Assert.AreEqual(AIConditionType.Distance, cond.conditionType);
            Assert.AreEqual(CompareOp.LessEqual, cond.compareOp);
            Assert.AreEqual(8, cond.operandA);

            Object.DestroyImmediate(info);
        }

        [Test]
        public void ConvertModes_ActJudge_MapsStaminaAndHpConditions()
        {
            AIInfo info = ScriptableObject.CreateInstance<AIInfo>();
            info.actDataList = new ActData[]
            {
                new ActData
                {
                    actName = "HeavyAttack",
                    actType = ActType.Attack,
                    weight = 50f,
                    triggerJudge = new TriggerJudgeData { condition = TriggerConditionType.Always },
                    actJudge = new ActJudgeData
                    {
                        requiredStamina = 20f,
                        minHpRatio = 0.3f,
                        maxHpRatio = 1f,
                    },
                }
            };
            info.modes = new CharacterModeData[]
            {
                new CharacterModeData
                {
                    mode = CharacterMode.Combat,
                    availableActIndices = new int[] { 0 },
                    transitions = new TransitionCondition[0],
                }
            };

            AIMode[] result = AIInfoConverter.ConvertModes(info);

            // Should have stamina condition and hp range condition
            AICondition[] conditions = result[0].actionRules[0].conditions;
            Assert.IsTrue(conditions.Length >= 2, "Should have at least stamina + hp conditions");

            bool hasStamina = false;
            bool hasHpRange = false;
            for (int i = 0; i < conditions.Length; i++)
            {
                if (conditions[i].conditionType == AIConditionType.StaminaRatio)
                {
                    hasStamina = true;
                    Assert.AreEqual(CompareOp.GreaterEqual, conditions[i].compareOp);
                }
                if (conditions[i].conditionType == AIConditionType.HpRatio &&
                    conditions[i].compareOp == CompareOp.InRange)
                {
                    hasHpRange = true;
                    Assert.AreEqual(30, conditions[i].operandA);  // 0.3 * 100
                    Assert.AreEqual(100, conditions[i].operandB); // 1.0 * 100
                }
            }

            Assert.IsTrue(hasStamina, "Should have stamina condition");
            Assert.IsTrue(hasHpRange, "Should have HP range condition");

            Object.DestroyImmediate(info);
        }

        [Test]
        public void ConvertTransitions_MapsCorrectly()
        {
            AIInfo info = ScriptableObject.CreateInstance<AIInfo>();
            info.actDataList = new ActData[0];
            info.modes = new CharacterModeData[]
            {
                new CharacterModeData
                {
                    mode = CharacterMode.Patrol,
                    transitions = new TransitionCondition[]
                    {
                        new TransitionCondition
                        {
                            targetMode = CharacterMode.Combat,
                            trigger = TriggerType.EnemyDetected,
                            threshold = 10f,
                        }
                    },
                    availableActIndices = new int[0],
                },
                new CharacterModeData
                {
                    mode = CharacterMode.Combat,
                    transitions = new TransitionCondition[]
                    {
                        new TransitionCondition
                        {
                            targetMode = CharacterMode.Retreat,
                            trigger = TriggerType.HpBelowThreshold,
                            threshold = 20f,
                        }
                    },
                    availableActIndices = new int[0],
                },
                new CharacterModeData
                {
                    mode = CharacterMode.Retreat,
                    transitions = new TransitionCondition[0],
                    availableActIndices = new int[0],
                }
            };

            ModeTransitionRule[] rules = AIInfoConverter.ConvertTransitions(info);

            Assert.IsNotNull(rules);
            Assert.AreEqual(2, rules.Length);

            // First transition: Patrol -> Combat on EnemyDetected
            Assert.AreEqual(1, rules[0].targetModeIndex); // Combat is index 1
            Assert.AreEqual(AIConditionType.Distance, rules[0].conditions[0].conditionType);

            // Second transition: Combat -> Retreat on HpBelowThreshold
            Assert.AreEqual(2, rules[1].targetModeIndex); // Retreat is index 2
            Assert.AreEqual(AIConditionType.HpRatio, rules[1].conditions[0].conditionType);

            Object.DestroyImmediate(info);
        }

        [Test]
        public void ConvertModes_DefaultActionIndex_IsSet()
        {
            AIInfo info = ScriptableObject.CreateInstance<AIInfo>();
            info.actDataList = new ActData[]
            {
                new ActData { actName = "Wait", actType = ActType.Wait, weight = 10f },
                new ActData { actName = "Attack", actType = ActType.Attack, weight = 90f },
            };
            info.modes = new CharacterModeData[]
            {
                new CharacterModeData
                {
                    mode = CharacterMode.Combat,
                    availableActIndices = new int[] { 0, 1 },
                    transitions = new TransitionCondition[0],
                }
            };

            AIMode[] result = AIInfoConverter.ConvertModes(info);

            // Default action should be Wait (lowest weight non-attack) mapped to index 0
            Assert.IsTrue(result[0].defaultActionIndex >= 0);

            Object.DestroyImmediate(info);
        }

        [Test]
        public void ConvertModes_JudgeInterval_UsesReasonableDefaults()
        {
            AIInfo info = CreateSingleModeInfo(CharacterMode.Combat);

            AIMode[] result = AIInfoConverter.ConvertModes(info);

            // Should have positive judge intervals
            Assert.IsTrue(result[0].judgeInterval.x > 0f);
            Assert.IsTrue(result[0].judgeInterval.y > 0f);

            Object.DestroyImmediate(info);
        }

        [Test]
        public void ConvertModes_OutOfRangeActIndex_IsSkipped()
        {
            AIInfo info = ScriptableObject.CreateInstance<AIInfo>();
            info.actDataList = new ActData[]
            {
                new ActData { actName = "Attack", actType = ActType.Attack, weight = 50f },
            };
            info.modes = new CharacterModeData[]
            {
                new CharacterModeData
                {
                    mode = CharacterMode.Combat,
                    availableActIndices = new int[] { 0, 99 }, // 99 is out of range
                    transitions = new TransitionCondition[0],
                }
            };

            AIMode[] result = AIInfoConverter.ConvertModes(info);

            Assert.AreEqual(1, result[0].actions.Length);
            Assert.AreEqual("Attack", result[0].actions[0].displayName);

            Object.DestroyImmediate(info);
        }

        [Test]
        public void ConvertModes_NullAIInfo_ReturnsEmptyArray()
        {
            AIMode[] result = AIInfoConverter.ConvertModes(null);

            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Length);
        }

        [Test]
        public void ConvertTransitions_NullAIInfo_ReturnsEmptyArray()
        {
            ModeTransitionRule[] result = AIInfoConverter.ConvertTransitions(null);

            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Length);
        }

        [Test]
        public void ConvertModes_TargetRules_GeneratedFromDetectionRange()
        {
            AIInfo info = ScriptableObject.CreateInstance<AIInfo>();
            info.actDataList = new ActData[]
            {
                new ActData { actName = "Attack", actType = ActType.Attack, weight = 50f },
            };
            info.modes = new CharacterModeData[]
            {
                new CharacterModeData
                {
                    mode = CharacterMode.Combat,
                    detectionRange = 12f,
                    combatRange = 4f,
                    availableActIndices = new int[] { 0 },
                    transitions = new TransitionCondition[0],
                }
            };

            AIMode[] result = AIInfoConverter.ConvertModes(info);

            Assert.IsNotNull(result[0].targetRules);
            Assert.IsTrue(result[0].targetRules.Length > 0, "Should generate target rules from detection range");
            Assert.IsNotNull(result[0].targetSelects);
            Assert.IsTrue(result[0].targetSelects.Length > 0, "Should generate target selects");

            Object.DestroyImmediate(info);
        }

        // --- Helper ---

        private AIInfo CreateSingleModeInfo(CharacterMode mode)
        {
            AIInfo info = ScriptableObject.CreateInstance<AIInfo>();
            info.actDataList = new ActData[0];
            info.modes = new CharacterModeData[]
            {
                new CharacterModeData
                {
                    mode = mode,
                    detectionRange = 10f,
                    combatRange = 3f,
                    availableActIndices = new int[0],
                    transitions = new TransitionCondition[0],
                }
            };
            return info;
        }
    }
}
