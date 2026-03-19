using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class ConfusionMagic_AIOverrideTests
    {
        [Test]
        public void ConfusionAIOverride_ActivateOverride_RegistersTarget()
        {
            ConfusionAIOverride aiOverride = new ConfusionAIOverride();
            aiOverride.ActivateOverride(100);

            Assert.IsTrue(aiOverride.IsOverrideActive(100));
        }

        [Test]
        public void ConfusionAIOverride_DeactivateOverride_UnregistersTarget()
        {
            ConfusionAIOverride aiOverride = new ConfusionAIOverride();
            aiOverride.ActivateOverride(100);
            aiOverride.DeactivateOverride(100);

            Assert.IsFalse(aiOverride.IsOverrideActive(100));
        }

        [Test]
        public void ConfusionAIOverride_GetOverriddenBelong_ReversesAllyEnemy()
        {
            ConfusionAIOverride aiOverride = new ConfusionAIOverride();
            aiOverride.ActivateOverride(100);

            CharacterBelong overridden = aiOverride.GetOverriddenBelong(100, CharacterBelong.Enemy);
            Assert.AreEqual(CharacterBelong.Ally, overridden);

            overridden = aiOverride.GetOverriddenBelong(100, CharacterBelong.Ally);
            Assert.AreEqual(CharacterBelong.Enemy, overridden);
        }

        [Test]
        public void ConfusionAIOverride_GetOverriddenBelong_NonOverridden_ReturnsOriginal()
        {
            ConfusionAIOverride aiOverride = new ConfusionAIOverride();

            CharacterBelong result = aiOverride.GetOverriddenBelong(100, CharacterBelong.Enemy);
            Assert.AreEqual(CharacterBelong.Enemy, result);
        }
    }
}
