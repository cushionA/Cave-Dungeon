using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;
using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// 設計書05: AIConditionType未テスト5種 + 確率ゲーティング + EventFired + SelfActState
    /// </summary>
    public class AICore_ConditionEvaluatorExtendedTests
    {
        private SoACharaDataDic _data;
        private int _ownerHash;
        private int _ally1Hash;
        private int _ally2Hash;
        private int _enemyHash;

        [SetUp]
        public void SetUp()
        {
            _data = new SoACharaDataDic();
            _ownerHash = 100;
            _ally1Hash = 200;
            _ally2Hash = 300;
            _enemyHash = 400;

            CharacterVitals ownerV = new CharacterVitals
            {
                currentHp = 80, maxHp = 100, position = new Vector2(0f, 0f)
            };
            CharacterFlags ownerF = CharacterFlags.Pack(
                CharacterBelong.Ally, CharacterFeature.Player, AbilityFlag.None);

            CharacterVitals ally1V = new CharacterVitals
            {
                currentHp = 60, maxHp = 100, position = new Vector2(3f, 0f)
            };
            CharacterFlags ally1F = CharacterFlags.Pack(
                CharacterBelong.Ally, CharacterFeature.Companion, AbilityFlag.None);

            CharacterVitals ally2V = new CharacterVitals
            {
                currentHp = 40, maxHp = 100, position = new Vector2(20f, 0f) // 遠い
            };
            CharacterFlags ally2F = CharacterFlags.Pack(
                CharacterBelong.Ally, CharacterFeature.Companion, AbilityFlag.None);

            CharacterVitals enemyV = new CharacterVitals
            {
                currentHp = 90, maxHp = 100, position = new Vector2(5f, 0f)
            };
            CharacterFlags enemyF = CharacterFlags.Pack(
                CharacterBelong.Enemy, CharacterFeature.Minion, AbilityFlag.None);

            _data.Add(_ownerHash, ownerV, default, ownerF, default);
            _data.Add(_ally1Hash, ally1V, default, ally1F, default);
            _data.Add(_ally2Hash, ally2V, default, ally2F, default);
            _data.Add(_enemyHash, enemyV, default, enemyF, default);
        }

        [TearDown]
        public void TearDown()
        {
            _data.Dispose();
        }

        // --- NearbyFaction: 指定距離内の同陣営キャラ数 ---

        [Test]
        public void ConditionEvaluator_NearbyFaction_CountsAlliesWithinRange()
        {
            List<int> allHashes = new List<int> { _ownerHash, _ally1Hash, _ally2Hash, _enemyHash };
            List<int> allyHashes = new List<int> { _ownerHash, _ally1Hash, _ally2Hash };

            AICondition condition = new AICondition
            {
                conditionType = AIConditionType.NearbyFaction,
                compareOp = CompareOp.GreaterEqual,
                operandA = 5, // 距離閾値=5
                operandB = 0,
            };

            float value = ConditionEvaluator.GetConditionValue(
                condition, _ownerHash, _enemyHash, _data, 0f, null, allHashes, allyHashes);

            // ally1は距離3 (範囲内), ally2は距離20 (範囲外), selfは除外
            Assert.AreEqual(1f, value, "距離5以内の味方は1人(ally1)");
        }

        [Test]
        public void ConditionEvaluator_NearbyFaction_NoAlliesNearby_ReturnsZero()
        {
            List<int> allyHashes = new List<int> { _ownerHash, _ally2Hash }; // ally2は距離20

            AICondition condition = new AICondition
            {
                conditionType = AIConditionType.NearbyFaction,
                compareOp = CompareOp.GreaterEqual,
                operandA = 5,
                operandB = 0,
            };

            float value = ConditionEvaluator.GetConditionValue(
                condition, _ownerHash, _enemyHash, _data, 0f, null, allyHashes, allyHashes);

            Assert.AreEqual(0f, value, "距離5以内に味方がいない場合0");
        }

        // --- ProjectileNear: 現時点ではスタブ(0を返す) ---

        [Test]
        public void ConditionEvaluator_ProjectileNear_ReturnsZero_WhenNotImplemented()
        {
            AICondition condition = new AICondition
            {
                conditionType = AIConditionType.ProjectileNear,
                compareOp = CompareOp.Greater,
                operandA = 0,
            };

            float value = ConditionEvaluator.GetConditionValue(
                condition, _ownerHash, _enemyHash, _data, 0f);

            Assert.AreEqual(0f, value, "ProjectileNear: ランタイム依存のためスタブ値0");
        }

        // --- ObjectNearby: 現時点ではスタブ(0を返す) ---

        [Test]
        public void ConditionEvaluator_ObjectNearby_ReturnsZero_WhenNotImplemented()
        {
            AICondition condition = new AICondition
            {
                conditionType = AIConditionType.ObjectNearby,
                compareOp = CompareOp.Greater,
                operandA = 0,
            };

            float value = ConditionEvaluator.GetConditionValue(
                condition, _ownerHash, _enemyHash, _data, 0f);

            Assert.AreEqual(0f, value, "ObjectNearby: ランタイム依存のためスタブ値0");
        }

        // --- EventFired: BrainEventFlagsビット判定 ---

        [Test]
        public void ConditionEvaluator_EventFired_ReturnsTrueWhenFlagSet()
        {
            ref CharacterFlags flags = ref _data.GetFlags(_ownerHash);
            flags.BrainEventFlags = (byte)(1 << 0); // 大ダメージを与えた

            AICondition condition = new AICondition
            {
                conditionType = AIConditionType.EventFired,
                compareOp = CompareOp.Equal,
                operandA = (1 << 0), // BrainEventFlagType.大ダメージを与えた
                operandB = 0,
                filter = new TargetFilter { includeSelf = true },
            };

            float value = ConditionEvaluator.GetConditionValue(
                condition, _ownerHash, _enemyHash, _data, 0f);

            Assert.AreEqual(1f, value, "イベントフラグが立っていれば1");
        }

        [Test]
        public void ConditionEvaluator_EventFired_ReturnsFalseWhenFlagNotSet()
        {
            ref CharacterFlags flags = ref _data.GetFlags(_ownerHash);
            flags.BrainEventFlags = 0;

            AICondition condition = new AICondition
            {
                conditionType = AIConditionType.EventFired,
                compareOp = CompareOp.Equal,
                operandA = (1 << 2), // キャラを倒した
                operandB = 0,
            };

            float value = ConditionEvaluator.GetConditionValue(
                condition, _ownerHash, _enemyHash, _data, 0f);

            Assert.AreEqual(0f, value, "イベントフラグが立っていなければ0");
        }

        // --- SelfActState: 自分の行動状態 ---

        [Test]
        public void ConditionEvaluator_SelfActState_ReturnsCurrentState()
        {
            ref CharacterFlags flags = ref _data.GetFlags(_ownerHash);
            flags.ActState = ActState.Attacking;

            AICondition condition = new AICondition
            {
                conditionType = AIConditionType.SelfActState,
                compareOp = CompareOp.Equal,
                operandA = (int)ActState.Attacking,
                operandB = 0,
            };

            bool result = ConditionEvaluator.Evaluate(
                condition, _ownerHash, _enemyHash, _data, 0f);

            Assert.IsTrue(result, "Attacking状態で条件マッチ");
        }

        [Test]
        public void ConditionEvaluator_SelfActState_DoesNotMatch_WhenDifferentState()
        {
            ref CharacterFlags flags = ref _data.GetFlags(_ownerHash);
            flags.ActState = ActState.Neutral;

            AICondition condition = new AICondition
            {
                conditionType = AIConditionType.SelfActState,
                compareOp = CompareOp.Equal,
                operandA = (int)ActState.Guarding,
                operandB = 0,
            };

            bool result = ConditionEvaluator.Evaluate(
                condition, _ownerHash, _enemyHash, _data, 0f);

            Assert.IsFalse(result, "Neutral状態でGuarding条件はマッチしない");
        }

        // --- Compare拡張: InRange / HasFlag / HasAny ---

        [Test]
        public void ConditionEvaluator_Compare_InRange_ReturnsTrueWhenInBounds()
        {
            Assert.IsTrue(ConditionEvaluator.Compare(50f, CompareOp.InRange, 30, 70));
            Assert.IsTrue(ConditionEvaluator.Compare(30f, CompareOp.InRange, 30, 70), "境界含む");
            Assert.IsFalse(ConditionEvaluator.Compare(80f, CompareOp.InRange, 30, 70));
        }

        [Test]
        public void ConditionEvaluator_Compare_HasFlag_ChecksExactBits()
        {
            // HasFlag: (value & operandA) == operandA
            Assert.IsTrue(ConditionEvaluator.Compare(7f, CompareOp.HasFlag, 3, 0)); // 0b111 & 0b011 == 0b011
            Assert.IsFalse(ConditionEvaluator.Compare(5f, CompareOp.HasFlag, 3, 0)); // 0b101 & 0b011 != 0b011
        }

        [Test]
        public void ConditionEvaluator_Compare_HasAny_ChecksAnyBit()
        {
            // HasAny: (value & operandA) != 0
            Assert.IsTrue(ConditionEvaluator.Compare(5f, CompareOp.HasAny, 1, 0)); // 0b101 & 0b001 != 0
            Assert.IsFalse(ConditionEvaluator.Compare(4f, CompareOp.HasAny, 2, 0)); // 0b100 & 0b010 == 0
        }
    }
}
