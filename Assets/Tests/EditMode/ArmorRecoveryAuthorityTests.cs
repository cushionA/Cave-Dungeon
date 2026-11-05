using Game.Core;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    public class ArmorRecoveryAuthorityTests
    {
        [TestCase(20f, 5f, 10f)]
        [TestCase(60f, 3f, 100f)]
        public void ArmorRecovery_UsesVitalsMaximum_WhenDefenseOnlyContainsRecoveryState(
            float maximum, float rate, float deltaTime)
        {
            CharacterVitals vitals = new CharacterVitals { currentHp = 100, maxArmor = maximum };
            CharacterDefenseState defense = new CharacterDefenseState { armorRecoveryRate = rate };

            HitResolver.TickRecovery(ref vitals, ActState.Neutral, ref defense, deltaTime);

            Assert.AreEqual(maximum, vitals.currentArmor);
        }

        [Test]
        public void ArmorRecovery_MaximumChangesBetweenTicks_UsesCurrentDerivedMaximum()
        {
            CharacterVitals vitals = new CharacterVitals { currentHp = 100, maxArmor = 20f };
            CharacterDefenseState defense = new CharacterDefenseState { armorRecoveryRate = 10f };
            HitResolver.TickRecovery(ref vitals, ActState.Neutral, ref defense, 1f);
            Assert.AreEqual(10f, vitals.currentArmor);

            vitals.maxArmor = 45f;
            HitResolver.TickRecovery(ref vitals, ActState.Neutral, ref defense, 10f);

            Assert.AreEqual(45f, vitals.currentArmor);
        }
    }
}
