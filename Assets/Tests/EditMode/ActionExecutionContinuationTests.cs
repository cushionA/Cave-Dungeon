using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    public class ActionExecutionContinuationTests
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

        [SetUp]
        public void SetUp()
        {
            _world = new World();
            _world.Actors[k_Owner] = new ActionActorState
            {
                vitals = new CharacterVitals
                {
                    currentHp = 100, maxHp = 100, currentStamina = 100f, maxStamina = 100f,
                    currentMp = 10f, maxMp = 10,
                },
            };
            _system = new ActionExecutionSystem(_world);
            _system.RegisterActor(k_Owner);
        }

        [TearDown]
        public void TearDown() => _system.Dispose();

        private int Define(ActionResources resources, int priority = 10, float cancelAfter = 0f,
            float cost = 0f, ActState state = ActState.Attacking, ActionTransition[] transitions = null)
        {
            return _system.RegisterDefinition(new ActionDefinitionData
            {
                resources = resources, priority = priority, cancelAfter = cancelAfter, cancelPoint = -1f,
                staminaCost = cost, actState = state, endMode = ActionEndMode.Manual, transitions = transitions,
            });
        }

        private long Start(int definition, out long group, long source = 0)
        {
            Assert.IsTrue(_system.TryStart(new ActionRequest
            {
                ownerHash = k_Owner, definitionId = definition, sourceInstanceId = source,
            }, out long instance, out group));
            return instance;
        }

        [Test]
        public void ActionContinuation_NaturalCompletion_InsertsRecoveryWithoutNeutralGapAndWaitsBeforeAuthoredNext()
        {
            int after = Define(ActionResources.Hands);
            int interruptedNext = Define(ActionResources.None);
            int parentDefinition = Define(ActionResources.Hands, transitions: new[]
            {
                new ActionTransition { trigger = ActionSignalKind.Completed, nextDefinitionId = after, minElapsed = 0.5f },
                new ActionTransition { trigger = ActionSignalKind.Interrupted, nextDefinitionId = interruptedNext },
            });
            long parent = Start(parentDefinition, out long group);
            _system.Tick(0.6f);
            int recoveryDefinition = Define(ActionResources.Hands, priority: 20, state: ActState.Guarding);
            int completed = 0;
            int interrupted = 0;
            int groupCompleted = 0;
            _system.GroupChanged += _ => groupCompleted++;
            _system.Changed += change =>
            {
                if (change.execution.instanceId != parent) { return; }
                if (change.kind == ActionSignalKind.Interrupted) { interrupted++; }
                if (change.kind == ActionSignalKind.Completed)
                {
                    completed++;
                    Assert.AreEqual(ActState.Guarding, _world.Actors[k_Owner].flags.ActState);
                    Assert.IsTrue(_system.IsGroupActive(group));
                }
            };

            Assert.IsTrue(_system.TryCompleteAndContinue(parent, new ActionRequest
            {
                ownerHash = k_Owner, definitionId = recoveryDefinition,
            }, out long recovery, out long recoveryGroup));
            Assert.AreEqual(group, recoveryGroup);
            Assert.AreEqual(1, completed);
            Assert.AreEqual(0, interrupted);
            Assert.AreEqual(0, groupCompleted);
            for (int i = 0; i < 3; i++)
            {
                _system.Tick(0f);
                Assert.AreEqual(1, _system.PendingCount);
                Assert.IsTrue(_system.TryGetInstance(recovery, out _));
                Assert.AreEqual(1, _system.ActiveCount);
            }

            _system.Complete(recovery);
            Assert.IsTrue(_system.IsGroupActive(group));
            _system.Tick(0f);
            Assert.IsTrue(_system.GetGroupPrimary(group, out ActionExecution successor));
            Assert.AreEqual(after, successor.definitionId);
            Assert.AreEqual(recovery, successor.sourceInstanceId);
            _system.Complete(successor.instanceId);
            Assert.AreEqual(1, groupCompleted);
            Assert.AreEqual(0, _system.PendingCount);
            Assert.AreEqual(0, _system.ActiveGroupCount);
        }

        [Test]
        public void ActionContinuation_RejectedForCost_LeavesSourceAndResourcesUnchanged()
        {
            long parent = Start(Define(ActionResources.Hands), out long group);
            int recovery = Define(ActionResources.Hands, cost: 101f);
            int notifications = 0;
            _system.Changed += _ => notifications++;

            Assert.IsFalse(_system.TryCompleteAndContinue(parent,
                new ActionRequest { ownerHash = k_Owner, definitionId = recovery }, out _, out _));

            Assert.IsTrue(_system.TryGetInstance(parent, out _));
            Assert.IsTrue(_system.IsGroupActive(group));
            Assert.AreEqual(100f, _world.Actors[k_Owner].vitals.currentStamina);
            Assert.AreEqual(ActState.Attacking, _world.Actors[k_Owner].flags.ActState);
            Assert.AreEqual(0, notifications);
        }

        [Test]
        public void ActionContinuation_NaturalSourceAndOtherConflict_EmitCompletedAndInterruptedRespectively()
        {
            long parent = Start(Define(ActionResources.Hands), out long group);
            long other = Start(Define(ActionResources.HorizontalMovement, priority: 1), out long otherGroup);
            int recovery = Define(ActionResources.Hands | ActionResources.HorizontalMovement, priority: 20);
            Dictionary<long, ActionSignalKind> endings = new Dictionary<long, ActionSignalKind>();
            _system.Changed += change =>
            {
                if (change.kind == ActionSignalKind.Completed || change.kind == ActionSignalKind.Interrupted)
                {
                    endings[change.execution.instanceId] = change.kind;
                }
            };

            Assert.IsTrue(_system.TryCompleteAndContinue(parent,
                new ActionRequest { ownerHash = k_Owner, definitionId = recovery }, out _, out long nextGroup));

            Assert.AreEqual(group, nextGroup);
            Assert.AreEqual(ActionSignalKind.Completed, endings[parent]);
            Assert.AreEqual(ActionSignalKind.Interrupted, endings[other]);
            Assert.IsFalse(_system.IsGroupActive(otherGroup));
            Assert.IsTrue(_system.IsGroupActive(group));
        }

        [Test]
        public void ActionContinuation_RecoveryInterrupted_DiscardsAuthoredCompletedSuccessorAndRetainedState()
        {
            int after = Define(ActionResources.Hands);
            int parentDefinition = Define(ActionResources.Hands, transitions: new[]
            {
                new ActionTransition { trigger = ActionSignalKind.Completed, nextDefinitionId = after },
            });
            long parent = Start(parentDefinition, out long group);
            int recoveryDefinition = Define(ActionResources.Hands);
            Assert.IsTrue(_system.TryCompleteAndContinue(parent,
                new ActionRequest { ownerHash = k_Owner, definitionId = recoveryDefinition }, out long recovery));
            _system.Tick(0f);
            Assert.AreEqual(1, _system.PendingCount);
            _system.Cancel(recovery, true);
            _system.Tick(0f);

            Assert.IsFalse(_system.IsGroupActive(group));
            Assert.AreEqual(0, _system.ActiveCount);
            Assert.AreEqual(0, _system.PendingCount);
            Assert.AreEqual(0, _system.RetainedSourceCount);
        }

        [Test]
        public void ActionGroupReplacement_NonConflictingButNonCancelableMember_RejectsBeforeChangingAnything()
        {
            long previous = Start(Define(ActionResources.Hands, cancelAfter: -1f), out long group);
            int move = Define(ActionResources.HorizontalMovement, priority: 20, cost: 5f);
            Assert.IsFalse(_system.TryStart(new ActionRequest
            {
                ownerHash = k_Owner, definitionId = move, replaceGroupId = group,
            }, out _, out long rejectedGroup));

            Assert.AreEqual(0, rejectedGroup);
            Assert.IsTrue(_system.TryGetInstance(previous, out _));
            Assert.IsTrue(_system.IsGroupActive(group));
            Assert.AreEqual(1, _system.ActiveGroupCount);
            Assert.AreEqual(100f, _world.Actors[k_Owner].vitals.currentStamina);
        }

        [Test]
        public void ActionGroupReplacement_OneParallelMemberRefuses_LeavesEveryMemberAndPendingRequest()
        {
            long parent = Start(Define(ActionResources.Hands, priority: 1), out long group);
            long protectedChild = Start(Define(ActionResources.HorizontalMovement, priority: 30, cancelAfter: -1f), out _, parent);
            int next = Define(ActionResources.None, priority: 20, cost: 5f);
            Assert.IsFalse(_system.TryStart(new ActionRequest
            {
                ownerHash = k_Owner, definitionId = next, replaceGroupId = group,
            }, out _));

            Assert.IsTrue(_system.TryGetInstance(parent, out _));
            Assert.IsTrue(_system.TryGetInstance(protectedChild, out _));
            Assert.AreEqual(2, _system.ActiveCount);
            Assert.AreEqual(100f, _world.Actors[k_Owner].vitals.currentStamina);
        }

        [Test]
        public void ActionGroupReplacement_ZeroDurationStop_UsesCommonPermissionAndClearsOldActiveAndPending()
        {
            int after = Define(ActionResources.None);
            int parentDefinition = Define(ActionResources.Hands, priority: 1, transitions: new[]
            {
                new ActionTransition { trigger = ActionSignalKind.Completed, nextDefinitionId = after },
            });
            long parent = Start(parentDefinition, out long group);
            long parallel = Start(Define(ActionResources.HorizontalMovement, priority: 1), out _, parent);
            _system.Complete(parent);
            int stop = _system.RegisterDefinition(new ActionDefinitionData { priority = 20, cancelPoint = -1f });

            Assert.IsTrue(_system.TryStart(new ActionRequest
            {
                ownerHash = k_Owner, definitionId = stop, replaceGroupId = group,
            }, out _, out long stopGroup));

            Assert.AreNotEqual(group, stopGroup);
            Assert.IsFalse(_system.IsGroupActive(group));
            Assert.IsFalse(_system.TryGetInstance(parallel, out _));
            Assert.IsFalse(_system.IsGroupActive(stopGroup));
            Assert.AreEqual(0, _system.PendingCount);
            Assert.AreEqual(0, _system.ActiveCount);
            _system.Tick(0f);
            Assert.AreEqual(0, _system.ActiveCount);
        }

        [Test]
        public void ActionGroupReplacement_ForeignOrForgedGroup_IsRejectedWithoutCharging()
        {
            long previous = Start(Define(ActionResources.Hands), out long group);
            int next = Define(ActionResources.None, priority: 100, cost: 5f);
            _world.Actors[2] = new ActionActorState { vitals = new CharacterVitals { currentHp = 100, currentStamina = 100f } };
            _system.RegisterActor(2);
            Assert.IsFalse(_system.TryStart(new ActionRequest
            {
                ownerHash = 2, definitionId = next, replaceGroupId = group, forced = true,
            }, out _));
            Assert.IsFalse(_system.TryStart(new ActionRequest
            {
                ownerHash = k_Owner, definitionId = next, replaceGroupId = group + 100,
            }, out _));
            Assert.AreEqual(100f, _world.Actors[2].vitals.currentStamina);
            Assert.AreEqual(100f, _world.Actors[k_Owner].vitals.currentStamina);
            Assert.IsTrue(_system.TryGetInstance(previous, out _));
        }
    }
}
