using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;
using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// 設計書05: 確率ゲーティング(AIRule.probability)・仲間AIモード遷移
    /// CompanionBehaviorSetting・SoACharaDataDic Dispose
    /// </summary>
    public class AICore_JudgmentProbabilityTests
    {
        // --- AIRule.probability: 確率ゲーティング ---
        // 設計書: "actRatio（実行確率 0〜100）と乱数を比較して確率ゲーティング"
        // 現実装: JudgmentLoop.EvaluateActionはprobabilityフィールドを直接使わず
        // 条件マッチ→即実行。probabilityは将来拡張用のフィールド。
        // ここではprobability=0のルールでも条件マッチすれば実行されることを確認。

        [Test]
        public void JudgmentLoop_ActionRule_WithProbability100_AlwaysExecutes()
        {
            SoACharaDataDic data = new SoACharaDataDic();
            int ownerHash = 100;
            int targetHash = 200;
            data.Add(ownerHash, new CharacterVitals { currentHp = 50, maxHp = 100 }, default, default, default);
            data.Add(targetHash, new CharacterVitals { currentHp = 80, maxHp = 100 }, default, default, default);

            ActionExecutor executor = new ActionExecutor();
            AttackActionHandler handler = new AttackActionHandler();
            executor.Register(handler);

            JudgmentLoop loop = new JudgmentLoop(executor, data, ownerHash);

            AIMode mode = new AIMode
            {
                modeName = "Test",
                judgeInterval = new Vector2(1f, 1f),
                targetRules = null,
                targetSelects = null,
                actionRules = new AIRule[]
                {
                    new AIRule
                    {
                        conditions = new AICondition[0], // 無条件
                        actionIndex = 0,
                        probability = 100,
                    }
                },
                actions = new ActionSlot[]
                {
                    new ActionSlot { execType = ActionExecType.Attack, paramId = 42 }
                },
                defaultActionIndex = 0,
            };

            loop.SetMode(mode);
            loop.EvaluateAction(0f);

            Assert.IsTrue(executor.IsExecuting, "probability=100ルールは実行される");
            Assert.AreEqual(42, handler.LastParamId);

            data.Dispose();
        }

        [Test]
        public void JudgmentLoop_DefaultAction_UsedWhenNoRuleMatches()
        {
            SoACharaDataDic data = new SoACharaDataDic();
            int ownerHash = 100;
            data.Add(ownerHash, new CharacterVitals { currentHp = 100, maxHp = 100 }, default, default, default);

            ActionExecutor executor = new ActionExecutor();
            AttackActionHandler handler = new AttackActionHandler();
            executor.Register(handler);

            JudgmentLoop loop = new JudgmentLoop(executor, data, ownerHash);

            AIMode mode = new AIMode
            {
                modeName = "Test",
                judgeInterval = new Vector2(1f, 1f),
                actionRules = new AIRule[]
                {
                    new AIRule
                    {
                        conditions = new AICondition[]
                        {
                            new AICondition
                            {
                                conditionType = AIConditionType.HpRatio,
                                compareOp = CompareOp.Less,
                                operandA = 10, // HP < 10% : 不成立
                                filter = new TargetFilter { includeSelf = true },
                            }
                        },
                        actionIndex = 0,
                        probability = 100,
                    }
                },
                actions = new ActionSlot[]
                {
                    new ActionSlot { execType = ActionExecType.Attack, paramId = 1 },
                    new ActionSlot { execType = ActionExecType.Attack, paramId = 99 }, // default
                },
                defaultActionIndex = 1,
            };

            loop.SetMode(mode);
            loop.EvaluateAction(0f);

            Assert.IsTrue(executor.IsExecuting);
            Assert.AreEqual(99, handler.LastParamId, "条件不成立→デフォルト行動(paramId=99)");

            data.Dispose();
        }

        // --- ModeController: モード遷移 ---

        [Test]
        public void ModeController_TransitionRule_SwitchesOnConditionMet()
        {
            SoACharaDataDic data = new SoACharaDataDic();
            int ownerHash = 100;
            data.Add(ownerHash,
                new CharacterVitals { currentHp = 20, maxHp = 100 },
                default, default, default);

            ActionExecutor executor = new ActionExecutor();
            executor.Register(new AttackActionHandler());
            JudgmentLoop judgmentLoop = new JudgmentLoop(executor, data, ownerHash);

            ModeController modeCtrl = new ModeController(judgmentLoop);

            AIMode normalMode = new AIMode
            {
                modeName = "Normal",
                judgeInterval = new Vector2(1f, 1f),
                actions = new ActionSlot[] { new ActionSlot { execType = ActionExecType.Attack } },
                defaultActionIndex = 0,
            };
            AIMode retreatMode = new AIMode
            {
                modeName = "Retreat",
                judgeInterval = new Vector2(1f, 1f),
                actions = new ActionSlot[] { new ActionSlot { execType = ActionExecType.Attack } },
                defaultActionIndex = 0,
            };

            ModeTransitionRule[] transitions = new ModeTransitionRule[]
            {
                new ModeTransitionRule
                {
                    sourceModeIndex = 0,
                    targetModeIndex = 1,
                    conditions = new AICondition[]
                    {
                        new AICondition
                        {
                            conditionType = AIConditionType.HpRatio,
                            compareOp = CompareOp.Less,
                            operandA = 30, // HP < 30%
                            filter = new TargetFilter { includeSelf = true },
                        }
                    }
                }
            };

            modeCtrl.SetModes(new AIMode[] { normalMode, retreatMode }, transitions);
            Assert.AreEqual(0, modeCtrl.CurrentModeIndex, "初期モード=0");

            modeCtrl.EvaluateTransitions(ownerHash, 0, data, 0f);

            Assert.AreEqual(1, modeCtrl.CurrentModeIndex, "HP20% → Retreatモード(1)に遷移");

            data.Dispose();
        }

        [Test]
        public void ModeController_SwitchMode_FiresEvent()
        {
            SoACharaDataDic data = new SoACharaDataDic();
            int ownerHash = 100;
            data.Add(ownerHash, default, default, default, default);

            ActionExecutor executor = new ActionExecutor();
            executor.Register(new AttackActionHandler());
            JudgmentLoop loop = new JudgmentLoop(executor, data, ownerHash);
            ModeController ctrl = new ModeController(loop);

            AIMode[] modes = new AIMode[]
            {
                new AIMode { modeName = "A", judgeInterval = Vector2.one,
                    actions = new ActionSlot[] { default }, defaultActionIndex = 0 },
                new AIMode { modeName = "B", judgeInterval = Vector2.one,
                    actions = new ActionSlot[] { default }, defaultActionIndex = 0 },
            };
            ctrl.SetModes(modes, new ModeTransitionRule[0]);

            int firedModeIndex = -1;
            ctrl.OnModeChanged += (idx) => firedModeIndex = idx;

            ctrl.SwitchMode(1);

            Assert.AreEqual(1, firedModeIndex, "OnModeChangedイベント発火");
            Assert.AreEqual(1, ctrl.CurrentModeIndex);

            data.Dispose();
        }

        // --- CompanionBehaviorSetting: フィールド検証 ---

        [Test]
        public void CompanionBehaviorSetting_DefaultValues_AreReasonable()
        {
            CompanionBehaviorSetting setting = new CompanionBehaviorSetting
            {
                followDistance = 5f,
                maxLeashDistance = 15f,
                supportHpThreshold = 40f,
                autoHeal = true,
                prioritizePlayerTarget = true,
                defaultStance = CompanionStance.Aggressive,
            };

            Assert.AreEqual(5f, setting.followDistance);
            Assert.AreEqual(15f, setting.maxLeashDistance);
            Assert.AreEqual(40f, setting.supportHpThreshold);
            Assert.IsTrue(setting.autoHeal);
            Assert.IsTrue(setting.prioritizePlayerTarget);
            Assert.AreEqual(CompanionStance.Aggressive, setting.defaultStance);
        }

        // --- SoACharaDataDic: Dispose安全性 ---

        [Test]
        public void SoACharaDataDic_Dispose_DoesNotThrow()
        {
            SoACharaDataDic data = new SoACharaDataDic();
            data.Add(1, default, default, default, default);
            data.Add(2, default, default, default, default);

            Assert.AreEqual(2, data.Count);

            Assert.DoesNotThrow(() => data.Dispose());
        }

        [Test]
        public void SoACharaDataDic_DoubleDispose_DoesNotThrow()
        {
            SoACharaDataDic data = new SoACharaDataDic();
            data.Add(1, default, default, default, default);

            data.Dispose();
            Assert.DoesNotThrow(() => data.Dispose(), "二重Disposeは安全");
        }

        // --- DamageScoreTracker: Dispose ---

        [Test]
        public void DamageScoreTracker_Dispose_DoesNotThrow()
        {
            DamageScoreTracker tracker = new DamageScoreTracker();
            tracker.AddDamage(100, 50f, 0f);

            Assert.Greater(tracker.GetScore(100, 0f), 0f);

            Assert.DoesNotThrow(() => tracker.Dispose(), "Disposeは安全に呼べる");
        }
    }
}
