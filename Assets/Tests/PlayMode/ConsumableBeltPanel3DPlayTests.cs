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
using MouseButton = UnityEngine.InputSystem.LowLevel.MouseButton;

namespace Game.Tests.PlayMode
{
    public sealed class ConsumableBeltPanel3DPlayTests
    {
        private const int k_Potion = 0;
        private const int k_Unowned = 1;
        private readonly List<Object> _assets = new List<Object>();
        private readonly InputTestFixture _inputDevices = new InputTestFixture();
        private GameObject _knight;
        private GameObject _hud;
        private bool _mockDevices;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_hud != null) { Object.Destroy(_hud); }
            if (_knight != null) { Object.Destroy(_knight); }
            foreach (Object asset in _assets) { Object.Destroy(asset); }
            _assets.Clear();
            TestSceneHelper.Cleanup();
            yield return null;
            if (_mockDevices)
            {
                _inputDevices.TearDown();
                _mockDevices = false;
            }
        }

        [UnityTest]
        public IEnumerator BeltPanel_WhenRegisteringUsingAndClearingAnOwnedItem_UpdatesTheSameGameplayBelt()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            TestSceneHelper.CreateGameManager();
            GameManager.Instance.enabled = false;

            PlayerItemUse3D itemUse = CreateKnight(out CharacterActor3D knight, out PlayerInput input);
            UIDocument document = CreatePanel(itemUse);
            Assert.AreEqual(1, GameManager.Inventory.Add(k_Potion, ItemCategory.Consumable, 1, 99));
            GameManager.Data.GetVitals(knight.Actor.Hash).currentHp = 50;
            yield return null;

            VisualElement root = document.rootVisualElement;
            Assert.AreEqual("Player", input.currentActionMap.name);
            Assert.AreEqual(DisplayStyle.None, root.Q("consumable-belt-editor").style.display.value);
            Submit(root.Q<Button>("consumable-belt-open"));
            Assert.AreEqual("UI", input.currentActionMap.name);
            Assert.AreEqual(DisplayStyle.Flex, root.Q("consumable-belt-editor").style.display.value);
            Button flask = root.Q<Button>($"consumable-belt-item-{k_Unowned}");
            Assert.AreEqual(DisplayStyle.None, flask.style.display.value);
            GameManager.Inventory.Add(k_Unowned, ItemCategory.Consumable, 1, 99);
            Assert.IsTrue(GameManager.ConsumableBelt.TryAssign(0, k_Unowned));
            Submit(root.Q<Button>("consumable-belt-slot-0"));
            Assert.AreEqual(("火炎瓶 ×1（MP 8）", "枠 1　火炎瓶 ×1（MP 8）"),
                (flask.text, root.Q<Label>("consumable-belt-summary").text));

            Submit(root.Q<Button>("consumable-belt-slot-2"));
            Submit(root.Q<Button>($"consumable-belt-item-{k_Potion}"));
            Assert.AreEqual(2, GameManager.ConsumableBelt.ActiveIndex);
            Assert.AreEqual(k_Potion, GameManager.ConsumableBelt.GetItemId(2));
            Assert.AreEqual(1, GameManager.Inventory.GetConsumableCount(k_Potion));
            Assert.AreEqual("枠 3　回復薬 ×1", root.Q<Label>("consumable-belt-summary").text);

            Button use = root.Q<Button>("consumable-belt-use");
            Submit(use);
            Assert.AreEqual(50, GameManager.Data.GetVitals(knight.Actor.Hash).currentHp);
            Assert.AreEqual(1, GameManager.Inventory.GetConsumableCount(k_Potion));
            GameManager.Instance.SimulationTick(0.26f, 0.26f);
            Assert.AreEqual(70, GameManager.Data.GetVitals(knight.Actor.Hash).currentHp);
            Assert.AreEqual(0, GameManager.Inventory.GetConsumableCount(k_Potion));
            Assert.AreEqual(k_Potion, GameManager.ConsumableBelt.GetItemId(2));
            yield return new WaitForSecondsRealtime(0.15f);

            Assert.IsFalse(use.enabledInHierarchy);
            Assert.AreEqual("枠 3　回復薬 ×0", root.Q<Label>("consumable-belt-summary").text);
            Submit(root.Q<Button>("consumable-belt-clear"));
            Assert.That(GameManager.ConsumableBelt.ActiveItemId, Is.Negative);
            Assert.AreEqual("枠 3　空き", root.Q<Label>("consumable-belt-summary").text);
            Submit(root.Q<Button>("consumable-belt-close"));
            Assert.AreEqual("Player", input.currentActionMap.name);
            Assert.AreEqual(DisplayStyle.None, root.Q("consumable-belt-editor").style.display.value);
        }

        [UnityTest]
        public IEnumerator BeltPanel_MenuOpensFromKeyboardAndGamepad_AndRequiresTheCurrentGameplayMap()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            TestSceneHelper.CreateGameManager();
            GameManager.Instance.enabled = false;

            string shippedActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions").ToJson();
            _inputDevices.Setup();
            _mockDevices = true;
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            Mouse mouse = InputSystem.AddDevice<Mouse>();
            Gamepad gamepad = InputSystem.AddDevice<Gamepad>();
            InputActionAsset actions = InputActionAsset.FromJson(shippedActions);
            PlayerItemUse3D itemUse = CreateKnight(out _, out PlayerInput input, actions);
            input.SwitchCurrentControlScheme("Keyboard&Mouse", keyboard, mouse);
            UIDocument document = CreatePanel(itemUse);
            ConsumableBeltPanel3D panel = document.GetComponent<ConsumableBeltPanel3D>();
            yield return null;

            VisualElement root = document.rootVisualElement;
            TapMenu(keyboard, new KeyboardState(Key.Escape), new KeyboardState());
            input.SwitchCurrentActionMap("UI");
            input.SwitchCurrentActionMap("Player");
            yield return null;
            Assert.AreEqual(DisplayStyle.None, root.Q("consumable-belt-editor").style.display.value);

            TapMenu(keyboard, new KeyboardState(Key.Escape), new KeyboardState());
            yield return null;
            Assert.AreEqual("UI", input.currentActionMap.name);
            Assert.AreEqual(DisplayStyle.Flex, root.Q("consumable-belt-editor").style.display.value);
            CancelFromSlot(root);
            Assert.AreEqual("Player", input.currentActionMap.name);
            Assert.AreEqual(DisplayStyle.None, root.Q("consumable-belt-editor").style.display.value);

            input.SwitchCurrentControlScheme("Gamepad", gamepad);
            panel.enabled = false;
            TapMenu(gamepad, new GamepadState().WithButton(GamepadButton.Start), new GamepadState());
            yield return null;
            Assert.AreEqual("Player", input.currentActionMap.name);
            Assert.IsNull(root.Q("consumable-belt-editor"));

            panel.enabled = true;
            TapMenu(gamepad, new GamepadState().WithButton(GamepadButton.Start), new GamepadState());
            yield return null;
            Assert.AreEqual("UI", input.currentActionMap.name);
            Assert.AreEqual(DisplayStyle.Flex, root.Q("consumable-belt-editor").style.display.value);
            CancelFromSlot(root);
            Assert.AreEqual("Player", input.currentActionMap.name);
        }

        [UnityTest]
        public IEnumerator BeltPanel_OpenClickDoesNotAttack_ButClickOutsideStillAttacks()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            TestSceneHelper.CreateGameManager();
            GameManager.Instance.enabled = false;

            string shippedActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions").ToJson();
            _inputDevices.Setup();
            _mockDevices = true;
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            Mouse mouse = InputSystem.AddDevice<Mouse>();
            InputActionAsset actions = InputActionAsset.FromJson(shippedActions);
            AttackInfo sword = AssetDatabase.LoadAssetAtPath<AttackInfo>("Assets/MyAsset/Data/PLACEHOLDER_KnightSword3D.asset");
            Assert.IsNotNull(sword);
            PlayerItemUse3D itemUse = CreateKnight(out CharacterActor3D knight, out PlayerInput input, actions, sword);
            input.SwitchCurrentControlScheme("Keyboard&Mouse", keyboard, mouse);

            PanelSettings settings = Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/MyAsset/UI/GamePanelSettings.asset"));
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            settings.scale = 1f;
            _assets.Add(settings);
            VisualElement root = CreatePanel(itemUse, settings).rootVisualElement;
            Button open = root.Q<Button>("consumable-belt-open");
            Assert.IsNotNull(open);

            for (int frame = 0; frame < 10 && !(open.worldBound.width > 0f); frame++) { yield return null; }
            Assert.That(open.worldBound.width, Is.GreaterThan(0f));
            Vector2 openCenter = open.worldBound.center;
            Vector2 mouseOnButton = new Vector2(openCenter.x, Screen.height - openCenter.y);
            Vector2 outside = new Vector2(Screen.width * 0.8f, Screen.height * 0.7f);
            ActionExecutionSystem execution = GameManager.Actions.Execution;

            // Move and press in a single input event before the UI module has refreshed pointer hover.
            SendMouse(mouse, mouseOnButton, true);
            yield return new WaitForFixedUpdate();
            Assert.AreEqual("Player", input.currentActionMap.name);
            Assert.IsFalse(execution.GetPrimary(knight.Actor.Hash, out _));

            SendMouse(mouse, mouseOnButton, false);
            Submit(open);
            Assert.AreEqual("UI", input.currentActionMap.name);
            Assert.AreEqual(DisplayStyle.Flex, root.Q("consumable-belt-editor").style.display.value);
            CancelFromSlot(root);

            SendMouse(mouse, outside, true);
            yield return new WaitForFixedUpdate();
            Assert.AreEqual("Player", input.currentActionMap.name);
            Assert.IsTrue(execution.GetPrimary(knight.Actor.Hash, out ActionExecution attack));
            Assert.AreEqual(AnimationPhase.Anticipation, attack.phase);
            SendMouse(mouse, outside, false);
        }

        private PlayerItemUse3D CreateKnight(out CharacterActor3D knight, out PlayerInput input,
            InputActionAsset actions = null, AttackInfo attack = null)
        {
            MotionDefinition motion = ScriptableObject.CreateInstance<MotionDefinition>();
            motion.info = new MotionInfo { preMotionDuration = 0.25f, activeMotionDuration = 0.1f, recoveryDuration = 0.1f };
            ActionDefinition action = ScriptableObject.CreateInstance<ActionDefinition>();
            action.motion = motion;
            _assets.Add(motion);
            _assets.Add(action);

            _knight = new GameObject("Consumable belt knight");
            _knight.SetActive(false);
            knight = _knight.AddComponent<CharacterActor3D>();
            Game.Core.CharacterInfo character = TestSceneHelper.CreateTestCharacterInfo();
            _assets.Add(character);
            knight.SetCharacterInfoForTest(character);
            input = _knight.AddComponent<PlayerInput>();
            actions ??= Object.Instantiate(AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions"));
            _assets.Add(actions);
            input.actions = actions;
            input.defaultActionMap = "Player";
            input.neverAutoSwitchControlSchemes = true;
            PlayerItemUse3D itemUse = _knight.AddComponent<PlayerItemUse3D>();
            SetField(itemUse, "items", new[] { CreateItem(k_Potion, "回復薬", action), CreateItem(k_Unowned, "火炎瓶", action, mpCost: 8) });
            if (attack != null) { SetField(_knight.AddComponent<PlayerCharacterInput3D>(), "attack3D", attack); }
            _knight.SetActive(true);
            return itemUse;
        }

        private ConsumableDefinition CreateItem(int id, string name, ActionDefinition action, int mpCost = 0)
        {
            ConsumableDefinition item = ScriptableObject.CreateInstance<ConsumableDefinition>();
            _assets.Add(item);
            SetField(item, "itemId", id);
            SetField(item, "displayName", name);
            SetField(item, "useAction", action);
            SetField(item, "mpCost", mpCost);
            return item;
        }

        private UIDocument CreatePanel(PlayerItemUse3D itemUse, PanelSettings settings = null)
        {
            _hud = new GameObject("Consumable belt panel");
            _hud.SetActive(false);
            UIDocument document = _hud.AddComponent<UIDocument>();
            document.panelSettings = settings ?? AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/MyAsset/UI/GamePanelSettings.asset");
            ConsumableBeltPanel3D panel = _hud.AddComponent<ConsumableBeltPanel3D>();
            SetField(panel, "itemUse", itemUse);
            _hud.SetActive(true);
            return document;
        }

        private static void Submit(Button button)
        {
            Assert.IsNotNull(button);
            Assert.IsTrue(button.enabledInHierarchy);
            Assert.IsNotNull(button.panel);
            using NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled();
            submit.target = button;
            button.SendEvent(submit);
        }

        private static void TapMenu<TState>(InputDevice device, TState pressed, TState released) where TState : struct, IInputStateTypeInfo
        {
            InputSystem.QueueStateEvent(device, pressed);
            InputSystem.Update();
            InputSystem.QueueStateEvent(device, released);
            InputSystem.Update();
        }

        private static void SendMouse(Mouse mouse, Vector2 position, bool pressed)
        {
            MouseState state = new MouseState { position = position };
            if (pressed) { state = state.WithButton(MouseButton.Left); }
            InputSystem.QueueStateEvent(mouse, state);
            InputSystem.Update();
        }

        private static void CancelFromSlot(VisualElement root)
        {
            Button slot = root.Q<Button>("consumable-belt-slot-0");
            using NavigationCancelEvent cancel = NavigationCancelEvent.GetPooled();
            cancel.target = slot;
            slot.SendEvent(cancel);
        }

        private static void SetField(Object target, string name, object value)
            => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }
}
#endif
