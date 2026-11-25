#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Game.Tests.PlayMode
{
    public sealed class CharacterMotor3DScenePlayTests
    {
        private const string k_ScenePath = "Assets/Scenes/CharacterMotor3DTestScene.unity";
        private const float k_Step = 0.02f;
        private readonly InputTestFixture _devices = new InputTestFixture();
        private Scene _scene;
        private GameManager _managerBeforeSceneLoad;
        private bool _mockInputActive;
        private InputActionAsset _mockActions;

        [UnityTearDown]
        public IEnumerator UnloadTestScene()
        {
            if (_scene.isLoaded)
            {
                yield return SceneManager.UnloadSceneAsync(_scene);
            }
            if (_managerBeforeSceneLoad == null && GameManager.Instance != null)
            {
                Object.Destroy(GameManager.Instance.gameObject);
                yield return null;
            }
            if (_mockInputActive)
            {
                if (_mockActions != null) { Object.Destroy(_mockActions); }
                _devices.TearDown();
                _mockInputActive = false;
            }
        }

        [UnityTest]
        public IEnumerator AuthoredScene_WhenWalkingUpTheRampAndUnderTheBridge_ReportsBothSurfaces()
        {
            yield return LoadTestScene();
            CharacterMotor3D motor = FindKnightMotor();
            PlayerInput input = motor.GetComponent<PlayerInput>();
            Assert.IsNotNull(input.actions.FindAction("Player/Move"));
            Assert.IsNotNull(input.actions.FindAction("Player/Jump"));
            Assert.IsNotNull(input.camera.GetComponent<CameraFollow3D>());
            motor.GetComponent<PlayerCharacterInput3D>().enabled = false;

            Place(motor, new Vector3(0f, 0.12f, -8.8f));
            for (int i = 0; i < 140 && motor.FeetPosition.z < 0f; i++)
            {
                motor.Simulate(Vector3.forward, false, k_Step);
            }
            Assert.That(motor.FeetPosition.z, Is.GreaterThanOrEqualTo(0f));
            Assert.That(motor.FeetPosition.y, Is.GreaterThan(2.4f));
            Assert.IsNotNull(motor.Ground.Surface);
            Assert.AreEqual("Upper bridge", motor.Ground.Surface.name);

            Place(motor, new Vector3(-3f, 0.12f, motor.FeetPosition.z));
            for (int i = 0; i < 55 && motor.FeetPosition.x < 0f; i++)
            {
                motor.Simulate(Vector3.right, false, k_Step);
            }
            Assert.That(motor.FeetPosition.x, Is.GreaterThanOrEqualTo(0f));
            Assert.That(motor.FeetPosition.y, Is.LessThan(0.12f));
            Assert.IsNotNull(motor.Ground.Surface);
            Assert.AreEqual("Lower floor", motor.Ground.Surface.name);
        }

        [UnityTest]
        public IEnumerator AuthoredScene_LeftMouseButton_StartsOneSwordAttackAndDamagesTheTarget()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            _devices.Setup();
            _mockInputActive = true;
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            Mouse mouse = InputSystem.AddDevice<Mouse>();
            yield return LoadTestScene();

            CharacterMotor3D motor = FindKnightMotor();
            PlayerInput input = motor.GetComponent<PlayerInput>();
            input.SwitchCurrentControlScheme("Keyboard&Mouse", keyboard, mouse);
            CharacterActor3D knight = motor.GetComponent<CharacterActor3D>();
            CharacterActor3D target = _scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<CharacterActor3D>())
                .Single(actor => actor.name == "[PLACEHOLDER] Sword target");
            Assert.IsTrue(knight.IsAlive);
            Assert.IsTrue(target.IsAlive);
            int initialHp = GameManager.Data.GetVitals(target.Actor.Hash).currentHp;

            ActionExecutionSystem execution = GameManager.Actions.Execution;
            int attacksStarted = 0;
            void CountAttacks(ActionExecutionEvent notification)
            {
                if (notification.kind == ActionSignalKind.Started && notification.execution.ownerRef == knight.Actor)
                {
                    attacksStarted++;
                }
            }

            execution.Changed += CountAttacks;
            try
            {
                // A press that is queued across an action map transition must be discarded.
                _devices.Press(mouse.leftButton, queueEventOnly: true);
                InputSystem.Update();
                input.SwitchCurrentActionMap("UI");
                input.SwitchCurrentActionMap("Player");
                _devices.Release(mouse.leftButton, queueEventOnly: true);
                InputSystem.Update();
                yield return new WaitForFixedUpdate();
                Assert.That(attacksStarted, Is.Zero);

                Vector3 start = motor.transform.position;
                _devices.Press(mouse.leftButton, queueEventOnly: true);
                _devices.Press(keyboard.wKey, queueEventOnly: true);
                InputSystem.Update();
                yield return new WaitForFixedUpdate();
                Assert.IsTrue(GameManager.Actions.IsMovementBlocked(knight.Actor.Hash));
                Vector3 duringSwing = motor.transform.position - start;
                Assert.That(new Vector2(duringSwing.x, duringSwing.z).sqrMagnitude, Is.LessThan(0.000001f));
                _devices.Release(keyboard.wKey, queueEventOnly: true);
                InputSystem.Update();
                for (int i = 0; i < 24; i++) { yield return new WaitForFixedUpdate(); }
                Assert.That(attacksStarted, Is.EqualTo(1));
                Assert.That(GameManager.Data.GetVitals(target.Actor.Hash).currentHp, Is.LessThan(initialHp));
            }
            finally
            {
                execution.Changed -= CountAttacks;
            }
        }

        [UnityTest]
        public IEnumerator AuthoredScene_BridgeCasterWarnsOnItsOwnFloorAndHitsAnActorEnteringThatFloor()
        {
            yield return LoadTestScene();
            CharacterMotor3D motor = FindKnightMotor();
            motor.GetComponent<PlayerCharacterInput3D>().enabled = false;
            CharacterActor3D knight = motor.GetComponent<CharacterActor3D>();
            ProximityAttack3D caster = _scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<ProximityAttack3D>())
                .Single(candidate => candidate.name == "[PLACEHOLDER] Bridge caster");
            Vector3 casterPosition = caster.transform.position;
            Place(motor, new Vector3(casterPosition.x, 0.12f, casterPosition.z));
            Assert.AreEqual("Lower floor", motor.Ground.Surface.name);
            int initialHp = GameManager.Data.GetVitals(knight.Actor.Hash).currentHp;

            GroundAttackWarning3D warning = null;
            for (int i = 0; i < 12 && warning == null; i++)
            {
                yield return new WaitForFixedUpdate();
                warning = _scene.GetRootGameObjects()
                    .Select(root => root.GetComponent<GroundAttackWarning3D>())
                    .SingleOrDefault(candidate => candidate != null && candidate.isActiveAndEnabled);
            }
            Assert.IsNotNull(warning);
            Assert.That(warning.transform.position.y, Is.GreaterThan(2.6f));
            Assert.AreEqual(initialHp, GameManager.Data.GetVitals(knight.Actor.Hash).currentHp);

            Place(motor, casterPosition + new Vector3(0.85f, 0.12f, 0f));
            Assert.AreEqual("Upper bridge", motor.Ground.Surface.name);
            for (int i = 0; i < 60 && GameManager.Data.GetVitals(knight.Actor.Hash).currentHp == initialHp; i++)
            {
                yield return new WaitForFixedUpdate();
            }
            Assert.That(GameManager.Data.GetVitals(knight.Actor.Hash).currentHp, Is.LessThan(initialHp));
            Assert.IsTrue(warning == null || !warning.isActiveAndEnabled);
        }

        [UnityTest]
        public IEnumerator AuthoredScene_BoltCasterWarnsTheKnightBeforeImpactAndClearsOnHit()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            yield return LoadTestScene();
            CharacterMotor3D motor = FindKnightMotor();
            motor.GetComponent<PlayerCharacterInput3D>().enabled = false;
            CharacterActor3D knight = motor.GetComponent<CharacterActor3D>();
            CharacterActor3D caster = FindActor("[PLACEHOLDER] Bolt caster");
            UIDocument document = FindHud();
            Assert.IsNotNull(GameManager.Projectiles3D);

            Place(motor, caster.transform.position + new Vector3(0f, 0.12f, 3.2f));
            Assert.AreEqual("Lower floor", motor.Ground.Surface.name);
            int initialHp = GameManager.Data.GetVitals(knight.Actor.Hash).currentHp;
            ProjectileWarningOverlay warning = document.rootVisualElement.Q<ProjectileWarningOverlay>();
            Assert.IsNotNull(warning);
            Assert.That(warning.Directions.Count, Is.Zero);

            for (int i = 0; i < 60 && warning.Directions.Count == 0; i++)
            {
                yield return new WaitForFixedUpdate();
                yield return null;
            }
            Assert.That(warning.Directions.Count, Is.EqualTo(1));
            Assert.That(GameManager.Data.GetVitals(knight.Actor.Hash).currentHp, Is.EqualTo(initialHp));

            for (int i = 0; i < 60 && GameManager.Data.GetVitals(knight.Actor.Hash).currentHp == initialHp; i++)
            {
                yield return new WaitForFixedUpdate();
            }
            yield return null;
            Assert.That(GameManager.Data.GetVitals(knight.Actor.Hash).currentHp, Is.LessThan(initialHp));
            Assert.That(warning.Directions.Count, Is.Zero);
        }

        [UnityTest]
        public IEnumerator AuthoredScene_GroundBoltWarnsOnBridgeAndOnlyDamagesKnightOnThatFloor()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            yield return LoadTestScene();
            CharacterMotor3D motor = FindKnightMotor();
            motor.GetComponent<PlayerCharacterInput3D>().enabled = false;
            CharacterActor3D knight = motor.GetComponent<CharacterActor3D>();
            CharacterActor3D caster = FindActor("[PLACEHOLDER] Ground bolt caster");
            ProjectileWarningOverlay direction = FindHud().rootVisualElement.Q<ProjectileWarningOverlay>();
            Assert.IsNotNull(direction);
            Vector3 onBridge = new Vector3(0f, 2.72f, -2.3f);
            Place(motor, onBridge);
            Assert.AreEqual("Upper bridge", motor.Ground.Surface.name);
            int initialHp = GameManager.Data.GetVitals(knight.Actor.Hash).currentHp;

            GroundAttackWarning3D floorWarning = null;
            for (int i = 0; i < 35 && floorWarning == null; i++)
            {
                yield return new WaitForFixedUpdate();
                floorWarning = _scene.GetRootGameObjects()
                    .Select(root => root.GetComponent<GroundAttackWarning3D>())
                    .SingleOrDefault(candidate => candidate != null && candidate.isActiveAndEnabled);
            }
            Assert.IsNotNull(floorWarning, "The authored caster warns during windup.");
            Assert.AreEqual("Upper bridge", caster.Motor.Ground.Surface.name);
            Assert.That(floorWarning.transform.position.y, Is.EqualTo(2.6f).Within(0.08f));
            Assert.That(floorWarning.transform.position.z, Is.EqualTo(onBridge.z).Within(0.12f));
            Assert.That(GameManager.Projectiles3D.ActiveCount, Is.Zero);
            Assert.That(direction.Directions, Is.Empty);

            for (int i = 0; i < 65 && direction.Directions.Count == 0; i++)
            {
                yield return new WaitForFixedUpdate();
                yield return null;
            }
            Assert.That(direction.Directions.Count, Is.EqualTo(1));
            Assert.That(GameManager.Projectiles3D.ActiveCount, Is.EqualTo(1));
            Assert.IsTrue(floorWarning.isActiveAndEnabled);
            Assert.That(GameManager.Data.GetVitals(knight.Actor.Hash).currentHp, Is.EqualTo(initialHp));

            Place(motor, new Vector3(onBridge.x, 0.12f, onBridge.z));
            Assert.AreEqual("Lower floor", motor.Ground.Surface.name);
            Assert.IsTrue(floorWarning.isActiveAndEnabled);
            Assert.That(floorWarning.transform.position.y, Is.EqualTo(2.6f).Within(0.08f));
            for (int i = 0; i < 65 && GameManager.Projectiles3D.ActiveCount != 0; i++)
            {
                yield return new WaitForFixedUpdate();
            }
            yield return null;
            Assert.That(GameManager.Projectiles3D.ActiveCount, Is.Zero);
            Assert.That(GameManager.Data.GetVitals(knight.Actor.Hash).currentHp, Is.EqualTo(initialHp));
            Assert.IsTrue(floorWarning == null || !floorWarning.isActiveAndEnabled);
            Assert.That(direction.Directions, Is.Empty);

            Place(motor, onBridge);
            Assert.AreEqual("Upper bridge", motor.Ground.Surface.name);
            for (int i = 0; i < 240 && GameManager.Data.GetVitals(knight.Actor.Hash).currentHp == initialHp; i++)
            {
                yield return new WaitForFixedUpdate();
            }
            yield return null;
            Assert.That(GameManager.Data.GetVitals(knight.Actor.Hash).currentHp, Is.LessThan(initialHp));
            Assert.That(GameManager.Projectiles3D.ActiveCount, Is.Zero);
            Assert.That(direction.Directions, Is.Empty);
        }

        [UnityTest]
        public IEnumerator AuthoredScene_RegularAndDangerousMelee_DifferByWindupAudioAndNeverDrawAnArc()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            yield return LoadTestScene();
            CharacterMotor3D motor = FindKnightMotor();
            motor.GetComponent<PlayerCharacterInput3D>().enabled = false;
            CharacterActor3D knight = motor.GetComponent<CharacterActor3D>();
            CharacterActor3D regular = FindActor("[PLACEHOLDER] Regular melee opponent");
            CharacterActor3D dangerous = FindActor("[PLACEHOLDER] Dangerous melee opponent");
            ProjectileWarningOverlay direction = FindHud().rootVisualElement.Q<ProjectileWarningOverlay>();
            Assert.IsNotNull(direction);
            int initialHp = GameManager.Data.GetVitals(knight.Actor.Hash).currentHp;

            Place(motor, regular.transform.position + new Vector3(1.25f, 0.12f, 0f));
            for (int i = 0; i < 75 && GameManager.Data.GetVitals(knight.Actor.Hash).currentHp == initialHp; i++)
            {
                yield return new WaitForFixedUpdate();
            }
            int hpAfterRegular = GameManager.Data.GetVitals(knight.Actor.Hash).currentHp;
            Assert.That(hpAfterRegular, Is.LessThan(initialHp));
            Assert.IsNull(FindDangerousMeleeAudio());
            Assert.That(direction.Directions, Is.Empty);

            Place(motor, dangerous.transform.position + new Vector3(1.25f, 0.12f, 0f));
            AudioSource audio = null;
            for (int i = 0; i < 20 && audio == null; i++)
            {
                yield return new WaitForFixedUpdate();
                audio = FindDangerousMeleeAudio();
            }
            Assert.IsNotNull(audio);
            Assert.IsNotNull(audio.clip);
            Assert.That(audio.clip.samples, Is.GreaterThan(0));
            Assert.That(GameManager.Data.GetVitals(knight.Actor.Hash).currentHp, Is.EqualTo(hpAfterRegular));
            Assert.That(direction.Directions, Is.Empty);

            for (int i = 0; i < 75 && GameManager.Data.GetVitals(knight.Actor.Hash).currentHp == hpAfterRegular; i++)
            {
                yield return new WaitForFixedUpdate();
            }
            Assert.That(GameManager.Data.GetVitals(knight.Actor.Hash).currentHp, Is.LessThan(hpAfterRegular));
            Assert.That(direction.Directions, Is.Empty);
        }

        [UnityTest]
        public IEnumerator AuthoredScene_SkillHoldsASwordTargetMarkAndFiresOnRelease()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            _devices.Setup();
            _mockInputActive = true;
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            Mouse mouse = InputSystem.AddDevice<Mouse>();
            yield return LoadTestScene();

            CharacterMotor3D motor = FindKnightMotor();
            PlayerCharacterInput3D knightInput = motor.GetComponent<PlayerCharacterInput3D>();
            PlayerInput input = motor.GetComponent<PlayerInput>();
            // Each test has its own input manager and devices. Resolve the authored bindings in that manager.
            _mockActions = InputActionAsset.FromJson(input.actions.ToJson());
            input.actions = _mockActions;
            input.neverAutoSwitchControlSchemes = true;
            input.SwitchCurrentControlScheme("Keyboard&Mouse", keyboard, mouse);
            _devices.ScheduleFocusChangedEvent(true);
            yield return new WaitForFixedUpdate();

            Assert.IsTrue(keyboard.added);
            Assert.AreEqual("Player", input.currentActionMap.name);
            Assert.IsTrue(input.devices.Contains(keyboard));
            CharacterActor3D knight = motor.GetComponent<CharacterActor3D>();
            CharacterActor3D target = FindActor("[PLACEHOLDER] Sword target");
            Assert.IsNotNull(FindHud().GetComponent<MultiLockMarkerHud3D>());
            AttackInfo bolt = UnityEditor.AssetDatabase.LoadAssetAtPath<AttackInfo>("Assets/MyAsset/Data/PLACEHOLDER_Bolt3D.asset");
            Assert.IsTrue(new MultiLockTargeting3D().TryGetCandidate(knight, target.Actor, bolt, out Vector3 markPosition));
            Assert.IsTrue(PlayerInputHandler.TryProjectMark(input.camera, markPosition, out _));
            int initialHp = GameManager.Data.GetVitals(target.Actor.Hash).currentHp;

            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Q));
            InputSystem.Update();
            Assert.IsTrue(keyboard.qKey.isPressed);
            Assert.IsTrue(input.currentActionMap.FindAction("Skill").IsPressed());
            Assert.That(knightInput.MultiLockTargets.Count, Is.EqualTo(1));
            for (int i = 0; i < 6; i++) { yield return new WaitForFixedUpdate(); }
            Assert.That(knightInput.MultiLockTargets.Count, Is.EqualTo(1));
            Assert.AreEqual(target.Actor, knightInput.MultiLockTargets[0]);
            Assert.That(GameManager.Data.GetVitals(target.Actor.Hash).currentHp, Is.EqualTo(initialHp));

            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.Update();
            yield return new WaitForFixedUpdate();
            Assert.That(knightInput.MultiLockTargets.Count, Is.Zero);
            for (int i = 0; i < 70 && GameManager.Data.GetVitals(target.Actor.Hash).currentHp == initialHp; i++)
            {
                yield return new WaitForFixedUpdate();
            }
            Assert.That(GameManager.Data.GetVitals(target.Actor.Hash).currentHp, Is.LessThan(initialHp));
        }

        private CharacterActor3D FindActor(string name) => _scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<CharacterActor3D>())
            .Single(actor => actor.name == name);

        private UIDocument FindHud() => _scene.GetRootGameObjects()
            .Select(root => root.GetComponent<UIDocument>())
            .Single(document => document != null);

        private static AudioSource FindDangerousMeleeAudio() => GameManager.Instance.GetComponentsInChildren<AudioSource>()
            .SingleOrDefault(source => source.name == "[Dangerous Melee Warning]");

        private IEnumerator LoadTestScene()
        {
            _managerBeforeSceneLoad = GameManager.Instance;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(k_ScenePath,
                new LoadSceneParameters(LoadSceneMode.Additive, LocalPhysicsMode.Physics3D));
            _scene = SceneManager.GetSceneByPath(k_ScenePath);
        }

        private CharacterMotor3D FindKnightMotor() => _scene.GetRootGameObjects()
            .Select(root => root.GetComponent<CharacterMotor3D>())
            .Single(character => character != null);

        private static void Place(CharacterMotor3D motor, Vector3 position)
        {
            motor.enabled = false;
            motor.transform.position = position;
            Physics.SyncTransforms();
            motor.enabled = true;
            for (int i = 0; i < 15; i++)
            {
                motor.Simulate(Vector3.zero, false, k_Step);
            }
        }
    }
}
#endif
