using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// A1: CompanionController.RequestModeSwitch の手動切替と
    /// k_ManualOverrideTimeoutSeconds タイムアウト後の自動遷移再開を検証する。
    /// </summary>
    public class CompanionAI_ManualOverrideTests
    {
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

        private static (SoACharaDataDic data, CompanionController controller) CreateCompanion()
        {
            SoACharaDataDic data = new SoACharaDataDic();
            data.Add(1, new CharacterVitals { position = Vector2.zero },
                default, default, default);
            data.Add(2, new CharacterVitals { position = new Vector2(1f, 0f) },
                default, default, default);
            CompanionController controller = new CompanionController(1, 2, data, 100f, 50, DefaultMpSettings(), null);
            return (data, controller);
        }

        [Test]
        public void CompanionController_HasModeTransitionEditor()
        {
            (SoACharaDataDic data, CompanionController controller) = CreateCompanion();

            Assert.IsNotNull(controller.ModeTransitionEditor);

            data.Dispose();
        }

        [Test]
        public void RequestModeSwitch_ChangesCurrentMode()
        {
            (SoACharaDataDic data, CompanionController controller) = CreateCompanion();
            AIMode[] modes = new AIMode[]
            {
                new AIMode { modeName = "Combat", judgeInterval = Vector2.one },
                new AIMode { modeName = "Support", judgeInterval = Vector2.one },
                new AIMode { modeName = "Flee",    judgeInterval = Vector2.one }
            };
            controller.SetAIModes(modes, null);

            controller.RequestModeSwitch(2);

            Assert.AreEqual(2, controller.ModeController.CurrentModeIndex);
            Assert.IsTrue(controller.IsManualOverrideActive);
            Assert.Greater(controller.ManualOverrideRemaining, 0f);

            data.Dispose();
        }

        [Test]
        public void RequestModeSwitch_SuppressesAutoTransitionDuringTimeout()
        {
            (SoACharaDataDic data, CompanionController controller) = CreateCompanion();

            AIMode[] modes = new AIMode[]
            {
                new AIMode { modeName = "Combat",  judgeInterval = Vector2.one },
                new AIMode { modeName = "Support", judgeInterval = Vector2.one }
            };

            // 常に targetModeIndex=0 に戻す自動遷移ルール
            ModeTransitionRule[] rules = new ModeTransitionRule[]
            {
                new ModeTransitionRule
                {
                    conditions = new AICondition[0], // 条件なし = 常にマッチ
                    targetModeIndex = 0,
                    sourceModeIndex = -1
                }
            };
            controller.SetAIModes(modes, rules);

            // 手動で1に切替
            controller.RequestModeSwitch(1);
            Assert.AreEqual(1, controller.ModeController.CurrentModeIndex);

            // タイムアウト内の Tick は自動遷移を抑制 (deltaTime=1f < 5f)
            controller.Tick(1.0f, new List<int>(), 0f);
            Assert.AreEqual(1, controller.ModeController.CurrentModeIndex,
                "手動切替中は自動遷移が抑制されるべき");
            Assert.IsTrue(controller.IsManualOverrideActive);

            data.Dispose();
        }

        [Test]
        public void RequestModeSwitch_TimeoutRevertsToAutoTransition()
        {
            (SoACharaDataDic data, CompanionController controller) = CreateCompanion();

            AIMode[] modes = new AIMode[]
            {
                new AIMode { modeName = "Combat",  judgeInterval = Vector2.one },
                new AIMode { modeName = "Support", judgeInterval = Vector2.one }
            };
            ModeTransitionRule[] rules = new ModeTransitionRule[]
            {
                new ModeTransitionRule
                {
                    conditions = new AICondition[0],
                    targetModeIndex = 0,
                    sourceModeIndex = -1
                }
            };
            controller.SetAIModes(modes, rules);

            // 手動で1に切替
            controller.RequestModeSwitch(1);

            // k_ManualOverrideTimeoutSeconds (5秒) 以上進めてタイマーを消化
            controller.Tick(6.0f, new List<int>(), 0f);

            // タイマー消化完了 → 手動切替解除
            Assert.IsFalse(controller.IsManualOverrideActive);
            Assert.AreEqual(0f, controller.ManualOverrideRemaining);

            // 次の Tick で自動遷移が再開され、モード 0 に戻るはず
            controller.Tick(0.1f, new List<int>(), 0f);
            Assert.AreEqual(0, controller.ModeController.CurrentModeIndex,
                "タイムアウト後は自動遷移が再開し、ターゲットモードに切り替わるべき");

            data.Dispose();
        }

        [Test]
        public void RequestModeSwitch_InvalidIndex_DoesNotChangeMode()
        {
            (SoACharaDataDic data, CompanionController controller) = CreateCompanion();
            AIMode[] modes = new AIMode[]
            {
                new AIMode { modeName = "Combat",  judgeInterval = Vector2.one }
            };
            controller.SetAIModes(modes, null);

            // 範囲外インデックス → ModeController.SwitchMode が早期 return
            controller.RequestModeSwitch(99);

            Assert.AreEqual(0, controller.ModeController.CurrentModeIndex,
                "無効なインデックスではモードが変わらない");

            data.Dispose();
        }
    }
}
