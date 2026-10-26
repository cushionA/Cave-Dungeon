using System;
using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    public class ActionExecutionGroupTests
    {
        private sealed class World : IExecutionWorld
        {
            public readonly Dictionary<int, ActionActorState> Actors = new Dictionary<int, ActionActorState>();
            public bool TryGetActor(int hash, out ActionActorState actor) => Actors.TryGetValue(hash, out actor);
            public void CommitActor(int hash, in ActionActorState actor) => Actors[hash] = actor;
        }

        private const int k_Owner = 1;
        private World _world;
        private ActionExecutionSystem _system;
        private readonly List<ActionGroupEvent> _finished = new List<ActionGroupEvent>();

        [SetUp]
        public void SetUp()
        {
            _finished.Clear();
            _world = new World();
            _world.Actors[k_Owner] = new ActionActorState
            {
                vitals = new CharacterVitals { currentHp = 100, maxHp = 100, currentStamina = 100f, maxStamina = 100f },
            };
            _system = new ActionExecutionSystem(_world);
            _system.RegisterActor(k_Owner);
            _system.GroupChanged += change => _finished.Add(change);
        }

        [TearDown]
        public void TearDown() => _system.Dispose();

        private int Definition(ActionResources resources, ActionTransition[] transitions = null)
        {
            return _system.RegisterDefinition(new ActionDefinitionData
            {
                resources = resources, actState = ActState.Attacking,
                endMode = ActionEndMode.Manual, cancelAfter = 0f, cancelPoint = -1f,
                transitions = transitions,
            });
        }

        private long Start(int definition, out long group, long source = 0, bool detached = false)
        {
            Assert.IsTrue(_system.TryStart(new ActionRequest
            {
                ownerHash = k_Owner, definitionId = definition, sourceInstanceId = source, detached = detached,
            }, out long instance, out group));
            return instance;
        }

        private ActorRegistry UseActorRegistry()
        {
            _system.Dispose();
            ActorRegistry actors = new ActorRegistry();
            actors.Register(k_Owner);
            _system = new ActionExecutionSystem(_world, actors);
            _system.RegisterActor(k_Owner);
            _system.GroupChanged += change => _finished.Add(change);
            return actors;
        }

        [Test]
        public void ActionGroup_ParallelChildCompletesFirst_WaitsForRemainingParentAndNotifiesOnce()
        {
            long parent = Start(Definition(ActionResources.Hands), out long group);
            long child = Start(Definition(ActionResources.HorizontalMovement), out long childGroup, parent);
            Assert.AreEqual(group, childGroup);
            Assert.AreEqual(1, _system.ActiveGroupCount);
            _system.Complete(child);

            Assert.IsTrue(_system.IsGroupActive(group));
            Assert.IsTrue(_system.GetGroupPrimary(group, out ActionExecution primary));
            Assert.AreEqual(parent, primary.instanceId);
            Assert.AreEqual(0, _finished.Count);
            _system.Complete(parent);

            Assert.IsFalse(_system.IsGroupActive(group));
            Assert.AreEqual(0, _system.ActiveGroupCount);
            Assert.AreEqual(1, _finished.Count);
            Assert.AreEqual(ActionGroupSignalKind.Completed, _finished[0].kind);
            Assert.IsFalse(_system.Complete(parent));
            Assert.AreEqual(1, _finished.Count);
        }

        [Test]
        public void ActionGroup_CompletionQueuesRequiredChild_RemainsActiveBetweenInstances()
        {
            int childDefinition = Definition(ActionResources.Hands);
            int parentDefinition = Definition(ActionResources.Hands, new[]
            {
                new ActionTransition { trigger = ActionSignalKind.Completed, nextDefinitionId = childDefinition },
            });
            long parent = Start(parentDefinition, out long group);
            _system.Complete(parent);
            Assert.IsTrue(_system.IsGroupActive(group));
            Assert.AreEqual(0, _system.ActiveCount);
            Assert.AreEqual(1, _system.PendingCount);
            Assert.AreEqual(0, _finished.Count);

            _system.Tick(0f);
            Assert.IsTrue(_system.GetGroupPrimary(group, out ActionExecution child));
            Assert.AreEqual(childDefinition, child.definitionId);
            Assert.AreEqual(group, child.groupId);
            _system.Complete(child.instanceId);
            Assert.AreEqual(1, _finished.Count);
            Assert.AreEqual(0, _system.ActiveGroupCount);
        }

        [Test]
        public void ActionGroup_Cancel_CancelsAllParallelMembersAndQueuedRequiredChildren()
        {
            int next = Definition(ActionResources.None);
            int parentDefinition = Definition(ActionResources.Hands, new[]
            {
                new ActionTransition { trigger = ActionSignalKind.Completed, nextDefinitionId = next },
            });
            long parent = Start(parentDefinition, out long group);
            long parallel = Start(Definition(ActionResources.HorizontalMovement), out _, parent);
            _system.Complete(parent);
            Assert.AreEqual(1, _system.PendingCount);

            Assert.IsTrue(_system.CancelGroup(group));

            Assert.IsFalse(_system.TryGetInstance(parallel, out _));
            Assert.AreEqual(0, _system.PendingCount);
            Assert.AreEqual(0, _system.ActiveCount);
            Assert.AreEqual(0, _system.ActiveGroupCount);
            Assert.AreEqual(1, _finished.Count);
            Assert.AreEqual(ActionGroupSignalKind.Cancelled, _finished[0].kind);
            _system.Tick(0f);
            Assert.AreEqual(0, _system.ActiveCount);
            Assert.IsFalse(_system.CancelGroup(group));
            Assert.AreEqual(1, _finished.Count);
        }

        [Test]
        public void ActionGroup_DetachedContinuation_HasIndependentLifetimeAndCancellation()
        {
            int child = Definition(ActionResources.HorizontalMovement);
            long parent = Start(Definition(ActionResources.Hands), out long parentGroup);
            long detached = Start(child, out long detachedGroup, parent, detached: true);

            Assert.AreNotEqual(parentGroup, detachedGroup);
            _system.CancelGroup(parentGroup);
            Assert.IsTrue(_system.TryGetInstance(detached, out _));
            Assert.IsTrue(_system.IsGroupActive(detachedGroup));
            _system.Complete(detached);
            Assert.AreEqual(2, _finished.Count);
            Assert.AreEqual(0, _system.ActiveGroupCount);
        }

        [Test]
        public void ActionGroup_DetachedQueuedContinuation_DoesNotHoldParentOpen()
        {
            int child = Definition(ActionResources.None);
            int parentDefinition = Definition(ActionResources.Hands, new[]
            {
                new ActionTransition { trigger = ActionSignalKind.Completed, nextDefinitionId = child, detached = true },
            });
            long parent = Start(parentDefinition, out long group);
            _system.Complete(parent);

            Assert.IsFalse(_system.IsGroupActive(group));
            Assert.AreEqual(1, _finished.Count);
            Assert.AreEqual(1, _system.ActiveGroupCount);
            _system.Tick(0f);
            List<ActionExecution> active = new List<ActionExecution>();
            _system.CopyActive(k_Owner, active);
            Assert.AreEqual(1, active.Count);
            Assert.AreNotEqual(group, active[0].groupId);
            _system.Complete(active[0].instanceId);
            Assert.AreEqual(2, _finished.Count);
        }

        [Test]
        public void ActionGroup_ActorRemovedAndHashReused_ClosesOldGroupAndRejectsItsReuse()
        {
            int definition = Definition(ActionResources.Hands);
            Start(definition, out long oldGroup);
            _system.RemoveActor(k_Owner);
            _system.RegisterActor(k_Owner);
            Assert.IsFalse(_system.TryStart(new ActionRequest
            {
                ownerHash = k_Owner, definitionId = definition, groupId = oldGroup,
            }, out _));
            long next = Start(definition, out long nextGroup);
            Assert.AreNotEqual(oldGroup, nextGroup);
            Assert.AreEqual(ActionGroupSignalKind.Cancelled, _finished[0].kind);
            Assert.IsTrue(_system.TryGetInstance(next, out _));
            Assert.AreEqual(1, _system.ActiveGroupCount);
        }

        [Test]
        public void ActionGroup_CompletionCallbackQueuesChild_InheritsBeforeParentIsReleased()
        {
            int child = Definition(ActionResources.Hands);
            long parent = Start(Definition(ActionResources.Hands), out long group);
            _system.Changed += change =>
            {
                if (change.kind == ActionSignalKind.Completed && change.execution.instanceId == parent)
                {
                    _system.Queue(new ActionRequest { ownerHash = k_Owner, definitionId = child, sourceInstanceId = parent });
                }
            };

            _system.Complete(parent);
            Assert.IsTrue(_system.IsGroupActive(group));
            Assert.AreEqual(0, _finished.Count);
            _system.Tick(0f);
            Assert.IsTrue(_system.GetGroupPrimary(group, out ActionExecution execution));
            _system.Complete(execution.instanceId);
            Assert.AreEqual(1, _finished.Count);
        }

        [Test]
        public void ActionGroup_ReentrantCancellationFromStarted_NotifiesOnceWithoutLeaking()
        {
            _system.Changed += change =>
            {
                if (change.kind == ActionSignalKind.Started) { _system.CancelGroup(change.execution.groupId); }
            };
            Start(Definition(ActionResources.Hands), out long group);
            Assert.IsFalse(_system.IsGroupActive(group));
            Assert.AreEqual(1, _finished.Count);
            Assert.AreEqual(ActionGroupSignalKind.Cancelled, _finished[0].kind);
            Assert.AreEqual(0, _system.ActiveCount);
            Assert.AreEqual(0, _system.PendingCount);
        }

        [Test]
        public void ActionGroup_QueuedDefinitionRemoved_ReleasesPendingMembership()
        {
            int child = Definition(ActionResources.Hands);
            int parentDefinition = Definition(ActionResources.Hands, new[]
            {
                new ActionTransition { trigger = ActionSignalKind.Completed, nextDefinitionId = child },
            });
            long parent = Start(parentDefinition, out long group);
            _system.Complete(parent);
            _system.UnregisterDefinition(child);
            Assert.IsFalse(_system.IsGroupActive(group));
            Assert.AreEqual(0, _system.PendingCount);
            Assert.AreEqual(1, _finished.Count);
        }

        [Test]
        public void ActionGroup_ZeroDurationRoot_ReturnsSettledGroupAndLeavesNoRetainedState()
        {
            int definition = _system.RegisterDefinition(new ActionDefinitionData { cancelPoint = -1f });
            Start(definition, out long group);
            Assert.IsFalse(_system.IsGroupActive(group));
            Assert.AreEqual(0, _system.ActiveGroupCount);
            Assert.AreEqual(1, _finished.Count);
            Assert.AreEqual(0, _system.RetainedSourceCount);
        }

        [Test]
        public void ActionGroup_ChargeMultiplier_IsCapturedAndInheritedByRequiredContinuation()
        {
            int childDefinition = Definition(ActionResources.Hands);
            int parentDefinition = Definition(ActionResources.Hands, new[]
            {
                new ActionTransition { trigger = ActionSignalKind.Completed, nextDefinitionId = childDefinition },
            });
            Assert.IsTrue(_system.TryStart(new ActionRequest
            {
                ownerHash = k_Owner, definitionId = parentDefinition, chargeMultiplier = 2.5f,
            }, out long parent, out long group));
            Assert.IsTrue(_system.TryGetInstance(parent, out ActionExecution root));
            Assert.AreEqual(2.5f, root.chargeMultiplier);
            _system.Complete(parent);
            _system.Tick(0f);
            Assert.IsTrue(_system.GetGroupPrimary(group, out ActionExecution child));
            Assert.AreEqual(2.5f, child.chargeMultiplier);
            Assert.AreEqual(group, child.groupId);
        }

        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void ActionGroup_InvalidChargeMultiplier_DoesNotCreateAGroupOrConsumeResources(float multiplier)
        {
            int definition = Definition(ActionResources.Hands);
            Assert.IsFalse(_system.TryStart(new ActionRequest
            {
                ownerHash = k_Owner, definitionId = definition, chargeMultiplier = multiplier,
            }, out _, out long group));
            Assert.AreEqual(0, group);
            Assert.AreEqual(0, _system.ActiveGroupCount);
            Assert.AreEqual(100f, _world.Actors[k_Owner].vitals.currentStamina);
        }

        [Test]
        public void ActionGroup_QueuedTargetReused_RejectsOldIdentityAndReleasesItsGroup()
        {
            ActorRegistry actors = UseActorRegistry();
            ActorRef oldTarget = actors.Register(2);
            int definition = Definition(ActionResources.Hands);
            _system.Queue(new ActionRequest
            {
                ownerHash = k_Owner, targetHash = 2, targetRef = oldTarget, definitionId = definition,
            });
            Assert.AreEqual(1, _system.ActiveGroupCount);
            actors.Unregister(2);
            ActorRef replacement = actors.Register(2);
            Assert.AreNotEqual(oldTarget, replacement);

            _system.Tick(0f);

            Assert.AreEqual(0, _system.ActiveCount);
            Assert.AreEqual(0, _system.PendingCount);
            Assert.AreEqual(0, _system.ActiveGroupCount);
            Assert.AreEqual(1, _finished.Count);
            Assert.AreEqual(ActionGroupSignalKind.Cancelled, _finished[0].kind);
        }

        [Test]
        public void ActionGroup_ActiveOwnerIdentityReused_CancelsWithoutWritingIntoReplacement()
        {
            ActorRegistry actors = UseActorRegistry();
            int definition = Definition(ActionResources.Hands);
            Start(definition, out long group);
            actors.Unregister(k_Owner);
            ActorRef replacement = actors.Register(k_Owner);
            ActionActorState actor = _world.Actors[k_Owner];
            actor.vitals.currentHp = 77;
            actor.flags.ActState = ActState.Guarding;
            _world.Actors[k_Owner] = actor;

            _system.Tick(0f);

            Assert.IsFalse(_system.IsGroupActive(group));
            Assert.AreEqual(0, _system.ActiveCount);
            Assert.AreEqual(77, _world.Actors[k_Owner].vitals.currentHp);
            Assert.AreEqual(ActState.Guarding, _world.Actors[k_Owner].flags.ActState);
            Assert.AreNotEqual(replacement, _finished[0].ownerRef);
            Assert.AreEqual(ActionGroupSignalKind.Cancelled, _finished[0].kind);
        }

        [Test]
        public void ActionGroup_CompletionCallbackStartsZeroDurationRoot_SettlesEachGroupOnce()
        {
            long parent = Start(Definition(ActionResources.Hands), out long group);
            int zero = _system.RegisterDefinition(new ActionDefinitionData { cancelPoint = -1f });
            long nextGroup = 0;
            _system.GroupChanged += change =>
            {
                if (change.groupId == group)
                {
                    Assert.IsTrue(_system.TryStart(new ActionRequest { ownerHash = k_Owner, definitionId = zero },
                        out _, out nextGroup));
                }
            };
            _system.Complete(parent);
            Assert.AreNotEqual(group, nextGroup);
            Assert.AreEqual(2, _finished.Count);
            Assert.AreEqual(0, _system.ActiveGroupCount);
            Assert.AreEqual(0, _system.ActiveCount);
        }

        [Test]
        public void ActionGroup_VanishedOwner_RejectsNewRequestAndAcceptsAgainAfterReturn()
        {
            ActionActorState actor = _world.Actors[k_Owner];
            actor.flags.IsVanished = true;
            _world.Actors[k_Owner] = actor;
            int definition = Definition(ActionResources.Hands);
            Assert.IsFalse(_system.TryStart(new ActionRequest { ownerHash = k_Owner, definitionId = definition }, out _));
            Assert.AreEqual(0, _system.ActiveGroupCount);
            Assert.AreEqual(100f, _world.Actors[k_Owner].vitals.currentStamina);

            actor.flags.IsVanished = false;
            _world.Actors[k_Owner] = actor;
            Start(definition, out long group);
            Assert.IsTrue(_system.IsGroupActive(group));
            actor = _world.Actors[k_Owner];
            actor.flags.IsVanished = true;
            _world.Actors[k_Owner] = actor;
            _system.Tick(0f);
            Assert.IsTrue(_system.IsGroupActive(group));
        }

        [Test]
        public void ActionGroup_VanishedAfterAcceptance_AllowsFreeRequiredRecoveryInTheSameGroup()
        {
            long parent = Start(Definition(ActionResources.Hands), out long group);
            ActionActorState actor = _world.Actors[k_Owner];
            actor.flags.IsVanished = true;
            _world.Actors[k_Owner] = actor;

            long recovery = Start(Definition(ActionResources.Hands), out long recoveryGroup, parent);

            Assert.AreEqual(group, recoveryGroup);
            Assert.IsFalse(_system.TryGetInstance(parent, out _));
            Assert.IsTrue(_system.IsGroupActive(group));
            Assert.AreEqual(0, _finished.Count);
            _system.Complete(recovery);
            Assert.AreEqual(1, _finished.Count);
            Assert.AreEqual(ActionGroupSignalKind.Completed, _finished[0].kind);
        }

        [TestCase(1f, 0, 0f)]
        [TestCase(0f, 1, 0f)]
        [TestCase(0f, 0, 1f)]
        public void ActionGroup_VanishedOwner_DoesNotStartPaidOrSustainedCostContinuations(float stamina, int mp, float upkeep)
        {
            long parent = Start(Definition(ActionResources.Hands), out long group);
            ActionActorState actor = _world.Actors[k_Owner];
            actor.flags.IsVanished = true;
            actor.vitals.currentMp = 10f;
            _world.Actors[k_Owner] = actor;
            int child = _system.RegisterDefinition(new ActionDefinitionData
            {
                resources = ActionResources.Hands, endMode = ActionEndMode.Manual, cancelPoint = -1f,
                staminaCost = stamina, mpCost = mp, staminaPerSecond = upkeep,
            });

            Assert.IsFalse(_system.TryStart(new ActionRequest
            {
                ownerHash = k_Owner, definitionId = child, sourceInstanceId = parent, groupId = group,
            }, out _));
            Assert.IsTrue(_system.TryGetInstance(parent, out _));
            Assert.AreEqual(100f, _world.Actors[k_Owner].vitals.currentStamina);
            Assert.AreEqual(10f, _world.Actors[k_Owner].vitals.currentMp);
            Assert.AreEqual(1, _system.ActiveGroupCount);
        }

        [Test]
        public void ActionGroup_VanishedOwner_DoesNotAcceptDetachedOrForgedContinuation()
        {
            int definition = Definition(ActionResources.Hands);
            long parent = Start(definition, out long group);
            _world.Actors[2] = new ActionActorState { vitals = new CharacterVitals { currentHp = 100, maxHp = 100 } };
            _system.RegisterActor(2);
            Assert.IsTrue(_system.TryStart(new ActionRequest { ownerHash = 2, definitionId = definition }, out long foreign));
            ActionActorState actor = _world.Actors[k_Owner];
            actor.flags.IsVanished = true;
            _world.Actors[k_Owner] = actor;
            Assert.IsFalse(_system.TryStart(new ActionRequest
            {
                ownerHash = k_Owner, definitionId = definition, sourceInstanceId = parent, detached = true,
            }, out _));
            Assert.IsFalse(_system.TryStart(new ActionRequest
            {
                ownerHash = k_Owner, definitionId = definition, groupId = group,
            }, out _));
            Assert.IsFalse(_system.TryStart(new ActionRequest
            {
                ownerHash = k_Owner, definitionId = definition, sourceInstanceId = parent, groupId = group + 100,
            }, out _));
            Assert.IsFalse(_system.TryStart(new ActionRequest
            {
                ownerHash = k_Owner, definitionId = definition, sourceInstanceId = parent + 100, groupId = group,
            }, out _));
            Assert.IsFalse(_system.TryStart(new ActionRequest
            {
                ownerHash = k_Owner, definitionId = definition, sourceInstanceId = foreign, groupId = group,
            }, out _));
            Assert.IsTrue(_system.TryGetInstance(parent, out _));
            Assert.AreEqual(2, _system.ActiveGroupCount);
            Assert.AreEqual(0, _finished.Count);
        }

        [Test]
        public void ActionGroup_VanishedOwner_DoesNotReuseExpiredSourceEvenWhenItsGroupStillRuns()
        {
            int definition = Definition(ActionResources.Hands);
            long parent = Start(definition, out long group);
            long parallel = Start(Definition(ActionResources.HorizontalMovement), out _, parent);
            _system.Complete(parent);
            Assert.IsFalse(_system.TryGetGroupId(parent, out _));
            ActionActorState actor = _world.Actors[k_Owner];
            actor.flags.IsVanished = true;
            _world.Actors[k_Owner] = actor;

            Assert.IsFalse(_system.TryStart(new ActionRequest
            {
                ownerHash = k_Owner, definitionId = definition, sourceInstanceId = parent, groupId = group,
            }, out _));
            Assert.IsTrue(_system.TryGetInstance(parallel, out _));
            Assert.AreEqual(1, _system.ActiveGroupCount);
        }

        [Test]
        public void ActionGroup_CancellationInProgress_DoesNotAuthorizeEffectsFromStillListedMembers()
        {
            long parent = Start(Definition(ActionResources.Hands), out long group);
            long child = Start(Definition(ActionResources.HorizontalMovement), out _, parent);
            bool checkedRemainingMember = false;
            Assert.IsTrue(_system.CanApplyExecutionEffects(parent));
            Assert.IsTrue(_system.CanApplyExecutionEffects(child));
            _system.Changed += change =>
            {
                if (change.kind != ActionSignalKind.Interrupted) { return; }
                Assert.IsFalse(_system.CanApplyExecutionEffects(parent));
                Assert.IsFalse(_system.CanApplyExecutionEffects(child));
                checkedRemainingMember |= _system.TryGetInstance(parent, out _) || _system.TryGetInstance(child, out _);
            };
            _system.CancelGroup(group);
            Assert.IsTrue(checkedRemainingMember);
        }
    }
}
