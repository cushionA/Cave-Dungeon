using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using R3;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using CharacterInfo = Game.Core.CharacterInfo;

namespace Game.Tests.PlayMode
{
    public sealed class PlayerCooperationInput3DPlayTests
    {
        private readonly InputTestFixture _devices = new InputTestFixture();
        private readonly List<CharacterInfo> _actorSettings = new List<CharacterInfo>();
        private readonly List<ScriptableObject> _combatSettings = new List<ScriptableObject>();
        private Scene _scene;
        private Scene _previousScene;
        private InputActionAsset _actions;
        private CharacterActor3D _knight;
        private PlayerCombatLoadout3D _loadout;
        private CompanionController3D _companion;
        private PlayerInput _input;
        private PlayerCharacterInput3D _adapter;
        private Keyboard _keyboard;
        private Mouse _mouse;
        private Gamepad _pad;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            TestSceneHelper.Cleanup();
            yield return null;
#if UNITY_EDITOR
            InputActionAsset source = UnityEditor.AssetDatabase.LoadAssetAtPath<InputActionAsset>(
                "Assets/InputSystem_Actions.inputactions");
#else
            InputActionAsset source = InputSystem.actions;
#endif
            Assert.IsNotNull(source);
            string shippedActions = source.ToJson();
            _devices.Setup();
            _actions = InputActionAsset.FromJson(shippedActions);
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _mouse = InputSystem.AddDevice<Mouse>();
            _pad = InputSystem.AddDevice<Gamepad>();

            _previousScene = SceneManager.GetActiveScene();
            _scene = SceneManager.CreateScene("PlayerCooperationInput3D", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            SceneManager.SetActiveScene(_scene);
            GameObject manager = new GameObject("Cooperation game manager");
            manager.SetActive(false);
            GameObject projectiles = new GameObject("3D projectiles");
            projectiles.transform.SetParent(manager.transform, false);
            projectiles.AddComponent<ProjectileManager3D>();
            manager.AddComponent<GameManager>();
            manager.SetActive(true);
            GameObject floor = new GameObject("Floor");
            floor.transform.position = Vector3.down * 0.5f;
            floor.AddComponent<BoxCollider>().size = new Vector3(25f, 1f, 25f);

            _knight = CreateActor("Knight", Vector3.zero, CharacterFeature.Player);
            CharacterActor3D sister = CreateActor("Sister", Vector3.right * 1.5f, CharacterFeature.Companion);
            _companion = sister.gameObject.AddComponent<CompanionController3D>();
            _companion.ConfigureForTest(_knight);

            _input = _knight.gameObject.AddComponent<PlayerInput>();
            _input.actions = _actions;
            _input.defaultActionMap = "Player";
            _input.neverAutoSwitchControlSchemes = true;
            _adapter = _knight.gameObject.AddComponent<PlayerCharacterInput3D>();
            _loadout = _knight.gameObject.AddComponent<PlayerCombatLoadout3D>();
            typeof(PlayerCharacterInput3D).GetField("companion", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(_adapter, _companion);
            _knight.gameObject.SetActive(true);
            sister.gameObject.SetActive(true);
            _input.SwitchCurrentControlScheme("Keyboard&Mouse", _keyboard, _mouse);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            SceneManager.SetActiveScene(_previousScene);
            if (_scene.isLoaded) { yield return SceneManager.UnloadSceneAsync(_scene); }
            TestSceneHelper.Cleanup();
            Object.Destroy(_actions);
            foreach (CharacterInfo info in _actorSettings) { Object.Destroy(info); }
            _actorSettings.Clear();
            foreach (ScriptableObject asset in _combatSettings) { Object.Destroy(asset); }
            _combatSettings.Clear();
            yield return null;
            _devices.TearDown();
        }

        [UnityTest]
        public IEnumerator GamepadCooperation_DpadUpStartsSupportOnTheKnight()
        {
            _input.SwitchCurrentControlScheme("Gamepad", _pad);
            Assert.IsFalse(_companion.IsCooperating);
            InputSystem.QueueStateEvent(_pad, new GamepadState().WithButton(GamepadButton.DpadUp));
            InputSystem.Update();
            Assert.IsFalse(_companion.IsCooperating);
            yield return new WaitForFixedUpdate();
            Assert.IsTrue(_companion.IsCooperating);

            InputSystem.QueueStateEvent(_pad, new GamepadState());
            InputSystem.Update();
            for (int i = 0; i < 30 && DefenseMultiplier() <= 1f; i++) { yield return new WaitForFixedUpdate(); }
            Assert.That(DefenseMultiplier(), Is.GreaterThan(1f));
        }

        [UnityTest]
        public IEnumerator KeyboardCooperation_MapFocusDisableAndOwnerReplacementCancelThePendingPress()
        {
            PressKeyboard(true);
            _input.SwitchCurrentActionMap("UI");
            _input.SwitchCurrentActionMap("Player");
            PressKeyboard(false);
            yield return new WaitForFixedUpdate();
            Assert.IsFalse(_companion.IsCooperating);

            PressKeyboard(true);
            _knight.gameObject.SendMessage("OnApplicationFocus", false);
            PressKeyboard(false);
            yield return new WaitForFixedUpdate();
            Assert.IsFalse(_companion.IsCooperating);

            PressKeyboard(true);
            _adapter.enabled = false;
            _adapter.enabled = true;
            PressKeyboard(false);
            yield return new WaitForFixedUpdate();
            Assert.IsFalse(_companion.IsCooperating);

            PressKeyboard(true);
            ActorRef previous = _knight.Actor;
            _knight.enabled = false;
            _knight.enabled = true;
            Assert.AreNotEqual(previous, _knight.Actor);
            PressKeyboard(false);
            yield return new WaitForFixedUpdate();
            Assert.IsFalse(_companion.IsCooperating);

            PressKeyboard(true);
            PressKeyboard(false);
            yield return new WaitForFixedUpdate();
            Assert.IsTrue(_companion.IsCooperating);
        }

        [UnityTest]
        public IEnumerator KeyboardCooperation_RetiredRecipientDuringStartCancelsTheAcceptedCast()
        {
            ActorRef retiringKnight = _knight.Actor;
            ActorRef sister = _companion.GetComponent<CharacterActor3D>().Actor;
            long retiredCast = 0;
            int interruptions = 0;
            using var listener = GameManager.Events.OnActionExecution.Subscribe(notification =>
            {
                ActionExecution execution = notification.execution;
                if (execution.ownerRef != sister || execution.targetRef != retiringKnight) { return; }
                if (notification.kind == ActionSignalKind.Started)
                {
                    retiredCast = execution.instanceId;
                    _knight.enabled = false;
                    _knight.enabled = true;
                }
                else if (notification.kind == ActionSignalKind.Interrupted && execution.instanceId == retiredCast)
                {
                    interruptions++;
                }
            });

            TapKeyboard();
            yield return new WaitForFixedUpdate();
            Assert.That(retiredCast, Is.Positive);
            Assert.That(_knight.Actor, Is.Not.EqualTo(retiringKnight));
            Assert.That(GameManager.Actions.Execution.TryGetInstance(retiredCast, out _), Is.False);
            Assert.That(interruptions, Is.EqualTo(1));
            Assert.That(_companion.IsCooperating, Is.False);
            Assert.That(_companion.CooperationCooldownRemaining, Is.Zero);
            Assert.That(DefenseMultiplier(), Is.EqualTo(1f));

            TapKeyboard();
            yield return new WaitForFixedUpdate();
            Assert.That(_companion.IsCooperating, Is.True);
            Assert.That(DefenseMultiplier(), Is.GreaterThan(1f));
            Assert.That(interruptions, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator KeyboardCooperation_RejectsImmunityWithoutLeavingACastOrStartingCooldown()
        {
            ActorRef recipient = _knight.Actor;
            ActorRef sister = _companion.GetComponent<CharacterActor3D>().Actor;
            bool gainImmunityDuringStart = true;
            long rejectedCast = 0;
            int starts = 0;
            int interruptions = 0;
            using var listener = GameManager.Events.OnActionExecution.Subscribe(notification =>
            {
                ActionExecution execution = notification.execution;
                if (execution.ownerRef != sister || execution.targetRef != recipient) { return; }
                if (notification.kind == ActionSignalKind.Started)
                {
                    starts++;
                    if (gainImmunityDuringStart)
                    {
                        rejectedCast = execution.instanceId;
                        gainImmunityDuringStart = false;
                        SetWardImmunity(true);
                    }
                }
                else if (notification.kind == ActionSignalKind.Interrupted && execution.instanceId == rejectedCast)
                {
                    interruptions++;
                }
            });

            TapKeyboard();
            yield return new WaitForFixedUpdate();
            Assert.That(rejectedCast, Is.Positive);
            Assert.That(GameManager.Actions.Execution.TryGetInstance(rejectedCast, out _), Is.False);
            Assert.That(interruptions, Is.EqualTo(1));
            Assert.That(_companion.IsCooperating, Is.False);
            Assert.That(_companion.CooperationCooldownRemaining, Is.Zero);
            Assert.That(DefenseMultiplier(), Is.EqualTo(1f));

            SetWardImmunity(false);
            TapKeyboard();
            yield return new WaitForFixedUpdate();
            Assert.That(starts, Is.EqualTo(2));
            Assert.That(_companion.CooperationCooldownRemaining, Is.Positive);
            Assert.That(DefenseMultiplier(), Is.GreaterThan(1f));
            yield return WaitForSupportToFinish();
            Assert.That(GameManager.StatusEffects.Remove(recipient.Hash, StatusEffectId.DefenseUp), Is.True);

            SetWardImmunity(true);
            GameManager.Data.GetVitals(sister.Hash).currentMp = 25f;
            TapKeyboard();
            yield return new WaitForFixedUpdate();
            Assert.That(starts, Is.EqualTo(2), "Known immunity must reject a paid cast before admission.");
            Assert.That(GameManager.Data.GetVitals(sister.Hash).currentMp, Is.GreaterThanOrEqualTo(25f));
            Assert.That(_companion.IsCooperating, Is.False);
            Assert.That(DefenseMultiplier(), Is.EqualTo(1f));

            void SetWardImmunity(bool immune)
                => GameManager.Data.GetCombatStats(recipient.Hash).statusDefense.defenseUp.immune = immune;
        }

        [UnityTest]
        public IEnumerator KeyboardCooperation_UsesEquippedOwnedSupportAndKeepsEachCooldown()
        {
            const int ward = 7301;
            const int attack = 7302;
            const int unowned = 7303;
            const int unsupported = 7304;
            SetField(_loadout, "cooperationOptions", new[]
            {
                CreateSupport(ward, StatusEffectId.DefenseUp, startingOwned: true),
                CreateSupport(attack, StatusEffectId.AttackUp, startingOwned: true),
                CreateSupport(unowned, StatusEffectId.AttackUp, startingOwned: false),
                CreateSupport(unsupported, StatusEffectId.SpeedUp, startingOwned: true),
            });
            SetField(_loadout, "startingCooperationId", ward);
            yield return null;
            Assert.That(_loadout.CurrentCooperationId, Is.EqualTo(ward));
            Assert.That(_loadout.TryEquipCooperation(unowned), Is.False);
            Assert.That(GameManager.Inventory.GetCount(unsupported, ItemCategory.CompanionMagic), Is.Zero);
            GameManager.Inventory.Add(unsupported, ItemCategory.CompanionMagic, 1, 1);
            Assert.That(_loadout.TryEquipCooperation(unsupported), Is.False);
            Assert.That(_loadout.CurrentCooperationId, Is.EqualTo(ward));
            CharacterActor3D sister = _companion.GetComponent<CharacterActor3D>();
            GameManager.Data.GetVitals(sister.Actor.Hash).currentMp = 25f;

            TapKeyboard();
            yield return new WaitForFixedUpdate();
            Assert.That(_companion.IsCooperating, Is.True);
            Assert.That(DefenseMultiplier(), Is.GreaterThan(1f));
            Assert.That(AttackMultiplier(), Is.EqualTo(1f));
            Assert.That(CooperationMp(), Is.GreaterThanOrEqualTo(25f));
            Assert.That(_companion.CooperationCooldownRemaining, Is.Positive);
            yield return WaitForSupportToFinish();
            Assert.That(GameManager.StatusEffects.Remove(_knight.Actor.Hash, StatusEffectId.DefenseUp), Is.True);

            PressKeyboard(true);
            Assert.That(_loadout.TryEquipCooperation(attack), Is.True);
            PressKeyboard(false);
            yield return new WaitForFixedUpdate();
            Assert.That(_companion.IsCooperating, Is.False, "Changing equipment discards the press queued for the old choice.");
            Assert.That(AttackMultiplier(), Is.EqualTo(1f));
            Assert.That(_companion.CooperationCooldownRemaining, Is.Zero);

            float beforeFirstAttack = CooperationMp();
            TapKeyboard();
            yield return new WaitForFixedUpdate();
            Assert.That(_companion.IsCooperating, Is.True);
            Assert.That(AttackMultiplier(), Is.GreaterThan(1f));
            Assert.That(DefenseMultiplier(), Is.EqualTo(1f));
            Assert.That(CooperationMp(), Is.GreaterThanOrEqualTo(beforeFirstAttack));
            yield return WaitForSupportToFinish();

            Assert.That(_loadout.TryEquipCooperation(ward), Is.True);
            Assert.That(_companion.CooperationCooldownRemaining, Is.Positive, "Re-equipping the ward retains its previous cooldown.");
            float beforeRepeatWard = CooperationMp();
            TapKeyboard();
            yield return new WaitForFixedUpdate();
            Assert.That(_companion.IsCooperating, Is.True);
            Assert.That(DefenseMultiplier(), Is.GreaterThan(1f));
            Assert.That(beforeRepeatWard - CooperationMp(), Is.EqualTo(12f).Within(0.15f));
            yield return WaitForSupportToFinish();

            Assert.That(GameManager.Inventory.Remove(ward, 1), Is.EqualTo(1));
            Assert.That(GameManager.StatusEffects.Remove(_knight.Actor.Hash, StatusEffectId.DefenseUp), Is.True);
            TapKeyboard();
            yield return new WaitForFixedUpdate();
            Assert.That(_companion.IsCooperating, Is.False);
            Assert.That(DefenseMultiplier(), Is.EqualTo(1f), "An unowned configured choice cannot cast the legacy ward.");

            float CooperationMp() => GameManager.Data.GetVitals(sister.Actor.Hash).currentMp;
        }

        [UnityTest]
        public IEnumerator KeyboardCooperation_EquipsReflectionShieldAndRejectsInvalidPaidMetadata()
        {
            const int ward = 7321;
            const int reflection = 7322;
            PlayerCombatLoadout3D.CooperationOption shield = CreateSupport(reflection, StatusEffectId.None, startingOwned: true);
            SetField(shield, "effectKind", PlayerCombatLoadout3D.CooperationEffectKind.ReflectionShield);
            SetField(shield, "shieldDuration", 5f);
            SetField(shield, "shieldRadius", 1.2f);
            SetField(_loadout, "cooperationOptions", new[]
            {
                CreateSupport(ward, StatusEffectId.DefenseUp, startingOwned: true),
                shield,
            });
            SetField(_loadout, "startingCooperationId", ward);
            CharacterActor3D shooter = CreateActor("Enemy shooter", Vector3.forward * 5f, CharacterFeature.Minion,
                CharacterBelong.Enemy);
            shooter.transform.rotation = Quaternion.LookRotation(Vector3.back);
            shooter.gameObject.SetActive(true);
            yield return null;
            Assert.That(_loadout.CurrentCooperationId, Is.EqualTo(ward));
            Assert.That(_loadout.TryEquipCooperation(reflection), Is.True);
            GameManager.Data.GetCombatStats(_knight.Actor.Hash).statusDefense.defenseUp.immune = true;
            Physics.SyncTransforms();

            TapKeyboard();
            yield return new WaitForFixedUpdate();
            Assert.That(_companion.IsCooperating, Is.True);
            Assert.That(_companion.CooperationCooldownRemaining, Is.Positive);
            Assert.That(DefenseMultiplier(), Is.EqualTo(1f));
            Assert.That(AttackMultiplier(), Is.EqualTo(1f));

            int knightHp = GameManager.Data.GetVitals(_knight.Actor.Hash).currentHp;
            int enemyHp = GameManager.Data.GetVitals(shooter.Actor.Hash).currentHp;
            ActorRef shieldCaster = _companion.GetComponent<CharacterActor3D>().Actor;
            ActorRef hitSource = default;
            using var listener = GameManager.Events.OnDamageDealt.Subscribe(change =>
            {
                if (change.result.defenderRef == shooter.Actor) { hitSource = change.result.attackerRef; }
            });
            Assert.That(GameManager.Actions.Execute3D(shooter, CreateShieldTestProjectile(), _knight.Actor, out _), Is.True);
            for (int i = 0; i < 95 && GameManager.Data.GetVitals(shooter.Actor.Hash).currentHp == enemyHp; i++)
            {
                yield return new WaitForFixedUpdate();
            }
            Assert.That(GameManager.Data.GetVitals(_knight.Actor.Hash).currentHp, Is.EqualTo(knightHp));
            Assert.That(GameManager.Data.GetVitals(shooter.Actor.Hash).currentHp, Is.LessThan(enemyHp));
            Assert.That(hitSource, Is.EqualTo(shieldCaster));
            yield return WaitForSupportToFinish();

            SetField(shield, "shieldRadius", float.NaN);
            Assert.That(_loadout.OwnsCooperation(reflection), Is.False);
            Assert.That(_loadout.CurrentCooperationId, Is.EqualTo(reflection));
            GameManager.Data.GetVitals(shieldCaster.Hash).currentMp = 25f;
            TapKeyboard();
            yield return new WaitForFixedUpdate();
            Assert.That(_companion.IsCooperating, Is.False);
            Assert.That(GameManager.Data.GetVitals(shieldCaster.Hash).currentMp, Is.GreaterThanOrEqualTo(25f));
        }

        private AttackInfo CreateShieldTestProjectile()
        {
            MotionDefinition motion = ScriptableObject.CreateInstance<MotionDefinition>();
            _combatSettings.Add(motion);
            motion.info = new MotionInfo { activeMotionDuration = 0.15f, recoveryDuration = 0.15f };
            AttackInfo attack = ScriptableObject.CreateInstance<AttackInfo>();
            _combatSettings.Add(attack);
            attack.motion = motion;
            attack.damageMultiplier = 1f;
            attack.attackElement = Element.Slash;
            attack.attack3D = new AttackProfile3D
            {
                shape = AttackShape3D.Projectile, localCenter = new Vector3(0f, 0.8f, 0.45f),
                projectileRadius = 0.08f, targetLayers = LayerMask.GetMask("Default"),
            };
            attack.projectileInfo = new ProjectileInfo
            {
                hasProjectile = true, trajectory = BulletMoveType.Straight, speed = 15f, lifetime = 2f,
            };
            return attack;
        }

        private IEnumerator WaitForSupportToFinish()
        {
            for (int i = 0; i < 45 && _companion.IsCooperating; i++) { yield return new WaitForFixedUpdate(); }
            Assert.That(_companion.IsCooperating, Is.False);
        }

        [UnityTest]
        public IEnumerator Cooperation_WhenControllerIsDestroyedDuringStart_CancelsTheAcceptedCast()
        {
            ActorRef source = _companion.GetComponent<CharacterActor3D>().Actor;
            ActionExecutionSystem execution = GameManager.Actions.Execution;
            long started = 0;
            void RetireController(ActionExecutionEvent change)
            {
                if (change.kind != ActionSignalKind.Started || change.execution.ownerRef != source) { return; }
                started = change.execution.instanceId;
                Object.DestroyImmediate(_companion);
            }

            execution.Changed += RetireController;
            try { Assert.That(_companion.TryCooperate(_knight), Is.False); }
            finally { execution.Changed -= RetireController; }
            Assert.That(started, Is.Positive);
            Assert.That(GameManager.Actors.IsCurrent(source), Is.True);
            Assert.That(execution.TryGetInstance(started, out _), Is.False);
            Assert.That(DefenseMultiplier(), Is.EqualTo(1f));
            yield return null;
        }

        [UnityTest]
        public IEnumerator KeyboardCooperation_FullLegacyInventoryKeepsWardUntilAllStartersFit()
        {
            const int ward = 7311;
            const int attack = 7312;
            const int firstFiller = 10_000;
            InventoryManager inventory = GameManager.Inventory;
            for (int i = 0; i < InventoryManager.k_MaxSlotCount; i++)
            {
                Assert.That(inventory.Add(firstFiller + i, ItemCategory.Material, 1, 1), Is.EqualTo(1));
            }
            PlayerCombatLoadoutSelection selection = GameManager.CombatLoadout;
            SaveSlotData legacy = GameManager.Saves.Save(0);
            Assert.That(legacy.entries.Remove(selection.SaveId), Is.True);
            SetField(_loadout, "cooperationOptions", new[]
            {
                CreateSupport(ward, StatusEffectId.DefenseUp, startingOwned: true),
                CreateSupport(attack, StatusEffectId.AttackUp, startingOwned: true),
            });
            SetField(_loadout, "startingCooperationId", attack);
            Assert.That(GameManager.Saves.Load(0), Is.True);
            Assert.That(selection.IsCooperationInitialized, Is.False);
            Assert.That(_loadout.CurrentCooperationId, Is.EqualTo(PlayerCombatLoadoutSelection.k_EmptyItem));

            TapKeyboard();
            yield return new WaitForFixedUpdate();
            Assert.That(_companion.IsCooperating, Is.True);
            Assert.That(DefenseMultiplier(), Is.GreaterThan(1f));
            Assert.That(AttackMultiplier(), Is.EqualTo(1f));
            yield return WaitForSupportToFinish();
            Assert.That(GameManager.StatusEffects.Remove(_knight.Actor.Hash, StatusEffectId.DefenseUp), Is.True);

            Assert.That(inventory.Remove(firstFiller, 1), Is.EqualTo(1));
            yield return null;
            Assert.That(selection.IsCooperationInitialized, Is.False);
            Assert.That(_loadout.OwnsCooperation(ward), Is.False);
            Assert.That(_loadout.OwnsCooperation(attack), Is.False);

            Assert.That(inventory.Remove(firstFiller + 1, 1), Is.EqualTo(1));
            yield return null;
            Assert.That(selection.IsCooperationInitialized, Is.True);
            Assert.That(_loadout.OwnsCooperation(ward), Is.True);
            Assert.That(_loadout.OwnsCooperation(attack), Is.True);
            Assert.That(_loadout.CurrentCooperationId, Is.EqualTo(attack));
            Assert.That(inventory.ItemCount, Is.EqualTo(InventoryManager.k_MaxSlotCount));

            TapKeyboard();
            yield return new WaitForFixedUpdate();
            Assert.That(_companion.IsCooperating, Is.True);
            Assert.That(AttackMultiplier(), Is.GreaterThan(1f));
            Assert.That(DefenseMultiplier(), Is.EqualTo(1f));
        }

        private float DefenseMultiplier() => GameManager.Data.GetStatusEffects(_knight.Actor.Hash).DefenseMultiplier;
        private float AttackMultiplier() => GameManager.Data.GetStatusEffects(_knight.Actor.Hash).AttackMultiplier;

        private static PlayerCombatLoadout3D.CooperationOption CreateSupport(int id, StatusEffectId effect, bool startingOwned)
        {
            var option = new PlayerCombatLoadout3D.CooperationOption();
            SetField(option, "itemId", id);
            SetField(option, "displayName", effect.ToString());
            SetField(option, "effect", new StatusEffectInfo { effect = effect, duration = 3f, modifier = 0.4f, maxStack = 1 });
            SetField(option, "startingOwned", startingOwned);
            return option;
        }

        private static void SetField(object target, string name, object value)
        {
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        }

        private void TapKeyboard()
        {
            PressKeyboard(true);
            PressKeyboard(false);
        }

        private CharacterActor3D CreateActor(string name, Vector3 position, CharacterFeature feature,
            CharacterBelong side = CharacterBelong.Ally)
        {
            GameObject body = new GameObject(name);
            body.SetActive(false);
            body.transform.position = position;
            CharacterController controller = body.AddComponent<CharacterController>();
            controller.center = Vector3.up * 0.8f;
            controller.height = 1.6f;
            controller.radius = 0.3f;
            CharacterActor3D actor = body.AddComponent<CharacterActor3D>();
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(side, feature);
            _actorSettings.Add(info);
            actor.SetCharacterInfoForTest(info);
            return actor;
        }

        private void PressKeyboard(bool pressed)
        {
            InputSystem.QueueStateEvent(_keyboard, pressed ? new KeyboardState(Key.V) : new KeyboardState());
            InputSystem.Update();
        }
    }
}
