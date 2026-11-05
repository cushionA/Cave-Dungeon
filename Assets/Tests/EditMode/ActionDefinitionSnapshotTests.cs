using System;
using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    public class ActionDefinitionSnapshotTests
    {
        private sealed class World : IExecutionWorld
        {
            private ActionActorState _actor = new ActionActorState
            {
                vitals = new CharacterVitals { currentHp = 10, maxHp = 10 },
            };

            public bool TryGetActor(int hash, out ActionActorState actor)
            {
                actor = _actor;
                return hash == 1;
            }

            public void CommitActor(int hash, in ActionActorState actor) => _actor = actor;
        }

        [TestCase("instance")]
        [TestCase("active")]
        [TestCase("primary")]
        [TestCase("group-active")]
        [TestCase("group-primary")]
        public void ActionDefinitionSnapshot_PublicExecutionViews_CannotRewriteSharedEffectsOrTransitions(string view)
        {
            using ActionExecutionSystem system = new ActionExecutionSystem(new World());
            system.RegisterActor(1);
            int destination = system.RegisterDefinition(new ActionDefinitionData { activeDuration = 2f });
            int source = system.RegisterDefinition(new ActionDefinitionData
            {
                resources = ActionResources.Hands,
                activeDuration = 10f,
                effects = new[] { new ActionEffect { type = ActionEffectType.Invincible, duration = 10f } },
                transitions = new[]
                {
                    new ActionTransition { trigger = ActionSignalKind.Custom, signalId = 7, nextDefinitionId = destination },
                },
            });
            Assert.IsTrue(system.TryStart(new ActionRequest { ownerHash = 1, definitionId = source }, out long instance, out long group));
            ActionExecution execution = ReadView(system, view, instance, group);

            Assert.IsFalse(TryWrite(execution.definition.effects, new ActionEffect { type = ActionEffectType.Armor, duration = 10f, value = 50f }));
            Assert.IsFalse(TryWrite(execution.definition.transitions, new ActionTransition { nextDefinitionId = -1 }));
            Assert.IsTrue(system.EvaluateEffects(1).isInvincible);
            system.Signal(instance, ActionSignalKind.Custom, 7);
            Assert.AreEqual(1, system.PendingCount);

            Assert.IsTrue(system.TryGetInstance(instance, out ActionExecution again));
            Assert.AreSame(execution.definition.effects, again.definition.effects);
            Assert.AreSame(execution.definition.transitions, again.definition.transitions);
        }

        [Test]
        public void ActionDefinitionSnapshot_ChangedSubscriber_CannotRewriteLaterSubscriberOrSimulationDefinition()
        {
            using ActionExecutionSystem system = new ActionExecutionSystem(new World());
            system.RegisterActor(1);
            int definition = system.RegisterDefinition(new ActionDefinitionData
            {
                activeDuration = 10f,
                effects = new[] { new ActionEffect { type = ActionEffectType.Invincible, duration = 10f } },
                transitions = new[] { new ActionTransition { trigger = ActionSignalKind.Custom, nextDefinitionId = 1 } },
            });
            bool writable = false;
            bool laterSawInvincibility = false;
            system.Changed += change =>
            {
                if (change.kind == ActionSignalKind.Started)
                {
                    writable = TryWrite(change.execution.definition.effects, default);
                }
            };
            system.Changed += change =>
            {
                if (change.kind == ActionSignalKind.Started)
                {
                    laterSawInvincibility = change.execution.definition.effects[0].type == ActionEffectType.Invincible;
                }
            };

            Assert.IsTrue(system.TryStart(new ActionRequest { ownerHash = 1, definitionId = definition }, out _));

            Assert.IsFalse(writable);
            Assert.IsTrue(laterSawInvincibility);
            Assert.IsTrue(system.EvaluateEffects(1).isInvincible);
        }

        [Test]
        public void ActionDefinitionSnapshot_ToData_ReturnsIndependentEditableArrays()
        {
            ActionDefinitionData authoring = new ActionDefinitionData
            {
                effects = new[] { new ActionEffect { type = ActionEffectType.Invincible, duration = 2f } },
                transitions = new[] { new ActionTransition { trigger = ActionSignalKind.Completed, nextDefinitionId = 3 } },
            };
            ActionDefinitionSnapshot snapshot = authoring;
            authoring.effects[0] = default;
            authoring.transitions[0] = default;
            ActionDefinitionData editable = snapshot.ToData();
            editable.effects[0] = default;
            editable.transitions[0] = default;

            Assert.AreEqual(ActionEffectType.Invincible, snapshot.effects[0].type);
            Assert.AreEqual(3, snapshot.transitions[0].nextDefinitionId);
            Assert.AreEqual(ActionEffectType.Invincible, snapshot.ToData().effects[0].type);
        }

        [Test]
        public void ActionDefinitionSnapshot_UpdateDefinition_PreservesRunningArraysAndExistingQueuedRequestContract()
        {
            using ActionExecutionSystem system = new ActionExecutionSystem(new World());
            system.RegisterActor(1);
            ActionDefinitionData definition = new ActionDefinitionData
            {
                activeDuration = 10f,
                effects = new[] { new ActionEffect { type = ActionEffectType.Invincible, duration = 10f } },
            };
            int id = system.RegisterDefinition(definition);
            Assert.IsTrue(system.TryStart(new ActionRequest { ownerHash = 1, definitionId = id }, out long running));
            system.Queue(new ActionRequest { ownerHash = 1, definitionId = id });
            definition.effects[0] = new ActionEffect { type = ActionEffectType.SuperArmor, duration = 10f };
            system.UpdateDefinition(id, definition);

            system.Tick(0f);

            Assert.IsTrue(system.TryGetInstance(running, out ActionExecution original));
            Assert.AreEqual(ActionEffectType.Invincible, original.definition.effects[0].type);
            List<ActionExecution> active = new List<ActionExecution>();
            system.CopyActive(1, active);
            Assert.AreEqual(2, active.Count);
            ActionExecution queued = active[0].instanceId == running ? active[1] : active[0];
            Assert.AreEqual(ActionEffectType.SuperArmor, queued.definition.effects[0].type);
            Assert.IsFalse(TryWrite(queued.definition.effects, default));
        }

        [Test]
        public void ActionDefinitionSnapshot_DefaultValue_HasEmptyViewsAndEditableData()
        {
            ActionDefinitionSnapshot snapshot = default;

            Assert.AreEqual(0, snapshot.id);
            Assert.AreEqual(ActionResources.None, snapshot.resources);
            Assert.AreEqual(0f, snapshot.activeDuration);
            Assert.AreEqual(0f, snapshot.moveSpeed);
            Assert.IsEmpty(snapshot.effects);
            Assert.IsEmpty(snapshot.transitions);

            ActionDefinitionData editable = snapshot.ToData();
            Assert.AreEqual(0, editable.id);
            Assert.IsNotNull(editable.effects);
            Assert.IsNotNull(editable.transitions);
            Assert.IsEmpty(editable.effects);
            Assert.IsEmpty(editable.transitions);
        }

        [Test]
        public void ActionDefinitionSnapshot_CopiedValue_EditingAndRecapturePreserveOriginalScalarsAndViews()
        {
            ActionDefinitionSnapshot original = new ActionDefinitionData
            {
                id = 17,
                activeDuration = 2f,
                moveSpeed = 3f,
                effects = new[] { new ActionEffect { type = ActionEffectType.Invincible, duration = 2f } },
                transitions = new[] { new ActionTransition { trigger = ActionSignalKind.Completed, nextDefinitionId = 18 } },
            };
            ActionDefinitionSnapshot copied = original;
            ActionDefinitionData editable = copied.ToData();
            editable.id = 23;
            editable.activeDuration = 4f;
            editable.moveSpeed = 5f;
            editable.effects[0] = new ActionEffect { type = ActionEffectType.SuperArmor, duration = 4f };
            editable.transitions[0] = new ActionTransition { nextDefinitionId = 24 };
            ActionDefinitionSnapshot replacement = editable;

            Assert.AreEqual(17, copied.id);
            Assert.AreEqual(2f, copied.activeDuration);
            Assert.AreEqual(3f, copied.moveSpeed);
            Assert.AreSame(original.effects, copied.effects);
            Assert.AreSame(original.transitions, copied.transitions);
            Assert.AreEqual(ActionEffectType.Invincible, original.effects[0].type);
            Assert.AreEqual(18, original.transitions[0].nextDefinitionId);
            Assert.AreEqual(23, replacement.id);
            Assert.AreEqual(4f, replacement.activeDuration);
            Assert.AreEqual(5f, replacement.moveSpeed);
            Assert.AreEqual(ActionEffectType.SuperArmor, replacement.effects[0].type);
            Assert.AreEqual(24, replacement.transitions[0].nextDefinitionId);
            Assert.AreNotSame(copied.effects, replacement.effects);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ActionDefinitionSnapshot_EquivalentUpdate_ReusesViewsAndKeepsPendingRequests(bool hasEntries)
        {
            using ActionExecutionSystem system = new ActionExecutionSystem(new World());
            system.RegisterActor(1);
            ActionDefinitionData source = new ActionDefinitionData
            {
                activeDuration = 10f,
                effects = hasEntries
                    ? new[] { new ActionEffect { type = ActionEffectType.Invincible, duration = 10f } } : null,
                transitions = hasEntries
                    ? new[] { new ActionTransition { trigger = ActionSignalKind.Custom, nextDefinitionId = 1 } } : null,
            };
            int id = system.RegisterDefinition(source);
            Assert.IsTrue(system.TryStart(new ActionRequest { ownerHash = 1, definitionId = id }, out long first));
            Assert.IsTrue(system.TryGetInstance(first, out ActionExecution original));
            system.Queue(new ActionRequest { ownerHash = 1, definitionId = id });

            // 別配列の同値更新と、nullから空配列への更新をどちらも再捕捉しない。
            ActionDefinitionData equivalent = original.definition.ToData();
            system.UpdateDefinition(id, equivalent);
            Assert.AreEqual(1, system.PendingCount);
            system.Tick(0f);

            List<ActionExecution> active = new List<ActionExecution>();
            system.CopyActive(1, active);
            Assert.AreEqual(2, active.Count);
            ActionExecution pending = active[0].instanceId == first ? active[1] : active[0];
            Assert.AreSame(original.definition.effects, pending.definition.effects);
            Assert.AreSame(original.definition.transitions, pending.definition.transitions);
            Assert.AreEqual(hasEntries, system.EvaluateEffects(1).isInvincible);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ActionDefinitionSnapshot_UpdateWithMutatedInputArray_RecapturesChangedContents(bool changeTransition)
        {
            using ActionExecutionSystem system = new ActionExecutionSystem(new World());
            system.RegisterActor(1);
            ActionDefinitionData source = new ActionDefinitionData
            {
                activeDuration = 10f,
                effects = new[] { new ActionEffect { type = ActionEffectType.Invincible, duration = 10f } },
                transitions = new[] { new ActionTransition { trigger = ActionSignalKind.Custom, signalId = 7, nextDefinitionId = 1 } },
            };
            int id = system.RegisterDefinition(source);
            Assert.IsTrue(system.TryStart(new ActionRequest { ownerHash = 1, definitionId = id }, out long first));
            Assert.IsTrue(system.TryGetInstance(first, out ActionExecution original));
            system.Queue(new ActionRequest { ownerHash = 1, definitionId = id });
            if (changeTransition) { source.transitions[0].signalId = 9; }
            else { source.effects[0].type = ActionEffectType.SuperArmor; }
            system.UpdateDefinition(id, source);
            system.Tick(0f);

            List<ActionExecution> active = new List<ActionExecution>();
            system.CopyActive(1, active);
            Assert.AreEqual(2, active.Count);
            ActionExecution pending = active[0].instanceId == first ? active[1] : active[0];
            Assert.AreNotSame(original.definition.effects, pending.definition.effects);
            Assert.AreNotSame(original.definition.transitions, pending.definition.transitions);
            Assert.AreEqual(ActionEffectType.Invincible, original.definition.effects[0].type);
            Assert.AreEqual(7, original.definition.transitions[0].signalId);
            Assert.AreEqual(changeTransition ? ActionEffectType.Invincible : ActionEffectType.SuperArmor,
                pending.definition.effects[0].type);
            Assert.AreEqual(changeTransition ? 9 : 7, pending.definition.transitions[0].signalId);
        }

        [Test]
        public void ActionDefinitionSnapshot_UnregisterAndRegisterSameId_DoesNotReviveOldPendingRequests()
        {
            using ActionExecutionSystem system = new ActionExecutionSystem(new World());
            system.RegisterActor(1);
            ActionDefinitionData source = new ActionDefinitionData { id = 17, activeDuration = 10f };
            system.RegisterDefinition(source);
            system.Queue(new ActionRequest { ownerHash = 1, definitionId = source.id });
            Assert.AreEqual(1, system.PendingCount);

            Assert.IsTrue(system.UnregisterDefinition(source.id));
            system.RegisterDefinition(source);
            system.Tick(0f);

            Assert.AreEqual(0, system.PendingCount);
            Assert.AreEqual(0, system.ActiveCount);
            Assert.IsTrue(system.TryStart(new ActionRequest { ownerHash = 1, definitionId = source.id }, out _));
            Assert.AreEqual(1, system.ActiveCount);
        }

        private static bool TryWrite<T>(IReadOnlyList<T> values, T replacement)
        {
            if (!(values is IList<T> writable)) { return false; }
            try
            {
                writable[0] = replacement;
                return true;
            }
            catch (NotSupportedException)
            {
                return false;
            }
        }

        private static ActionExecution ReadView(ActionExecutionSystem system, string view, long instance, long group)
        {
            List<ActionExecution> values = new List<ActionExecution>();
            ActionExecution execution;
            switch (view)
            {
                case "instance": Assert.IsTrue(system.TryGetInstance(instance, out execution)); return execution;
                case "active": system.CopyActive(1, values); return values[0];
                case "primary": Assert.IsTrue(system.GetPrimary(1, out execution)); return execution;
                case "group-active": system.CopyGroupActive(group, values); return values[0];
                case "group-primary": Assert.IsTrue(system.GetGroupPrimary(group, out execution)); return execution;
                default: throw new ArgumentOutOfRangeException(nameof(view));
            }
        }
    }
}
