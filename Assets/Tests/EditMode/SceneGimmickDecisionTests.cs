using System;
using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    public class SceneGimmickDecisionTests
    {
        private const int Hazard = 1 << 4;
        private const int Owner = -1;
        private const int Ally = 2;
        private const int NearestEnemy = 10;
        private const int PreviousEnemy = 20;

        private GameManagerCore _core;
        private AIObservationCoordinator _observations;
        private JudgmentLoop _loop;
        private CommandRecorder _backend;
        private long _tick;

        [SetUp]
        public void SetUp()
        {
            _core = new GameManagerCore();
            _core.Initialize(8);
            _observations = new AIObservationCoordinator(_core.Data, _core.Actors, _core.Events);
            Register(Owner, CharacterBelong.Ally, Vector2.zero);
            Register(Ally, CharacterBelong.Ally, new Vector2(6f, 3f));
            Register(NearestEnemy, CharacterBelong.Enemy, new Vector2(6f, 0f));
            Register(PreviousEnemy, CharacterBelong.Enemy, new Vector2(9f, 0f));

            _backend = new CommandRecorder();
            ActionExecutor executor = new ActionExecutor();
            executor.SetExecutionBackend(_backend);
            _loop = new JudgmentLoop(executor, _core.Data, Owner);
        }

        [TearDown]
        public void TearDown()
        {
            _loop.Dispose();
            _observations.Dispose();
            _core.Dispose();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void InteractObject_SwitchesTargetAndSelectsSameSafeGimmickInEitherOrder(bool scheduled)
        {
            SetMode(new ActionSlot { execType = ActionExecType.Instant, paramId = (int)InstantAction.InteractObject },
                CompareOp.HasAny, Hazard);
            Capture();
            Decide(scheduled, PreviousEnemy);
            Assert.AreEqual(PreviousEnemy, _loop.CurrentTargetHash);

            AISceneGimmick wrongTarget = Gimmick(1, 0f, new Vector2(9f, 0f));
            AISceneGimmick hitsAlly = Gimmick(2, 0f, new Vector2(6f, 1.5f));
            hitsAlly.effectRadius = 2f;
            AISceneGimmick wrongType = Gimmick(3, 0f, new Vector2(6f, 0f));
            wrongType.objectMask = 1 << 5;
            AISceneGimmick rightSwitch = Gimmick(71, 1f, new Vector2(6f, 0f));
            AISceneGimmick leftSwitch = Gimmick(19, -1f, new Vector2(6f, 0f));
            rightSwitch.requiresClearOperation = true;
            leftSwitch.requiresClearOperation = true;
            AISceneGimmick behindWall = Gimmick(8, 0f, new Vector2(6f, 0f));
            behindWall.requiresClearOperation = true;
            behindWall.operationRadius = 8f;
            AISceneGimmick[] switches = { wrongTarget, hitsAlly, wrongType, behindWall, rightSwitch, leftSwitch };

            for (int order = 0; order < 2; ++order)
            {
                Capture(switches);
                AIObservationContext observation = _observations.Observation;
                observation.SetGimmickOperationVisible(Actor(Ally), behindWall);
                observation.SetGimmickOperationVisible(Actor(Owner), rightSwitch);
                observation.SetGimmickOperationVisible(Actor(Owner), leftSwitch);
                Decide(scheduled, PreviousEnemy, NearestEnemy);

                Assert.AreEqual(NearestEnemy, _loop.CurrentTargetHash);
                Assert.AreEqual(1, _backend.Commands.Count);
                ActionCommand command = _backend.Commands[0];
                Assert.AreEqual(ActionCommandKind.InteractObject, command.kind);
                Assert.AreEqual(leftSwitch.id, command.gimmick.id);
                Assert.AreEqual(leftSwitch.generation, command.gimmick.generation);
                Assert.AreEqual(_tick, command.observationTick);
                Assert.AreEqual(Actor(Owner), command.gimmickOwner);
                Assert.AreEqual(Actor(NearestEnemy), command.gimmickTarget);
                Array.Reverse(switches);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LurePlate_OnlySelectsTheHostilePursuingThisOperator(bool scheduled)
        {
            SetMode(new ActionSlot { execType = ActionExecType.Instant, paramId = (int)InstantAction.InteractObject },
                CompareOp.HasAny, Hazard);
            Vector2 enemyPosition = new Vector2(-2f, 0f);
            _core.Data.GetVitals(NearestEnemy).position = enemyPosition;
            AISceneGimmick plate = Gimmick(77, 0f, new Vector2(2f, 0f));
            plate.requiresLure = true;
            plate.lurePlateX = 2f;
            plate.lureExit = new Vector2(4f, 0f);
            Assert.IsFalse(plate.WillAffectTarget(enemyPosition, Vector2.zero));

            ActorRef enemy = Actor(NearestEnemy);
            Assert.IsTrue(_observations.Targets.SetTarget(enemy, Actor(Ally)));
            Capture(new[] { plate });
            Decide(scheduled, NearestEnemy);
            Assert.AreEqual(ActionCommandKind.Idle, _backend.Commands[0].kind);
            Assert.AreEqual(NearestEnemy, _loop.CurrentTargetHash);

            ActorRef owner = Actor(Owner);
            Assert.IsTrue(_observations.Targets.SetTarget(enemy, owner));
            Capture(new[] { plate });
            Decide(scheduled, NearestEnemy);
            Assert.AreEqual(1, _backend.Commands.Count);
            ActionCommand bait = _backend.Commands[0];
            Assert.AreEqual(ActionCommandKind.InteractObject, bait.kind);
            Assert.AreEqual(owner, bait.gimmickOwner);
            Assert.AreEqual(enemy, bait.gimmickTarget);
            Assert.AreEqual(plate.id, bait.gimmick.id);
            Assert.AreEqual(plate.lureExit, bait.gimmick.lureExit);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ObjectNearby_EqualZeroRequiresACompletedObservationForThisTick(bool scheduled)
        {
            SetMode(new ActionSlot { execType = ActionExecType.Attack }, CompareOp.Equal, 0);

            Capture(Array.Empty<AISceneGimmick>());
            Decide(scheduled, NearestEnemy);
            Assert.AreEqual(ActionCommandKind.Attack, _backend.Commands[0].kind);

            Capture();
            Decide(scheduled, NearestEnemy);
            Assert.AreEqual(ActionCommandKind.Idle, _backend.Commands[0].kind);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void InteractObject_RejectedAtDispatch_ExecutesConfiguredDefault(bool scheduled)
        {
            SetMode(new ActionSlot { execType = ActionExecType.Instant, paramId = (int)InstantAction.InteractObject },
                CompareOp.HasAny, Hazard);
            _backend.RejectInteraction = true;
            Capture(new[] { Gimmick(51, 0f, new Vector2(6f, 0f)) });
            Decide(scheduled, NearestEnemy);

            Assert.AreEqual(2, _backend.Commands.Count);
            Assert.AreEqual(ActionCommandKind.InteractObject, _backend.Commands[0].kind);
            Assert.AreEqual(ActionCommandKind.Idle, _backend.Commands[1].kind);
        }

        [TestCase(0, PreviousEnemy)]
        [TestCase(NearestEnemy, PreviousEnemy)]
        [TestCase(NearestEnemy, 0)]
        public void RetargetAfterScheduling_PreservesNewRelationAndDiscardsOldInteraction(
            int startingTarget, int replacement)
        {
            SetMode(new ActionSlot { execType = ActionExecType.Instant, paramId = (int)InstantAction.InteractObject },
                CompareOp.HasAny, Hazard);
            Capture(new[] { Gimmick(51, 0f, new Vector2(6f, 0f)) });
            ActorRef owner = Actor(Owner);
            Assert.IsTrue(_observations.Targets.SetTarget(owner, startingTarget == 0 ? default : Actor(startingTarget)));
            AIDecisionSystem decisions = _core.AIDecisions;
            decisions.BeginTick(_observations.Observation);
            _loop.PrepareTick(0.02f, new List<int> { NearestEnemy }, _observations.Observation.Time, decisions);
            decisions.Schedule();

            Assert.IsTrue(_observations.Targets.SetTarget(owner, replacement == 0 ? default : Actor(replacement)));
            decisions.CompleteAndApply();
            Assert.AreEqual(replacement, _loop.CurrentTargetHash);
            Assert.IsEmpty(_backend.Commands);
        }

        [Test]
        public void StartingTargetReboundToNewGeneration_RejectsAnOtherwiseLiveResult()
        {
            SetMode(new ActionSlot { execType = ActionExecType.Instant, paramId = (int)InstantAction.InteractObject },
                CompareOp.HasAny, Hazard);
            Capture(new[] { Gimmick(51, 0f, new Vector2(6f, 0f)) });
            ActorRef owner = Actor(Owner);
            ActorRef previous = Actor(PreviousEnemy);
            Assert.IsTrue(_observations.Targets.SetTarget(owner, previous));
            AIDecisionSystem decisions = _core.AIDecisions;
            decisions.BeginTick(_observations.Observation);
            _loop.PrepareTick(0.02f, new List<int> { NearestEnemy }, _observations.Observation.Time, decisions);
            decisions.Schedule();

            _core.UnregisterCharacter(PreviousEnemy);
            Register(PreviousEnemy, CharacterBelong.Enemy, new Vector2(9f, 0f));
            ActorRef replacement = Actor(PreviousEnemy);
            Assert.AreNotEqual(previous, replacement);
            Assert.IsTrue(_observations.Targets.SetTarget(owner, replacement));
            decisions.CompleteAndApply();

            Assert.IsTrue(_observations.Targets.TryGetTarget(owner, out ActorRef actual));
            Assert.AreEqual(replacement, actual);
            Assert.IsEmpty(_backend.Commands);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RetargetInsideRejectedInteraction_SkipsFallbackForTheSupersededDecision(bool scheduled)
        {
            SetMode(new ActionSlot { execType = ActionExecType.Instant, paramId = (int)InstantAction.InteractObject },
                CompareOp.HasAny, Hazard);
            _backend.RejectInteraction = true;
            _backend.OnRejected = () => _observations.Targets.SetTarget(Actor(Owner), Actor(PreviousEnemy));
            Capture(new[] { Gimmick(51, 0f, new Vector2(6f, 0f)) });
            Decide(scheduled, NearestEnemy);
            Assert.AreEqual(PreviousEnemy, _loop.CurrentTargetHash);
            Assert.AreEqual(1, _backend.Commands.Count);
            Assert.AreEqual(ActionCommandKind.InteractObject, _backend.Commands[0].kind);
            Assert.AreEqual(Actor(NearestEnemy), _backend.Commands[0].gimmickTarget);
        }

        [TestCase(false, ActType.Dodge, ActionCommandKind.Dodge)]
        [TestCase(true, ActType.Dodge, ActionCommandKind.Dodge)]
        [TestCase(true, ActType.Attack, ActionCommandKind.Attack)]
        [TestCase(false, ActType.Heal, ActionCommandKind.Cast)]
        public void AuthoredInteraction_RejectedAtDispatch_UsesOrdinaryAction(bool scheduled, ActType alternative,
            ActionCommandKind expected)
        {
            AIInfo info = ScriptableObject.CreateInstance<AIInfo>();
            try
            {
                info.actDataList = new[]
                {
                    new ActData { actType = ActType.InteractObject, weight = 100f },
                    new ActData { actType = alternative, weight = 1f }
                };
                info.modes = new[]
                {
                    new CharacterModeData { mode = CharacterMode.Combat, detectionRange = 12f,
                        availableActIndices = new[] { 0, 1 } }
                };
                // Converted enemy modes select the Ally faction; use the normal authoring contract.
                _core.Data.GetFlags(Owner).Belong = CharacterBelong.Enemy;
                _core.Data.GetFlags(NearestEnemy).Belong = CharacterBelong.Ally;
                _loop.SetMode(AIInfoConverter.ConvertModes(info)[0]);
                _loop.ForceEvaluate();
                _backend.RejectInteraction = true;
                Capture(new[] { Gimmick(51, 0f, new Vector2(6f, 0f)) });
                Decide(scheduled, NearestEnemy);

                Assert.AreEqual(2, _backend.Commands.Count);
                Assert.AreEqual(ActionCommandKind.InteractObject, _backend.Commands[0].kind);
                Assert.AreEqual(expected, _backend.Commands[1].kind);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(info);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DelayedGimmick_RequiresKnownTargetAtImpactAndClearFriendlyPath(bool scheduled)
        {
            SetMode(new ActionSlot { execType = ActionExecType.Instant, paramId = (int)InstantAction.InteractObject },
                CompareOp.HasAny, Hazard);
            AISceneGimmick fallingRock = Gimmick(41, 0f, new Vector2(8f, 0f));
            fallingRock.effectDelay = 1f;
            Vector2 enemyVelocity = new Vector2(2f, 0f);

            // The target starts at (6,0); it will enter the impact circle after one second.
            Assert.AreEqual(ActionCommandKind.InteractObject,
                DecideFallingRock(scheduled, fallingRock, enemyVelocity, Vector2.zero));
            // Ally moves from (6,3) to (10,-3), crossing the circle between the two samples.
            Assert.AreEqual(ActionCommandKind.Idle,
                DecideFallingRock(scheduled, fallingRock, enemyVelocity, new Vector2(4f, -6f)));

            fallingRock.effectPosition = new Vector2(6f, 0f);
            Assert.AreEqual(ActionCommandKind.Idle,
                DecideFallingRock(scheduled, fallingRock, enemyVelocity, Vector2.zero),
                "The target leaves the area before impact.");
            Assert.AreEqual(ActionCommandKind.Idle,
                DecideFallingRock(scheduled, fallingRock, null, Vector2.zero),
                "An unobserved velocity must not be treated as standing still.");
        }

        private ActionCommandKind DecideFallingRock(bool scheduled, AISceneGimmick fallingRock,
            Vector2? targetVelocity, Vector2 allyVelocity)
        {
            Capture(new[] { fallingRock });
            AIObservationContext observation = _observations.Observation;
            observation.SetGimmickVelocity(Actor(Owner), Vector2.zero);
            observation.SetGimmickVelocity(Actor(Ally), allyVelocity);
            if (targetVelocity.HasValue)
            {
                observation.SetGimmickVelocity(Actor(NearestEnemy), targetVelocity.Value);
            }
            Decide(scheduled, NearestEnemy);
            Assert.AreEqual(1, _backend.Commands.Count);
            return _backend.Commands[0].kind;
        }

        private ActorRef Actor(int hash)
        {
            Assert.IsTrue(_core.Actors.TryGet(hash, out ActorRef actor));
            return actor;
        }

        private void Register(int hash, CharacterBelong belong, Vector2 position)
        {
            _core.RegisterCharacter(hash, new CharacterVitals { currentHp = 100, maxHp = 100, position = position },
                default, CharacterFlags.Pack(belong, default, ActState.Neutral, default), default);
        }

        private void SetMode(ActionSlot action, CompareOp compare, int operand)
        {
            AITargetSelect nearestEnemy = new AITargetSelect
            {
                sortKey = TargetSortKey.Distance,
                filter = new TargetFilter { belong = CharacterBelong.Enemy }
            };
            AICondition nearby = new AICondition
            {
                conditionType = AIConditionType.ObjectNearby, compareOp = compare, operandA = operand
            };
            ActionSlot idle = new ActionSlot { execType = ActionExecType.Sustained, paramId = (int)SustainedAction.Idle };
            _loop.SetMode(new AIMode
            {
                targetRules = new[] { new AIRule { actionIndex = 0 } },
                targetSelects = new[] { nearestEnemy },
                actionRules = new[] { new AIRule { actionIndex = 0, conditions = new[] { nearby } } },
                actions = new[] { action, idle },
                defaultActionIndex = 1,
                judgeInterval = Vector2.zero
            });
        }

        private static AISceneGimmick Gimmick(int id, float operateAtX, Vector2 affects)
        {
            return new AISceneGimmick
            {
                id = id, generation = 4, objectMask = Hazard,
                operationPosition = new Vector2(operateAtX, 0f), operationRadius = 2f,
                effectPosition = affects, effectRadius = 0.5f
            };
        }

        private void Capture(AISceneGimmick[] switches = null)
        {
            _observations.Capture(++_tick, 0.02f * _tick);
            AIObservationContext observation = _observations.Observation;
            if (switches != null)
            {
                observation.BeginGimmickObservation();
                foreach (AISceneGimmick item in switches) { observation.AddGimmick(item); }
            }
            Assert.IsTrue(_loop.UseObservation(observation, _observations.Targets));
        }

        private void Decide(bool scheduled, params int[] hashes)
        {
            _backend.Commands.Clear();
            List<int> candidates = new List<int>(hashes);
            AIObservationContext observation = _observations.Observation;
            if (!scheduled)
            {
                _loop.Tick(0.02f, candidates, observation.Time);
                return;
            }

            AIDecisionSystem decisions = _core.AIDecisions;
            decisions.BeginTick(observation);
            _loop.PrepareTick(0.02f, candidates, observation.Time, decisions);
            decisions.Schedule();
            decisions.CompleteAndApply();
        }

        private sealed class CommandRecorder : IActionCommandBackend
        {
            public readonly List<ActionCommand> Commands = new List<ActionCommand>();
            public bool RejectInteraction;
            public Action OnRejected;
            public bool IsExecuting => false;
            public event Action OnActionCompleted { add { } remove { } }
            public bool Supports(ActionCommandKind kind) => kind == ActionCommandKind.InteractObject
                || kind == ActionCommandKind.Attack || kind == ActionCommandKind.Cast
                || kind == ActionCommandKind.Dodge || kind == ActionCommandKind.Idle;
            public ActionDispatchResult ExecuteCommand(int ownerHash, int targetHash, in ActionCommand command)
            {
                Commands.Add(command);
                if (RejectInteraction && command.kind == ActionCommandKind.InteractObject)
                {
                    OnRejected?.Invoke();
                    return new ActionDispatchResult(ActionDispatchStatus.Rejected);
                }
                return new ActionDispatchResult(ActionDispatchStatus.Accepted);
            }
            public bool Execute(int ownerHash, int targetHash, ActionSlot slot) =>
                throw new InvalidOperationException("A typed backend must receive the sampled command.");
            public void CancelCurrent() { }
            public void Tick(float deltaTime) { }
        }
    }
}
