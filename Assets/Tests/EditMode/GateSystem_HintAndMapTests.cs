using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class GateSystem_HintAndMapTests
    {
        [Test]
        public void GateHint_ClosedGate_ShowsHint()
        {
            GateDefinition def = new GateDefinition
            {
                gateId = "g1",
                gateType = GateType.Ability,
                requiredAbility = AbilityFlag.WallKick
            };
            GateStateController gate = new GateStateController(def);
            GateHintDisplay display = new GateHintDisplay();
            string receivedHint = null;
            display.OnHintRequested += (id, hint) => receivedHint = hint;

            display.ShowHint(gate);

            Assert.IsNotNull(receivedHint);
            Assert.IsTrue(receivedHint.Contains("WallKick"));
        }

        [Test]
        public void GateHint_OpenGate_NoHint()
        {
            GateDefinition def = new GateDefinition
            {
                gateId = "g1",
                gateType = GateType.Clear,
                requiredClearFlag = "boss"
            };
            GateStateController gate = new GateStateController(def);
            gate.ForceOpen();
            GateHintDisplay display = new GateHintDisplay();
            bool hintFired = false;
            display.OnHintRequested += (id, hint) => hintFired = true;

            display.ShowHint(gate);

            Assert.IsFalse(hintFired);
        }

        [Test]
        public void GateHint_MapIconUpdate_Fires()
        {
            GateHintDisplay display = new GateHintDisplay();
            string receivedId = null;
            bool receivedOpen = false;
            display.OnMapIconUpdated += (id, open) =>
            {
                receivedId = id;
                receivedOpen = open;
            };

            display.UpdateMapIcon("gate_01", true);

            Assert.AreEqual("gate_01", receivedId);
            Assert.IsTrue(receivedOpen);
        }
    }
}
