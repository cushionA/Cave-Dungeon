using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class TestCoopAction : CoopActionBase
    {
        public override string ActionName => "TestCoop";
        public override int MpCost => 10;
        public override float CooldownDuration => 5f;
        public override int MaxComboCount => 3;
        public override float ComboInputWindow => 1f;

        public int LastComboIndex { get; private set; }
        public int ExecuteCount { get; private set; }

        public override void ExecuteCombo(int comboIndex, int companionHash, int targetHash)
        {
            LastComboIndex = comboIndex;
            ExecuteCount++;
        }
    }

    public class CoopAction_CoreAndComboTests
    {
        [Test]
        public void CoopAction_PlayerDead_Rejected()
        {
            CoopInterruptionHandler ih = new CoopInterruptionHandler();
            CoopActionManager manager = new CoopActionManager(1, ih);

            bool result = manager.Activate(new TestCoopAction(), 10, false, false, default, 0);

            Assert.IsFalse(result);
        }

        [Test]
        public void CoopAction_CompanionStaggered_Rejected()
        {
            CoopInterruptionHandler ih = new CoopInterruptionHandler();
            CoopActionManager manager = new CoopActionManager(1, ih);

            bool result = manager.Activate(new TestCoopAction(), 10, true, true, default, 0);

            Assert.IsFalse(result);
        }

        [Test]
        public void CoopAction_Activate_ExecutesCombo()
        {
            CoopInterruptionHandler ih = new CoopInterruptionHandler();
            CoopActionManager manager = new CoopActionManager(1, ih);
            TestCoopAction action = new TestCoopAction();

            bool result = manager.Activate(action, 10, true, false, default, 0);

            Assert.IsTrue(result);
            Assert.IsTrue(manager.IsInCombo);
            Assert.AreEqual(0, action.LastComboIndex);
        }

        [Test]
        public void CoopAction_ComboWindowExpiry_EndsCombo()
        {
            CoopInterruptionHandler ih = new CoopInterruptionHandler();
            CoopActionManager manager = new CoopActionManager(1, ih);
            manager.Activate(new TestCoopAction(), 10, true, false, default, 0);

            manager.Tick(1.5f);

            Assert.IsFalse(manager.IsInCombo);
        }

        [Test]
        public void CoopAction_MaxCombo_EndsCombo()
        {
            CoopInterruptionHandler ih = new CoopInterruptionHandler();
            CoopActionManager manager = new CoopActionManager(1, ih);
            TestCoopAction action = new TestCoopAction();

            manager.Activate(action, 10, true, false, default, 0);
            manager.Activate(action, 10, true, false, default, 0);
            manager.Activate(action, 10, true, false, default, 0);

            Assert.AreEqual(3, action.ExecuteCount);
        }
    }
}
