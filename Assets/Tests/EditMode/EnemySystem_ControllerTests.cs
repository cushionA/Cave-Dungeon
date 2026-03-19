using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class EnemySystem_ControllerTests
    {
        private SoACharaDataDic _data;

        [SetUp]
        public void SetUp()
        {
            _data = new SoACharaDataDic();
        }

        [TearDown]
        public void TearDown()
        {
            _data.Dispose();
        }

        [Test]
        public void EnemyController_Initialize_IsActive()
        {
            _data.Add(1, new CharacterVitals { currentHp = 100, maxHp = 100 },
                default, default, default);

            EnemyController controller = new EnemyController(1, _data);

            Assert.IsTrue(controller.IsActive);
            Assert.AreEqual(1, controller.EnemyHash);
        }

        [Test]
        public void EnemyController_HpZero_Deactivates()
        {
            _data.Add(1, new CharacterVitals { currentHp = 0, maxHp = 100 },
                default, default, default);
            _data.Add(2, default, default, default, default);

            EnemyController controller = new EnemyController(1, _data);
            controller.SetAIModes(
                new AIMode[]
                {
                    new AIMode
                    {
                        modeName = "Normal",
                        judgeInterval = new Vector2(10f, 10f)
                    }
                },
                null);

            controller.Tick(0.1f, new List<int> { 2 }, 0f);

            Assert.IsFalse(controller.IsActive);
        }

        [Test]
        public void EnemyController_SetAIModes_Works()
        {
            _data.Add(1, new CharacterVitals { currentHp = 100, maxHp = 100 },
                default, default, default);

            EnemyController controller = new EnemyController(1, _data);
            AIMode[] modes = new AIMode[]
            {
                new AIMode { modeName = "Normal", judgeInterval = Vector2.one },
                new AIMode { modeName = "Flee", judgeInterval = Vector2.one }
            };
            controller.SetAIModes(modes, null);

            Assert.AreEqual(0, controller.ModeController.CurrentModeIndex);
        }

        [Test]
        public void EnemyController_Deactivate_StopsTicking()
        {
            _data.Add(1, new CharacterVitals { currentHp = 100, maxHp = 100 },
                default, default, default);

            EnemyController controller = new EnemyController(1, _data);
            controller.Deactivate();

            Assert.IsFalse(controller.IsActive);
            Assert.DoesNotThrow(() => controller.Tick(0.1f, new List<int>(), 0f));
        }

        [Test]
        public void EnemyController_MissingHash_Deactivates()
        {
            EnemyController controller = new EnemyController(999, _data);
            controller.SetAIModes(
                new AIMode[]
                {
                    new AIMode { judgeInterval = new Vector2(10f, 10f) }
                },
                null);

            controller.Tick(0.1f, new List<int>(), 0f);

            Assert.IsFalse(controller.IsActive);
        }
    }
}
