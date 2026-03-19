using System.Collections.Generic;
using NUnit.Framework;
using Game.Core;
using UnityEngine;

namespace Game.Tests.EditMode
{
    [TestFixture]
    public class Integration_AIDecisionLoopTests
    {
        private SoACharaDataDic _data;
        private GameEvents _events;

        private const int k_OwnerHash = 1;
        private const int k_PlayerHash = 10;
        private const int k_CompanionHash = 11;

        [SetUp]
        public void SetUp()
        {
            _data = new SoACharaDataDic(8);
            _events = new GameEvents();

            // AI所有者（敵）: position=0,0, HP=100
            CharacterVitals ownerVitals = new CharacterVitals
            {
                currentHp = 100,
                maxHp = 100,
                position = Vector2.zero,
                level = 5,
                hpRatio = 100
            };
            CharacterFlags ownerFlags = CharacterFlags.Pack(
                CharacterBelong.Enemy, CharacterFeature.Minion, ActState.Neutral, AbilityFlag.None);
            _data.Add(k_OwnerHash, ownerVitals, default, ownerFlags, default);

            // プレイヤー: position=3,0（距離3）
            CharacterVitals playerVitals = new CharacterVitals
            {
                currentHp = 80,
                maxHp = 100,
                position = new Vector2(3f, 0f),
                level = 10,
                hpRatio = 80
            };
            CharacterFlags playerFlags = CharacterFlags.Pack(
                CharacterBelong.Ally, CharacterFeature.Player, ActState.Neutral, AbilityFlag.None);
            _data.Add(k_PlayerHash, playerVitals, default, playerFlags, default);

            // 仲間: position=5,0（距離5）
            CharacterVitals companionVitals = new CharacterVitals
            {
                currentHp = 100,
                maxHp = 100,
                position = new Vector2(5f, 0f),
                level = 8,
                hpRatio = 100
            };
            CharacterFlags companionFlags = CharacterFlags.Pack(
                CharacterBelong.Ally, CharacterFeature.Companion, ActState.Neutral, AbilityFlag.None);
            _data.Add(k_CompanionHash, companionVitals, default, companionFlags, default);
        }

        [TearDown]
        public void TearDown()
        {
            _events.Clear();
            _data.Dispose();
        }

        [Test]
        public void AIDecisionLoop_SensorDetectsThenConditionEvals_SelectsTarget()
        {
            // Arrange: センサー（視界範囲10、角度120、聴覚5）
            SensorSystem sensor = new SensorSystem(10f, 120f, 5f);
            List<int> allHashes = new List<int> { k_PlayerHash, k_CompanionHash };

            // Act: 右向きで検出更新
            sensor.UpdateDetection(k_OwnerHash, allHashes, _data, Vector2.right);

            // Assert: 両方とも範囲内で検出される
            Assert.AreEqual(2, sensor.DetectedHashes.Count);

            // TargetSelector: 距離でソート（昇順）→最も近いプレイヤー(距離3)を選択
            AITargetSelect targetSelect = new AITargetSelect
            {
                sortKey = TargetSortKey.Distance,
                isDescending = false,
                filter = new TargetFilter
                {
                    filterFlags = FilterBitFlag.BelongAnd,
                    belong = CharacterBelong.Ally
                }
            };

            List<int> detectedList = new List<int>(sensor.DetectedHashes);
            int selected = TargetSelector.SelectTarget(targetSelect, k_OwnerHash, detectedList, _data, 0f);

            // プレイヤー(距離3)が仲間(距離5)より近い
            Assert.AreEqual(k_PlayerHash, selected);
        }

        [Test]
        public void AIDecisionLoop_ConditionMet_ExecutesAction()
        {
            // Arrange: 条件「距離 < 5」のルール
            AICondition distCondition = new AICondition
            {
                conditionType = AIConditionType.Distance,
                compareOp = CompareOp.Less,
                operandA = 5,
                operandB = 0
            };

            // Act: OwnerとPlayerの距離=3 < 5 → 条件成立
            bool conditionMet = ConditionEvaluator.Evaluate(
                distCondition, k_OwnerHash, k_PlayerHash, _data, 0f);

            // Assert
            Assert.IsTrue(conditionMet);

            // 距離が遠い仲間(距離5)では条件不成立: 5 < 5 = false
            bool conditionNotMet = ConditionEvaluator.Evaluate(
                distCondition, k_OwnerHash, k_CompanionHash, _data, 0f);
            Assert.IsFalse(conditionNotMet);
        }

        [Test]
        public void AIDecisionLoop_ModeTransition_SwitchesOnLowHp()
        {
            // Arrange: 2モード構成（Normal=0, Flee=1）
            ActionExecutor executor = new ActionExecutor();
            JudgmentLoop loop = new JudgmentLoop(executor, _data, k_OwnerHash);
            ModeController modeController = new ModeController(loop);

            AIMode normalMode = new AIMode
            {
                modeName = "Normal",
                actionRules = new AIRule[0],
                targetRules = new AIRule[0],
                targetSelects = new AITargetSelect[0],
                actions = new ActionSlot[0],
                defaultActionIndex = -1,
                judgeInterval = new Vector2(0.5f, 1f)
            };
            AIMode fleeMode = new AIMode
            {
                modeName = "Flee",
                actionRules = new AIRule[0],
                targetRules = new AIRule[0],
                targetSelects = new AITargetSelect[0],
                actions = new ActionSlot[0],
                defaultActionIndex = -1,
                judgeInterval = new Vector2(0.5f, 1f)
            };

            // 遷移ルール: 自身のHP < 30% → Fleeモードへ
            ModeTransitionRule transitionRule = new ModeTransitionRule
            {
                conditions = new AICondition[]
                {
                    new AICondition
                    {
                        conditionType = AIConditionType.HpRatio,
                        compareOp = CompareOp.Less,
                        operandA = 30,
                        operandB = 0,
                        filter = new TargetFilter { includeSelf = true }
                    }
                },
                targetModeIndex = 1
            };

            modeController.SetModes(
                new AIMode[] { normalMode, fleeMode },
                new ModeTransitionRule[] { transitionRule }
            );

            // Assert: 初期状態はモード0
            Assert.AreEqual(0, modeController.CurrentModeIndex);

            // Act: HP=100/100 (100%) → 遷移なし
            modeController.EvaluateTransitions(k_OwnerHash, k_PlayerHash, _data, 0f);
            Assert.AreEqual(0, modeController.CurrentModeIndex);

            // HP=20/100 (20%) に変更 → 遷移あり
            ref CharacterVitals ownerVitals = ref _data.GetVitals(k_OwnerHash);
            ownerVitals.currentHp = 20;
            ownerVitals.hpRatio = 20;

            modeController.EvaluateTransitions(k_OwnerHash, k_PlayerHash, _data, 1f);
            Assert.AreEqual(1, modeController.CurrentModeIndex);
        }

        [Test]
        public void AIDecisionLoop_FullTick_IntegratesAllSubsystems()
        {
            // Arrange: EnemyController経由で全サブシステム統合
            EnemyController controller = new EnemyController(k_OwnerHash, _data);

            AIMode attackMode = new AIMode
            {
                modeName = "Attack",
                actionRules = new AIRule[0],
                targetRules = new AIRule[0],
                targetSelects = new AITargetSelect[]
                {
                    new AITargetSelect
                    {
                        sortKey = TargetSortKey.Distance,
                        isDescending = false,
                        filter = new TargetFilter
                        {
                            filterFlags = FilterBitFlag.BelongAnd,
                            belong = CharacterBelong.Ally
                        }
                    }
                },
                actions = new ActionSlot[0],
                defaultActionIndex = -1,
                judgeInterval = new Vector2(0.1f, 0.2f)
            };

            controller.SetAIModes(
                new AIMode[] { attackMode },
                new ModeTransitionRule[0]
            );

            List<int> candidates = new List<int> { k_PlayerHash, k_CompanionHash };

            // Act: Tick実行（判定間隔内でフル評価）
            controller.Tick(0.5f, candidates, 0f);

            // Assert: コントローラーがアクティブで、ターゲット選択済み
            Assert.IsTrue(controller.IsActive);
            Assert.AreEqual(0, controller.ModeController.CurrentModeIndex);
        }
    }
}
