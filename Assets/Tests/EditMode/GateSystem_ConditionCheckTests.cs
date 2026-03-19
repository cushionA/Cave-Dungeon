using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class GateSystem_ConditionCheckTests
    {
        [Test]
        public void GateCondition_ClearGate_ChecksFlag()
        {
            GateDefinition def = new GateDefinition { gateType = GateType.Clear, requiredClearFlag = "boss_defeated" };
            bool result = GateConditionChecker.Evaluate(def, default, null, (flag) => flag == "boss_defeated");
            Assert.IsTrue(result);
        }

        [Test]
        public void GateCondition_AbilityGate_ChecksAbility()
        {
            GateDefinition def = new GateDefinition { gateType = GateType.Ability, requiredAbility = AbilityFlag.WallKick };
            CharacterFlags flags = CharacterFlags.Pack(CharacterBelong.Ally, CharacterFeature.Player, AbilityFlag.WallKick);

            bool result = GateConditionChecker.Evaluate(def, flags, null, null);
            Assert.IsTrue(result);
        }

        [Test]
        public void GateCondition_AbilityGate_MissingAbility_Fails()
        {
            GateDefinition def = new GateDefinition { gateType = GateType.Ability, requiredAbility = AbilityFlag.WallKick };
            CharacterFlags flags = CharacterFlags.Pack(CharacterBelong.Ally, CharacterFeature.Player, AbilityFlag.None);

            bool result = GateConditionChecker.Evaluate(def, flags, null, null);
            Assert.IsFalse(result);
        }

        [Test]
        public void GateCondition_KeyGate_ChecksItem()
        {
            GateDefinition def = new GateDefinition { gateType = GateType.Key, requiredItemId = 42 };

            bool result = GateConditionChecker.Evaluate(def, default, (id) => id == 42, null);
            Assert.IsTrue(result);
        }

        [Test]
        public void GateCondition_KeyGate_MissingItem_Fails()
        {
            GateDefinition def = new GateDefinition { gateType = GateType.Key, requiredItemId = 42 };

            bool result = GateConditionChecker.Evaluate(def, default, (id) => false, null);
            Assert.IsFalse(result);
        }
    }
}
