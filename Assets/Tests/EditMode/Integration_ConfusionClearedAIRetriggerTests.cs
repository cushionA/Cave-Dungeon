using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// 混乱解除時に AI Controller が <see cref="JudgmentLoop.ForceEvaluate"/> を呼び、
    /// ターゲット/行動を即時再評価することの結合テスト。
    /// Controller は GameEvents.OnConfusionCleared を購読し、自ハッシュに一致した
    /// イベントに対してのみ ForceEvaluate を発火する。
    /// </summary>
    public class Integration_ConfusionClearedAIRetriggerTests
    {
        private SoACharaDataDic _data;
        private GameEvents _events;

        private const int k_EnemyHash = 101;
        private const int k_CompanionHash = 201;
        private const int k_OtherHash = 901;
        private const int k_TargetHash = 501;

        [SetUp]
        public void SetUp()
        {
            _data = new SoACharaDataDic();
            _events = new GameEvents();

            _data.Add(k_EnemyHash,
                new CharacterVitals { position = Vector2.zero, currentHp = 100, maxHp = 100 },
                default,
                CharacterFlags.Pack(CharacterBelong.Enemy, 0, 0),
                default);
            _data.Add(k_CompanionHash,
                new CharacterVitals { position = Vector2.zero, currentHp = 100, maxHp = 100 },
                default,
                CharacterFlags.Pack(CharacterBelong.Ally, 0, 0),
                default);
            _data.Add(k_OtherHash,
                new CharacterVitals { position = Vector2.zero, currentHp = 100, maxHp = 100 },
                default,
                CharacterFlags.Pack(CharacterBelong.Enemy, 0, 0),
                default);
            _data.Add(k_TargetHash,
                new CharacterVitals { position = new Vector2(2f, 0f), currentHp = 50, maxHp = 100 },
                default,
                CharacterFlags.Pack(CharacterBelong.Ally, 0, 0),
                default);
        }

        [TearDown]
        public void TearDown()
        {
            _events.Dispose();
            _data.Dispose();
        }

        private static CompanionMpSettings DefaultMpSettings()
        {
            return new CompanionMpSettings
            {
                baseRecoveryRate = 5f,
                mpRecoverActionRate = 10f,
                vanishRecoveryMultiplier = 1.3f,
                returnThresholdRatio = 0.5f,
                maxReserveMp = 100
            };
        }

        /// <summary>
        /// 継続時間の長い Sustained アクションで埋める AIMode を作る。
        /// judgeInterval を大きく取ることで、ForceEvaluate を呼ばなければ
        /// 次 Tick の evaluateAction が走らない状態を作れる。
        /// </summary>
        private static AIMode MakeLongSustainedMode()
        {
            return new AIMode
            {
                modeName = "LongSustain",
                // judgeInterval を十分大きく取り、0.1s Tick では再判定が発生しないようにする
                judgeInterval = new Vector2(10f, 10f),
                actionRules = new AIRule[]
                {
                    new AIRule
                    {
                        conditions = new AICondition[0],
                        actionIndex = 0,
                        probability = 100,
                    }
                },
                actions = new ActionSlot[]
                {
                    new ActionSlot
                    {
                        execType = ActionExecType.Sustained,
                        paramId = (int)SustainedAction.Idle,
                        // 10秒継続 → 0.1s Tick では終わらない
                        paramValue = 10f,
                    }
                },
                defaultActionIndex = 0,
            };
        }

        [Test]
        public void EnemyController_OnConfusionClearedForSelf_CancelsCurrentActionAndReEvaluates()
        {
            EnemyController controller = new EnemyController(k_EnemyHash, _data, _events);
            controller.SetAIModes(new AIMode[] { MakeLongSustainedMode() }, null);

            // 初回 Tick で Sustained アクションが走り始める
            controller.Tick(0.1f, new List<int> { k_TargetHash }, 0f);
            Assert.IsTrue(controller.JudgmentLoop.Executor.IsExecuting,
                "前提: 初回 Tick で何らかのアクションが実行中であること");

            // 混乱解除イベント発火 → 自ハッシュなので ForceEvaluate が走り、実行中アクションはキャンセルされる
            _events.FireConfusionCleared(k_EnemyHash);

            Assert.IsFalse(controller.JudgmentLoop.Executor.IsExecuting,
                "自分向けの OnConfusionCleared で ActionExecutor がキャンセルされるべき");

            controller.Dispose();
        }

        [Test]
        public void EnemyController_OnConfusionClearedForOther_DoesNotForceEvaluate()
        {
            EnemyController controller = new EnemyController(k_EnemyHash, _data, _events);
            controller.SetAIModes(new AIMode[] { MakeLongSustainedMode() }, null);

            controller.Tick(0.1f, new List<int> { k_TargetHash }, 0f);
            Assert.IsTrue(controller.JudgmentLoop.Executor.IsExecuting);

            // 他キャラ向けのイベント → この Controller は反応しないべき
            _events.FireConfusionCleared(k_OtherHash);

            Assert.IsTrue(controller.JudgmentLoop.Executor.IsExecuting,
                "他キャラの OnConfusionCleared では ForceEvaluate されないべき");

            controller.Dispose();
        }

        [Test]
        public void EnemyController_Dispose_UnsubscribesFromConfusionCleared()
        {
            EnemyController controller = new EnemyController(k_EnemyHash, _data, _events);
            controller.SetAIModes(new AIMode[] { MakeLongSustainedMode() }, null);

            controller.Tick(0.1f, new List<int> { k_TargetHash }, 0f);
            Assert.IsTrue(controller.JudgmentLoop.Executor.IsExecuting);

            // 購読解除前の baseline: 自ハッシュ一致イベントでアクションがキャンセルされる
            _events.FireConfusionCleared(k_EnemyHash);
            Assert.IsFalse(controller.JudgmentLoop.Executor.IsExecuting,
                "前提: Dispose 前は自ハッシュイベントで反応すること");

            // アクションを復元して Dispose 以降のイベントが届かないことを確認する。
            // Executor を直接操作してアクションをセット（再 Tick だと judgeInterval 経過待ちになるため）。
            controller.JudgmentLoop.Executor.Execute(
                k_EnemyHash, k_TargetHash,
                new ActionSlot
                {
                    execType = ActionExecType.Sustained,
                    paramId = (int)SustainedAction.Idle,
                    paramValue = 10f,
                });
            Assert.IsTrue(controller.JudgmentLoop.Executor.IsExecuting);

            controller.Dispose();
            // Dispose 自体は CancelCurrent を呼ぶのでここでは false
            Assert.IsFalse(controller.JudgmentLoop.Executor.IsExecuting);

            // もう一度アクションを直接実行
            controller.JudgmentLoop.Executor.Execute(
                k_EnemyHash, k_TargetHash,
                new ActionSlot
                {
                    execType = ActionExecType.Sustained,
                    paramId = (int)SustainedAction.Idle,
                    paramValue = 10f,
                });
            Assert.IsTrue(controller.JudgmentLoop.Executor.IsExecuting);

            // Dispose 済みの Controller はイベントに反応しない = アクションは続行
            _events.FireConfusionCleared(k_EnemyHash);

            Assert.IsTrue(controller.JudgmentLoop.Executor.IsExecuting,
                "Dispose 済みの Controller はイベントで反応しないべき（購読解除確認）");
        }

        [Test]
        public void CompanionController_OnConfusionClearedForSelf_CancelsCurrentAction()
        {
            CompanionController controller = new CompanionController(
                k_CompanionHash, k_TargetHash, _data, 100f, 50, DefaultMpSettings(), _events);
            controller.SetAIModes(new AIMode[] { MakeLongSustainedMode() }, null);

            controller.Tick(0.1f, new List<int> { k_TargetHash }, 0f);
            Assert.IsTrue(controller.JudgmentLoop.Executor.IsExecuting,
                "前提: 初回 Tick で何らかのアクションが実行中であること");

            _events.FireConfusionCleared(k_CompanionHash);

            Assert.IsFalse(controller.JudgmentLoop.Executor.IsExecuting,
                "自分向けの OnConfusionCleared で ActionExecutor がキャンセルされるべき");

            controller.Dispose();
        }

        [Test]
        public void CompanionController_OnConfusionClearedForOther_DoesNotForceEvaluate()
        {
            CompanionController controller = new CompanionController(
                k_CompanionHash, k_TargetHash, _data, 100f, 50, DefaultMpSettings(), _events);
            controller.SetAIModes(new AIMode[] { MakeLongSustainedMode() }, null);

            controller.Tick(0.1f, new List<int> { k_TargetHash }, 0f);
            Assert.IsTrue(controller.JudgmentLoop.Executor.IsExecuting);

            _events.FireConfusionCleared(k_OtherHash);

            Assert.IsTrue(controller.JudgmentLoop.Executor.IsExecuting,
                "他キャラの OnConfusionCleared では ForceEvaluate されないべき");

            controller.Dispose();
        }

        [Test]
        public void CompanionController_Dispose_UnsubscribesFromConfusionCleared()
        {
            CompanionController controller = new CompanionController(
                k_CompanionHash, k_TargetHash, _data, 100f, 50, DefaultMpSettings(), _events);
            controller.SetAIModes(new AIMode[] { MakeLongSustainedMode() }, null);

            controller.Tick(0.1f, new List<int> { k_TargetHash }, 0f);
            Assert.IsTrue(controller.JudgmentLoop.Executor.IsExecuting);

            _events.FireConfusionCleared(k_CompanionHash);
            Assert.IsFalse(controller.JudgmentLoop.Executor.IsExecuting,
                "前提: Dispose 前は自ハッシュイベントで反応すること");

            controller.JudgmentLoop.Executor.Execute(
                k_CompanionHash, k_TargetHash,
                new ActionSlot
                {
                    execType = ActionExecType.Sustained,
                    paramId = (int)SustainedAction.Idle,
                    paramValue = 10f,
                });
            Assert.IsTrue(controller.JudgmentLoop.Executor.IsExecuting);

            controller.Dispose();
            Assert.IsFalse(controller.JudgmentLoop.Executor.IsExecuting);

            controller.JudgmentLoop.Executor.Execute(
                k_CompanionHash, k_TargetHash,
                new ActionSlot
                {
                    execType = ActionExecType.Sustained,
                    paramId = (int)SustainedAction.Idle,
                    paramValue = 10f,
                });
            Assert.IsTrue(controller.JudgmentLoop.Executor.IsExecuting);

            _events.FireConfusionCleared(k_CompanionHash);

            Assert.IsTrue(controller.JudgmentLoop.Executor.IsExecuting,
                "Dispose 済みの Controller はイベントで反応しないべき（購読解除確認）");
        }

        [Test]
        public void ConfusionEffectProcessor_FiresEventOnClear_TriggersAIForceEvaluate()
        {
            // End-to-end: ConfusionEffectProcessor が GameEvents 経由で発火するイベントを
            // EnemyController が受け取り、ForceEvaluate が呼ばれることを確認する。
            ConfusionEffectProcessor processor = new ConfusionEffectProcessor(_events);
            EnemyController controller = new EnemyController(k_EnemyHash, _data, _events);
            controller.SetAIModes(new AIMode[] { MakeLongSustainedMode() }, null);

            controller.Tick(0.1f, new List<int> { k_TargetHash }, 0f);
            Assert.IsTrue(controller.JudgmentLoop.Executor.IsExecuting);

            // 混乱→解除の正規フロー
            processor.ApplyConfusion(k_EnemyHash, 10f, k_CompanionHash);
            processor.ClearConfusion(k_EnemyHash);

            Assert.IsFalse(controller.JudgmentLoop.Executor.IsExecuting,
                "ConfusionEffectProcessor の ClearConfusion → GameEvents 経由で AI Controller が ForceEvaluate するべき");

            controller.Dispose();
        }
    }
}
