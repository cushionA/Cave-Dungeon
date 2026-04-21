using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class GateSystem_OpenCloseTests
    {
        [Test]
        public void GateController_ConditionMet_Opens()
        {
            GateDefinition def = new GateDefinition { gateType = GateType.Clear, requiredClearFlag = "boss1" };
            GateStateController gate = new GateStateController(def);

            bool result = gate.TryOpen(default, null, (f) => f == "boss1");

            Assert.IsTrue(result);
            Assert.IsTrue(gate.IsOpen);
        }

        [Test]
        public void GateController_ConditionNotMet_StaysClosed()
        {
            GateDefinition def = new GateDefinition { gateType = GateType.Clear, requiredClearFlag = "boss1" };
            GateStateController gate = new GateStateController(def);

            bool result = gate.TryOpen(default, null, (f) => false);

            Assert.IsFalse(result);
            Assert.IsFalse(gate.IsOpen);
        }

        [Test]
        public void GateController_ShowsHint_WhenClosed()
        {
            GateDefinition def = new GateDefinition { gateType = GateType.Ability, requiredAbility = AbilityFlag.WallKick };
            GateStateController gate = new GateStateController(def);

            Assert.IsTrue(gate.HintText.Contains("WallKick"));
        }

        [Test]
        public void GateController_OpenedEvent_Fires()
        {
            GateDefinition def = new GateDefinition { gateId = "gate_01", gateType = GateType.Clear, requiredClearFlag = "boss1" };
            GateStateController gate = new GateStateController(def);
            string firedId = null;
            gate.OnGateOpened += (id) => firedId = id;

            gate.TryOpen(default, null, (f) => f == "boss1");

            Assert.AreEqual("gate_01", firedId);
        }

        [Test]
        public void GateController_ForceClose_OnTemporaryGate_ClosesAndReturnsTrue()
        {
            GateDefinition def = new GateDefinition
            {
                gateId = "tmp_01",
                gateType = GateType.Clear,
                requiredClearFlag = "boss1",
                isPermanent = false
            };
            GateStateController gate = new GateStateController(def);
            gate.ForceOpen();
            Assert.IsTrue(gate.IsOpen);

            bool closed = gate.ForceClose();

            Assert.IsTrue(closed);
            Assert.IsFalse(gate.IsOpen);
        }

        [Test]
        public void GateController_ForceClose_OnPermanentGate_IsIgnored()
        {
            GateDefinition def = new GateDefinition
            {
                gateId = "perm_01",
                gateType = GateType.Clear,
                requiredClearFlag = "boss_final",
                isPermanent = true
            };
            GateStateController gate = new GateStateController(def);
            gate.ForceOpen();
            Assert.IsTrue(gate.IsOpen);

            bool closed = gate.ForceClose();

            Assert.IsFalse(closed, "永続ゲートは ForceClose で閉じてはならない");
            Assert.IsTrue(gate.IsOpen, "永続ゲートは開いたままであるべき");
        }
    }
}
