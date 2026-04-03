using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class AICore_JudgmentLoopTests
    {
        private SoACharaDataDic _data;
        private ActionExecutor _executor;
        private int _ownerHash;
        private int _targetHash;

        [SetUp]
        public void SetUp()
        {
            _data = new SoACharaDataDic();
            _ownerHash = 1;
            _targetHash = 10;
            _data.Add(_ownerHash, new CharacterVitals { position = Vector2.zero, currentHp = 100, maxHp = 100 }, default, CharacterFlags.Pack(CharacterBelong.Ally, 0, 0), default);
            _data.Add(_targetHash, new CharacterVitals { position = new Vector2(3f, 0f), currentHp = 50, maxHp = 100 }, default, CharacterFlags.Pack(CharacterBelong.Enemy, 0, 0), default);

            _executor = new ActionExecutor();
            _executor.Register(new AttackActionHandler());
            _executor.Register(new SustainedActionHandler());
        }

        [TearDown]
        public void TearDown()
        {
            _data.Dispose();
        }

        [Test]
        public void JudgmentLoop_EvaluateAction_MatchesFirstRule()
        {
            JudgmentLoop loop = new JudgmentLoop(_executor, _data, _ownerHash);
            AIMode mode = new AIMode
            {
                actionRules = new AIRule[]
                {
                    new AIRule
                    {
                        conditions = new AICondition[]
                        {
                            new AICondition { conditionType = AIConditionType.HpRatio, compareOp = CompareOp.Greater, operandA = 50, filter = new TargetFilter { includeSelf = true } }
                        },
                        actionIndex = 0
                    }
                },
                actions = new ActionSlot[]
                {
                    new ActionSlot { execType = ActionExecType.Attack, paramId = 1 }
                },
                defaultActionIndex = -1,
                judgeInterval = new Vector2(1f, 1f)
            };
            loop.SetMode(mode);

            loop.EvaluateAction(0f);

            Assert.IsTrue(_executor.IsExecuting);
        }

        [Test]
        public void JudgmentLoop_EvaluateAction_FallsToDefault()
        {
            JudgmentLoop loop = new JudgmentLoop(_executor, _data, _ownerHash);
            AIMode mode = new AIMode
            {
                actionRules = new AIRule[]
                {
                    new AIRule
                    {
                        conditions = new AICondition[]
                        {
                            new AICondition { conditionType = AIConditionType.Distance, compareOp = CompareOp.Less, operandA = 1 }
                        },
                        actionIndex = 0
                    }
                },
                actions = new ActionSlot[]
                {
                    new ActionSlot { execType = ActionExecType.Attack, paramId = 1 },
                    new ActionSlot { execType = ActionExecType.Sustained, paramId = 0, paramValue = 5f }
                },
                defaultActionIndex = 1,
                judgeInterval = new Vector2(1f, 1f)
            };
            loop.SetMode(mode);

            loop.EvaluateAction(0f);

            Assert.IsTrue(_executor.IsExecuting);
        }

        [Test]
        public void JudgmentLoop_EvaluateTarget_SelectsFromRules()
        {
            JudgmentLoop loop = new JudgmentLoop(_executor, _data, _ownerHash);
            AIMode mode = new AIMode
            {
                targetRules = new AIRule[]
                {
                    new AIRule
                    {
                        conditions = new AICondition[0],
                        actionIndex = 0
                    }
                },
                targetSelects = new AITargetSelect[]
                {
                    new AITargetSelect
                    {
                        sortKey = TargetSortKey.Distance,
                        isDescending = false,
                        filter = new TargetFilter { belong = CharacterBelong.Enemy }
                    }
                },
                actions = new ActionSlot[0],
                judgeInterval = new Vector2(1f, 1f)
            };
            loop.SetMode(mode);
            List<int> candidates = new List<int> { _targetHash };

            loop.EvaluateTarget(candidates, 0f);

            Assert.AreEqual(_targetHash, loop.CurrentTargetHash);
        }

        [Test]
        public void JudgmentLoop_Tick_RespectsJudgeInterval()
        {
            JudgmentLoop loop = new JudgmentLoop(_executor, _data, _ownerHash);
            AIMode mode = new AIMode
            {
                actionRules = new AIRule[0],
                actions = new ActionSlot[] { new ActionSlot { execType = ActionExecType.Attack } },
                defaultActionIndex = 0,
                judgeInterval = new Vector2(2f, 2f)
            };
            loop.SetMode(mode);
            List<int> candidates = new List<int>();

            // SetMode直後の初回Tickはアクション即実行される（_actionJudgeTimer=0f仕様）
            loop.Tick(0.01f, candidates, 0f);
            Assert.IsTrue(_executor.IsExecuting);

            // 実行完了後、2回目のTickはインターバル未到達なので再実行されない
            _executor.CancelCurrent();
            Assert.IsFalse(_executor.IsExecuting);

            loop.Tick(0.5f, candidates, 0.5f);
            Assert.IsFalse(_executor.IsExecuting);
        }

        [Test]
        public void JudgmentLoop_NullRules_DoesNotCrash()
        {
            JudgmentLoop loop = new JudgmentLoop(_executor, _data, _ownerHash);
            AIMode mode = new AIMode
            {
                judgeInterval = new Vector2(1f, 1f)
            };
            loop.SetMode(mode);

            Assert.DoesNotThrow(() => loop.EvaluateAction(0f));
            Assert.DoesNotThrow(() => loop.EvaluateTarget(new List<int>(), 0f));
        }

    }
}

