using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class ElementalGate_HintDisplayTests
    {
        [Test]
        public void ElementalGateHintProvider_Fire_ReturnsFireHint()
        {
            string hint = ElementalGateHintProvider.GetHint(ElementalRequirement.Fire);
            Assert.IsTrue(hint.Contains("炎"));
        }

        [Test]
        public void ElementalGateHintProvider_AllElements_ReturnNonEmpty()
        {
            Assert.IsNotEmpty(ElementalGateHintProvider.GetHint(ElementalRequirement.Fire));
            Assert.IsNotEmpty(ElementalGateHintProvider.GetHint(ElementalRequirement.Thunder));
            Assert.IsNotEmpty(ElementalGateHintProvider.GetHint(ElementalRequirement.Light));
            Assert.IsNotEmpty(ElementalGateHintProvider.GetHint(ElementalRequirement.Dark));
            Assert.IsNotEmpty(ElementalGateHintProvider.GetHint(ElementalRequirement.Slash));
            Assert.IsNotEmpty(ElementalGateHintProvider.GetHint(ElementalRequirement.Strike));
            Assert.IsNotEmpty(ElementalGateHintProvider.GetHint(ElementalRequirement.Pierce));
        }

        [Test]
        public void ElementalGateHintProvider_GetSolvedText_ReturnsSolvedMessage()
        {
            string solved = ElementalGateHintProvider.GetSolvedText(ElementalRequirement.Fire);
            Assert.IsNotEmpty(solved);
        }
    }
}
