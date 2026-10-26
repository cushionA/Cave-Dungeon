using Game.Core;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    public class ActionAuthoringCommandTests
    {
        [TestCase(ActType.Guard, ActionCommandKind.Guard)]
        [TestCase(ActType.Wait, ActionCommandKind.Idle)]
        [TestCase(ActType.Move, ActionCommandKind.MoveToTarget)]
        [TestCase(ActType.Dodge, ActionCommandKind.Dodge)]
        [TestCase(ActType.Flee, ActionCommandKind.Flee)]
        public void ActionAuthoring_BuiltinType_IgnoresUnrelatedAttackIndex(ActType type, ActionCommandKind expected)
        {
            ActionSlot slot = AIInfoConverter.ConvertAction(new ActData
            {
                actType = type, attackInfoIndex = 999, actionDefinitionId = "unrelated", coolTime = 2f,
            });
            ActionCommand command = ActionCommand.FromLegacySlot(slot);

            Assert.AreEqual(expected, command.kind);
            Assert.IsNull(slot.actionDefinitionId);
            if (slot.execType == ActionExecType.Sustained) { Assert.AreEqual(2f, command.sustained.duration); }
        }

        [Test]
        public void ActionAuthoring_AttackAndHeal_PreservePersistentDefinition()
        {
            ActionSlot attack = AIInfoConverter.ConvertAction(new ActData
            {
                actType = ActType.Attack, actionDefinitionId = "sword", attackInfoIndex = 7,
            });
            ActionSlot heal = AIInfoConverter.ConvertAction(new ActData
            {
                actType = ActType.Heal, actionDefinitionId = "healing", attackInfoIndex = 2,
            });

            Assert.AreEqual("sword", ActionCommand.FromLegacySlot(attack).attack.definitionId);
            Assert.AreEqual("healing", ActionCommand.FromLegacySlot(heal).attack.definitionId);
            Assert.AreEqual(ActionCommandKind.Cast, ActionCommand.FromLegacySlot(heal).kind);
            Assert.AreEqual(7, attack.paramId);
        }

        [Test]
        public void ActionAuthoring_UnimplementedTypes_DoNotTurnIntoDodgeOrAttack()
        {
            ActionSlot feint = AIInfoConverter.ConvertAction(new ActData { actType = ActType.FeintCancel });
            ActionSlot buff = AIInfoConverter.ConvertAction(new ActData { actType = ActType.Buff });

            Assert.AreEqual(ActionCommandKind.Unsupported, ActionCommand.FromLegacySlot(feint).kind);
            Assert.AreEqual(ActionCommandKind.Unsupported, ActionCommand.FromLegacySlot(buff).kind);
        }

        [Test]
        public void ActionAuthoring_UnknownTypeWithDefinition_RemainsUnsupported()
        {
            ActionSlot unknown = AIInfoConverter.ConvertAction(new ActData
            {
                actType = (ActType)255, actionDefinitionId = "known-attack", attackInfoIndex = 0,
            });

            Assert.AreEqual(ActionCommandKind.Unsupported, ActionCommand.FromLegacySlot(unknown).kind);
            Assert.IsNull(unknown.actionDefinitionId);
        }
    }
}
