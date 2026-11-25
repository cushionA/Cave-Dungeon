using System;
using System.Collections;
using System.Collections.Generic;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using Unity.PerformanceTesting;
using Unity.PerformanceTesting.Measurements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using CharacterInfo = Game.Core.CharacterInfo;
using Object = UnityEngine.Object;

namespace Game.Tests.PlayMode
{
    public sealed class Performance_ObservedTargetLineOfSightTests
    {
        private const float k_Step = 0.02f;
        private const float k_LaneSpacing = 3f;
        private const int k_VisibleAction = 7;
        private const int k_FallbackAction = 55;
        private const string k_CaptureMarker = "AI.Observation.TargetLineOfSight";
        private const string k_CopyMarker = "AI.Decision.CopyTargetLineOfSight";
        private Scene _room;
        private Scene _previousRoom;
        private CharacterInfo _observerInfo;
        private CharacterInfo _targetInfo;
        private AIInfo _observerAi;

        [UnityTest, Performance, Category("AITerrainProfile")]
        public IEnumerator RealSight_MeasuresFourObserversAndEightCandidates()
        {
            yield return MeasureRealSight(4, 8);
        }

        [UnityTest, Performance, Category("AITerrainProfile")]
        public IEnumerator RealSight_MeasuresSixteenObserversAndThirtyTwoCandidates()
        {
            yield return MeasureRealSight(16, 32);
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (_previousRoom.isLoaded) { SceneManager.SetActiveScene(_previousRoom); }
            if (_room.isLoaded) { yield return SceneManager.UnloadSceneAsync(_room); }
            Object.Destroy(_observerInfo);
            Object.Destroy(_targetInfo);
            Object.Destroy(_observerAi);
            TestSceneHelper.Cleanup();
            yield return null;
        }

        private IEnumerator MeasureRealSight(int observerCount, int candidateCount)
        {
            TestSceneHelper.Cleanup();
            yield return null;
            _previousRoom = SceneManager.GetActiveScene();
            _room = SceneManager.CreateScene("Observed sight profile", new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            Assert.IsTrue(SceneManager.SetActiveScene(_room));
            GameManager manager = TestSceneHelper.CreateGameManager().GetComponent<GameManager>();
            manager.enabled = false;

            _observerInfo = TestSceneHelper.CreateTestCharacterInfo(CharacterBelong.Enemy, CharacterFeature.Minion);
            _targetInfo = TestSceneHelper.CreateTestCharacterInfo(CharacterBelong.Ally, CharacterFeature.Minion);
            _observerAi = ScriptableObject.CreateInstance<AIInfo>();
            _observerAi.actDataList = new[]
            {
                new ActData { actType = ActType.Attack, weight = 1f,
                    actJudge = new ActJudgeData { requireLineOfSight = true } },
            };
            _observerAi.modes = new[]
            {
                new CharacterModeData { mode = CharacterMode.Combat, detectionRange = 20f, availableActIndices = new[] { 0 } },
            };

            BaseCharacter[] owners = new BaseCharacter[observerCount + 1];
            ActorRef[] ownerRefs = new ActorRef[owners.Length];
            ActorRef[] targets = new ActorRef[candidateCount];
            BaseCharacter[] candidates = new BaseCharacter[candidateCount];
            List<int> candidateHashes = new List<int>(candidateCount);
            List<BoxCollider2D> walls = new List<BoxCollider2D>(observerCount / 2);
            for (int index = 0; index < candidateCount; index++)
            {
                float x = index * k_LaneSpacing;
                candidates[index] = CreatePassiveActor(_targetInfo, x, 4f);
                if (index >= observerCount) { continue; }
                owners[index] = CreateObserver(x);
                if ((index & 1) == 0)
                {
                    GameObject cover = new GameObject("LOS cover") { layer = LayerMask.NameToLayer("Ground") };
                    cover.transform.position = new Vector3(x, 2f, 0f);
                    BoxCollider2D wall = cover.AddComponent<BoxCollider2D>();
                    wall.size = new Vector2(0.6f, 0.25f);
                    wall.enabled = false;
                    walls.Add(wall);
                }
            }
            // The last owner has a physical body but no authored rule requiring sight.
            owners[observerCount] = CreatePassiveActor(_observerInfo, observerCount * k_LaneSpacing, 0f);
            yield return null;

            AITargetRelations synchronousTargets = new AITargetRelations();
            AITargetRelations scheduledTargets = new AITargetRelations();
            for (int index = 0; index < candidates.Length; index++)
            {
                targets[index] = RegisterTargetRelations(candidates[index], synchronousTargets, scheduledTargets);
                candidateHashes.Add(targets[index].Hash);
            }
            for (int index = 0; index < owners.Length; index++)
            {
                ownerRefs[index] = RegisterTargetRelations(owners[index], synchronousTargets, scheduledTargets);
                Assert.That(owners[index].FieldOrderJudgment?.RequiresTargetLineOfSight ?? false,
                    Is.EqualTo(index < observerCount));
            }

            ISimulationTickWorld world = manager;
            world.CaptureSimulationActors();
            world.CaptureSimulationPhysics();
            long tick = 0;
            Capture();
            AIObservationContext snapshot = GameManager.Observation;
            AssertSight(snapshot, ownerRefs, targets, observerCount, withCover: false);

            foreach (BoxCollider2D wall in walls) { wall.enabled = true; }
            MeasureSamples(Capture, "ObservationCaptureWithCover").ProfilerMarkers(k_CaptureMarker).Run();
            Capture();
            AssertSight(snapshot, ownerRefs, targets, observerCount, withCover: true);
            long measuredTick = snapshot.Tick;
            AIDecisionSystem decisions = GameManager.AIDecisions;
            AIMode mode = CreateMeasuredMode();
            JudgmentLoop[] synchronous = new JudgmentLoop[owners.Length];
            JudgmentLoop[] scheduled = new JudgmentLoop[owners.Length];
            RecordingBackend[] synchronousCommands = new RecordingBackend[owners.Length];
            RecordingBackend[] scheduledCommands = new RecordingBackend[owners.Length];
            try
            {
                for (int index = 0; index < owners.Length; index++)
                {
                    synchronous[index] = CreateLoop(ownerRefs[index], mode, snapshot, synchronousTargets, out synchronousCommands[index]);
                    scheduled[index] = CreateLoop(ownerRefs[index], mode, snapshot, scheduledTargets, out scheduledCommands[index]);
                }

                MeasureSamples(DecideSynchronously, "SynchronousDecisionOnSnapshot").Run();
                MeasureSamples(DecideScheduled, "BurstDecisionOnSnapshot")
                    .ProfilerMarkers(k_CopyMarker, "AI.Decision.Complete", "AI.Decision.Apply").Run();
                for (int index = 0; index < owners.Length; index++)
                {
                    bool visible = index < observerCount && (index & 1) != 0;
                    int expectedAction = visible ? k_VisibleAction : k_FallbackAction;
                    Assert.That(synchronous[index].ObservationTick, Is.EqualTo(measuredTick));
                    Assert.That(scheduled[index].ObservationTick, Is.EqualTo(measuredTick));
                    Assert.IsTrue(synchronousTargets.TryGetTarget(ownerRefs[index], out ActorRef synchronousTarget));
                    Assert.IsTrue(scheduledTargets.TryGetTarget(ownerRefs[index], out ActorRef scheduledTarget));
                    Assert.That(synchronousTarget, Is.EqualTo(targets[index]));
                    Assert.That(scheduledTarget, Is.EqualTo(targets[index]));
                    Assert.That(synchronousCommands[index].Action, Is.EqualTo(expectedAction));
                    Assert.That(scheduledCommands[index].Action, Is.EqualTo(expectedAction));
                    Assert.That(synchronousCommands[index].Count, Is.GreaterThan(0));
                    Assert.That(scheduledCommands[index].Count, Is.EqualTo(synchronousCommands[index].Count));
                }
            }
            finally
            {
                foreach (JudgmentLoop loop in synchronous) { loop?.Dispose(); }
                foreach (JudgmentLoop loop in scheduled) { loop?.Dispose(); }
            }

            void Capture() => world.CaptureSimulationObservation(++tick, tick * k_Step);

            void DecideSynchronously()
            {
                foreach (JudgmentLoop loop in synchronous) { loop.Tick(k_Step, candidateHashes, snapshot.Time); }
            }

            void DecideScheduled()
            {
                decisions.BeginTick(snapshot);
                foreach (JudgmentLoop loop in scheduled) { loop.PrepareTick(k_Step, candidateHashes, snapshot.Time, decisions); }
                decisions.Schedule();
                decisions.CompleteAndApply();
            }
        }

        private EnemyCharacter CreateObserver(float x)
        {
            GameObject actor = new GameObject("LOS observer");
            actor.transform.position = new Vector3(x, 0f, 0f);
            actor.AddComponent<Rigidbody2D>().gravityScale = 0f;
            actor.AddComponent<BoxCollider2D>();
            actor.AddComponent<ActionExecutorController>();
            EnemyCharacter observer = actor.AddComponent<EnemyCharacter>();
            observer.SetCharacterInfoForTest(_observerInfo);
            observer.SetAIInfo(_observerAi);
            return observer;
        }

        private static BaseCharacter CreatePassiveActor(CharacterInfo info, float x, float y)
        {
            GameObject actor = TestSceneHelper.CreateBaseCharacterObject(info, new Vector3(x, y, 0f));
            actor.GetComponent<Rigidbody2D>().gravityScale = 0f;
            return actor.GetComponent<BaseCharacter>();
        }

        private static ActorRef RegisterTargetRelations(BaseCharacter character, AITargetRelations sync, AITargetRelations scheduled)
        {
            Assert.IsTrue(GameManager.Actors.TryGet(character.ObjectHash, out ActorRef actor));
            sync.Register(actor);
            scheduled.Register(actor);
            return actor;
        }

        private static void AssertSight(AIObservationContext snapshot, ActorRef[] owners, ActorRef[] targets,
            int observerCount, bool withCover)
        {
            for (int index = 0; index < owners.Length; index++)
            {
                bool known = snapshot.TryGetTargetLineOfSight(owners[index], targets[index], out bool isClear);
                Assert.That(known, Is.EqualTo(index < observerCount));
                if (known) { Assert.That(isClear, Is.EqualTo(!withCover || (index & 1) != 0)); }
            }
        }

        private static AIMode CreateMeasuredMode() => new AIMode
        {
            targetRules = new[] { new AIRule { actionIndex = 0 } },
            targetSelects = new[] { new AITargetSelect { sortKey = TargetSortKey.Distance,
                filter = new TargetFilter { belong = CharacterBelong.Ally } } },
            actionRules = new[] { new AIRule { actionIndex = 0, conditions = new[]
            {
                new AICondition { conditionType = AIConditionType.TargetLineOfSight, compareOp = CompareOp.Equal, operandA = 1 },
            } } },
            actions = new[]
            {
                new ActionSlot { execType = ActionExecType.Attack, paramId = k_VisibleAction },
                new ActionSlot { execType = ActionExecType.Attack, paramId = k_FallbackAction },
            },
            defaultActionIndex = 1,
            judgeInterval = Vector2.zero,
        };

        private static JudgmentLoop CreateLoop(ActorRef owner, AIMode mode, AIObservationContext snapshot,
            AITargetRelations targets, out RecordingBackend backend)
        {
            ActionExecutor executor = new ActionExecutor();
            backend = new RecordingBackend();
            executor.SetExecutionBackend(backend);
            JudgmentLoop loop = new JudgmentLoop(executor, GameManager.Data, owner.Hash);
            loop.SetMode(mode);
            Assert.IsTrue(loop.UseObservation(snapshot, targets));
            return loop;
        }

        private static MethodMeasurement MeasureSamples(Action action, string sample) =>
            Measure.Method(action).SampleGroup(sample).WarmupCount(2).MeasurementCount(8).IterationsPerMeasurement(1).GC();

        private sealed class RecordingBackend : IActionExecutionBackend
        {
            public int Action { get; private set; }
            public int Count { get; private set; }
            public bool IsExecuting => false;
            public event System.Action OnActionCompleted { add { } remove { } }

            public bool Execute(int ownerHash, int targetHash, ActionSlot slot)
            {
                Action = slot.paramId;
                Count++;
                return true;
            }

            public void CancelCurrent() { }
            public void Tick(float deltaTime) { }
        }
    }
}
