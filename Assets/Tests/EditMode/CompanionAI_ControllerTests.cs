using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class CompanionAI_ControllerTests
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

        [Test]
        public void CompanionController_Initialize_HasComponents()
        {
            SoACharaDataDic data = new SoACharaDataDic();
            data.Add(1, new CharacterVitals { position = Vector2.zero },
                default, default, default);
            data.Add(2, new CharacterVitals { position = new Vector2(1f, 0f) },
                default, default, default);

            CompanionController controller = new CompanionController(1, 2, data, 100f, 50, DefaultMpSettings(), null);

            Assert.IsNotNull(controller.JudgmentLoop);
            Assert.IsNotNull(controller.ModeController);
            Assert.IsNotNull(controller.FollowBehavior);
            Assert.IsNotNull(controller.StanceManager);

            data.Dispose();
        }

        [Test]
        public void CompanionController_Teleport_WhenFarFromPlayer()
        {
            SoACharaDataDic data = new SoACharaDataDic();
            data.Add(1, new CharacterVitals { position = Vector2.zero },
                default, default, default);
            data.Add(2, new CharacterVitals { position = new Vector2(100f, 0f) },
                default, default, default);

            CompanionController controller = new CompanionController(1, 2, data, 100f, 50, DefaultMpSettings(), null);
            AIMode[] modes = new AIMode[]
            {
                new AIMode { modeName = "Default", judgeInterval = new Vector2(10f, 10f) }
            };
            controller.SetAIModes(modes, null);

            controller.Tick(0.1f, new List<int>(), 0f);

            ref CharacterVitals companionV = ref data.GetVitals(1);
            Assert.AreEqual(100f, companionV.position.x, 0.01f);

            data.Dispose();
        }

        [Test]
        public void CompanionController_SetStance_Works()
        {
            SoACharaDataDic data = new SoACharaDataDic();
            data.Add(1, new CharacterVitals { position = Vector2.zero },
                default, default, default);
            data.Add(2, new CharacterVitals { position = new Vector2(1f, 0f) },
                default, default, default);

            CompanionController controller = new CompanionController(1, 2, data, 100f, 50, DefaultMpSettings(), null);
            controller.StanceManager.SetStance(CompanionStance.Supportive);

            Assert.AreEqual(CompanionStance.Supportive, controller.StanceManager.CurrentStance);

            data.Dispose();
        }

        [Test]
        public void CompanionController_MissingHash_DoesNotCrash()
        {
            SoACharaDataDic data = new SoACharaDataDic();

            CompanionController controller = new CompanionController(999, 888, data, 100f, 50, DefaultMpSettings(), null);

            Assert.DoesNotThrow(() => controller.Tick(0.1f, new List<int>(), 0f));

            data.Dispose();
        }

        [Test]
        public void CompanionController_SetAIModes_AppliesMode()
        {
            SoACharaDataDic data = new SoACharaDataDic();
            data.Add(1, new CharacterVitals { position = Vector2.zero },
                default, default, default);
            data.Add(2, new CharacterVitals { position = new Vector2(1f, 0f) },
                default, default, default);

            CompanionController controller = new CompanionController(1, 2, data, 100f, 50, DefaultMpSettings(), null);
            AIMode[] modes = new AIMode[]
            {
                new AIMode { modeName = "Combat", judgeInterval = Vector2.one },
                new AIMode { modeName = "Support", judgeInterval = Vector2.one }
            };
            controller.SetAIModes(modes, null);

            Assert.AreEqual(0, controller.ModeController.CurrentModeIndex);

            data.Dispose();
        }
    }
}
