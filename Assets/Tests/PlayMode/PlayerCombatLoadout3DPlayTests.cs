using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using CharacterInfo = Game.Core.CharacterInfo;

namespace Game.Tests.PlayMode
{
    public sealed class PlayerCombatLoadout3DPlayTests
    {
        private const int k_ActorLayer = 8;
        private const int k_MagicCost = 12;
        private const float k_CastWindup = 0.2f;
        private readonly InputTestFixture _devices = new InputTestFixture();
        private readonly List<ScriptableObject> _assets = new List<ScriptableObject>();
        private Scene _previousScene;
        private Scene _room;
        private InputActionAsset _actions;
        private Gamepad _pad;
        private PlayerInput _playerInput;
        private CharacterActor3D _knight;
        private bool _isolatedInput;

        [UnitySetUp]
        public IEnumerator CreateRoom()
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
            _isolatedInput = true;
            _actions = InputActionAsset.FromJson(shippedActions);
            _pad = InputSystem.AddDevice<Gamepad>();

            _previousScene = SceneManager.GetActiveScene();
            _room = SceneManager.CreateScene(nameof(PlayerCombatLoadout3DPlayTests),
                new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            SceneManager.SetActiveScene(_room);
            GameObject world = new GameObject("Loadout test world");
            world.SetActive(false);
            world.AddComponent<ProjectileManager3D>();
            world.AddComponent<GameManager>();
            world.SetActive(true);
            GameManager.Instance.enabled = false;

            GameObject floor = new GameObject("Floor");
            floor.transform.position = Vector3.down * 0.5f;
            floor.AddComponent<BoxCollider>().size = new Vector3(20f, 1f, 20f);

            CreateKnight();
            yield return new WaitForFixedUpdate();
            yield return null;
        }

        private PlayerCombatLoadout3D CreateKnight(PlayerCombatLoadout3D.WeaponOption[] weapons = null,
            PlayerCombatLoadout3D.CoreOption[] cores = null, PlayerCombatLoadout3D.SpellOption[] spells = null)
        {
            if (_knight != null)
            {
                _knight.gameObject.SetActive(false);
                Object.Destroy(_knight.gameObject);
            }
            _knight = CreateActor("Knight", Vector3.zero, CharacterBelong.Ally, CharacterFeature.Player);
            PlayerCombatLoadout3D loadout = null;
            if (weapons != null || cores != null || spells != null)
            {
                loadout = _knight.gameObject.AddComponent<PlayerCombatLoadout3D>();
                if (weapons != null) { SetField(loadout, "weapons", weapons); }
                if (cores != null) { SetField(loadout, "cores", cores); }
                if (spells != null) { SetField(loadout, "spells", spells); }
            }
            _playerInput = _knight.gameObject.AddComponent<PlayerInput>();
            _playerInput.actions = _actions;
            _playerInput.defaultActionMap = "Player";
            _playerInput.neverAutoSwitchControlSchemes = true;
            PlayerCharacterInput3D adapter = _knight.gameObject.AddComponent<PlayerCharacterInput3D>();
            SetField(adapter, "attack3D", CreateProjectile("Ranged weapon", AttackCategory.Ranged));
            SetField(adapter, "skill3D", CreateProjectile("Magic skill", AttackCategory.Magic, k_MagicCost, k_CastWindup));
            _knight.gameObject.SetActive(true);
            _playerInput.SwitchCurrentControlScheme("Gamepad", _pad);
            return loadout;
        }

        [UnityTearDown]
        public IEnumerator UnloadRoom()
        {
            if (_room.isLoaded)
            {
                SceneManager.SetActiveScene(_previousScene);
                yield return SceneManager.UnloadSceneAsync(_room);
            }
            TestSceneHelper.Cleanup();
            if (_actions != null) { Object.Destroy(_actions); }
            foreach (ScriptableObject asset in _assets) { Object.Destroy(asset); }
            _assets.Clear();
            yield return null;
            if (_isolatedInput)
            {
                _devices.TearDown();
                _isolatedInput = false;
            }
            _pad = null;
        }

        [UnityTest]
        public IEnumerator RangedAttack_WhenWeaponAndCoreAreReplaced_UsesNewStatsOnlyForTheNextShot()
        {
            CharacterActor3D first = CreateTarget("First target", Vector3.forward * 5f);
            CharacterActor3D second = CreateTarget("Second target", new Vector3(4f, 0f, 5f));
            yield return null;
            int owner = _knight.Actor.Hash;
            EquipmentStore equipment = GameManager.Equipment;
            EquipmentDefinition firstWeapon = CreateGear(EquipSlot.Weapon, 6);
            EquipmentDefinition firstCore = CreateGear(EquipSlot.Core, 4, AbilityFlag.DoubleJump);
            EquipmentDefinition nextWeapon = CreateGear(EquipSlot.Weapon, 20);
            EquipmentDefinition nextCore = CreateGear(EquipSlot.Core, 10, AbilityFlag.AirDash);
            Assert.IsNull(equipment.Equip(owner, firstWeapon));
            Assert.IsNull(equipment.Equip(owner, firstCore));
            Assert.AreEqual(20, GameManager.Data.GetCombatStats(owner).attack.slash);
            Assert.AreEqual(AbilityFlag.DoubleJump, GameManager.Data.GetFlags(owner).AbilityFlags);

            Tap(GamepadButton.West);
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(1, GameManager.Projectiles3D.ActiveCount);
            Assert.AreEqual(100, Hp(first));

            Assert.AreSame(firstWeapon, equipment.Equip(owner, nextWeapon));
            Assert.AreSame(firstCore, equipment.Equip(owner, nextCore));
            Assert.AreSame(nextWeapon, equipment.GetHolder(owner).Weapon);
            Assert.AreSame(nextCore, equipment.GetHolder(owner).Core);
            Assert.AreEqual(40, GameManager.Data.GetCombatStats(owner).attack.slash);
            Assert.AreEqual(AbilityFlag.AirDash, GameManager.Data.GetFlags(owner).AbilityFlags);

            Fly();
            Assert.AreEqual(80, Hp(first));
            Assert.AreEqual(100, Hp(second));
            Assert.AreEqual(0, GameManager.Projectiles3D.ActiveCount);
            GameManager.Actions.Execution.Tick(0.5f);

            first.transform.position += Vector3.left * 4f;
            second.transform.position = Vector3.forward * 5f;
            Tap(GamepadButton.West);
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(1, GameManager.Projectiles3D.ActiveCount);
            Fly();
            Assert.AreEqual(60, Hp(second));
            Assert.AreEqual(80, Hp(first));
            Assert.AreEqual(0, GameManager.Projectiles3D.ActiveCount);
        }

        [UnityTest]
        public IEnumerator MagicSkill_WhenInputAndManaRemainValid_CastsOnceAndDoesNotCrossActorGenerations()
        {
            CharacterActor3D target = CreateTarget("Magic target", Vector3.forward * 5f);
            yield return null;
            ActionExecutionSystem execution = GameManager.Actions.Execution;

            SetMp(k_MagicCost);
            SetButton(GamepadButton.RightShoulder, true);
            _playerInput.SwitchCurrentActionMap("UI");
            SetButton(GamepadButton.RightShoulder, false);
            _playerInput.SwitchCurrentActionMap("Player");
            yield return new WaitForFixedUpdate();
            AssertNoCast(execution, k_MagicCost);

            ActorRef oldOwner = _knight.Actor;
            Tap(GamepadButton.RightShoulder);
            _knight.enabled = false;
            _knight.enabled = true;
            Assert.AreNotEqual(oldOwner, _knight.Actor);
            SetMp(k_MagicCost);
            yield return new WaitForFixedUpdate();
            AssertNoCast(execution, k_MagicCost);

            SetMp(k_MagicCost - 1);
            Tap(GamepadButton.RightShoulder);
            yield return new WaitForFixedUpdate();
            AssertNoCast(execution, k_MagicCost - 1);

            SetMp(k_MagicCost);
            Tap(GamepadButton.RightShoulder);
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(0f, Mp());
            Assert.IsTrue(execution.GetPrimary(_knight.Actor.Hash, out ActionExecution accepted));
            Assert.AreEqual(_knight.Actor, accepted.ownerRef);
            Assert.AreEqual(AnimationPhase.Anticipation, accepted.phase);
            Assert.AreEqual(0, GameManager.Projectiles3D.ActiveCount);
            Assert.AreEqual(100, Hp(target));

            execution.Tick(k_CastWindup);
            Assert.AreEqual(1, GameManager.Projectiles3D.ActiveCount);
            Fly();
            Assert.AreEqual(90, Hp(target));
            Assert.AreEqual(0f, Mp());
            Assert.AreEqual(0, GameManager.Projectiles3D.ActiveCount);
            execution.Tick(1f);
            yield return new WaitForFixedUpdate();
            AssertNoCast(execution, 0);
        }

        [UnityTest]
        public IEnumerator LoadoutAttack_AfterEquippingOwnedWeaponAndCore_UsesTheSelectedAttackAndStats()
        {
            const int swordId = 1801, bowId = 1802, firstCoreId = 1803, nextCoreId = 1804;
            AttackInfo sword = CreateAttack("Sword", AttackCategory.Melee);
            sword.attack3D = new AttackProfile3D
            {
                shape = AttackShape3D.Melee, localCenter = new Vector3(0f, 0.8f, 1.2f),
                halfExtents = new Vector3(1f, 0.8f, 1f), targetLayers = 1 << k_ActorLayer,
            };
            AttackInfo bow = CreateProjectile("Bow", AttackCategory.Ranged);
            EquipmentDefinition swordGear = CreateGear(EquipSlot.Weapon, 6);
            EquipmentDefinition bowGear = CreateGear(EquipSlot.Weapon, 20);
            EquipmentDefinition firstCore = CreateGear(EquipSlot.Core, 4, AbilityFlag.DoubleJump);
            EquipmentDefinition nextCore = CreateGear(EquipSlot.Core, 10, AbilityFlag.AirDash);
            PlayerCombatLoadout3D loadout = CreateKnight(
                weapons: new[] { Weapon(swordId, swordGear, sword), Weapon(bowId, bowGear, bow) },
                cores: new[] { Core(firstCoreId, firstCore), Core(nextCoreId, nextCore) });
            CharacterActor3D near = CreateTarget("Sword target", Vector3.forward * 1.7f);
            CharacterActor3D far = CreateTarget("Bow target", Vector3.forward * 5f);
            yield return new WaitForFixedUpdate();
            yield return null;

            Assert.IsFalse(loadout.TryEquipWeapon(bowId));
            Assert.IsFalse(loadout.TryEquipCore(nextCoreId));
            Assert.AreEqual(1, GameManager.Inventory.Add(swordId, ItemCategory.Weapon, 1, 1));
            Assert.AreEqual(1, GameManager.Inventory.Add(firstCoreId, ItemCategory.Core, 1, 1));
            Assert.IsTrue(loadout.TryEquipWeapon(swordId));
            Assert.IsTrue(loadout.TryEquipCore(firstCoreId));
            Assert.AreEqual(AbilityFlag.DoubleJump, GameManager.Data.GetFlags(_knight.Actor.Hash).AbilityFlags);
            Physics.SyncTransforms();
            _room.GetPhysicsScene().Simulate(Time.fixedDeltaTime);

            Tap(GamepadButton.West);
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(0, GameManager.Projectiles3D.ActiveCount);
            Assert.AreEqual(80, Hp(near));
            Assert.AreEqual(100, Hp(far));
            GameManager.Actions.Execution.Tick(0.5f);

            near.transform.position += Vector3.left * 4f;
            Assert.AreEqual(1, GameManager.Inventory.Add(bowId, ItemCategory.Weapon, 1, 1));
            Assert.AreEqual(1, GameManager.Inventory.Add(nextCoreId, ItemCategory.Core, 1, 1));
            Assert.IsTrue(loadout.TryEquipWeapon(bowId));
            Assert.IsTrue(loadout.TryEquipCore(nextCoreId));
            Assert.AreEqual(bowId, loadout.CurrentWeaponId);
            Assert.AreEqual(nextCoreId, loadout.CurrentCoreId);
            Assert.AreEqual(AbilityFlag.AirDash, GameManager.Data.GetFlags(_knight.Actor.Hash).AbilityFlags);
            Assert.AreSame(bowGear, GameManager.Equipment.GetHolder(_knight.Actor.Hash).Weapon);
            Assert.AreSame(nextCore, GameManager.Equipment.GetHolder(_knight.Actor.Hash).Core);

            Tap(GamepadButton.West);
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(1, GameManager.Projectiles3D.ActiveCount);
            Fly();
            Assert.AreEqual(60, Hp(far));
            Assert.AreEqual(80, Hp(near));
        }

        [UnityTest]
        public IEnumerator LoadoutSkill_SelectingOrClearingAnOwnedSpellSlot_DeterminesTheCastAndCost()
        {
            const int quickId = 1805, heavyId = 1806;
            AttackInfo quick = CreateProjectile("Quick spell", AttackCategory.Magic, mpCost: 6, windup: k_CastWindup);
            AttackInfo heavy = CreateProjectile("Heavy spell", AttackCategory.Magic, mpCost: 9, windup: k_CastWindup);
            heavy.damageMultiplier = 2f;
            PlayerCombatLoadout3D loadout = CreateKnight(spells: new[] { Spell(quickId, quick), Spell(heavyId, heavy) });
            CharacterActor3D first = CreateTarget("Heavy spell target", Vector3.forward * 5f);
            CharacterActor3D second = CreateTarget("Quick spell target", new Vector3(4f, 0f, 5f));
            yield return new WaitForFixedUpdate();
            yield return null;
            ActionExecutionSystem execution = GameManager.Actions.Execution;
            SetMp(40);

            Assert.AreEqual(1, GameManager.Inventory.Add(quickId, ItemCategory.PlayerMagic, 1, 1));
            Assert.AreEqual(1, GameManager.Inventory.Add(heavyId, ItemCategory.Consumable, 1, 1));
            Assert.IsFalse(loadout.TryAssignSpell(1, heavyId));
            Assert.AreEqual(1, GameManager.Inventory.Add(heavyId, ItemCategory.PlayerMagic, 1, 1));
            Assert.IsTrue(loadout.TryAssignSpell(0, quickId));
            Assert.IsTrue(loadout.TryAssignSpell(1, heavyId));
            Assert.IsTrue(loadout.TrySelectSpellSlot(1));
            GameManager.Saves.Save(0);

            Tap(GamepadButton.RightShoulder);
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(31f, Mp());
            Assert.AreEqual(0, GameManager.Projectiles3D.ActiveCount);
            execution.Tick(k_CastWindup);
            Fly();
            Assert.AreEqual(80, Hp(first));
            execution.Tick(0.5f);

            Assert.IsTrue(loadout.TryClearSpell(1));
            Tap(GamepadButton.RightShoulder);
            yield return new WaitForFixedUpdate();
            AssertNoCast(execution, 31);
            Assert.IsTrue(loadout.TrySelectSpellSlot(0));
            first.transform.position += Vector3.left * 4f;
            second.transform.position = Vector3.forward * 5f;

            Tap(GamepadButton.RightShoulder);
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(25f, Mp());
            execution.Tick(k_CastWindup);
            Fly();
            Assert.AreEqual(90, Hp(second));
            execution.Tick(0.5f);

            Assert.IsTrue(GameManager.Saves.Load(0));
            Assert.AreEqual(1, loadout.ActiveSpellSlot);
            Assert.AreEqual(heavyId, loadout.GetSpellId(1));
        }

        private void AssertNoCast(ActionExecutionSystem execution, float expectedMp)
        {
            Assert.AreEqual(expectedMp, Mp());
            Assert.IsFalse(execution.GetPrimary(_knight.Actor.Hash, out _));
            Assert.AreEqual(0, GameManager.Projectiles3D.ActiveCount);
        }

        private float Mp() => GameManager.Data.GetVitals(_knight.Actor.Hash).currentMp;
        private void SetMp(float value) => GameManager.Data.GetVitals(_knight.Actor.Hash).currentMp = value;
        private static int Hp(CharacterActor3D actor) => GameManager.Data.GetVitals(actor.Actor.Hash).currentHp;

        private void Fly()
        {
            Physics.SyncTransforms();
            _room.GetPhysicsScene().Simulate(Time.fixedDeltaTime);
            GameManager.Projectiles3D.SimulationTick(0.65f);
        }

        private void Tap(GamepadButton button)
        {
            SetButton(button, true);
            SetButton(button, false);
        }

        private void SetButton(GamepadButton button, bool pressed)
        {
            InputSystem.QueueStateEvent(_pad, new GamepadState().WithButton(button, pressed));
            InputSystem.Update();
        }

        private CharacterActor3D CreateTarget(string label, Vector3 position)
        {
            CharacterActor3D target = CreateActor(label, position, CharacterBelong.Enemy, CharacterFeature.Minion);
            target.transform.rotation = Quaternion.LookRotation(Vector3.back);
            target.gameObject.SetActive(true);
            return target;
        }

        private CharacterActor3D CreateActor(string label, Vector3 position, CharacterBelong belong, CharacterFeature feature)
        {
            GameObject root = new GameObject(label);
            root.SetActive(false);
            root.layer = k_ActorLayer;
            root.transform.position = position;
            CharacterController body = root.AddComponent<CharacterController>();
            body.center = Vector3.up * 0.8f;
            body.height = 1.6f;
            body.radius = 0.3f;
            CharacterActor3D actor = root.AddComponent<CharacterActor3D>();
            CharacterInfo definition = TestSceneHelper.CreateTestCharacterInfo(belong, feature);
            definition.baseDefense = default;
            actor.SetCharacterInfoForTest(definition);
            _assets.Add(definition);
            return actor;
        }

        private AttackInfo CreateProjectile(string label, AttackCategory category, int mpCost = 0, float windup = 0f)
        {
            AttackInfo attack = CreateAttack(label, category, windup);
            attack.mpCost = mpCost;
            attack.attack3D = new AttackProfile3D
            {
                shape = AttackShape3D.Projectile, localCenter = new Vector3(0f, 0.85f, 0.5f),
                projectileRadius = 0.1f, targetLayers = 1 << k_ActorLayer, obstructionLayers = 1,
            };
            attack.projectileInfo = new ProjectileInfo
            {
                hasProjectile = true, trajectory = BulletMoveType.Straight, speed = 10f, lifetime = 2f,
            };
            return attack;
        }

        private AttackInfo CreateAttack(string label, AttackCategory category, float windup = 0f)
        {
            MotionDefinition motion = ScriptableObject.CreateInstance<MotionDefinition>();
            motion.info = new MotionInfo { preMotionDuration = windup, activeMotionDuration = 0.15f, recoveryDuration = 0.1f };
            _assets.Add(motion);
            AttackInfo attack = ScriptableObject.CreateInstance<AttackInfo>();
            attack.attackName = label;
            attack.category = category;
            attack.motion = motion;
            attack.damageMultiplier = 1f;
            attack.attackElement = Element.Slash;
            _assets.Add(attack);
            return attack;
        }

        private EquipmentDefinition CreateGear(EquipSlot slot, int attackBonus, AbilityFlag flags = AbilityFlag.None)
        {
            EquipmentDefinition gear = ScriptableObject.CreateInstance<EquipmentDefinition>();
            SetField(gear, "_slot", slot);
            SetField(gear, "_grantedFlags", flags);
            SetField(gear, "_statModifiers", new EquipmentStatModifiers { attackBonus = new ElementalStatus { slash = attackBonus } });
            _assets.Add(gear);
            return gear;
        }

        private static PlayerCombatLoadout3D.WeaponOption Weapon(int id, EquipmentDefinition equipment, AttackInfo attack)
        {
            PlayerCombatLoadout3D.WeaponOption option = new PlayerCombatLoadout3D.WeaponOption();
            SetField(option, "itemId", id);
            SetField(option, "equipment", equipment);
            SetField(option, "attack", attack);
            return option;
        }

        private static PlayerCombatLoadout3D.CoreOption Core(int id, EquipmentDefinition equipment)
        {
            PlayerCombatLoadout3D.CoreOption option = new PlayerCombatLoadout3D.CoreOption();
            SetField(option, "itemId", id);
            SetField(option, "equipment", equipment);
            return option;
        }

        private static PlayerCombatLoadout3D.SpellOption Spell(int id, AttackInfo attack)
        {
            PlayerCombatLoadout3D.SpellOption option = new PlayerCombatLoadout3D.SpellOption();
            SetField(option, "itemId", id);
            SetField(option, "attack", attack);
            return option;
        }

        private static void SetField(object target, string name, object value)
            => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }
}
