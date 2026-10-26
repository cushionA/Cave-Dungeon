using System;
using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    public class ActionDefinitionValidationTests
    {
        private sealed class World : IExecutionWorld
        {
            public readonly Dictionary<int, ActionActorState> actors = new Dictionary<int, ActionActorState>();
            public bool TryGetActor(int hash, out ActionActorState actor) => actors.TryGetValue(hash, out actor);
            public void CommitActor(int hash, in ActionActorState actor) => actors[hash] = actor;
        }

        [TestCase("resources")]
        [TestCase("gameplay")]
        [TestCase("actState")]
        [TestCase("effectType")]
        public void ActionDefinition_UnknownEnumsOrFlags_RejectBeforeRegistration(string invalidField)
        {
            using ActionExecutionSystem system = new ActionExecutionSystem(new World());
            ActionDefinitionData definition = ValidDefinition();
            switch (invalidField)
            {
                case "resources": definition.resources = (ActionResources)128; break;
                case "gameplay": definition.gameplay = (ActionGameplayFlags)128; break;
                case "actState": definition.actState = (ActState)255; break;
                case "effectType":
                    definition.effects = new[] { new ActionEffect { type = (ActionEffectType)255, duration = 1f } };
                    break;
            }

            Assert.Throws<ArgumentException>(() => system.RegisterDefinition(definition));
            Assert.AreEqual(0, system.DefinitionCount);
        }

        [TestCase("target")]
        [TestCase("trigger")]
        [TestCase("elapsed")]
        [TestCase("nonFiniteElapsed")]
        public void ActionDefinition_InvalidTransition_RejectBeforeRegistration(string invalidField)
        {
            using ActionExecutionSystem system = new ActionExecutionSystem(new World());
            int next = system.RegisterDefinition(ValidDefinition());
            ActionTransition transition = new ActionTransition
            {
                trigger = ActionSignalKind.Completed, nextDefinitionId = next,
            };
            switch (invalidField)
            {
                case "target": transition.nextDefinitionId = 0; break;
                case "trigger": transition.trigger = (ActionSignalKind)255; break;
                case "elapsed": transition.minElapsed = -1f; break;
                case "nonFiniteElapsed": transition.minElapsed = float.NaN; break;
            }
            ActionDefinitionData definition = ValidDefinition();
            definition.transitions = new[] { transition };

            Assert.Throws<ArgumentException>(() => system.RegisterDefinition(definition));
            Assert.AreEqual(1, system.DefinitionCount);
        }

        [TestCase("phases")]
        [TestCase("movement")]
        [TestCase("effect")]
        public void ActionDefinition_FiniteTimesWithOverflowingEnd_RejectBeforeRegistration(string invalidField)
        {
            using ActionExecutionSystem system = new ActionExecutionSystem(new World());
            ActionDefinitionData definition = ValidDefinition();
            switch (invalidField)
            {
                case "phases":
                    definition.anticipationDuration = float.MaxValue;
                    definition.activeDuration = float.MaxValue;
                    break;
                case "movement":
                    definition.moveStartTime = float.MaxValue;
                    definition.moveDuration = float.MaxValue;
                    break;
                case "effect":
                    definition.effects = new[]
                    {
                        new ActionEffect { type = ActionEffectType.Invincible, startTime = float.MaxValue, duration = float.MaxValue },
                    };
                    break;
            }

            Assert.Throws<ArgumentException>(() => system.RegisterDefinition(definition));
            Assert.AreEqual(0, system.DefinitionCount);
        }

        [TestCase(ActionEndMode.Timed)]
        [TestCase(ActionEndMode.Held)]
        [TestCase(ActionEndMode.Manual)]
        public void ActionDefinition_ZeroTimingAndNegativeCancelSentinels_RemainValid(ActionEndMode endMode)
        {
            using ActionExecutionSystem system = new ActionExecutionSystem(new World());
            ActionDefinitionData definition = new ActionDefinitionData
            {
                endMode = endMode, cancelAfter = -99f, cancelPoint = -99f,
            };

            Assert.Greater(system.RegisterDefinition(definition), 0);
            Assert.AreEqual(1, system.DefinitionCount);
            Assert.IsNull(definition.effects);
            Assert.IsNull(definition.transitions);
        }

        [Test]
        public void ActionDefinition_InvalidUpdate_PreservesOldDefinitionAndActiveExecution()
        {
            World world = new World();
            world.actors.Add(1, new ActionActorState
            {
                vitals = new CharacterVitals { currentHp = 100, maxHp = 100, currentStamina = 100f },
            });
            using ActionExecutionSystem system = new ActionExecutionSystem(world);
            system.RegisterActor(1);
            ActionDefinitionData definition = ValidDefinition();
            int id = system.RegisterDefinition(definition);
            Assert.IsTrue(system.TryStart(new ActionRequest { ownerHash = 1, definitionId = id }, out long first));
            ActionDefinitionData invalidUpdate = definition;
            invalidUpdate.activeDuration = 7f;
            invalidUpdate.transitions = new[]
            {
                new ActionTransition { trigger = ActionSignalKind.Completed, nextDefinitionId = id, minElapsed = -1f },
            };

            Assert.Throws<ArgumentException>(() => system.UpdateDefinition(id, invalidUpdate));
            Assert.IsTrue(system.TryGetInstance(first, out ActionExecution active));
            Assert.AreEqual(0.25f, active.definition.activeDuration);
            system.Tick(0.25f);
            Assert.AreEqual(0, system.ActiveCount);
            Assert.IsTrue(system.TryStart(new ActionRequest { ownerHash = 1, definitionId = id }, out long second));
            Assert.IsTrue(system.TryGetInstance(second, out active));
            Assert.AreEqual(0.25f, active.definition.activeDuration);
            Assert.AreEqual(0, system.PendingCount);
        }

        private static ActionDefinitionData ValidDefinition() => new ActionDefinitionData
        {
            resources = ActionResources.Hands, activeDuration = 0.25f,
            cancelAfter = 0f, cancelPoint = -1f, actState = ActState.Attacking,
        };
    }
}
