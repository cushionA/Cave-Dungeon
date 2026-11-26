using System;
using System.Collections;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Game.Tests.PlayMode
{
    public sealed partial class ExpeditionSummonDeployment3DPlayTests
    {
        private const string k_PersistentOrderRoom = "Exploration field order persistent room";
        private const string k_OrderAreaPath = "Assets/Scenes/StreamingAreaB3DTestScene.unity";
        private Scene _foreignOrderScene;
        private bool _streamsOrderArea;

        [UnityTest]
        public IEnumerator ExplorationKeyboardOrders_RallyAndDefendYieldToTheEncounterAndRestoreFollow()
        {
            Vector3 spawn = _positions[0].position;
            CharacterActor3D knight = CreateCharacter("Knight", spawn + Vector3.forward * 2f, CharacterFeature.Player);
            EncounterCombat3D encounter = NewObject("Encounter", Vector3.zero).AddComponent<EncounterCombat3D>();
            encounter.enabled = false;
            SetSerializedField(encounter, "knight", knight);
            _scoutPrefab.gameObject.AddComponent<EncounterCombatant3D>();
            CharacterActor3D sister = _deployment.GetComponent<CharacterActor3D>();
            sister.transform.position = spawn + Vector3.right * 0.9f + Vector3.forward * 0.25f;
            CompanionController3D companion = sister.gameObject.AddComponent<CompanionController3D>();
            SetSerializedField(companion, "knight", knight);
            SetSerializedField(companion, "groupCombat", encounter);
            CharacterActor3D scout = PrepareOrderGroup(knight, encounter);
            ActorRef scoutIdentity = scout.Actor;
            Vector3 defend = new Vector3(spawn.x, 0f, spawn.z - 1.7f);
            Camera camera = OrderCamera(defend);
            using var input = new ExplorationOrderKeyboard(knight, _deployment, camera);
            yield return new WaitForFixedUpdate();
            float originalSisterDistance = Vector3.Distance(knight.Motor.FeetPosition, sister.Motor.FeetPosition);
            Assert.That(originalSisterDistance, Is.GreaterThan(1.5f));

            input.Press(true);
            Assert.That(_deployment.ActiveExplorationOrder, Is.Null);
            input.Release(0.08);
            Assert.That(_deployment.ActiveExplorationOrder, Is.EqualTo(BroadcastAction.Rally));
            for (int i = 0; i < 45 && _deployment.ActiveExplorationOrder.HasValue; i++) { yield return new WaitForFixedUpdate(); }
            Assert.That(_deployment.ActiveExplorationOrder, Is.Null);
            Assert.That(Vector3.Distance(scout.Motor.FeetPosition, knight.Motor.FeetPosition), Is.LessThan(1.22f));
            Assert.That(Vector3.Distance(sister.Motor.FeetPosition, knight.Motor.FeetPosition), Is.LessThan(1.22f));

            input.Tap(0.45);
            input.Tap(0.45);
            input.Aim(defend);
            input.Tap();
            Assert.That(_deployment.ActiveExplorationOrder, Is.EqualTo(BroadcastAction.DefendPoint));
            const float settledDefendDistance = 0.32f;
            for (int i = 0; i < 65 && Vector3.Distance(scout.Motor.FeetPosition, defend) > settledDefendDistance; i++)
            {
                yield return new WaitForFixedUpdate();
            }
            Assert.That(Vector3.Distance(scout.Motor.FeetPosition, defend), Is.LessThanOrEqualTo(settledDefendDistance));
            Assert.That(scout.Motor.Ground.Surface, Is.SameAs(_floor));
            float defendedZ = scout.Motor.FeetPosition.z;
            for (int i = 0; i < 4; i++) { yield return new WaitForFixedUpdate(); }
            Assert.That(scout.Motor.FeetPosition.z, Is.EqualTo(defendedZ).Within(0.05f));
            Assert.That(_deployment.ActiveExplorationOrder, Is.EqualTo(BroadcastAction.DefendPoint));

            input.Press(true);
            encounter.enabled = true;
            input.Release(0.08);
            Assert.That(encounter.ActiveOrder, Is.Null, "A press from exploration cannot issue into a newly active encounter.");
            yield return new WaitForFixedUpdate();
            Assert.That(_deployment.ActiveExplorationOrder, Is.Null);

            encounter.enabled = false;
            for (int i = 0; i < 65; i++) { yield return new WaitForFixedUpdate(); }
            Assert.That(Vector3.Distance(scout.Motor.FeetPosition, knight.Motor.FeetPosition), Is.InRange(2.02f, 2.23f));
            Assert.That(scout.Actor, Is.EqualTo(scoutIdentity));
        }

        [UnityTest]
        public IEnumerator ExplorationKeyboardFocus_RejectsAForeignWorldAndDropsTheOldTargetGeneration()
        {
            Vector3 spawn = _positions[0].position;
            CharacterActor3D knight = CreateCharacter("Knight", spawn + Vector3.forward * 2f, CharacterFeature.Player);
            CharacterActor3D enemy = CreateCharacter("Focus target", spawn - Vector3.forward * 2.5f,
                CharacterFeature.Minion, CharacterBelong.Enemy);
            enemy.gameObject.SetActive(true);
            _foreignOrderScene = SceneManager.CreateScene("Foreign 3D exploration order world",
                new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            CharacterActor3D foreign = CreateCharacter("Other world enemy", enemy.transform.position,
                CharacterFeature.Minion, CharacterBelong.Enemy);
            SceneManager.MoveGameObjectToScene(foreign.gameObject, _foreignOrderScene);
            foreign.gameObject.SetActive(true);
            AttackInfo melee = CreateOrderMelee();
            EncounterCombatant3D combatant = _scoutPrefab.gameObject.AddComponent<EncounterCombatant3D>();
            SetSerializedField(combatant, "attack", melee);
            CharacterActor3D scout = PrepareOrderGroup(knight);
            Camera camera = OrderCamera(enemy.transform.position);
            using var input = new ExplorationOrderKeyboard(knight, _deployment, camera);
            GameManager.Instance.enabled = true;
            yield return new WaitForFixedUpdate();

            input.Tap(0.45);
            input.Aim(enemy.Body.bounds.center);
            input.Tap();
            Assert.That(_deployment.ActiveExplorationOrder, Is.EqualTo(BroadcastAction.FocusFire));
            Assert.That(_deployment.TryIssueExplorationOrder(knight, BroadcastAction.FocusFire, foreign.Actor), Is.False);
            Assert.That(_deployment.ActiveExplorationOrder, Is.EqualTo(BroadcastAction.FocusFire));
            int initialHp = GameManager.Data.GetVitals(enemy.Actor.Hash).currentHp;
            for (int i = 0; i < 80 && GameManager.Data.GetVitals(enemy.Actor.Hash).currentHp == initialHp; i++)
            {
                yield return new WaitForFixedUpdate();
            }
            Assert.That(scout.Motor.FeetPosition.z, Is.LessThan(spawn.z - 0.3f));
            Assert.That(GameManager.Data.GetVitals(enemy.Actor.Hash).currentHp, Is.LessThan(initialHp));
            Assert.That(GameManager.Data.GetVitals(foreign.Actor.Hash).currentHp, Is.EqualTo(initialHp));

            ActorRef retired = enemy.Actor;
            enemy.gameObject.SetActive(false);
            enemy.gameObject.SetActive(true);
            Assert.That(enemy.Actor, Is.Not.EqualTo(retired));
            yield return new WaitForFixedUpdate();
            Assert.That(_deployment.ActiveExplorationOrder, Is.Null);
            Assert.That(GameManager.Data.GetVitals(enemy.Actor.Hash).currentHp, Is.EqualTo(initialHp));

            input.Tap(0.45);
            input.Tap(0.45);
            Assert.That(input.Adapter.ActiveFieldOrder, Is.EqualTo(BroadcastAction.ReleaseOrder));
            input.Tap();
            Assert.That(_deployment.ActiveExplorationOrder, Is.Null);
        }

        [UnityTest]
        public IEnumerator ExplorationKeyboardDefend_AcceptsManagedAreaFloorUntilItUnloads()
        {
            Scene originalRoom = _room;
            _room = SceneManager.CreateScene(k_PersistentOrderRoom);
            foreach (GameObject root in originalRoom.GetRootGameObjects()) { SceneManager.MoveGameObjectToScene(root, _room); }
            yield return SceneManager.UnloadSceneAsync(originalRoom);

            LevelStreamingController streaming = GameManager.LevelStreaming;
            _streamsOrderArea = true;
            Assert.That(streaming.RequestAreaLoad(k_OrderAreaPath), Is.True);
            float deadline = Time.realtimeSinceStartup + 5f;
            while (!streaming.Orchestrator.IsLoaded(k_OrderAreaPath) && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
            Assert.That(streaming.Orchestrator.IsLoaded(k_OrderAreaPath), Is.True);
            Scene area = SceneManager.GetSceneByPath(k_OrderAreaPath);
            Assert.That(area.GetPhysicsScene(), Is.EqualTo(_room.GetPhysicsScene()));

            CharacterActor3D knight = CreateCharacter("Knight at the deep mine", new Vector3(1.55f, 0.04f, 11.15f),
                CharacterFeature.Player);
            _positions[0].position = new Vector3(-0.55f, 0.02f, 11.5f);
            CharacterActor3D scout = PrepareOrderGroup(knight);
            SetSerializedField(_deployment, "followGroundLayers", (LayerMask)LayerMask.GetMask("Ground", "Default"));
            Vector3 station = new Vector3(0.25f, 0f, 10.2f);
            Camera camera = OrderCamera(station);
            using var input = new ExplorationOrderKeyboard(knight, _deployment, camera);
            yield return new WaitForFixedUpdate();
            Assert.That(knight.Motor.Ground.Surface.gameObject.scene, Is.EqualTo(area));

            input.Tap(0.45);
            input.Tap(0.45);
            input.Aim(station);
            input.Tap();
            Assert.That(_deployment.ActiveExplorationOrder, Is.EqualTo(BroadcastAction.DefendPoint));

            _foreignOrderScene = SceneManager.CreateScene("Unmanaged room sharing expedition physics");
            Vector3 foreignPoint = station + Vector3.left * 4f;
            BoxCollider unrelatedFloor = CreateBox("Unmanaged floor", foreignPoint + Vector3.down * 0.25f,
                new Vector3(2f, 0.5f, 2f));
            SceneManager.MoveGameObjectToScene(unrelatedFloor.gameObject, _foreignOrderScene);
            Physics.SyncTransforms();
            Assert.That(_foreignOrderScene.GetPhysicsScene(), Is.EqualTo(_room.GetPhysicsScene()));
            Assert.That(_deployment.TryIssueExplorationOrder(knight, BroadcastAction.DefendPoint,
                position: foreignPoint, surface: unrelatedFloor), Is.False);
            Assert.That(_deployment.ActiveExplorationOrder, Is.EqualTo(BroadcastAction.DefendPoint));

            for (int i = 0; i < 60 && Vector3.Distance(scout.Motor.FeetPosition, station) > 0.33f; i++)
            {
                yield return new WaitForFixedUpdate();
            }
            Assert.That(Vector3.Distance(scout.Motor.FeetPosition, station), Is.LessThanOrEqualTo(0.33f));
            Assert.That(scout.Motor.Ground.Surface.gameObject.scene, Is.EqualTo(area));
            ActorRef identity = scout.Actor;

            yield return SceneManager.UnloadSceneAsync(area);
            yield return new WaitForFixedUpdate();
            Assert.That(_deployment.ActiveExplorationOrder, Is.Null);
            Assert.That(scout.Actor, Is.EqualTo(identity));
        }

        private CharacterActor3D PrepareOrderGroup(CharacterActor3D knight, EncounterCombat3D encounter = null)
        {
            Assert.That(_squad.TryConfirm(new[] { new ExpeditionSquad.Selection("scout", 1) }), Is.True);
            _floor.gameObject.layer = LayerMask.NameToLayer("Ground");
            SetSerializedField(_deployment, "followGroundLayers", (LayerMask)(1 << _floor.gameObject.layer));
            SetSerializedField(_deployment, "followObstructionLayers", (LayerMask)(1 << _floor.gameObject.layer));
            Configure(encounter, knight);
            Physics.SyncTransforms();
            Assert.That(_deployment.TryDeployConfirmed(), Is.True);
            _deployment.CopyActiveSummons(_summons);
            return GetUnit(_summons[0].Actor);
        }

        private Camera OrderCamera(Vector3 target)
        {
            Camera camera = NewObject("Field order camera", target + new Vector3(1.5f, 7f, -4f)).AddComponent<Camera>();
            camera.transform.LookAt(target);
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.pixelRect = new Rect(0f, 0f, 640f, 480f);
            return camera;
        }

        private AttackInfo CreateOrderMelee()
        {
            var motion = ScriptableObject.CreateInstance<MotionDefinition>();
            motion.info = new MotionInfo { preMotionDuration = 0.1f, activeMotionDuration = 0.15f, recoveryDuration = 0.15f };
            var attack = ScriptableObject.CreateInstance<AttackInfo>();
            attack.motion = motion;
            attack.attackElement = Element.Slash;
            attack.damageMultiplier = 1f;
            attack.attack3D = new AttackProfile3D
            {
                shape = AttackShape3D.Melee,
                localCenter = new Vector3(0f, 0.95f, 1.2f),
                halfExtents = new Vector3(0.7f, 0.75f, 1.2f),
                targetLayers = LayerMask.GetMask("Default"),
                obstructionLayers = LayerMask.GetMask("Ground"),
            };
            _assets.Add(motion);
            _assets.Add(attack);
            return attack;
        }

        private sealed class ExplorationOrderKeyboard : IDisposable
        {
            private readonly InputTestFixture _devices = new InputTestFixture();
            private readonly CharacterActor3D _knight;
            private readonly InputActionAsset _actions;
            private readonly Keyboard _keyboard;
            private readonly Mouse _mouse;
            private readonly Camera _camera;

            public PlayerCharacterInput3D Adapter { get; }

            public ExplorationOrderKeyboard(CharacterActor3D knight, ExpeditionSummonDeployment3D deployment, Camera camera)
            {
#if UNITY_EDITOR
                InputActionAsset source = UnityEditor.AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions");
#else
                InputActionAsset source = InputSystem.actions;
#endif
                string shippedActions = source.ToJson();
                _devices.Setup();
                _knight = knight;
                _camera = camera;
                _keyboard = InputSystem.AddDevice<Keyboard>();
                _mouse = InputSystem.AddDevice<Mouse>();
                _actions = InputActionAsset.FromJson(shippedActions);
                PlayerInput input = knight.gameObject.AddComponent<PlayerInput>();
                input.actions = _actions;
                input.defaultActionMap = "Player";
                input.camera = camera;
                input.neverAutoSwitchControlSchemes = true;
                Adapter = knight.gameObject.AddComponent<PlayerCharacterInput3D>();
                SetSerializedField(Adapter, "explorationOrders", deployment);
                knight.gameObject.SetActive(true);
                input.SwitchCurrentControlScheme("Keyboard&Mouse", _keyboard, _mouse);
            }

            public void Aim(Vector3 point)
            {
                Physics.SyncTransforms();
                InputSystem.QueueStateEvent(_mouse, new MouseState { position = _camera.WorldToScreenPoint(point) });
                InputSystem.Update();
            }

            public void Tap(double duration = 0.08)
            {
                Press(true);
                Release(duration);
            }

            public void Release(double duration)
            {
                _devices.currentTime += duration;
                Press(false);
            }

            public void Press(bool pressed)
            {
                InputSystem.QueueStateEvent(_keyboard, pressed ? new KeyboardState(Key.F) : new KeyboardState());
                InputSystem.Update();
            }

            public void Dispose()
            {
                _knight.gameObject.SetActive(false);
                Object.Destroy(_actions);
                _devices.TearDown();
            }
        }
    }
}
