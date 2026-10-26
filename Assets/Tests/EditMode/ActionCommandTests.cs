using System;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    public class ActionCommandTests
    {
        private sealed class TypedBackend : IActionCommandBackend
        {
            public bool IsExecuting { get; private set; }
            public ActionCommand received;
            public int cancelled;
            public event Action OnActionCompleted;
            public bool Supports(ActionCommandKind kind) => kind == ActionCommandKind.Attack || kind == ActionCommandKind.Dodge;
            public bool Execute(int owner, int target, ActionSlot slot) => throw new InvalidOperationException("Legacy dispatch must not replace typed dispatch.");
            public ActionDispatchResult ExecuteCommand(int owner, int target, in ActionCommand command)
            {
                received = command;
                if (!Supports(command.kind)) { return new ActionDispatchResult(ActionDispatchStatus.Unsupported); }
                IsExecuting = true;
                return new ActionDispatchResult(ActionDispatchStatus.Accepted, 1, 2);
            }
            public void CancelCurrent() { cancelled++; IsExecuting = false; }
            public void Tick(float deltaTime) { }
            public void Complete() { IsExecuting = false; OnActionCompleted?.Invoke(); }
        }

        [Test]
        public void ActionExecutor_TypedBackend_UsesSameTranslationForExecutionAndCapability()
        {
            ActionExecutor executor = new ActionExecutor();
            TypedBackend backend = new TypedBackend();
            executor.SetExecutionBackend(backend);
            ActionSlot attack = new ActionSlot
            {
                execType = ActionExecType.Attack, actionDefinitionId = "heavy", paramValue = 2f,
            };
            Assert.IsTrue(executor.CanExecute(attack));
            Assert.IsTrue(executor.Execute(3, 4, attack));
            Assert.AreEqual("heavy", backend.received.attack.definitionId);
            Assert.AreEqual(2f, backend.received.attack.chargeMultiplier);
            ActionSlot unsupported = new ActionSlot { execType = ActionExecType.Instant, paramId = (int)InstantAction.UseItem };
            Assert.IsFalse(executor.CanExecute(unsupported));
            Assert.IsFalse(executor.Execute(3, 4, unsupported));
            Assert.AreEqual(ActionDispatchStatus.Unsupported, executor.LastDispatchStatus);
            Assert.IsTrue(executor.IsExecuting);
            Assert.AreEqual(0, backend.cancelled);
        }

        [Test]
        public void ActionDispatch_DefaultResult_IsRejected()
        {
            ActionDispatchResult result = default;
            Assert.IsFalse(result.Accepted);
            Assert.AreEqual(ActionDispatchStatus.Rejected, result.Status);
        }

        [Test]
        public void ActionCommand_LegacyAttack_PreservesChargeAndDefinitionReference()
        {
            ActionSlot slot = new ActionSlot
            {
                execType = ActionExecType.Attack,
                actionDefinitionId = "sword-heavy",
                paramId = 3,
                paramValue = 1.75f,
            };

            ActionCommand command = ActionCommand.FromLegacySlot(slot);

            Assert.AreEqual(ActionCommandKind.Attack, command.kind);
            Assert.AreEqual("sword-heavy", command.attack.definitionId);
            Assert.AreEqual(3, command.attack.legacyIndex);
            Assert.AreEqual(1.75f, command.attack.chargeMultiplier);
            Assert.AreEqual(0f, command.sustained.duration);
        }

        [Test]
        public void ActionCommand_SustainedAndUnsupported_KeepDistinctMeaning()
        {
            ActionCommand guard = ActionCommand.FromLegacySlot(new ActionSlot
            {
                execType = ActionExecType.Sustained, paramId = (int)SustainedAction.Guard, paramValue = 2f,
            });
            ActionCommand item = ActionCommand.FromLegacySlot(new ActionSlot
            {
                execType = ActionExecType.Instant, paramId = (int)InstantAction.UseItem, paramValue = 42f,
            });

            Assert.AreEqual(ActionCommandKind.Guard, guard.kind);
            Assert.AreEqual(2f, guard.sustained.duration);
            Assert.AreEqual(ActionCommandKind.Unsupported, item.kind);
        }

        [Test]
        public void ActionReference_PersistentId_ResolvesAfterReorderingAndNeverFallsBackWhenMissing()
        {
            string[] initial = { "first", "second" };
            string[] reordered = { "second", "first" };

            Assert.AreEqual(ActionDispatchStatus.Accepted,
                ActionDefinitionReference.Resolve("second", 0, initial, out int first));
            Assert.AreEqual(ActionDispatchStatus.Accepted,
                ActionDefinitionReference.Resolve("second", 0, reordered, out int moved));
            Assert.AreEqual(1, first);
            Assert.AreEqual(0, moved);
            Assert.AreEqual(ActionDispatchStatus.UnknownDefinition,
                ActionDefinitionReference.Resolve("deleted", 0, initial, out int missing));
            Assert.AreEqual(-1, missing);
        }

        [Test]
        public void ActionReference_DuplicateId_IsRejectedAndLegacyIndexRemainsExplicit()
        {
            string[] definitions = { "same", "same" };
            Assert.AreEqual(ActionDispatchStatus.AmbiguousDefinition,
                ActionDefinitionReference.Resolve("same", 0, definitions, out _));
            Assert.AreEqual(ActionDispatchStatus.Accepted,
                ActionDefinitionReference.Resolve(null, 1, definitions, out int legacy));
            Assert.AreEqual(1, legacy);
            Assert.AreEqual(ActionDispatchStatus.InvalidLegacyReference,
                ActionDefinitionReference.Resolve(null, 9, definitions, out _));
        }
    }
}
