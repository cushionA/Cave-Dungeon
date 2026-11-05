using System;
using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    public class ActionExecutionTests
    {
        private sealed class World : IExecutionWorld
        {
            public readonly Dictionary<int, ActionActorState> actors = new Dictionary<int, ActionActorState>();
            public bool TryGetActor(int hash, out ActionActorState actor) => actors.TryGetValue(hash, out actor);
            public void CommitActor(int hash, in ActionActorState actor) => actors[hash] = actor;
        }

        private World _world;
        private ActionExecutionSystem _system;
        private const int k_Owner = 10;

        [SetUp]
        public void SetUp()
        {
            _world = new World();
            _world.actors[k_Owner] = new ActionActorState
            {
                vitals = new CharacterVitals { currentHp = 100, maxHp = 100, currentStamina = 100f, maxStamina = 100f, currentMp = 30 },
                flags = CharacterFlags.Pack(CharacterBelong.Ally, CharacterFeature.Player, ActState.Neutral, AbilityFlag.None),
            };
            _system = new ActionExecutionSystem(_world);
            _system.RegisterActor(k_Owner);
        }

        [TearDown]
        public void TearDown() => _system.Dispose();

        private static ActionDefinitionData Definition(ActionResources resources = ActionResources.Hands)
        {
            return new ActionDefinitionData
            {
                resources = resources, activeDuration = 1f, cancelAfter = 0f, cancelPoint = -1f,
                endMode = ActionEndMode.Timed, actState = ActState.Attacking, gameplay = ActionGameplayFlags.Attack,
            };
        }

        private bool Start(int definition, out long instance, bool forced = false)
        {
            return _system.TryStart(new ActionRequest
            {
                ownerHash = k_Owner, definitionId = definition, direction = Vector2.right,
                speedMultiplier = 1f, forced = forced,
            }, out instance);
        }

        [Test]
        public void ActionExecution_Start_CommitsCostAndActStateBeforeNotifications()
        {
            ActionDefinitionData data = Definition();
            data.staminaCost = 12f;
            data.mpCost = 4;
            int id = _system.RegisterDefinition(data);
            bool observed = false;
            _system.Changed += change =>
            {
                if (change.kind != ActionSignalKind.Started) return;
                observed = true;
                Assert.AreEqual(88f, _world.actors[k_Owner].vitals.currentStamina);
                Assert.AreEqual(26, _world.actors[k_Owner].vitals.currentMp);
                Assert.AreEqual(ActState.Attacking, _world.actors[k_Owner].flags.ActState);
                Assert.IsTrue(_system.TryGetInstance(change.execution.instanceId, out _));
            };

            Assert.IsTrue(Start(id, out _));
            Assert.IsTrue(observed);
        }

        [TestCase(30f, 30, 4, false, 26f, 86)]
        [TestCase(2f, 30, 4, true, 0f, 0)]
        [TestCase(0f, 0, 0, false, 0f, 0)]
        public void ActionExecution_Start_PublishesSynchronizedMpRatio(float currentMp, int maxMp,
            int cost, bool forced, float expectedMp, int expectedRatio)
        {
            ActionActorState actor = _world.actors[k_Owner];
            actor.vitals.currentMp = currentMp;
            actor.vitals.maxMp = maxMp;
            _world.actors[k_Owner] = actor;
            ActionDefinitionData definition = Definition();
            definition.mpCost = cost;
            int id = _system.RegisterDefinition(definition);
            int notifications = 0;
            _system.Changed += change =>
            {
                if (change.kind != ActionSignalKind.Started) { return; }
                notifications++;
                Assert.AreEqual(expectedMp, _world.actors[k_Owner].vitals.currentMp);
                Assert.AreEqual(expectedRatio, _world.actors[k_Owner].vitals.mpRatio);
            };

            Assert.IsTrue(Start(id, out _, forced));
            Assert.AreEqual(1, notifications);
        }

        [Test]
        public void ActionExecution_InsufficientMp_LeavesSharedResourceAndRatioUnchanged()
        {
            ActionActorState actor = _world.actors[k_Owner];
            actor.vitals.currentMp = 2.5f;
            actor.vitals.maxMp = 30;
            _world.actors[k_Owner] = actor;
            ActionDefinitionData definition = Definition();
            definition.mpCost = 4;
            int id = _system.RegisterDefinition(definition);

            Assert.IsFalse(Start(id, out _));

            Assert.AreEqual(2.5f, _world.actors[k_Owner].vitals.currentMp);
            Assert.AreEqual(8, _world.actors[k_Owner].vitals.mpRatio);
            Assert.AreEqual(0, _system.ActiveCount);
        }

        [Test]
        public void ActionExecution_NonConflictingActions_RunTogetherAndMotionOnlyDoesNotChangeGameplay()
        {
            int attack = _system.RegisterDefinition(Definition(ActionResources.Hands));
            ActionDefinitionData motion = Definition(ActionResources.UpperPose);
            motion.motionOnly = true;
            motion.staminaCost = 1000f;
            motion.mpCost = 1000;
            motion.effects = new[] { new ActionEffect { type = ActionEffectType.Invincible, duration = 10f } };
            int bow = _system.RegisterDefinition(motion);

            Assert.IsTrue(Start(attack, out long attackInstance));
            Assert.IsTrue(Start(bow, out long bowInstance));
            Assert.AreEqual(100f, _world.actors[k_Owner].vitals.currentStamina);
            Assert.AreEqual(ActState.Attacking, _world.actors[k_Owner].flags.ActState);
            Assert.IsFalse(_system.EvaluateEffects(k_Owner).isInvincible);
            _system.Complete(bowInstance);
            Assert.IsTrue(_system.TryGetInstance(attackInstance, out _));
            Assert.AreEqual(ActState.Attacking, _world.actors[k_Owner].flags.ActState);
        }

        [Test]
        public void ActionExecution_MotionOnly_CannotPreemptGameplayEvenWithHighPriority()
        {
            int attack = _system.RegisterDefinition(Definition());
            ActionDefinitionData motion = Definition();
            motion.motionOnly = true;
            motion.priority = 1000;
            int bow = _system.RegisterDefinition(motion);
            Assert.IsTrue(Start(attack, out long active));
            Assert.IsFalse(Start(bow, out _));
            Assert.IsTrue(_system.TryGetInstance(active, out _));
        }

        [Test]
        public void ActionExecution_CancelWindow_AndForcedReactionControlPreemption()
        {
            ActionDefinitionData attack = Definition();
            attack.cancelAfter = 0.5f;
            int attackId = _system.RegisterDefinition(attack);
            ActionDefinitionData next = Definition();
            next.priority = 1;
            int nextId = _system.RegisterDefinition(next);
            Assert.IsTrue(Start(attackId, out long first));
            Assert.IsFalse(Start(nextId, out _));
            _system.Tick(0.5f);
            Assert.IsTrue(Start(nextId, out long second));
            Assert.IsFalse(_system.TryGetInstance(first, out _));
            ActionDefinitionData flinch = Definition(ActionResources.All);
            flinch.actState = ActState.Flinch;
            flinch.priority = -10;
            int flinchId = _system.RegisterDefinition(flinch);
            Assert.IsTrue(Start(flinchId, out long reaction, true));
            Assert.IsFalse(_system.TryGetInstance(second, out _));
            Assert.AreEqual(ActState.Flinch, _world.actors[k_Owner].flags.ActState);
            _system.Complete(reaction);
            Assert.AreEqual(ActState.Neutral, _world.actors[k_Owner].flags.ActState);
        }

        [Test]
        public void ActionExecution_ZeroDurations_NotifyStartAndInitialPhaseBeforeCompletion()
        {
            ActionDefinitionData data = Definition();
            data.activeDuration = 0f;
            int id = _system.RegisterDefinition(data);
            List<ActionSignalKind> events = new List<ActionSignalKind>();
            _system.Changed += change => events.Add(change.kind);
            Assert.IsTrue(Start(id, out long instance));
            Assert.AreEqual(ActionSignalKind.Started, events[0]);
            Assert.AreEqual(ActionSignalKind.PhaseChanged, events[1]);
            Assert.AreEqual(ActionSignalKind.Completed, events[events.Count - 1]);
            Assert.IsFalse(_system.TryGetInstance(instance, out _));
        }

        [Test]
        public void ActionExecution_HeldAction_ContinuesUntilReleaseThenRunsRecovery()
        {
            ActionDefinitionData data = Definition();
            data.endMode = ActionEndMode.Held;
            data.recoveryDuration = 0.25f;
            int id = _system.RegisterDefinition(data);
            Assert.IsTrue(Start(id, out long instance));
            _system.Tick(10f);
            Assert.IsTrue(_system.TryGetInstance(instance, out ActionExecution active));
            Assert.AreEqual(AnimationPhase.Active, active.phase);
            _system.Release(instance);
            Assert.IsTrue(_system.TryGetInstance(instance, out ActionExecution recovery));
            Assert.AreEqual(AnimationPhase.Recovery, recovery.phase);
            _system.Tick(0.25f);
            Assert.IsFalse(_system.TryGetInstance(instance, out _));
        }

        [Test]
        public void ActionExecution_ArmorOwnership_ConsumptionDoesNotLeakAcrossInstancesOrTimeWindows()
        {
            ActionDefinitionData first = Definition(ActionResources.UpperPose);
            first.activeDuration = 10f;
            first.effects = new[]
            {
                new ActionEffect { type = ActionEffectType.Armor, value = 50f, duration = 1f },
                new ActionEffect { type = ActionEffectType.Armor, value = 30f, startTime = 1f, duration = 1f },
            };
            ActionDefinitionData second = Definition(ActionResources.LowerPose);
            second.activeDuration = 10f;
            second.effects = new[] { new ActionEffect { type = ActionEffectType.Armor, value = 20f, duration = 10f } };
            Assert.IsTrue(Start(_system.RegisterDefinition(first), out _));
            _system.ConsumeArmor(k_Owner, 40f);
            Assert.IsTrue(Start(_system.RegisterDefinition(second), out _));
            Assert.AreEqual(30f, _system.EvaluateEffects(k_Owner).actionArmorValue);
            _system.Tick(1f);
            Assert.AreEqual(50f, _system.EvaluateEffects(k_Owner).actionArmorValue);
            Assert.AreEqual(0f, _system.GetConsumedArmor(k_Owner));
        }

        [Test]
        public void ActionExecution_MoveAndInvulnerability_AreIndependentAttributes()
        {
            ActionDefinitionData data = Definition(ActionResources.HorizontalMovement);
            data.gameplay = ActionGameplayFlags.Move;
            data.actState = ActState.Running;
            data.moveSpeed = 8f;
            data.moveDuration = 0.5f;
            data.effects = new[] { new ActionEffect { type = ActionEffectType.Invincible, duration = 0.2f } };
            int id = _system.RegisterDefinition(data);
            Assert.IsTrue(Start(id, out _));
            Assert.IsTrue(_system.TryGetMovement(k_Owner, true, out Vector2 velocity));
            Assert.AreEqual(8f, velocity.x);
            Assert.IsTrue(_system.EvaluateEffects(k_Owner).isInvincible);
            _system.Tick(0.3f);
            Assert.IsFalse(_system.EvaluateEffects(k_Owner).isInvincible);
            Assert.IsTrue(_system.TryGetMovement(k_Owner, true, out velocity));
            Assert.AreEqual(8f, velocity.x);
        }

        [Test]
        public void ActionExecution_ConfirmedHit_QueuesSuccessorAndRejectsLateHits()
        {
            ActionDefinitionData next = Definition();
            int nextId = _system.RegisterDefinition(next);
            ActionDefinitionData first = Definition();
            first.cancelAfter = -1f;
            first.transitions = new[] { new ActionTransition { trigger = ActionSignalKind.HitConfirmed, nextDefinitionId = nextId } };
            int firstId = _system.RegisterDefinition(first);
            Assert.IsTrue(Start(firstId, out long firstInstance));
            _system.Signal(firstInstance, ActionSignalKind.HitConfirmed);
            Assert.IsTrue(_system.TryGetInstance(firstInstance, out _));
            _system.Tick(0f);
            Assert.IsFalse(_system.TryGetInstance(firstInstance, out _));
            Assert.IsTrue(_system.GetPrimary(k_Owner, out ActionExecution primary));
            Assert.AreEqual(nextId, primary.definitionId);
            _system.Signal(firstInstance, ActionSignalKind.HitConfirmed);
            Assert.IsTrue(_system.GetPrimary(k_Owner, out ActionExecution afterLateHit));
            Assert.AreEqual(primary.instanceId, afterLateHit.instanceId);
        }

        [Test]
        public void ActionExecution_CyclicZeroTimeTransitions_ProcessOnlyQueueSnapshotPerTick()
        {
            ActionDefinitionData data = Definition();
            data.id = 100;
            data.activeDuration = 0f;
            data.transitions = new[] { new ActionTransition { trigger = ActionSignalKind.Completed, nextDefinitionId = 100 } };
            int id = _system.RegisterDefinition(data);
            int starts = 0;
            _system.Changed += change => { if (change.kind == ActionSignalKind.Started) starts++; };
            Assert.IsTrue(Start(id, out _));
            Assert.AreEqual(1, starts);
            _system.Tick(0f);
            Assert.AreEqual(2, starts);
            _system.Tick(0f);
            Assert.AreEqual(3, starts);
        }

        [Test]
        public void ActionExecution_UpdatedDefinition_DoesNotMutateRunningSnapshot()
        {
            ActionDefinitionData data = Definition();
            data.activeDuration = 5f;
            int id = _system.RegisterDefinition(data);
            Assert.IsTrue(Start(id, out long running));
            data.activeDuration = 0f;
            _system.UpdateDefinition(id, data);
            _system.Tick(1f);
            Assert.IsTrue(_system.TryGetInstance(running, out ActionExecution snapshot));
            Assert.AreEqual(5f, snapshot.definition.activeDuration);
            _system.Cancel(running, true);
            Assert.IsTrue(Start(id, out long next));
            Assert.IsFalse(_system.TryGetInstance(next, out _));
        }

        [Test]
        public void ActionExecution_FixedLifetime_CanOutlastMotionPhases()
        {
            ActionDefinitionData data = Definition();
            data.activeDuration = 0.1f;
            data.recoveryDuration = 0.1f;
            data.durationMode = ActionDurationMode.Fixed;
            data.duration = 1f;
            Assert.IsTrue(Start(_system.RegisterDefinition(data), out long instance));
            _system.Tick(0.5f);
            Assert.IsTrue(_system.TryGetInstance(instance, out ActionExecution active));
            Assert.AreEqual(AnimationPhase.Recovery, active.phase);
            _system.Tick(0.5f);
            Assert.IsFalse(_system.TryGetInstance(instance, out _));
        }

        [Test]
        public void ActionExecution_FixedLifetimeWithoutMotion_ExecutesMovementAndTimedEffects()
        {
            ActionDefinitionData data = Definition(ActionResources.HorizontalMovement);
            data.gameplay = ActionGameplayFlags.Move;
            data.actState = ActState.Running;
            data.activeDuration = 0f;
            data.durationMode = ActionDurationMode.Fixed;
            data.duration = 0.3f;
            data.moveSpeed = 6f;
            data.effects = new[] { new ActionEffect { type = ActionEffectType.Invincible, duration = 0.1f } };
            Assert.IsTrue(Start(_system.RegisterDefinition(data), out long instance));
            _system.Tick(0.2f);
            Assert.IsTrue(_system.TryGetInstance(instance, out ActionExecution active));
            Assert.AreEqual(AnimationPhase.Active, active.phase);
            Assert.IsFalse(_system.EvaluateEffects(k_Owner).isInvincible);
            Assert.IsTrue(_system.TryGetMovement(k_Owner, true, out Vector2 velocity));
            Assert.AreEqual(6f, velocity.x);
            _system.Tick(0.2f);
            Assert.IsFalse(_system.TryGetInstance(instance, out _));
        }

        [Test]
        public void ActionExecution_ContinuousStamina_InterruptsOnExhaustionWithoutChargingTwice()
        {
            ActionDefinitionData data = Definition();
            data.endMode = ActionEndMode.Held;
            data.staminaCost = 10f;
            data.staminaPerSecond = 20f;
            Assert.IsTrue(Start(_system.RegisterDefinition(data), out long instance));
            _system.Tick(1f);
            Assert.AreEqual(70f, _world.actors[k_Owner].vitals.currentStamina);
            _system.Tick(3.5f);
            Assert.AreEqual(0f, _world.actors[k_Owner].vitals.currentStamina);
            Assert.IsFalse(_system.TryGetInstance(instance, out _));
            Assert.AreEqual(ActState.Neutral, _world.actors[k_Owner].flags.ActState);
        }

        [Test]
        public void ActionExecution_ForcedReaction_BypassesStatusBlockAndPreservesDeadStateAtEnd()
        {
            StatusEffectManager statuses = new StatusEffectManager();
            statuses.Apply(new StatusEffectInfo { effect = StatusEffectId.Stun, duration = 10f });
            ActionActorState actor = _world.actors[k_Owner];
            actor.statusEffects = statuses.GetSummary();
            _world.actors[k_Owner] = actor;
            ActionDefinitionData reaction = Definition(ActionResources.All);
            reaction.actState = ActState.Flinch;
            int id = _system.RegisterDefinition(reaction);
            Assert.IsFalse(Start(id, out _));
            Assert.IsTrue(Start(id, out long instance, true));
            actor = _world.actors[k_Owner];
            actor.flags.ActState = ActState.Dead;
            actor.vitals.currentHp = 0;
            _world.actors[k_Owner] = actor;
            _system.Complete(instance);
            Assert.AreEqual(ActState.Dead, _world.actors[k_Owner].flags.ActState);
        }

        [Test]
        public void ActionExecution_CompletionTransition_RequiresItsOwnHitAndWaitsForCompletion()
        {
            int nextId = _system.RegisterDefinition(Definition());
            ActionDefinitionData first = Definition();
            first.transitions = new[]
            {
                new ActionTransition
                {
                    trigger = ActionSignalKind.HitConfirmed, nextDefinitionId = nextId,
                    requiresHit = true, onCompletion = true,
                }
            };
            int firstId = _system.RegisterDefinition(first);
            Assert.IsTrue(Start(firstId, out long instance));
            _system.Signal(instance, ActionSignalKind.HitConfirmed);
            _system.Tick(0f);
            Assert.IsTrue(_system.TryGetInstance(instance, out _));
            _system.Complete(instance);
            _system.Tick(0f);
            Assert.IsTrue(_system.GetPrimary(k_Owner, out ActionExecution next));
            Assert.AreEqual(nextId, next.definitionId);
        }

        [Test]
        public void ActionExecution_CancelledHitTransition_DoesNotStartAfterItsSourceWasInterrupted()
        {
            int nextId = _system.RegisterDefinition(Definition());
            ActionDefinitionData first = Definition();
            first.transitions = new[] { new ActionTransition { trigger = ActionSignalKind.HitConfirmed, nextDefinitionId = nextId } };
            Assert.IsTrue(Start(_system.RegisterDefinition(first), out long instance));
            _system.Signal(instance, ActionSignalKind.HitConfirmed);
            _system.Cancel(instance, true);
            _system.Tick(0f);
            Assert.IsFalse(_system.IsGameplayActive(k_Owner));
        }

        [Test]
        public void ActionExecution_InterruptedTransition_CanStartItsConfiguredRecoveryAction()
        {
            int nextId = _system.RegisterDefinition(Definition());
            ActionDefinitionData first = Definition();
            first.transitions = new[] { new ActionTransition { trigger = ActionSignalKind.Interrupted, nextDefinitionId = nextId } };
            Assert.IsTrue(Start(_system.RegisterDefinition(first), out long instance));
            _system.Cancel(instance, true);
            _system.Tick(0f);
            Assert.IsTrue(_system.GetPrimary(k_Owner, out ActionExecution next));
            Assert.AreEqual(nextId, next.definitionId);
        }

        [Test]
        public void ActionExecution_StartCallbackCanRemoveActor_WithoutStalePhaseNotification()
        {
            int id = _system.RegisterDefinition(Definition());
            int phases = 0;
            _system.Changed += change =>
            {
                if (change.kind == ActionSignalKind.Started) _system.RemoveActor(k_Owner);
                if (change.kind == ActionSignalKind.PhaseChanged) phases++;
            };
            Assert.IsTrue(Start(id, out long instance));
            Assert.IsFalse(_system.TryGetInstance(instance, out _));
            Assert.AreEqual(0, phases);
        }

        [Test]
        public void ActionExecution_SourceInstanceOfAnotherActor_CannotAuthorizeARequest()
        {
            int id = _system.RegisterDefinition(Definition());
            Assert.IsTrue(Start(id, out long first));
            _world.actors[11] = _world.actors[k_Owner];
            _system.RegisterActor(11);
            Assert.IsFalse(_system.TryStart(new ActionRequest
            {
                ownerHash = 11, definitionId = id, sourceInstanceId = first,
            }, out _));
        }

        [Test]
        public void ActionExecution_UpdateInput_OnlyChangesInputDrivenMovement()
        {
            ActionDefinitionData data = Definition(ActionResources.HorizontalMovement);
            data.gameplay = ActionGameplayFlags.Move;
            data.actState = ActState.Running;
            data.moveSpeed = 4f;
            data.movementUsesInput = true;
            Assert.IsTrue(Start(_system.RegisterDefinition(data), out long input));
            Assert.IsTrue(_system.UpdateInput(input, Vector2.left, 2f));
            Assert.IsTrue(_system.TryGetMovement(k_Owner, true, out Vector2 velocity));
            Assert.AreEqual(-8f, velocity.x);
            _system.Cancel(input, true);
            data.movementUsesInput = false;
            Assert.IsTrue(Start(_system.RegisterDefinition(data), out long fixedDirection));
            Assert.IsFalse(_system.UpdateInput(fixedDirection, Vector2.left));
            Assert.IsTrue(_system.TryGetMovement(k_Owner, true, out velocity));
            Assert.AreEqual(4f, velocity.x);
        }

        [Test]
        public void ActionExecution_DeadActorAndActorRemoval_DoNotReviveOrRunQueuedOldRequests()
        {
            int id = _system.RegisterDefinition(Definition());
            Assert.IsTrue(Start(id, out _));
            _system.Queue(new ActionRequest { ownerHash = k_Owner, definitionId = id });
            _system.RemoveActor(k_Owner);
            _system.RegisterActor(k_Owner);
            _system.Tick(0f);
            Assert.IsFalse(_system.IsGameplayActive(k_Owner));
            ActionActorState actor = _world.actors[k_Owner];
            actor.vitals.currentHp = 0;
            actor.flags.ActState = ActState.Dead;
            _world.actors[k_Owner] = actor;
            Assert.IsFalse(Start(id, out _));
            Assert.AreEqual(ActState.Dead, _world.actors[k_Owner].flags.ActState);
        }

        [Test]
        public void ActionExecution_UnregisterDefinition_DropsQueuedStartsAndKeepsRunningSnapshot()
        {
            ActionDefinitionData data = Definition();
            data.activeDuration = 5f;
            int id = _system.RegisterDefinition(data);
            Assert.IsTrue(Start(id, out long active));
            _system.Queue(new ActionRequest { ownerHash = k_Owner, definitionId = id });

            Assert.IsTrue(_system.UnregisterDefinition(id));
            Assert.IsFalse(_system.UnregisterDefinition(id));
            Assert.AreEqual(0, _system.DefinitionCount);
            Assert.AreEqual(0, _system.PendingCount);
            _system.Tick(1f);
            Assert.IsTrue(_system.TryGetInstance(active, out ActionExecution snapshot));
            Assert.AreEqual(5f, snapshot.definition.activeDuration);
            Assert.IsFalse(Start(id, out _));
        }

        [Test]
        public void ActionExecution_RepeatedTemporaryDefinitions_DoNotRetainDefinitionsOrCompletionHistory()
        {
            ActionDefinitionData data = Definition();
            data.activeDuration = 0f;
            for (int i = 0; i < 100; i++)
            {
                int id = _system.RegisterDefinition(data);
                Assert.IsTrue(Start(id, out _));
                Assert.IsTrue(_system.UnregisterDefinition(id));
            }
            Assert.AreEqual(0, _system.DefinitionCount);
            Assert.AreEqual(0, _system.ActiveCount);
            Assert.AreEqual(0, _system.PendingCount);
            Assert.AreEqual(0, _system.RetainedSourceCount);
        }

        [Test]
        public void ActionExecution_RemoveActor_ImmediatelyClearsPendingTransitionsAndTheirProvenance()
        {
            int next = _system.RegisterDefinition(Definition());
            ActionDefinitionData first = Definition();
            first.activeDuration = 0f;
            first.transitions = new[] { new ActionTransition { trigger = ActionSignalKind.Completed, nextDefinitionId = next } };
            Assert.IsTrue(Start(_system.RegisterDefinition(first), out _));
            Assert.AreEqual(1, _system.PendingCount);
            Assert.AreEqual(1, _system.RetainedSourceCount);

            _system.RemoveActor(k_Owner);

            Assert.AreEqual(0, _system.PendingCount);
            Assert.AreEqual(0, _system.RetainedSourceCount);
            Assert.AreEqual(ActState.Neutral, _world.actors[k_Owner].flags.ActState);
            _system.RegisterActor(k_Owner);
            _system.Tick(0f);
            Assert.AreEqual(0, _system.ActiveCount);
        }

        [TestCase(ActionSignalKind.Started)]
        [TestCase(ActionSignalKind.PhaseChanged)]
        public void ActionExecution_CallbackCanStartForcedReaction_WithoutContinuingOldPhases(ActionSignalKind trigger)
        {
            ActionDefinitionData first = Definition();
            first.recoveryDuration = 1f;
            int firstId = _system.RegisterDefinition(first);
            ActionDefinitionData reaction = Definition(ActionResources.All);
            reaction.actState = ActState.Flinch;
            int reactionId = _system.RegisterDefinition(reaction);
            bool started = false;
            int oldRecovery = 0;
            _system.Changed += change =>
            {
                if (change.execution.definitionId != firstId) return;
                if (change.kind == trigger && !started)
                {
                    started = Start(reactionId, out _, true);
                }
                if (change.kind == ActionSignalKind.PhaseChanged && change.execution.phase == AnimationPhase.Recovery)
                {
                    oldRecovery++;
                }
            };

            Assert.IsTrue(Start(firstId, out long original));

            Assert.IsTrue(started);
            Assert.IsFalse(_system.TryGetInstance(original, out _));
            Assert.AreEqual(ActState.Flinch, _world.actors[k_Owner].flags.ActState);
            Assert.AreEqual(0, oldRecovery);
            Assert.AreEqual(0, _system.RetainedSourceCount);
        }

        [Test]
        public void ActionExecution_CallbackActorRemoval_DoesNotConsumeRequestsQueuedDuringCurrentTick()
        {
            int first = _system.RegisterDefinition(Definition());
            ActionDefinitionData otherData = Definition(ActionResources.HorizontalMovement);
            int deferred = _system.RegisterDefinition(otherData);
            _world.actors[11] = _world.actors[k_Owner];
            _system.RegisterActor(11);
            int starts = 0;
            _system.Changed += change =>
            {
                if (change.kind != ActionSignalKind.Started) return;
                starts++;
                if (change.execution.definitionId == first && change.execution.ownerHash == k_Owner)
                {
                    _system.RemoveActor(11);
                    _system.Queue(new ActionRequest { ownerHash = k_Owner, definitionId = deferred });
                }
            };
            _system.Queue(new ActionRequest { ownerHash = k_Owner, definitionId = first });
            _system.Queue(new ActionRequest { ownerHash = 11, definitionId = first });

            _system.Tick(0f);

            Assert.AreEqual(1, starts);
            Assert.AreEqual(1, _system.PendingCount);
            _system.Tick(0f);
            Assert.AreEqual(2, starts);
        }

        [Test]
        public void ActionExecution_DefinitionIdReusedInsideCallback_DoesNotRunOldQueuedRequest()
        {
            int first = _system.RegisterDefinition(Definition());
            ActionDefinitionData old = Definition(ActionResources.HorizontalMovement);
            old.id = 100;
            _system.RegisterDefinition(old);
            _system.Changed += change =>
            {
                if (change.kind == ActionSignalKind.Started && change.execution.definitionId == first)
                {
                    _system.UnregisterDefinition(100);
                    _system.RegisterDefinition(old);
                }
            };
            _system.Queue(new ActionRequest { ownerHash = k_Owner, definitionId = first });
            _system.Queue(new ActionRequest { ownerHash = k_Owner, definitionId = 100 });

            _system.Tick(0f);

            Assert.AreEqual(1, _system.ActiveCount);
        }

        [Test]
        public void ActionExecution_AttributeOnlyInstance_DoesNotReplaceMovementPrimaryOrState()
        {
            ActionDefinitionData move = Definition(ActionResources.HorizontalMovement);
            move.actState = ActState.Running;
            move.gameplay = ActionGameplayFlags.Move;
            Assert.IsTrue(Start(_system.RegisterDefinition(move), out long moving));
            ActionDefinitionData modifier = Definition(ActionResources.None);
            modifier.actState = ActState.Neutral;
            modifier.gameplay = ActionGameplayFlags.None;
            modifier.priority = 100;
            modifier.endMode = ActionEndMode.Manual;
            modifier.effects = new[] { new ActionEffect { type = ActionEffectType.Invincible, duration = 1f } };
            Assert.IsTrue(Start(_system.RegisterDefinition(modifier), out long attribute));

            Assert.IsTrue(_system.GetPrimary(k_Owner, out ActionExecution primary));
            Assert.AreEqual(moving, primary.instanceId);
            Assert.AreEqual(ActState.Running, _world.actors[k_Owner].flags.ActState);
            Assert.IsTrue(_system.EvaluateEffects(k_Owner).isInvincible);
            _system.Complete(moving);
            Assert.IsFalse(_system.GetPrimary(k_Owner, out _));
            Assert.IsTrue(_system.TryGetInstance(attribute, out _));
            Assert.AreEqual(ActState.Neutral, _world.actors[k_Owner].flags.ActState);
        }

        [Test]
        public void ActionExecution_ActorDiesDuringAction_InterruptsGameplayAndRejectsLateHits()
        {
            ActionDefinitionData attack = Definition();
            attack.endMode = ActionEndMode.Manual;
            Assert.IsTrue(Start(_system.RegisterDefinition(attack), out long active));
            ActionDefinitionData modifier = Definition(ActionResources.None);
            modifier.actState = ActState.Neutral;
            modifier.gameplay = ActionGameplayFlags.None;
            modifier.endMode = ActionEndMode.Manual;
            modifier.effects = new[] { new ActionEffect { type = ActionEffectType.Invincible, duration = 5f } };
            Assert.IsTrue(Start(_system.RegisterDefinition(modifier), out _));
            ActionActorState actor = _world.actors[k_Owner];
            actor.vitals.currentHp = 0;
            _world.actors[k_Owner] = actor;

            Assert.IsFalse(_system.Signal(active, ActionSignalKind.HitConfirmed));
            _system.Tick(0f);

            Assert.AreEqual(0, _system.ActiveCount);
            Assert.IsFalse(_system.EvaluateEffects(k_Owner).isInvincible);
            Assert.AreEqual(ActState.Dead, _world.actors[k_Owner].flags.ActState);
        }

        [Test]
        public void ActionExecution_AttributeOnlyLifecycle_PreservesExistingStateAndChargesCosts()
        {
            ActionActorState actor = _world.actors[k_Owner];
            actor.flags.ActState = ActState.Jumping;
            _world.actors[k_Owner] = actor;
            ActionDefinitionData modifier = Definition(ActionResources.None);
            modifier.actState = ActState.Neutral;
            modifier.gameplay = ActionGameplayFlags.None;
            modifier.staminaCost = 5f;
            int id = _system.RegisterDefinition(modifier);

            Assert.IsTrue(Start(id, out long active));
            Assert.AreEqual(ActState.Jumping, _world.actors[k_Owner].flags.ActState);
            Assert.AreEqual(95f, _world.actors[k_Owner].vitals.currentStamina);
            _system.Tick(1f);

            Assert.IsFalse(_system.TryGetInstance(active, out _));
            Assert.AreEqual(ActState.Jumping, _world.actors[k_Owner].flags.ActState);
        }

        [Test]
        public void ActionExecution_CancelPendingSource_ClearsSuccessorAndProvenance()
        {
            int next = _system.RegisterDefinition(Definition());
            ActionDefinitionData first = Definition();
            first.activeDuration = 0f;
            first.transitions = new[] { new ActionTransition { trigger = ActionSignalKind.Completed, nextDefinitionId = next } };
            Assert.IsTrue(Start(_system.RegisterDefinition(first), out long source));
            Assert.IsTrue(_system.HasPendingFrom(source));

            Assert.AreEqual(1, _system.CancelPendingFrom(source));

            Assert.IsFalse(_system.HasPendingFrom(source));
            Assert.AreEqual(0, _system.RetainedSourceCount);
            _system.Tick(0f);
            Assert.AreEqual(0, _system.ActiveCount);
        }

        [Test]
        public void ActionExecution_CancelPendingSourceFromCallback_ClearsCurrentBatchAndNextTickRequests()
        {
            ActionDefinitionData first = Definition();
            first.endMode = ActionEndMode.Manual;
            int firstId = _system.RegisterDefinition(first);
            int nextId = _system.RegisterDefinition(Definition());
            Assert.IsTrue(Start(firstId, out long source));
            int cancelled = 0;
            _system.Changed += change =>
            {
                if (change.kind == ActionSignalKind.Completed && change.execution.instanceId == source)
                {
                    _system.Queue(new ActionRequest { ownerHash = k_Owner, definitionId = nextId, sourceInstanceId = source });
                    _system.Queue(new ActionRequest { ownerHash = k_Owner, definitionId = nextId, sourceInstanceId = source });
                }
                if (change.kind == ActionSignalKind.Started && change.execution.definitionId == nextId)
                {
                    _system.Queue(new ActionRequest { ownerHash = k_Owner, definitionId = nextId, sourceInstanceId = source });
                    cancelled = _system.CancelPendingFrom(source);
                }
            };
            _system.Complete(source);

            _system.Tick(0f);

            Assert.AreEqual(2, cancelled);
            Assert.IsFalse(_system.HasPendingFrom(source));
            Assert.AreEqual(0, _system.PendingCount);
            Assert.AreEqual(0, _system.RetainedSourceCount);
            Assert.AreEqual(1, _system.ActiveCount);
        }
    }
}
