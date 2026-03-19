using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class SummonSystem_ControllerTests
    {
        [Test]
        public void SummonedCharacterLogic_Initialize_SetsCorrectValues()
        {
            SummonedCharacterLogic logic = new SummonedCharacterLogic();
            logic.Initialize(100, 30f, SummonType.Combat);

            Assert.AreEqual(100, logic.SummonerId);
            Assert.AreEqual(30f, logic.RemainingDuration, 0.001f);
            Assert.AreEqual(SummonType.Combat, logic.Type);
            Assert.IsTrue(logic.IsActive);
        }

        [Test]
        public void SummonedCharacterLogic_Dismiss_DeactivatesAndFiresEvent()
        {
            SummonedCharacterLogic logic = new SummonedCharacterLogic();
            logic.Initialize(100, 30f, SummonType.Combat);

            bool dismissed = false;
            logic.OnDismissed += () => dismissed = true;

            logic.Dismiss();

            Assert.IsFalse(logic.IsActive);
            Assert.IsTrue(dismissed);
        }

        [Test]
        public void SummonedCharacterLogic_Tick_ReducesDuration()
        {
            SummonedCharacterLogic logic = new SummonedCharacterLogic();
            logic.Initialize(100, 10f, SummonType.Utility);

            logic.Tick(3f);
            Assert.AreEqual(7f, logic.RemainingDuration, 0.001f);
        }

        [Test]
        public void SummonedCharacterLogic_Tick_Expiry_DismissesAutomatically()
        {
            SummonedCharacterLogic logic = new SummonedCharacterLogic();
            logic.Initialize(100, 5f, SummonType.Decoy);

            bool dismissed = false;
            logic.OnDismissed += () => dismissed = true;

            logic.Tick(6f);

            Assert.IsFalse(logic.IsActive);
            Assert.IsTrue(dismissed);
        }

        [Test]
        public void SummonedCharacterLogic_ZeroDuration_NeverExpires()
        {
            SummonedCharacterLogic logic = new SummonedCharacterLogic();
            logic.Initialize(100, 0f, SummonType.Utility);

            logic.Tick(1000f);

            Assert.IsTrue(logic.IsActive);
        }
    }
}
