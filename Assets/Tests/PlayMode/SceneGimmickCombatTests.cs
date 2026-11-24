using System.Collections;
using System.Collections.Generic;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Game.Tests.PlayMode
{
    public class SceneGimmickCombatTests
    {
        private const float DecisionInterval = 0.6f;
        private readonly List<Object> _created = new List<Object>();
        private Scene _previousScene;
        private Scene _testScene;
        private Scene _nextRoom;
        private EnemyCharacter _operator;
        private BaseCharacter _target;
        private BaseCharacter _teammate;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            _previousScene = SceneManager.GetActiveScene();
            _testScene = SceneManager.CreateScene("SceneGimmickCombat",
                new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            Assert.IsTrue(SceneManager.SetActiveScene(_testScene));
            TestSceneHelper.CreateGameManager();
            GameManager.Instance.enabled = false;
            _operator = CreateOperator(0f);
            _target = CreatePassiveActor(CharacterBelong.Ally, 3f);
            _teammate = CreatePassiveActor(CharacterBelong.Enemy, 8f);
            yield return null;
            CharacterRegistry.RegisterPlayer(_target.ObjectHash);
        }

        private AIInfo CreateInteractionAI()
        {
            var ai = ScriptableObject.CreateInstance<AIInfo>();
            _created.Add(ai);
            ai.actDataList = new[]
            {
                new ActData { actType = ActType.InteractObject, weight = 100f },
                new ActData { actType = ActType.Guard, weight = 1f },
            };
            ai.modes = new[]
            {
                new CharacterModeData { mode = CharacterMode.Combat, detectionRange = 20f,
                    availableActIndices = new[] { 0, 1 } },
            };
            return ai;
        }

        private EnemyCharacter CreateOperator(float x)
        {
            GameObject actor = CreateObject("Operator", new Vector2(x, 0f));
            actor.AddComponent<Rigidbody2D>().gravityScale = 0f;
            actor.AddComponent<BoxCollider2D>();
            actor.AddComponent<ActionExecutorController>();
            var character = actor.AddComponent<EnemyCharacter>();
            character.SetCharacterInfoForTest(CreateInfo(CharacterBelong.Enemy));
            character.SetAIInfo(CreateInteractionAI());
            return character;
        }

        private Game.Core.CharacterInfo CreateInfo(CharacterBelong belong)
        {
            var info = TestSceneHelper.CreateTestCharacterInfo(belong, CharacterFeature.Minion);
            _created.Add(info);
            return info;
        }

        private GameObject CreateObject(string name, Vector2 position)
        {
            var obj = new GameObject(name);
            _created.Add(obj);
            obj.transform.position = position;
            return obj;
        }

        private BaseCharacter CreatePassiveActor(CharacterBelong belong, float x)
        {
            GameObject obj = TestSceneHelper.CreateBaseCharacterObject(CreateInfo(belong), new Vector2(x, 0f));
            _created.Add(obj);
            var actor = obj.GetComponent<BaseCharacter>();
            actor.Rigidbody.gravityScale = 0f;
            return actor;
        }

        private AISceneGimmickController CreateSwitch(float effectX, float delay = 0f, float operationX = 0f)
        {
            GameObject obj = CreateObject("Switch", new Vector2(operationX, 0f));
            var effect = CreateObject("Effect", new Vector2(effectX, 0f));
            var gimmick = obj.AddComponent<AISceneGimmickController>();
            // Even at x=3 the user can operate the switch. Entering the effect radius is a separate veto.
            gimmick.Configure(1 << 4, obj.transform, 5f, effect.transform, 0.75f, delay);
            return gimmick;
        }

        [Test]
        public void ObjectNearby_ReportsKnownZeroBeforeRegistrationAndAfterLastSwitchIsDisabled()
        {
            var nearby = new AICondition { conditionType = AIConditionType.ObjectNearby };
            int owner = _operator.ObjectHash;
            int target = _target.ObjectHash;
            GameManager.Instance.SimulationTick(0f, 1f);
            Assert.IsTrue(GameManager.Observation.TryGetConditionValue(nearby, owner, target, out float mask));
            Assert.AreEqual(0f, mask);

            // In operating range but unable to affect the target, so it stays armed.
            var gimmick = CreateSwitch(-6f);
            GameManager.Instance.SimulationTick(DecisionInterval, 1f + DecisionInterval);
            Assert.IsTrue(GameManager.Observation.TryGetConditionValue(nearby, owner, target, out mask));
            Assert.AreEqual((float)(1 << 4), mask);

            gimmick.enabled = false;
            GameManager.Instance.SimulationTick(DecisionInterval, 1f + 2f * DecisionInterval);
            Assert.IsTrue(GameManager.Observation.TryGetConditionValue(nearby, owner, target, out mask));
            Assert.AreEqual(0f, mask);
        }

        [UnityTest]
        public IEnumerator AuthoredEnemy_ActivatesOnlyTheSwitchForItsNewlySelectedTarget()
        {
            var newTarget = CreatePassiveActor(CharacterBelong.Ally, 3f);
            _target.SetPhysicsPosition(new Vector2(-6f, 0f));
            yield return null;
            CharacterRegistry.RegisterAlly(newTarget.ObjectHash);
            Assert.IsTrue(GameManager.Actors.TryGet(_operator.ObjectHash, out ActorRef owner));
            Assert.IsTrue(GameManager.Actors.TryGet(_target.ObjectHash, out ActorRef oldTarget));
            Assert.IsTrue(GameManager.TargetRelations.SetTarget(owner, oldTarget));

            var oldSwitch = CreateSwitch(-6f);
            var selected = CreateSwitch(3f);
            var gate = CreateObject("Gate", new Vector2(12f, 0f)).AddComponent<BoxCollider2D>();
            int activations = 0;
            selected.OnActivated.AddListener(() => { activations++; gate.enabled = false; });
            GameManager.Instance.SimulationTick(DecisionInterval, 1f);

            Assert.IsTrue(GameManager.TargetRelations.TryGetTarget(owner, out ActorRef current));
            Assert.AreEqual(newTarget.ObjectHash, current.Hash);
            Assert.AreEqual(1, activations);
            Assert.IsFalse(gate.enabled);
            Assert.IsFalse(selected.IsAvailable);
            Assert.IsTrue(oldSwitch.IsAvailable);

            GameManager.Instance.SimulationTick(DecisionInterval, 1f + DecisionInterval);
            Assert.AreEqual(1, activations);
            Assert.IsTrue(GameManager.Data.GetDefenseState(owner.Hash).isGuarding);
        }

        [UnityTest]
        public IEnumerator TwoOperatorsInOneTick_ActivateOnceAndOnlyTheOtherFallsBackToGuard()
        {
            var second = CreateOperator(-2f);
            yield return null;
            var gimmick = CreateSwitch(3f);
            int activations = 0;
            gimmick.OnActivated.AddListener(() => activations++);
            GameManager.Instance.SimulationTick(DecisionInterval, 1f);

            Assert.IsTrue(GameManager.Actors.TryGet(_operator.ObjectHash, out ActorRef firstOwner));
            Assert.IsTrue(GameManager.Actors.TryGet(second.ObjectHash, out ActorRef secondOwner));
            Assert.IsTrue(GameManager.TargetRelations.TryGetTarget(firstOwner, out ActorRef firstTarget));
            Assert.IsTrue(GameManager.TargetRelations.TryGetTarget(secondOwner, out ActorRef secondTarget));
            Assert.AreEqual(_target.ObjectHash, firstTarget.Hash);
            Assert.AreEqual(firstTarget, secondTarget);
            Assert.AreEqual(1, activations);
            Assert.IsFalse(gimmick.IsAvailable);
            Assert.AreNotEqual(GameManager.Data.GetDefenseState(firstOwner.Hash).isGuarding,
                GameManager.Data.GetDefenseState(secondOwner.Hash).isGuarding);

            GameManager.Instance.SimulationTick(DecisionInterval, 1f + DecisionInterval);
            Assert.AreEqual(1, activations);
            Assert.IsTrue(GameManager.Data.GetDefenseState(firstOwner.Hash).isGuarding);
            Assert.IsTrue(GameManager.Data.GetDefenseState(secondOwner.Hash).isGuarding);
        }

        [UnityTest]
        public IEnumerator Sister_WaitsWhilePlayerIsInTrapAndActivatesWhenOnlyEnemyRemains()
        {
            GameObject actor = CreateObject("Sister", new Vector2(-2f, 0f));
            actor.AddComponent<Rigidbody2D>().gravityScale = 0f;
            actor.AddComponent<BoxCollider2D>();
            actor.AddComponent<ActionExecutorController>();
            var sister = actor.AddComponent<CompanionCharacter>();
            var info = TestSceneHelper.CreateTestCharacterInfo(CharacterBelong.Ally, CharacterFeature.Companion);
            _created.Add(info);
            sister.SetCharacterInfoForTest(info);
            sister.SetAIInfo(CreateInteractionAI());
            yield return null;
            Assert.AreEqual(_target.ObjectHash, CharacterRegistry.PlayerHash);
            Assert.IsNotNull(sister.AIController);

            var trap = CreateSwitch(3f, operationX: -2.5f);
            int activations = 0;
            trap.OnActivated.AddListener(() => activations++);
            // Each tick allows the one-second fallback guard to finish before the next decision.
            const float interval = 1.1f;
            _target.SetPhysicsPosition(new Vector2(3f, 0f));
            _operator.SetPhysicsPosition(new Vector2(9f, 0f));
            GameManager.Instance.SimulationTick(interval, interval);
            Assert.AreEqual(_operator.ObjectHash, sister.AIController.JudgmentLoop.CurrentTargetHash);
            Assert.IsTrue(GameManager.Data.GetDefenseState(sister.ObjectHash).isGuarding);
            Assert.AreEqual(0, activations);

            _operator.SetPhysicsPosition(new Vector2(3.1f, 0f));
            GameManager.Instance.SimulationTick(interval, 2f * interval);
            Assert.AreEqual(_operator.ObjectHash, sister.AIController.JudgmentLoop.CurrentTargetHash);
            Assert.IsTrue(GameManager.Data.GetDefenseState(sister.ObjectHash).isGuarding);
            Assert.AreEqual(0, activations);
            Assert.IsTrue(trap.IsAvailable);

            _target.SetPhysicsPosition(new Vector2(6f, 0f));
            GameManager.Instance.SimulationTick(interval, 3f * interval);
            Assert.AreEqual(_operator.ObjectHash, sister.AIController.JudgmentLoop.CurrentTargetHash);
            Assert.AreEqual(1, activations);
            Assert.IsFalse(trap.IsAvailable);
            GameManager.Instance.SimulationTick(interval, 4f * interval);
            Assert.AreEqual(1, activations);
        }

        [UnityTest]
        public IEnumerator PersistentManager_ActivatesSwitchInNextRoomDespiteTeammateInSeparateWorld()
        {
            var manager = GameManager.Instance;
            PhysicsScene2D originalWorld = _testScene.GetPhysicsScene2D();
            _nextRoom = SceneManager.CreateScene("NextGimmickRoom", new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            Assert.IsTrue(SceneManager.SetActiveScene(_nextRoom));
            Assert.AreNotEqual(originalWorld, _nextRoom.GetPhysicsScene2D());
            SceneManager.MoveGameObjectToScene(_operator.gameObject, _nextRoom);
            SceneManager.MoveGameObjectToScene(_target.gameObject, _nextRoom);
            SceneManager.MoveGameObjectToScene(_teammate.gameObject, _previousScene);
            yield return SceneManager.UnloadSceneAsync(_testScene);
            Assert.AreSame(manager, GameManager.Instance);
            Assert.IsFalse(originalWorld.IsValid());

            _operator.SetPhysicsPosition(Vector2.zero);
            _target.SetPhysicsPosition(new Vector2(3f, 0f));
            _teammate.SetPhysicsPosition(_target.PhysicsPosition);
            Assert.AreNotEqual(_nextRoom.GetPhysicsScene2D(), _teammate.gameObject.scene.GetPhysicsScene2D());
            var control = CreateObject("Next room switch", new Vector2(1f, 0f));
            var effect = CreateObject("Next room effect", _target.PhysicsPosition);
            var gimmick = control.AddComponent<AISceneGimmickController>();
            gimmick.Configure(1 << 4, control.transform, 2f, effect.transform, 0.75f, blockers: 0);
            int activations = 0;
            gimmick.OnActivated.AddListener(() => activations++);
            manager.SimulationTick(DecisionInterval, 1f);

            Assert.AreEqual(1, activations);
            Assert.IsFalse(gimmick.IsAvailable);
        }

        [Test]
        public void CombatMovement_UsesBothSnapshotPositionsUntilTargetIsRecycled()
        {
            int ownerHash = _operator.ObjectHash;
            int targetHash = _target.ObjectHash;
            var backend = _operator.GetComponent<ActionExecutorController>();
            var movement = new ActionCommand { kind = ActionCommandKind.MoveToTarget };
            GameManager.Data.GetVitals(ownerHash).position = Vector2.zero;
            GameManager.Data.GetVitals(targetHash).position = new Vector2(3f, 0f);
            ((ISimulationTickWorld)GameManager.Instance).CaptureSimulationObservation(1, 1f);
            GameManager.Data.GetVitals(ownerHash).position = new Vector2(10f, 0f);
            GameManager.Data.GetVitals(targetHash).position = new Vector2(4f, 0f);

            Assert.IsTrue(backend.ExecuteCommand(ownerHash, targetHash, movement).Accepted);
            Assert.IsTrue(GameManager.Actions.Execution.TryGetMovement(ownerHash, true, out Vector2 before));
            Assert.Greater(before.x, 0f);
            ((IActionExecutionBackend)backend).CancelCurrent();

            Assert.IsTrue(GameManager.Actors.TryGet(targetHash, out ActorRef original));
            _target.OnPoolReturn();
            _target.OnPoolAcquire();
            Assert.IsFalse(GameManager.Actors.IsCurrent(original));
            GameManager.Data.GetVitals(targetHash).position = new Vector2(4f, 0f);
            Assert.IsTrue(backend.ExecuteCommand(ownerHash, targetHash, movement).Accepted);
            Assert.IsTrue(GameManager.Actions.Execution.TryGetMovement(ownerHash, true, out Vector2 after));
            Assert.Less(after.x, 0f);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CombatMovement_SeparateWorldsRejectNewCommandsAndStopTracking(bool targetMissingFromSnapshot)
        {
            int ownerHash = _operator.ObjectHash;
            int targetHash = _target.ObjectHash;
            var backend = _operator.GetComponent<ActionExecutorController>();
            var movement = new ActionCommand { kind = ActionCommandKind.MoveToTarget };
            ISimulationTickWorld world = GameManager.Instance;
            GameManager.Data.GetVitals(ownerHash).position = Vector2.zero;
            GameManager.Data.GetVitals(targetHash).position = new Vector2(3f, 0f);
            world.CaptureSimulationObservation(1, 1f);
            ActionDispatchResult active = backend.ExecuteCommand(ownerHash, targetHash, movement);
            Assert.IsTrue(active.Accepted);
            Assert.IsTrue(GameManager.Actions.Execution.IsGroupActive(active.GroupId));

            _nextRoom = SceneManager.CreateScene("OtherMovementRoom", new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            SceneManager.MoveGameObjectToScene(_target.gameObject, _nextRoom);
            // Omit only the target for one capture, restoring it before movement refresh.
            ref CharacterFlags flags = ref GameManager.Data.GetFlags(targetHash);
            flags.IsVanished = targetMissingFromSnapshot;
            world.CaptureSimulationObservation(2, 2f);
            flags.IsVanished = false;
            Assert.AreEqual(!targetMissingFromSnapshot, GameManager.Observation.TryGetActor(targetHash, out _));

            ((IActionExecutionBackend)backend).Tick(0f);
            Assert.IsFalse(GameManager.Actions.Execution.IsGroupActive(active.GroupId));
            Assert.AreEqual(ActionDispatchStatus.InvalidTarget, backend.ExecuteCommand(ownerHash, targetHash, movement).Status);
        }

        [Test]
        public void CombatMovement_UnobservedDataOnlyActorUsesDefaultPhysicsScene()
        {
            ((ISimulationTickWorld)GameManager.Instance).CaptureSimulationObservation(1, 1f);
            const int targetHash = 60001;
            GameManager.Instance.RegisterCharacter(targetHash, CreateInfo(CharacterBelong.Ally));
            Assert.IsNull(GameManager.Data.GetManaged(targetHash));
            Assert.IsFalse(GameManager.Observation.TryGetActor(targetHash, out _));
            Assert.AreNotEqual(Physics2D.defaultPhysicsScene, _operator.gameObject.scene.GetPhysicsScene2D());
            GameManager.Data.GetVitals(targetHash).position = new Vector2(3f, 0f);
            var backend = _operator.GetComponent<ActionExecutorController>();
            var movement = new ActionCommand { kind = ActionCommandKind.MoveToTarget };
            Assert.AreEqual(ActionDispatchStatus.InvalidTarget,
                backend.ExecuteCommand(_operator.ObjectHash, targetHash, movement).Status);
        }

        // Stop at the actual Job/dispatch boundary to change the scene after the observations
        // have been captured. No alternate decision maker or mocked action backend is involved.
        private void ScheduleDecision(long tick = 1, float currentTime = 1f, float deltaTime = DecisionInterval)
        {
            ISimulationTickWorld world = GameManager.Instance;
            world.CaptureSimulationActors();
            world.CaptureSimulationPhysics();
            world.PrepareSimulationInputs(currentTime);
            world.AdvanceSharedSimulation(deltaTime, currentTime);
            world.CaptureSimulationObservation(tick, currentTime);
            GameManager.AIDecisions.BeginTick(GameManager.Observation);
            _operator.PrepareAIDecisionTick(deltaTime, currentTime, GameManager.AIDecisions);
            GameManager.AIDecisions.Schedule();
        }

        [TestCase(false, false, 1, 0)]
        [TestCase(true, false, 0, 1)]
        [TestCase(false, true, 0, 0)]
        public void GroundWall_RestrictsSwitchChoiceAtObservationAndUseAtDispatch(
            bool closedAtObservation, bool closedAtDispatch, int expectedNear, int expectedFar)
        {
            var near = CreateSwitch(3f, operationX: 2f);
            var far = CreateSwitch(3f, operationX: -4f);
            int nearActivations = 0, farActivations = 0;
            near.OnActivated.AddListener(() => nearActivations++);
            far.OnActivated.AddListener(() => farActivations++);
            var gate = CreateObject("Gate between operator and nearer switch", new Vector2(1f, 0f));
            gate.layer = 6;
            var wall = gate.AddComponent<BoxCollider2D>();
            wall.size = new Vector2(0.2f, 2f);
            wall.enabled = closedAtObservation;
            Physics2D.SyncTransforms();
            ScheduleDecision();
            wall.enabled = closedAtDispatch;
            Physics2D.SyncTransforms();
            GameManager.AIDecisions.CompleteAndApply();

            Assert.AreEqual(expectedNear, nearActivations);
            Assert.AreEqual(expectedFar, farActivations);
            Assert.AreEqual(expectedNear + expectedFar == 0, GameManager.Data.GetDefenseState(_operator.ObjectHash).isGuarding);
        }

        [Test]
        public void GateTransformMovesAfterSelection_BlocksDispatchBeforeNextPhysicsStep()
        {
            var gimmick = CreateSwitch(3f, operationX: 2f);
            int activations = 0;
            gimmick.OnActivated.AddListener(() => activations++);
            var gate = CreateObject("Sliding gate", new Vector2(1f, 3f));
            gate.layer = 6;
            gate.AddComponent<BoxCollider2D>().size = new Vector2(0.2f, 2f);
            bool previousAutoSync = Physics2D.autoSyncTransforms;
            try
            {
                Physics2D.autoSyncTransforms = false;
                ScheduleDecision();
                Assert.IsTrue(GameManager.Actors.TryGet(_operator.ObjectHash, out ActorRef owner));
                Assert.IsTrue(GameManager.Observation.IsGimmickOperationVisible(owner, gimmick.Snapshot));
                gate.transform.position = new Vector2(1f, 0f);
                GameManager.AIDecisions.CompleteAndApply();
                Assert.IsTrue(GameManager.Data.GetDefenseState(owner.Hash).isGuarding);
                Assert.AreEqual(0, activations);
                Assert.IsTrue(gimmick.IsAvailable);
            }
            finally
            {
                Physics2D.autoSyncTransforms = previousAutoSync;
            }
        }

        [Test]
        public void FlinchBeforeDispatch_PreventsOperatingTheSelectedSwitch()
        {
            var gimmick = CreateSwitch(3f);
            int activations = 0;
            gimmick.OnActivated.AddListener(() => activations++);
            ScheduleDecision();
            // Hit-stun can already be reflected in actor flags before an execution owns the body.
            GameManager.Data.GetFlags(_operator.ObjectHash).ActState = ActState.Flinch;
            GameManager.AIDecisions.CompleteAndApply();

            Assert.AreEqual(0, activations);
            Assert.IsTrue(gimmick.IsAvailable);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void EnteringEffectBeforeDispatch_KeepsSwitchArmedAndChoosesGuard(bool operatorEnters)
        {
            var gimmick = CreateSwitch(3f);
            int activations = 0;
            gimmick.OnActivated.AddListener(() => activations++);
            ScheduleDecision();
            var entering = operatorEnters ? (BaseCharacter)_operator : _teammate;
            entering.SetPhysicsPosition(new Vector2(3f, 0.5f));
            GameManager.AIDecisions.CompleteAndApply();

            Assert.AreEqual(0, activations);
            Assert.IsTrue(gimmick.IsAvailable);
            Assert.IsTrue(GameManager.Data.GetDefenseState(_operator.ObjectHash).isGuarding);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DelayedRelease_TracksIncomingTargetAndRechecksTeammateVelocity(bool teammateStartsCrossing)
        {
            var gimmick = CreateSwitch(3f, 1f);
            int activations = 0;
            gimmick.OnActivated.AddListener(() => activations++);
            // Outside the effect now, inside it one second later.
            _target.SetPhysicsPosition(new Vector2(4.5f, 0f));
            _target.SetPhysicsVelocity(new Vector2(-1.5f, 0f));
            ScheduleDecision();
            if (teammateStartsCrossing)
            {
                _teammate.SetPhysicsVelocity(new Vector2(-5f, 0f));
            }
            GameManager.AIDecisions.CompleteAndApply();

            Assert.AreEqual(teammateStartsCrossing ? 0 : 1, activations);
            Assert.AreEqual(teammateStartsCrossing, gimmick.IsAvailable);
            Assert.AreEqual(teammateStartsCrossing, GameManager.Data.GetDefenseState(_operator.ObjectHash).isGuarding);
        }

        [Test]
        public void SwitchReenabledBeforeDispatch_RequiresANewDecisionToActivate()
        {
            var gimmick = CreateSwitch(3f);
            int activations = 0;
            gimmick.OnActivated.AddListener(() => activations++);
            var original = gimmick.Snapshot;
            ScheduleDecision();
            gimmick.enabled = false;
            gimmick.enabled = true;
            GameManager.AIDecisions.CompleteAndApply();

            Assert.AreEqual(original.id, gimmick.Snapshot.id);
            Assert.AreNotEqual(original.generation, gimmick.Snapshot.generation);
            Assert.AreEqual(0, activations);
            Assert.IsTrue(gimmick.IsAvailable);
            Assert.IsTrue(GameManager.Data.GetDefenseState(_operator.ObjectHash).isGuarding);

            // Let the fallback finish and perform a fresh capture for the new lifetime.
            ScheduleDecision(2, 3f, 2f);
            GameManager.AIDecisions.CompleteAndApply();
            Assert.AreEqual(1, activations);
            Assert.IsFalse(gimmick.IsAvailable);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_previousScene.IsValid() && _previousScene.isLoaded)
                SceneManager.SetActiveScene(_previousScene);
            for (int i = _created.Count - 1; i >= 0; i--)
            {
                if (_created[i] != null) Object.Destroy(_created[i]);
            }
            _created.Clear();
            yield return null;
            TestSceneHelper.Cleanup();
            yield return null;
            if (_testScene.IsValid() && _testScene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(_testScene);
            if (_nextRoom.IsValid() && _nextRoom.isLoaded)
                yield return SceneManager.UnloadSceneAsync(_nextRoom);
        }
    }
}
