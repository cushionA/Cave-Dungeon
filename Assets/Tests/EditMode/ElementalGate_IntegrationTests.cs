using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class ElementalGate_IntegrationTests
    {
        [Test]
        public void GateConditionChecker_Elemental_DefaultReturnsFalse()
        {
            // GateType.Elemental の条件は ElementalGateLogic 側で判定するため
            // GateConditionChecker.Evaluate は Elemental に対して false を返す
            GateDefinition def = new GateDefinition
            {
                gateId = "elemental_01",
                gateType = GateType.Elemental,
                isPermanent = true
            };

            CharacterFlags flags = new CharacterFlags();
            bool result = GateConditionChecker.Evaluate(def, flags, null, null);

            Assert.IsFalse(result);
        }

        [Test]
        public void ElementalGateLogic_WithGateStateController_OpensCorrectly()
        {
            // ElementalGateLogic → GateStateController.ForceOpen() の連携テスト
            GateDefinition def = new GateDefinition
            {
                gateId = "ice_wall_01",
                gateType = GateType.Elemental,
                isPermanent = true
            };

            GateStateController gateController = new GateStateController(def);
            ElementalGateLogic gateLogic = new ElementalGateLogic(
                ElementalRequirement.Fire, 0f, false, 0);

            // ゲート開放時にGateStateControllerをForceOpenする
            gateLogic.OnGateOpened += () => gateController.ForceOpen();

            Assert.IsFalse(gateController.IsOpen);

            gateLogic.OnElementalHit(Element.Fire, 10f);

            Assert.IsTrue(gateController.IsOpen);
        }

        [Test]
        public void ElementalGateLogic_Permanent_StaysOpen()
        {
            ElementalGateLogic gate = new ElementalGateLogic(
                ElementalRequirement.Thunder, 0f, false, 0);

            gate.OnElementalHit(Element.Thunder, 10f);

            Assert.IsTrue(gate.IsOpened);
            // 再度ヒットしてもfalse（既に開いている）
            Assert.IsFalse(gate.OnElementalHit(Element.Thunder, 10f));
        }

        [Test]
        public void GateStateController_ElementalType_DefaultHintIsEmpty()
        {
            GateDefinition def = new GateDefinition
            {
                gateId = "elemental_01",
                gateType = GateType.Elemental
            };

            GateStateController controller = new GateStateController(def);
            // Elemental タイプはデフォルトヒントが空文字列（ElementalGateLogic側で管理）
            Assert.AreEqual("", controller.HintText);
        }
    }
}
