using NUnit.Framework;
using UnityEngine;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class UISystemTests
    {
        // === HUD ===

        [Test]
        public void HudDataProvider_CalculateBarRatio_ReturnsCorrectRatio()
        {
            float ratio = HudDataProvider.CalculateBarRatio(50f, 100f);

            Assert.AreEqual(0.5f, ratio, 0.001f);

            // 0除算防止: max=0 => 0
            float zeroMax = HudDataProvider.CalculateBarRatio(0f, 0f);

            Assert.AreEqual(0f, zeroMax);
        }

        [Test]
        public void HudDataProvider_GetVitalsRatios_ReturnsAllThree()
        {
            CharacterVitals vitals = new CharacterVitals
            {
                currentHp = 80,
                maxHp = 100,
                currentMp = 30,
                maxMp = 50,
                currentStamina = 60f,
                maxStamina = 200f
            };

            HudDataProvider.GetVitalsRatios(vitals, out float hpRatio, out float mpRatio, out float staminaRatio);

            Assert.AreEqual(0.8f, hpRatio, 0.001f);
            Assert.AreEqual(0.6f, mpRatio, 0.001f);
            Assert.AreEqual(0.3f, staminaRatio, 0.001f);
        }

        [Test]
        public void HudDataProvider_CalculateBarRatio_ClampsToOne()
        {
            // current > max => 1.0 にクランプ
            float ratio = HudDataProvider.CalculateBarRatio(150f, 100f);

            Assert.AreEqual(1.0f, ratio, 0.001f);
        }

        // === Menus ===

        [Test]
        public void MenuStackManager_Push_SetsCurrentScreen()
        {
            MenuStackManager manager = new MenuStackManager();

            manager.Push(MenuScreen.Inventory);

            Assert.AreEqual(MenuScreen.Inventory, manager.CurrentScreen);
            Assert.IsTrue(manager.IsOpen);
            Assert.AreEqual(1, manager.Depth);
        }

        [Test]
        public void MenuStackManager_Pop_ReturnsToPrevious()
        {
            MenuStackManager manager = new MenuStackManager();
            manager.Push(MenuScreen.Inventory);
            manager.Push(MenuScreen.Equipment);

            MenuScreen popped = manager.Pop();

            Assert.AreEqual(MenuScreen.Equipment, popped);
            Assert.AreEqual(MenuScreen.Inventory, manager.CurrentScreen);
        }

        [Test]
        public void MenuStackManager_CloseAll_ClearsStack()
        {
            MenuStackManager manager = new MenuStackManager();
            manager.Push(MenuScreen.Inventory);
            manager.Push(MenuScreen.Equipment);
            manager.Push(MenuScreen.Status);

            manager.CloseAll();

            Assert.IsFalse(manager.IsOpen);
            Assert.AreEqual(MenuScreen.None, manager.CurrentScreen);
            Assert.AreEqual(0, manager.Depth);
        }

        [Test]
        public void MenuStackManager_Pop_WhenEmpty_ReturnsNone()
        {
            MenuStackManager manager = new MenuStackManager();

            MenuScreen result = manager.Pop();

            Assert.AreEqual(MenuScreen.None, result);
            Assert.IsFalse(manager.IsOpen);
        }

        // === BattleFeedback ===

        [Test]
        public void BattleFeedbackFactory_CreateDamagePopup_SetsCriticalType()
        {
            Vector2 position = new Vector2(1f, 2f);

            DamagePopupData data = BattleFeedbackFactory.CreateDamagePopup(150, true, position);

            Assert.AreEqual(150, data.value);
            Assert.AreEqual(FeedbackType.Critical, data.type);
            Assert.AreEqual(position, data.worldPosition);
        }

        [Test]
        public void BattleFeedbackFactory_CreateHealPopup_SetsHealType()
        {
            Vector2 position = new Vector2(3f, 4f);

            DamagePopupData data = BattleFeedbackFactory.CreateHealPopup(50, position);

            Assert.AreEqual(50, data.value);
            Assert.AreEqual(FeedbackType.Heal, data.type);
            Assert.AreEqual(position, data.worldPosition);
        }
    }
}
