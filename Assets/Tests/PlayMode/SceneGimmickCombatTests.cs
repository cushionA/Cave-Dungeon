using System.Collections;
using System.Collections.Generic;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using R3;
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
            var manager = new GameObject("SceneGimmickCombatManager");
            manager.SetActive(false);
            var projectiles = new GameObject("Projectiles");
            projectiles.transform.SetParent(manager.transform, false);
            projectiles.AddComponent<ProjectileManager>();
            manager.AddComponent<GameManager>();
            manager.SetActive(true);
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

        private EnemyCharacter CreateOperator(float x, AIInfo ai = null)
        {
            GameObject actor = CreateObject("Operator", new Vector2(x, 0f));
            actor.AddComponent<Rigidbody2D>().gravityScale = 0f;
            actor.AddComponent<BoxCollider2D>();
            actor.AddComponent<ActionExecutorController>();
            var character = actor.AddComponent<EnemyCharacter>();
            character.SetCharacterInfoForTest(CreateInfo(CharacterBelong.Enemy));
            character.SetAIInfo(ai != null ? ai : CreateInteractionAI());
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

        private AISceneGimmickController CreateContactPlate(out BoxCollider2D trigger)
        {
            GameObject plate = CreateObject("Pressure plate", new Vector2(4f, 0f));
            trigger = plate.AddComponent<BoxCollider2D>();
            trigger.isTrigger = true;
            trigger.size = new Vector2(0.24f, 0.45f);
            AISceneGimmickController gimmick = plate.AddComponent<AISceneGimmickController>();
            gimmick.Configure(1 << 4, _operator.transform, 0.75f, plate.transform, 2f);
            gimmick.SetContactTriggerForTest(trigger);
            return gimmick;
        }

        private void StepContactPhysics()
        {
            Physics2D.SyncTransforms();
            Assert.IsTrue(_testScene.GetPhysicsScene2D().Simulate(0.02f));
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

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator AuthoredGroundBurst_CoverSelectsWaitAndOpeningSightCastsAtTheSameTarget()
        {
            const string dataPath = "Assets/MyAsset/Data/";
            var ai = UnityEditor.AssetDatabase.LoadAssetAtPath<AIInfo>(dataPath + "PLACEHOLDER_GroundBurstEnemyAI.asset");
            var melee = UnityEditor.AssetDatabase.LoadAssetAtPath<AttackInfo>(dataPath + "PLACEHOLDER_EnemyAttack.asset");
            var burst = UnityEditor.AssetDatabase.LoadAssetAtPath<AttackInfo>(dataPath + "PLACEHOLDER_GroundBurst.asset");
            Assert.IsNotNull(ai);
            Assert.IsNotNull(melee);
            Assert.IsNotNull(burst);

            _operator.gameObject.SetActive(false);
            EnemyCharacter caster = CreateOperator(0f, ai);
            caster.GetComponent<ActionExecutorController>().SetAttackInfosForTest(new[] { melee, burst });
            GameObject barrier = CreateObject("Ground burst cover", new Vector2(1.5f, 0f));
            barrier.layer = LayerMask.NameToLayer("Ground");
            BoxCollider2D wall = barrier.AddComponent<BoxCollider2D>();
            wall.size = new Vector2(0.2f, 2f);
            yield return null;

            Assert.IsTrue(GameManager.Actors.TryGet(caster.ObjectHash, out ActorRef owner));
            Assert.IsTrue(GameManager.Actors.TryGet(_target.ObjectHash, out ActorRef target));
            Assert.IsTrue(GameManager.TargetRelations.SetTarget(owner, target));
            int burstId = GameManager.Actions.RegisterDefinition(burst);
            Vector2 targetPosition = _target.PhysicsPosition;
            Vector2 ownerPosition = caster.PhysicsPosition;
            int waits = 0;
            int casts = 0;
            ActionExecution cast = default;
            using var listener = GameManager.Events.OnActionExecution.Subscribe(change =>
            {
                if (change.kind != ActionSignalKind.Started || change.execution.ownerRef != owner) { return; }
                if (change.execution.definitionId == burstId)
                {
                    casts++;
                    cast = change.execution;
                }
                else if (change.execution.definition.actState == ActState.Neutral) { waits++; }
            });
            GameManager.Instance.SimulationTick(DecisionInterval, 1f);
            Assert.That(waits, Is.GreaterThan(0));
            Assert.AreEqual(0, casts);
            Assert.IsTrue(GameManager.Observation.TryGetTargetLineOfSight(owner, target, out bool visible));
            Assert.IsFalse(visible);
            Assert.IsTrue(GameManager.TargetRelations.TryGetTarget(owner, out ActorRef blockedTarget));
            Assert.AreEqual(target, blockedTarget);

            wall.isTrigger = true;
            for (int step = 1; step <= 4 && casts == 0; step++)
            {
                GameManager.Instance.SimulationTick(DecisionInterval, 1f + step * DecisionInterval);
            }
            Assert.IsTrue(GameManager.Observation.TryGetTargetLineOfSight(owner, target, out visible));
            Assert.IsTrue(visible);
            Assert.AreEqual(1, casts);
            Assert.AreEqual(target, cast.targetRef);
            Assert.AreEqual(targetPosition, _target.PhysicsPosition);
            Assert.AreEqual(ownerPosition, caster.PhysicsPosition);

            Assert.IsTrue(GameManager.Actions.Execution.Cancel(cast.instanceId, force: true));
            wall.isTrigger = false;
            GameManager.Instance.SimulationTick(DecisionInterval, 4f);
            Assert.IsTrue(GameManager.Observation.TryGetTargetLineOfSight(owner, target, out visible));
            Assert.IsFalse(visible);
            Assert.AreEqual(1, casts);
        }
#endif

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
        public IEnumerator ContactPlate_WhenAiArmsIt_RequiresTheSelectedHostilesSafePhysicalContact()
        {
            AISceneGimmickController plate = CreateContactPlate(out BoxCollider2D trigger);
            var sideTrigger = trigger.gameObject.AddComponent<BoxCollider2D>();
            sideTrigger.isTrigger = true;
            sideTrigger.size = trigger.size;
            sideTrigger.offset = Vector2.up;
            BoxCollider2D door = CreateObject("Trapped door", new Vector2(12f, 0f)).AddComponent<BoxCollider2D>();
            door.enabled = false;
            int activations = 0;
            plate.OnActivated.AddListener(() => { door.enabled = true; activations++; });
            BaseCharacter unrelated = CreatePassiveActor(CharacterBelong.Ally, -8f);
            yield return null;

            _target.SetPhysicsPosition(new Vector2(4f, 0f));
            StepContactPhysics();
            Assert.IsFalse(door.enabled, "An unarmed plate ignores physical contact.");
            _target.SetPhysicsPosition(new Vector2(3f, 0f));
            StepContactPhysics();
            GameManager.Instance.SimulationTick(DecisionInterval, 1f);
            Assert.IsTrue(plate.IsContactArmed);
            Assert.IsFalse(plate.IsAvailable);
            Assert.IsFalse(door.enabled, "Arming selects the plate without moving the door.");

            unrelated.SetPhysicsPosition(new Vector2(4f, 0f));
            StepContactPhysics();
            Assert.IsFalse(door.enabled, "Another hostile cannot spend the reservation.");
            unrelated.SetPhysicsPosition(new Vector2(-8f, 0f));
            _target.SetPhysicsPosition(new Vector2(4f, 1f));
            StepContactPhysics();
            Assert.IsFalse(door.enabled, "Contact with the nearby sensor is not contact with the configured plate.");

            _teammate.SetPhysicsPosition(new Vector2(4.8f, 1.2f));
            _target.SetPhysicsPosition(new Vector2(4f, 0f));
            StepContactPhysics();
            Assert.IsTrue(plate.IsContactArmed);
            Assert.IsFalse(door.enabled, "Friendly occupancy vetoes a genuine contact.");

            _teammate.SetPhysicsPosition(new Vector2(8f, 0f));
            StepContactPhysics();
            Assert.IsTrue(door.enabled);
            Assert.IsFalse(plate.IsContactArmed);
            Assert.IsFalse(plate.IsAvailable);
            Assert.AreEqual(1, activations);

            _target.SetPhysicsPosition(new Vector2(3f, 0f));
            StepContactPhysics();
            _target.SetPhysicsPosition(new Vector2(4f, 0f));
            StepContactPhysics();
            Assert.AreEqual(1, activations);
        }

        [Test]
        public void ContactPlate_WhenTheSelectedActorIsRecycled_RequiresANewObservedArm()
        {
            AISceneGimmickController plate = CreateContactPlate(out _);
            int activations = 0;
            plate.OnActivated.AddListener(() => activations++);
            GameManager.Instance.SimulationTick(DecisionInterval, 1f);
            Assert.IsTrue(plate.IsContactArmed);
            Assert.IsTrue(GameManager.Actors.TryGet(_target.ObjectHash, out ActorRef oldTarget));

            _target.OnPoolReturn();
            _target.OnPoolAcquire();
            CharacterRegistry.RegisterPlayer(_target.ObjectHash);
            Assert.IsFalse(GameManager.Actors.IsCurrent(oldTarget));
            _target.SetPhysicsPosition(new Vector2(4f, 0f));
            StepContactPhysics();
            Assert.AreEqual(0, activations);
            Assert.IsFalse(plate.IsContactArmed);
            Assert.IsTrue(plate.IsAvailable);

            GameManager.Instance.SimulationTick(DecisionInterval, 1f + DecisionInterval);
            Assert.IsTrue(plate.IsContactArmed);
            StepContactPhysics();
            Assert.AreEqual(1, activations);
        }

        [UnityTest]
        public IEnumerator ContactPlate_WhenCompanionLuresAnActivePursuer_ActivatesFromRealContactOnce()
            => RunContactLure(loseAggro: false);

        [UnityTest]
        public IEnumerator ContactPlate_WhenPursuerChoosesAnotherTarget_ReleasesTheLure()
            => RunContactLure(loseAggro: true);

        private IEnumerator RunContactLure(bool loseAggro)
        {
            _operator.OnPoolReturn();
            _teammate.OnPoolReturn();
            _operator.gameObject.SetActive(false);
            _teammate.gameObject.SetActive(false);
            _target.SetPhysicsPosition(new Vector2(10f, 0f));
            GameObject floor = CreateObject("Lure corridor floor", new Vector2(0f, -1f));
            floor.layer = LayerMask.NameToLayer("Ground");
            floor.AddComponent<BoxCollider2D>().size = new Vector2(24f, 1f);

            AIInfo chase = CreateInteractionAI();
            chase.actDataList = new[] { new ActData { actType = ActType.Move, weight = 1f, coolTime = 0.3f } };
            chase.modes[0].availableActIndices = new[] { 0 };
            EnemyCharacter pursuer = CreateOperator(-6.5f, chase);
            pursuer.CharacterInfoRef.moveSpeed = 2.75f;
            pursuer.Rigidbody.constraints = RigidbodyConstraints2D.FreezeRotation;
            GameObject baitObject = CreateObject("Sister bait", new Vector2(-1.7f, 0f));
            Rigidbody2D baitBody = baitObject.AddComponent<Rigidbody2D>();
            baitBody.gravityScale = 0f;
            baitBody.constraints = RigidbodyConstraints2D.FreezeRotation;
            baitObject.AddComponent<BoxCollider2D>();
            ActionExecutorController baitActions = baitObject.AddComponent<ActionExecutorController>();
            CompanionCharacter bait = baitObject.AddComponent<CompanionCharacter>();
            Game.Core.CharacterInfo baitInfo = TestSceneHelper.CreateTestCharacterInfo(CharacterBelong.Ally,
                CharacterFeature.Companion, moveSpeed: 4.5f);
            _created.Add(baitInfo);
            bait.SetCharacterInfoForTest(baitInfo);
            AIInfo lureRules = CreateInteractionAI();
            lureRules.actDataList[1].coolTime = DecisionInterval;
            bait.SetAIInfo(lureRules);

            GameObject plateObject = CreateObject("Lure pressure plate", Vector2.zero);
            BoxCollider2D trigger = plateObject.AddComponent<BoxCollider2D>();
            trigger.isTrigger = true;
            trigger.size = new Vector2(0.22f, 0.8f);
            AISceneGimmickController plate = plateObject.AddComponent<AISceneGimmickController>();
            Transform operation = CreateObject("Bait waiting point", new Vector2(-1.7f, 0f)).transform;
            Transform exit = CreateObject("Bait escape point", new Vector2(3.8f, 0f)).transform;
            plate.Configure(1 << 4, operation, 0.7f, plateObject.transform, 0.95f);
            plate.SetContactTriggerForTest(trigger, timeout: 4f);
            plate.SetLureExitForTest(exit);

            BaseCharacter otherTarget = loseAggro ? CreatePassiveActor(CharacterBelong.Ally, -7.6f) : null;
            if (otherTarget != null) { otherTarget.gameObject.SetActive(false); }
            int activations = 0;
            Vector2 baitAtContact = default;
            Vector2 pursuerAtContact = default;
            bool exactTriggerContact = false;
            Collider2D enemyBody = pursuer.GetComponent<BoxCollider2D>();
            plate.OnActivated.AddListener(() =>
            {
                activations++;
                baitAtContact = bait.PhysicsPosition;
                pursuerAtContact = pursuer.PhysicsPosition;
                exactTriggerContact = trigger.Distance(enemyBody).distance <= 0f;
            });
            yield return null;

            Assert.IsNotNull(bait.AIController);
            Assert.IsTrue(GameManager.Actors.TryGet(bait.ObjectHash, out ActorRef baitRef));
            Assert.IsTrue(GameManager.Actors.TryGet(pursuer.ObjectHash, out ActorRef pursuerRef));
            Vector2 baitBefore = bait.PhysicsPosition;
            Vector2 pursuerBefore = pursuer.PhysicsPosition;
            bool armed = false;
            bool retargeted = false;
            bool movedThroughPlate = false;
            long lureGroup = 0;
            const float stepSize = 0.02f;
            for (int step = 1; step <= 245; step++)
            {
                GameManager.Instance.SimulationTick(stepSize, step * stepSize);
                if (!armed && plate.IsContactArmed)
                {
                    armed = true;
                    lureGroup = baitActions.CurrentGroupId;
                    Assert.AreNotEqual(0, lureGroup);
                    Assert.IsFalse(plate.Snapshot.ContainsEffect(pursuer.PhysicsPosition));
                    Assert.IsTrue(GameManager.TargetRelations.TryGetTarget(pursuerRef, out ActorRef pursued));
                    Assert.AreEqual(baitRef, pursued);
                    if (otherTarget != null)
                    {
                        otherTarget.gameObject.SetActive(true);
                        yield return null;
                        CharacterRegistry.RegisterAlly(otherTarget.ObjectHash);
                    }
                }
                StepContactPhysics();
                movedThroughPlate |= armed && bait.PhysicsPosition.x > 0.15f;
                if (loseAggro && armed && GameManager.TargetRelations.TryGetTarget(pursuerRef, out ActorRef newTarget)
                    && newTarget.Hash == otherTarget.ObjectHash)
                {
                    retargeted = true;
                    if (!plate.IsContactArmed && !GameManager.Actions.Execution.IsGroupActive(lureGroup)) { break; }
                }
                if (activations > 0)
                {
                    GameManager.Instance.SimulationTick(stepSize, (step + 1) * stepSize);
                    break;
                }
                yield return new WaitForFixedUpdate();
            }

            Assert.IsTrue(armed, $"Bait {bait.PhysicsPosition}, pursuer {pursuer.PhysicsPosition}.");
            Assert.That(pursuer.PhysicsPosition.x, Is.GreaterThan(pursuerBefore.x));
            if (loseAggro)
            {
                Assert.IsTrue(retargeted);
                Assert.IsFalse(plate.IsContactArmed);
                Assert.IsFalse(GameManager.Actions.Execution.IsGroupActive(lureGroup));
                Assert.AreEqual(0, activations);
                yield break;
            }

            Assert.IsTrue(movedThroughPlate, $"Bait {bait.PhysicsPosition}, pursuer {pursuer.PhysicsPosition}.");
            Assert.AreEqual(1, activations);
            Assert.IsFalse(plate.IsAvailable);
            Assert.IsFalse(GameManager.Actions.Execution.IsGroupActive(lureGroup));
            Assert.IsTrue(exactTriggerContact);
            Assert.That(baitAtContact.x - baitBefore.x, Is.GreaterThan(2f));
            Assert.IsFalse(plate.Snapshot.ContainsEffect(baitAtContact));
            Assert.IsTrue(plate.Snapshot.ContainsEffect(pursuerAtContact));
            for (int step = 0; step < 10; step++) { StepContactPhysics(); }
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
