#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Game.Tests.PlayMode
{
    public sealed class ExpeditionLoadoutPanel3DPlayTests
    {
        private const int k_Sword = 1;
        private const int k_OtherWeapon = 2;
        private const int k_UnownedWeapon = 3;
        private const int k_Core = 4;
        private const int k_Spell = 5;

        private readonly List<ScriptableObject> _assets = new List<ScriptableObject>();
        private readonly InputTestFixture _devices = new InputTestFixture();
        private GameObject _knightObject;
        private GameObject _hud;
        private bool _mockDevices;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_hud != null) { Object.Destroy(_hud); }
            if (_knightObject != null) { Object.Destroy(_knightObject); }
            foreach (ScriptableObject asset in _assets) { Object.Destroy(asset); }
            _assets.Clear();
            TestSceneHelper.Cleanup();
            yield return null;
            if (_mockDevices) { _devices.TearDown(); _mockDevices = false; }
        }

        [UnityTest]
        public IEnumerator FieldLoadout_WhenOpenedFromKeyboardOrPad_EquipsOwnedItemsAndSpendsEarnedPoints()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            TestSceneHelper.CreateGameManager();
            GameManager.Instance.enabled = false;
            string actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions").ToJson();
            _devices.Setup();
            _mockDevices = true;
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            Mouse mouse = InputSystem.AddDevice<Mouse>();
            Gamepad gamepad = InputSystem.AddDevice<Gamepad>();

            InputActionAsset bindings = InputActionAsset.FromJson(actions);
            _assets.Add(bindings);
            AttackInfo slash = AssetDatabase.LoadAssetAtPath<AttackInfo>("Assets/MyAsset/Data/PLACEHOLDER_KnightSword3D.asset");
            AttackInfo bolt = AssetDatabase.LoadAssetAtPath<AttackInfo>("Assets/MyAsset/Data/PLACEHOLDER_Bolt3D.asset");
            Assert.IsNotNull(slash);
            Assert.IsNotNull(bolt);
            EquipmentDefinition sword = Gear(EquipSlot.Weapon);
            EquipmentDefinition otherWeapon = Gear(EquipSlot.Weapon);
            EquipmentDefinition core = Gear(EquipSlot.Core);
            PlayerCombatLoadout3D loadout = CreateKnight(bindings, out CharacterActor3D knight,
                out PlayerInput input, out PlayerProgression3D progression);
            SetField(loadout, "weapons", new[]
            {
                Option<PlayerCombatLoadout3D.WeaponOption>(k_Sword, "剣", sword, slash),
                Option<PlayerCombatLoadout3D.WeaponOption>(k_OtherWeapon, "予備武器", otherWeapon, slash),
                Option<PlayerCombatLoadout3D.WeaponOption>(k_UnownedWeapon, "未取得の武器", Gear(EquipSlot.Weapon), slash),
            });
            SetField(loadout, "cores", new[] { Option<PlayerCombatLoadout3D.CoreOption>(k_Core, "魔物のコア", core) });
            SetField(loadout, "spells", new[] { Option<PlayerCombatLoadout3D.SpellOption>(k_Spell, "追尾弾", attack: bolt) });
            SetField(loadout, "startingWeaponId", k_Sword);
            GameManager.Inventory.Add(k_Sword, ItemCategory.Weapon, 1, 1);
            GameManager.Inventory.Add(k_OtherWeapon, ItemCategory.Weapon, 1, 1);
            GameManager.Inventory.Add(k_Core, ItemCategory.Core, 1, 1);
            GameManager.Inventory.Add(k_Spell, ItemCategory.PlayerMagic, 1, 1);
            _knightObject.SetActive(true);
            input.SwitchCurrentControlScheme("Keyboard&Mouse", keyboard, mouse);
            UIDocument document = CreatePanel(loadout);
            yield return null;

            Assert.IsTrue(progression.TryAwardExploration("loadout-ui-test.discovered", 100));
            int startingHp = GameManager.Data.GetVitals(knight.Actor.Hash).maxHp;
            VisualElement root = document.rootVisualElement;
            Assert.IsNull(root.Q("expedition-loadout"));

            Tap(keyboard, new KeyboardState(Key.Tab), new KeyboardState());
            input.SwitchCurrentActionMap("UI");
            input.SwitchCurrentActionMap("Player");
            yield return null;
            Assert.IsNull(root.Q("expedition-loadout"));

            Tap(keyboard, new KeyboardState(Key.Tab), new KeyboardState());
            yield return null;
            Assert.AreEqual("UI", input.currentActionMap.name);
            Assert.IsNull(root.Q<Button>($"loadout-weapon-{k_UnownedWeapon}"));
            Submit(root, $"loadout-weapon-{k_OtherWeapon}");
            Assert.AreEqual(k_OtherWeapon, loadout.CurrentWeaponId);
            Assert.AreSame(otherWeapon, GameManager.Equipment.GetHolder(knight.Actor.Hash).Weapon);

            Submit(root, "loadout-tab-cores");
            Submit(root, $"loadout-core-{k_Core}");
            Assert.AreEqual(k_Core, loadout.CurrentCoreId);
            Assert.AreSame(core, GameManager.Equipment.GetHolder(knight.Actor.Hash).Core);

            Submit(root, "loadout-tab-magic");
            Submit(root, "loadout-magic-slot-2");
            Submit(root, $"loadout-magic-{k_Spell}");
            Assert.AreEqual(2, loadout.ActiveSpellSlot);
            Assert.AreEqual(k_Spell, loadout.GetSpellId(2));
            Assert.AreEqual("枠 3　追尾弾", root.Q<Button>("loadout-magic-slot-2").text);
            Submit(root, "loadout-magic-clear");
            Assert.That(loadout.GetSpellId(2), Is.Negative);
            Submit(root, $"loadout-magic-{k_Spell}");

            Submit(root, "loadout-tab-growth");
            Assert.AreEqual("残り 3 pt", root.Q<Label>("loadout-points").text);
            Submit(root, "loadout-stat-Vit");
            Assert.AreEqual("残り 2 pt", root.Q<Label>("loadout-points").text);
            Assert.AreEqual(1, progression.AllocatedStats.vit);
            Assert.AreEqual(startingHp + LevelUpLogic.k_HpPerVit, GameManager.Data.GetVitals(knight.Actor.Hash).maxHp);
            Cancel(root.Q<Button>("loadout-stat-Vit"));
            Assert.IsNull(root.Q("expedition-loadout"));
            Assert.AreEqual("Player", input.currentActionMap.name);

            input.SwitchCurrentControlScheme("Gamepad", gamepad);
            Tap(gamepad, new GamepadState { leftTrigger = 1f }, new GamepadState());
            yield return null;
            Assert.AreEqual("UI", input.currentActionMap.name);
            Assert.AreEqual("残り 2 pt", root.Q<Label>("loadout-points").text);
            Submit(root, "loadout-close");
            Assert.AreEqual("Player", input.currentActionMap.name);
        }

        private PlayerCombatLoadout3D CreateKnight(InputActionAsset actions, out CharacterActor3D knight,
            out PlayerInput input, out PlayerProgression3D progression)
        {
            _knightObject = new GameObject("Loadout test knight");
            _knightObject.SetActive(false);
            knight = _knightObject.AddComponent<CharacterActor3D>();
            Game.Core.CharacterInfo character = TestSceneHelper.CreateTestCharacterInfo();
            _assets.Add(character);
            knight.SetCharacterInfoForTest(character);
            input = _knightObject.AddComponent<PlayerInput>();
            input.actions = actions;
            input.defaultActionMap = "Player";
            input.neverAutoSwitchControlSchemes = true;
            progression = _knightObject.AddComponent<PlayerProgression3D>();
            return _knightObject.AddComponent<PlayerCombatLoadout3D>();
        }

        private UIDocument CreatePanel(PlayerCombatLoadout3D loadout)
        {
            _hud = new GameObject("Expedition loadout panel");
            _hud.SetActive(false);
            UIDocument document = _hud.AddComponent<UIDocument>();
            document.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/MyAsset/UI/GamePanelSettings.asset");
            ExpeditionLoadoutPanel3D panel = _hud.AddComponent<ExpeditionLoadoutPanel3D>();
            SetField(panel, "loadout", loadout);
            _hud.SetActive(true);
            return document;
        }

        private EquipmentDefinition Gear(EquipSlot slot)
        {
            EquipmentDefinition definition = ScriptableObject.CreateInstance<EquipmentDefinition>();
            _assets.Add(definition);
            SetField(definition, "_slot", slot);
            return definition;
        }

        private static T Option<T>(int itemId, string title, EquipmentDefinition equipment = null, AttackInfo attack = null)
        {
            object option = System.Activator.CreateInstance(typeof(T));
            SetField(option, "itemId", itemId);
            SetField(option, "displayName", title);
            if (equipment != null) { SetField(option, "equipment", equipment); }
            if (attack != null) { SetField(option, "attack", attack); }
            return (T)option;
        }

        private static void SetField(object target, string name, object value)
            => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(target, value);

        private static void Tap<TState>(InputDevice device, TState pressed, TState released) where TState : struct, IInputStateTypeInfo
        {
            InputSystem.QueueStateEvent(device, pressed);
            InputSystem.Update();
            InputSystem.QueueStateEvent(device, released);
            InputSystem.Update();
        }

        private static void Submit(VisualElement root, string id)
        {
            Button button = root.Q<Button>(id);
            Assert.IsNotNull(button, id);
            Assert.IsTrue(button.enabledInHierarchy, id);
            using NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled();
            submit.target = button;
            button.SendEvent(submit);
        }

        private static void Cancel(Button button)
        {
            Assert.IsNotNull(button);
            using NavigationCancelEvent cancel = NavigationCancelEvent.GetPooled();
            cancel.target = button;
            button.SendEvent(cancel);
        }
    }
}
#endif
