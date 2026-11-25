using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.Runtime
{
    /// <summary>騎士の消耗品の所持数とベルトを表示し、在庫から登録できるようにする。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class ConsumableBeltPanel3D : MonoBehaviour
    {
        private const float k_RefreshInterval = 0.1f;
        private static readonly Color s_SelectedColor = new Color(0.36f, 0.28f, 0.14f);

        private sealed class SlotRow
        {
            internal Button Button;
            internal int ItemId = int.MinValue;
            internal int Count = -1;
            internal bool Selected;
        }

        private sealed class ItemRow
        {
            internal ConsumableDefinition Item;
            internal Button Button;
            internal int Count = -1;
        }

        [SerializeField] private PlayerItemUse3D itemUse;

        private readonly List<SlotRow> _slots = new List<SlotRow>(4);
        private readonly Dictionary<int, ItemRow> _items = new Dictionary<int, ItemRow>();
        private UIDocument _document;
        private PlayerInput _playerInput;
        private PlayerCharacterInput3D _characterInput;
        private InputActionAsset _shortcutAsset;
        private InputAction _shortcut;
        private InputActionMap _previousMap;
        private InputActionMap _menuMap;
        private VisualElement _root;
        private VisualElement _panel;
        private VisualElement _editor;
        private VisualElement _slotList;
        private ScrollView _itemList;
        private Label _summary;
        private Label _empty;
        private Label _status;
        private Button _open;
        private Button _clear;
        private Button _use;
        private IReadOnlyList<ConsumableDefinition> _definitions;
        private bool _editing;
        private bool _openPending;
        private float _nextRefresh;
        private float _statusUntil;

        private void Awake()
        {
            _document = GetComponent<UIDocument>();
            _playerInput = itemUse != null ? itemUse.GetComponent<PlayerInput>() : null;
            _characterInput = itemUse != null ? itemUse.GetComponent<PlayerCharacterInput3D>() : null;
        }

        private void OnEnable()
        {
            if (_characterInput != null) { _characterInput.ConsumableBeltPanel = this; }
            InputSystem.onActionChange += InvalidateMenuRequest;
            Refresh();
            RefreshMenuBinding();
        }

        private void OnDisable()
        {
            if (_characterInput != null && _characterInput.ConsumableBeltPanel == this)
            {
                _characterInput.ConsumableBeltPanel = null;
            }
            InputSystem.onActionChange -= InvalidateMenuRequest;
            BindMenuAction(null);
            _editing = false;
            RestoreInputMap();
            UnbindDocument();
        }

        internal bool BlocksGameplayClick(Vector2 mousePosition)
        {
            if (_editing || _open?.panel == null || !_open.enabledInHierarchy) { return false; }

            Vector2 screenPosition = new Vector2(mousePosition.x, Screen.height - mousePosition.y);
            IPanel panel = _open.panel;
            VisualElement picked = panel.Pick(RuntimePanelUtils.ScreenToPanel(panel, screenPosition));
            return picked == _open || (picked != null && _open.Contains(picked));
        }

        private void LateUpdate()
        {
            if (Time.unscaledTime >= _nextRefresh || !ReferenceEquals(CurrentRoot(), _root)) { Refresh(); }
            RefreshMenuBinding();
            if (!_openPending) { return; }

            // 先に同じ入力のUI取消イベントを処理する。Escで開いた直後に閉じないようにする。
            _openPending = false;
            if (OwnsShortcutMap()) { Open(); }
        }

        private void RefreshMenuBinding()
        {
            InputActionAsset actions = _playerInput != null && _playerInput.isActiveAndEnabled && CurrentRoot() != null
                ? _playerInput.actions : null;
            if (!ReferenceEquals(actions, _shortcutAsset)) { BindMenuAction(actions); }
        }

        private void BindMenuAction(InputActionAsset actions)
        {
            if (_shortcut != null) { _shortcut.performed -= QueueOpen; }
            _openPending = false;
            _shortcutAsset = actions;
            _shortcut = actions != null ? actions.FindAction("Player/Menu") : null;
            if (_shortcut != null) { _shortcut.performed += QueueOpen; }
        }

        private bool OwnsShortcutMap() => _shortcut != null && _shortcut.enabled && _playerInput != null
            && _playerInput.isActiveAndEnabled && ReferenceEquals(_playerInput.currentActionMap, _shortcut.actionMap);

        private void QueueOpen(InputAction.CallbackContext context)
        {
            if (!_editing && ReferenceEquals(context.action, _shortcut) && OwnsShortcutMap()) { _openPending = true; }
        }

        private void InvalidateMenuRequest(object source, InputActionChange change)
        {
            if (!_openPending || _shortcut == null) { return; }
            if ((change == InputActionChange.ActionMapDisabled && ReferenceEquals(source, _shortcut.actionMap))
                || (change == InputActionChange.ActionDisabled && ReferenceEquals(source, _shortcut)))
            {
                _openPending = false;
            }
        }

        private void Open()
        {
            ConsumableBelt belt = GameManager.ConsumableBelt;
            if (_editing || belt == null || itemUse == null || !itemUse.isActiveAndEnabled || CurrentRoot() == null) { return; }
            if (_playerInput != null && _playerInput.isActiveAndEnabled)
            {
                InputActionMap previous = _playerInput.currentActionMap;
                if (!_playerInput.inputIsActive || previous == null || !previous.enabled || previous.name != "Player") { return; }
                InputActionMap menu = _playerInput.actions.FindActionMap("UI");
                if (menu != null)
                {
                    _previousMap = previous;
                    _menuMap = menu;
                    _playerInput.SwitchCurrentActionMap(menu.name);
                }
            }
            _editing = true;
            Refresh();
            Button selected = _slots[belt.ActiveIndex].Button;
            selected.schedule.Execute(() =>
            {
                if (_editing && selected.panel != null) { selected.Focus(); }
            });
        }

        private void Close()
        {
            _editing = false;
            RestoreInputMap();
            Refresh();
            _open?.Focus();
        }

        private void RestoreInputMap()
        {
            if (_previousMap != null && _playerInput != null && _playerInput.isActiveAndEnabled
                && ReferenceEquals(_playerInput.currentActionMap, _menuMap))
            {
                _playerInput.SwitchCurrentActionMap(_previousMap.name);
            }
            _previousMap = _menuMap = null;
        }

        private void Select(int slot)
        {
            if (_editing && GameManager.ConsumableBelt?.TrySelect(slot) == true) { Refresh(); }
        }

        private void Assign(int itemId)
        {
            if (!_editing || GameManager.ConsumableBelt is not ConsumableBelt belt) { return; }
            bool assigned = GameManager.Inventory?.GetConsumableCount(itemId) > 0 && belt.TryAssign(belt.ActiveIndex, itemId);
            ShowStatus(assigned ? "登録しました" : "登録できません");
        }

        private void Clear()
        {
            if (_editing && GameManager.ConsumableBelt is ConsumableBelt belt && belt.TryClear(belt.ActiveIndex))
            {
                ShowStatus("登録を解除しました");
            }
        }

        private void Use()
        {
            if (!_editing || itemUse == null) { return; }
            ShowStatus(itemUse.TryUseActive() ? "使用動作を開始しました" : "今は使えません");
        }

        private void ShowStatus(string message)
        {
            _status.text = message;
            _statusUntil = Time.unscaledTime + 2f;
            Refresh();
        }

        private void Refresh()
        {
            _nextRefresh = Time.unscaledTime + k_RefreshInterval;
            if (!BindDocument()) { return; }

            ConsumableBelt belt = GameManager.ConsumableBelt;
            InventoryManager inventory = GameManager.Inventory;
            _panel.style.display = itemUse != null && belt != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (belt == null || itemUse == null) { return; }
            BindItems(itemUse.Items);
            BindSlots(belt.SlotCount);
            bool hasItems = false;
            foreach (ItemRow row in _items.Values)
            {
                int count = inventory?.GetConsumableCount(row.Item.ItemId) ?? 0;
                if (row.Count != count)
                {
                    row.Count = count;
                    int mpCost = row.Item.MpCost;
                    row.Button.text = mpCost > 0
                        ? $"{row.Item.DisplayName} ×{count}（MP {mpCost}）"
                        : $"{row.Item.DisplayName} ×{count}";
                    row.Button.style.display = count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                }
                hasItems |= count > 0;
            }
            _empty.style.display = hasItems ? DisplayStyle.None : DisplayStyle.Flex;
            for (int i = 0; i < _slots.Count; i++) { RefreshSlot(_slots[i], belt, i); }

            int itemId = belt.ActiveItemId;
            _summary.text = _slots[belt.ActiveIndex].Button.text;
            _clear.SetEnabled(itemId >= 0);
            _use.SetEnabled(itemUse.isActiveAndEnabled && _items.TryGetValue(itemId, out ItemRow active) && active.Count > 0);
            _editor.style.display = _editing ? DisplayStyle.Flex : DisplayStyle.None;
            _open.style.display = _editing ? DisplayStyle.None : DisplayStyle.Flex;
            _status.style.display = Time.unscaledTime < _statusUntil ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void RefreshSlot(SlotRow row, ConsumableBelt belt, int index)
        {
            int id = belt.GetItemId(index);
            bool known = _items.TryGetValue(id, out ItemRow item);
            int count = known ? item.Count : 0;
            if (row.ItemId != id || row.Count != count)
            {
                row.ItemId = id;
                row.Count = count;
                string description = id < 0 ? "空き" : known ? item.Button.text : "不明なアイテム";
                row.Button.text = $"枠 {index + 1}　{description}";
            }
            bool selected = index == belt.ActiveIndex;
            if (row.Selected != selected)
            {
                row.Selected = selected;
                row.Button.style.backgroundColor = selected ? new StyleColor(s_SelectedColor) : new StyleColor(StyleKeyword.Null);
            }
        }

        private void BindSlots(int count)
        {
            if (_slots.Count == count) { return; }
            _slotList.Clear();
            _slots.Clear();
            for (int i = 0; i < count; i++)
            {
                int index = i;
                Button button = ButtonFor($"consumable-belt-slot-{index}", string.Empty, () => Select(index));
                _slotList.Add(button);
                _slots.Add(new SlotRow { Button = button });
            }
        }

        private void BindItems(IReadOnlyList<ConsumableDefinition> items)
        {
            if (ReferenceEquals(items, _definitions)) { return; }
            _definitions = items;
            _itemList.Clear();
            _items.Clear();
            foreach (SlotRow row in _slots) { row.ItemId = int.MinValue; }
            if (items == null) { return; }

            foreach (ConsumableDefinition item in items)
            {
                if (item == null || item.ItemId < 0 || _items.ContainsKey(item.ItemId)) { continue; }
                int id = item.ItemId;
                Button button = ButtonFor($"consumable-belt-item-{id}", string.Empty, () => Assign(id));
                _itemList.Add(button);
                _items.Add(id, new ItemRow { Item = item, Button = button });
            }
        }

        private bool BindDocument()
        {
            VisualElement root = CurrentRoot();
            if (ReferenceEquals(root, _root)) { return _panel != null; }
            UnbindDocument();
            if (root == null)
            {
                _editing = false;
                RestoreInputMap();
                return false;
            }

            _root = root;
            _panel = new VisualElement { name = "consumable-belt" };
            _panel.style.position = Position.Absolute;
            _panel.style.left = _panel.style.bottom = 16f;
            _panel.style.maxWidth = 300f;
            _panel.style.paddingLeft = _panel.style.paddingRight = 10f;
            _panel.style.paddingTop = _panel.style.paddingBottom = 8f;
            _panel.style.backgroundColor = new Color(0.10f, 0.14f, 0.20f, 0.95f);
            _panel.style.color = Color.white;
            _summary = new Label { name = "consumable-belt-summary", pickingMode = PickingMode.Ignore };
            _summary.style.unityFontStyleAndWeight = FontStyle.Bold;
            _panel.Add(_summary);
            _open = ButtonFor("consumable-belt-open", "消耗品", Open);
            _panel.Add(_open);
            _editor = new VisualElement { name = "consumable-belt-editor" };
            _editor.RegisterCallback<NavigationCancelEvent>(evt =>
            {
                if (!_editing) { return; }
                evt.StopPropagation();
                Close();
            });
            _panel.Add(_editor);
            _slotList = new VisualElement();
            _editor.Add(_slotList);

            VisualElement actions = new VisualElement();
            actions.style.flexDirection = FlexDirection.Row;
            _use = ButtonFor("consumable-belt-use", "使う", Use);
            _clear = ButtonFor("consumable-belt-clear", "登録解除", Clear);
            actions.Add(_use);
            actions.Add(_clear);
            actions.Add(ButtonFor("consumable-belt-close", "閉じる", Close));
            _editor.Add(actions);
            _editor.Add(new Label("選択中の枠に登録") { pickingMode = PickingMode.Ignore });
            _empty = new Label("所持している消耗品がありません") { pickingMode = PickingMode.Ignore };
            _editor.Add(_empty);
            _itemList = new ScrollView();
            _itemList.style.maxHeight = 144f;
            _editor.Add(_itemList);
            _status = new Label { name = "consumable-belt-status", pickingMode = PickingMode.Ignore };
            _panel.Add(_status);
            _root.Add(_panel);
            return true;
        }

        private VisualElement CurrentRoot() => _document != null && _document.isActiveAndEnabled ? _document.rootVisualElement : null;

        private void UnbindDocument()
        {
            _panel?.RemoveFromHierarchy();
            _root = _panel = _editor = _slotList = null;
            _itemList = null;
            _summary = _empty = _status = null;
            _open = _clear = _use = null;
            _definitions = null;
            _slots.Clear();
            _items.Clear();
        }

        private static Button ButtonFor(string name, string text, Action clicked) => new Button(clicked) { name = name, text = text };
    }
}
