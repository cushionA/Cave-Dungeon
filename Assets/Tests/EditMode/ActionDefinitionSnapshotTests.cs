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
