using NUnit.Framework;
using UnityEngine;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class AICore_ConditionEvaluatorTests
    {
        private SoACharaDataDic _data;
        private int _ownerHash;
        private int _targetHash;

        [SetUp]
        public void SetUp()
        {
            _data = new SoACharaDataDic();
            _ownerHash = 100;
            _targetHash = 200;

            CharacterVitals ownerVitals = new CharacterVitals
            {
                currentHp = 80,
                maxHp = 100,
                currentMp = 30,
                maxMp = 100,
                currentStamina = 50f,
                maxStamina = 100f,
                currentArmor = 25f,
                maxArmor = 50f,
                position = new Vector2(0f, 0f)
            };
            CharacterVitals targetVitals = new CharacterVitals
            {
                currentHp = 40,
                maxHp = 200,
                currentMp = 10,
                maxMp = 50,
                currentStamina = 80f,
                maxStamina = 100f,
                currentArmor = 0f,
                maxArmor = 0f,
                position = new Vector2(5f, 0f)
            };

            _data.Add(_ownerHash, ownerVitals, default, default, default);
            _data.Add(_targetHash, targetVitals, default, default, default);
        }

        [TearDown]
        public void TearDown()
        {
            _data.Dispose();
        }

        // --- Compare ---

        [Test]
        public void ConditionEvaluator_Compare_LessThan_ReturnsTrueWhenValueBelow()
        {
            Assert.IsTrue(ConditionEvaluator.Compare(3f, CompareOp.Less, 5, 0));
            Assert.IsFalse(ConditionEvaluator.Compare(5f, CompareOp.Less, 5, 0));
            Assert.IsFalse(ConditionEvaluator.Compare(7f, CompareOp.Less, 5, 0));
        }

        [Test]
        public void ConditionEvaluator_Compare_LessEqual_ReturnsTrueWhenValueAtOrBelow()
        {
            Assert.IsTrue(ConditionEvaluator.Compare(3f, CompareOp.LessEqual, 5, 0));
            Assert.IsTrue(ConditionEvaluator.Compare(5f, CompareOp.LessEqual, 5, 0));
            Assert.IsFalse(ConditionEvaluator.Compare(7f, CompareOp.LessEqual, 5, 0));
        }

        [Test]
        public void ConditionEvaluator_Compare_GreaterEqual_ReturnsTrueWhenValueAtOrAbove()
        {
            Assert.IsTrue(ConditionEvaluator.Compare(5f, CompareOp.GreaterEqual, 5, 0));
            Assert.IsTrue(ConditionEvaluator.Compare(7f, CompareOp.GreaterEqual, 5, 0));
            Assert.IsFalse(ConditionEvaluator.Compare(3f, CompareOp.GreaterEqual, 5, 0));
        }

        [Test]
        public void ConditionEvaluator_Compare_Greater_ReturnsTrueWhenValueAbove()
        {
            Assert.IsTrue(ConditionEvaluator.Compare(7f, CompareOp.Greater, 5, 0));
            Assert.IsFalse(ConditionEvaluator.Compare(5f, CompareOp.Greater, 5, 0));
        }

        [Test]
        public void ConditionEvaluator_Compare_EqualAndNotEqual_WorkCorrectly()
        {
            Assert.IsTrue(ConditionEvaluator.Compare(5f, CompareOp.Equal, 5, 0));
            Assert.IsFalse(ConditionEvaluator.Compare(5.1f, CompareOp.Equal, 5, 0));
            Assert.IsTrue(ConditionEvaluator.Compare(3f, CompareOp.NotEqual, 5, 0));
            Assert.IsFalse(ConditionEvaluator.Compare(5f, CompareOp.NotEqual, 5, 0));
        }

        // --- HpRatio ---

        [Test]
        public void ConditionEvaluator_HpRatio_EvaluatesOwnerCorrectly()
        {
            // Owner HP: 80/100 = 80% (value=80)
            AICondition condition = new AICondition
            {
                conditionType = AIConditionType.HpRatio,
                compareOp = CompareOp.GreaterEqual,
                operandA = 50,
                filter = new TargetFilter { includeSelf = true }
            };

            bool result = ConditionEvaluator.Evaluate(condition, _ownerHash, _targetHash, _data, 0f);

            Assert.IsTrue(result);
        }

        [Test]
        public void ConditionEvaluator_HpRatio_EvaluatesTargetWhenTargetParamIsOne()
        {
            // Target HP: 40/200 = 20% (value=20)
            AICondition condition = new AICondition
            {
                conditionType = AIConditionType.HpRatio,
                compareOp = CompareOp.Less,
                operandA = 50
            };

            bool result = ConditionEvaluator.Evaluate(condition, _ownerHash, _targetHash, _data, 0f);

            Assert.IsTrue(result);
        }

        // --- MpRatio ---

        [Test]
        public void ConditionEvaluator_MpRatio_EvaluatesTargetCorrectly()
        {
            // Target MP: 10/50 = 20% (value=20)
            AICondition condition = new AICondition
            {
                conditionType = AIConditionType.MpRatio,
                compareOp = CompareOp.Less,
                operandA = 50
            };

            bool result = ConditionEvaluator.Evaluate(condition, _ownerHash, _targetHash, _data, 0f);

            Assert.IsTrue(result);
        }

        [Test]
        public void ConditionEvaluator_MpRatio_EvaluatesOwnerCorrectly()
        {
            // Owner MP: 30/100 = 30% (value=30)
            AICondition condition = new AICondition
            {
                conditionType = AIConditionType.MpRatio,
                compareOp = CompareOp.LessEqual,
                operandA = 30,
                filter = new TargetFilter { includeSelf = true }
            };

            bool result = ConditionEvaluator.Evaluate(condition, _ownerHash, _targetHash, _data, 0f);

            Assert.IsTrue(result);
        }

        // --- StaminaRatio ---

        [Test]
        public void ConditionEvaluator_StaminaRatio_EvaluatesCorrectly()
        {
            // Owner Stamina: 50/100 = 50% (value=50)
            AICondition condition = new AICondition
            {
                conditionType = AIConditionType.StaminaRatio,
                compareOp = CompareOp.Equal,
                operandA = 50,
                filter = new TargetFilter { includeSelf = true }
            };

            bool result = ConditionEvaluator.Evaluate(condition, _ownerHash, _targetHash, _data, 0f);

            Assert.IsTrue(result);
        }

        // --- ArmorRatio ---

        [Test]
        public void ConditionEvaluator_ArmorRatio_EvaluatesCorrectly()
        {
            // Owner Armor: 25/50 = 50% (value=50)
            AICondition condition = new AICondition
            {
                conditionType = AIConditionType.ArmorRatio,
                compareOp = CompareOp.GreaterEqual,
                operandA = 50,
                filter = new TargetFilter { includeSelf = true }
            };

            bool result = ConditionEvaluator.Evaluate(condition, _ownerHash, _targetHash, _data, 0f);

            Assert.IsTrue(result);
        }

        [Test]
        public void ConditionEvaluator_ArmorRatio_ReturnsZeroWhenMaxIsZero()
        {
            // Target Armor: 0/0 = 0% (value=0)
            AICondition condition = new AICondition
            {
                conditionType = AIConditionType.ArmorRatio,
                compareOp = CompareOp.Equal,
                operandA = 0
            };

            bool result = ConditionEvaluator.Evaluate(condition, _ownerHash, _targetHash, _data, 0f);

            Assert.IsTrue(result);
        }

        // --- Distance ---

        [Test]
        public void ConditionEvaluator_Distance_CalculatesCorrectly()
        {
            // Owner at (0,0), Target at (5,0) => distance = 5
            AICondition condition = new AICondition
            {
                conditionType = AIConditionType.Distance,
                compareOp = CompareOp.Less,
                operandA = 10
            };

            bool result = ConditionEvaluator.Evaluate(condition, _ownerHash, _targetHash, _data, 0f);

            Assert.IsTrue(result);
        }

        [Test]
        public void ConditionEvaluator_Distance_FailsWhenTooFar()
        {
            // Owner at (0,0), Target at (5,0) => distance = 5, threshold = 3
            AICondition condition = new AICondition
            {
                conditionType = AIConditionType.Distance,
                compareOp = CompareOp.Less,
                operandA = 3
            };

            bool result = ConditionEvaluator.Evaluate(condition, _ownerHash, _targetHash, _data, 0f);

            Assert.IsFalse(result);
        }

        // --- EvaluateAll ---

        [Test]
        public void ConditionEvaluator_EvaluateAll_EmptyConditions_ReturnsTrue()
        {
            bool resultNull = ConditionEvaluator.EvaluateAll(null, _ownerHash, _targetHash, _data, 0f);
            bool resultEmpty = ConditionEvaluator.EvaluateAll(
                new AICondition[0], _ownerHash, _targetHash, _data, 0f);

            Assert.IsTrue(resultNull);
            Assert.IsTrue(resultEmpty);
        }

        [Test]
        public void ConditionEvaluator_EvaluateAll_AllTrue_ReturnsTrue()
        {
            AICondition[] conditions = new AICondition[]
            {
                new AICondition
                {
                    conditionType = AIConditionType.HpRatio,
                    compareOp = CompareOp.Greater,
                    operandA = 50,
                    filter = new TargetFilter { includeSelf = true }
                },
                new AICondition
                {
                    conditionType = AIConditionType.Distance,
                    compareOp = CompareOp.Less,
                    operandA = 10
                }
            };

            bool result = ConditionEvaluator.EvaluateAll(conditions, _ownerHash, _targetHash, _data, 0f);

            Assert.IsTrue(result);
        }

        [Test]
        public void ConditionEvaluator_EvaluateAll_OneFails_ReturnsFalse()
        {
            // HP > 50 is true (80%), but Distance < 3 is false (5)
            AICondition[] conditions = new AICondition[]
            {
                new AICondition
                {
                    conditionType = AIConditionType.HpRatio,
                    compareOp = CompareOp.Greater,
                    operandA = 50,
                    filter = new TargetFilter { includeSelf = true }
                },
                new AICondition
                {
                    conditionType = AIConditionType.Distance,
                    compareOp = CompareOp.Less,
                    operandA = 3
                }
            };

            bool result = ConditionEvaluator.EvaluateAll(conditions, _ownerHash, _targetHash, _data, 0f);

            Assert.IsFalse(result);
        }

        // --- Edge cases ---

        [Test]
        public void ConditionEvaluator_InvalidHash_ReturnsZeroForRatio()
        {
            AICondition condition = new AICondition
            {
                conditionType = AIConditionType.HpRatio,
                compareOp = CompareOp.Equal,
                operandA = 0,
                filter = new TargetFilter { includeSelf = true }
            };

            float value = ConditionEvaluator.GetConditionValue(condition, 9999, _targetHash, _data, 0f);

            Assert.AreEqual(0f, value, 0.001f);
        }

        [Test]
        public void ConditionEvaluator_Distance_InvalidHash_ReturnsMaxValue()
        {
            AICondition condition = new AICondition
            {
                conditionType = AIConditionType.Distance
            };

            float value = ConditionEvaluator.GetConditionValue(condition, 9999, _targetHash, _data, 0f);

            Assert.AreEqual(float.MaxValue, value);
        }

        [Test]
        public void ConditionEvaluator_Count_ReturnsZeroWhenAllHashesIsNull()
        {
            AICondition condition = new AICondition
            {
                conditionType = AIConditionType.Count,
                compareOp = CompareOp.Equal,
                operandA = 0
            };

            bool result = ConditionEvaluator.Evaluate(condition, _ownerHash, _targetHash, _data, 0f);

            Assert.IsTrue(result);
        }

        [Test]
        public void ConditionEvaluator_Count_CountsMatchingHashes()
        {
            AICondition condition = new AICondition
            {
                conditionType = AIConditionType.Count,
                compareOp = CompareOp.GreaterEqual,
                operandA = 1
            };

            System.Collections.Generic.List<int> allHashes =
                new System.Collections.Generic.List<int> { _ownerHash, _targetHash };
            bool result = ConditionEvaluator.Evaluate(
                condition, _ownerHash, _targetHash, _data, 0f, null, allHashes);

            Assert.IsTrue(result);
        }

        [Test]
        public void ConditionEvaluator_DamageScore_ReturnsTargetParam()
        {
            AICondition condition = new AICondition
            {
                conditionType = AIConditionType.DamageScore,
                compareOp = CompareOp.Equal,
                operandA = 0
            };

            bool result = ConditionEvaluator.Evaluate(condition, _ownerHash, _targetHash, _data, 0f);

            Assert.IsTrue(result);
        }
    }
}
