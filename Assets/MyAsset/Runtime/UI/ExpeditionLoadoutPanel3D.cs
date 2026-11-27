using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.Runtime
{
    /// <summary>探索中の騎士の装備、魔法枠、連携、能力値を編集する。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class ExpeditionLoadoutPanel3D : MonoBehaviour
    {
        private enum Section { Weapons, Cores, Magic, Growth, Cooperation }

        private static readonly (string id, string title)[] s_Sections =
        {
            ("weapons", "武器"), ("cores", "コア"), ("magic", "魔法"), ("growth", "能力値"), ("cooperation", "連携"),
        };

        private static readonly (StatType stat, string title)[] s_Attributes =
        {
            (StatType.Str, "筋力"), (StatType.Dex, "技量"), (StatType.Intel, "知力"),
            (StatType.Vit, "生命力"), (StatType.Mnd, "精神力"), (StatType.End, "持久力"),
        };

        [SerializeField] private PlayerCombatLoadout3D loadout;

        private readonly Button[] _tabs = new Button[s_Sections.Length];
        private UIDocument _document;
        private CharacterActor3D _knight;
        private PlayerProgression3D _progression;
        private PlayerInput _input;
        private InputActionAsset _shortcutAsset;
        private InputAction _shortcut;
        private InputActionMap _gameplayMap;
        private InputActionMap _menuMap;
        private SaveManager _saves;
        private int _slot;
        private ulong _loadRevision;
        private ActorRef _owner;
        private VisualElement _root;
        private VisualElement _overlay;
        private ScrollView _body;
        private Label _status;
        private Section _section;
        private bool _openPending;

        private void Awake()
        {
            _document = GetComponent<UIDocument>();
            if (loadout == null) { return; }
            _knight = loadout.GetComponent<CharacterActor3D>();
            _progression = loadout.GetComponent<PlayerProgression3D>();
            _input = loadout.GetComponent<PlayerInput>();
        }

        private void OnEnable()
        {
            InputSystem.onActionChange += InvalidateMenuRequest;
            UpdateShortcut();
        }

        private void OnDisable()
        {
            InputSystem.onActionChange -= InvalidateMenuRequest;
            BindShortcut(null);
            Close();
        }

        private void LateUpdate()
        {
            if (_overlay != null && !CanEdit()) { Close(); }
            UpdateShortcut();
            if (!_openPending) { return; }
            _openPending = false;
            if (OwnsShortcutMap()) { Open(); }
        }

        public void Open()
        {
            if (_overlay != null || !IsAvailable()) { return; }
            InputActionAsset actions = _input.actions;
            if (actions == null) { return; }
            InputActionMap gameplayMap = actions.FindActionMap("Player");
            InputActionMap menuMap = actions.FindActionMap("UI");
            if (gameplayMap == null || !gameplayMap.enabled || menuMap == null
                || !ReferenceEquals(_input.currentActionMap, gameplayMap)) { return; }

            _saves = GameManager.Saves;
            if (_saves == null) { return; }
            _slot = _saves.ActiveSlotIndex;
            _loadRevision = _saves.LoadRevision;
            _owner = _knight.Actor;
            _gameplayMap = gameplayMap;
            _menuMap = menuMap;
            _input.SwitchCurrentActionMap(menuMap.name);
            BuildDocument(_document.rootVisualElement);
            ShowStatus(string.Empty);
            RenderPage();
            Focus(_tabs[(int)_section]);
        }

        public void Close()
        {
            _overlay?.RemoveFromHierarchy();
            _root = _overlay = null;
            _body = null;
            _status = null;
            _saves = null;
            _owner = default;
            Array.Clear(_tabs, 0, _tabs.Length);
            if (_gameplayMap != null && _input != null && _input.isActiveAndEnabled
                && ReferenceEquals(_input.currentActionMap, _menuMap))
            {
                if (_input.inputIsActive) { _input.SwitchCurrentActionMap(_gameplayMap.name); }
                else { _input.currentActionMap = null; }
            }
            _gameplayMap = _menuMap = null;
        }

        private bool IsAvailable() => isActiveAndEnabled && _document != null && _document.isActiveAndEnabled
            && _document.rootVisualElement != null && loadout != null && loadout.isActiveAndEnabled
            && loadout.gameObject.scene == gameObject.scene && _knight != null && _knight.IsAlive
            && _input != null && _input.isActiveAndEnabled && _input.inputIsActive;

        private bool CanEdit() => IsAvailable() && ReferenceEquals(_document.rootVisualElement, _root)
            && ReferenceEquals(_input.currentActionMap, _menuMap) && _owner == _knight.Actor
            && _saves != null && ReferenceEquals(_saves, GameManager.Saves) && _slot == _saves.ActiveSlotIndex
            && _loadRevision == _saves.LoadRevision;

        private void UpdateShortcut()
        {
            InputActionAsset actions = IsAvailable() ? _input.actions : null;
            if (!ReferenceEquals(actions, _shortcutAsset)) { BindShortcut(actions); }
        }

        private void BindShortcut(InputActionAsset actions)
        {
            if (_shortcut != null) { _shortcut.performed -= QueueOpen; }
            _openPending = false;
            _shortcutAsset = actions;
            _shortcut = actions != null ? actions.FindAction("Player/WeaponSwitch") : null;
            if (_shortcut != null) { _shortcut.performed += QueueOpen; }
        }

        private bool OwnsShortcutMap() => _shortcut != null && _shortcut.enabled && IsAvailable()
            && ReferenceEquals(_input.currentActionMap, _shortcut.actionMap);

        private void QueueOpen(InputAction.CallbackContext context)
        {
            if (_overlay == null && ReferenceEquals(context.action, _shortcut) && OwnsShortcutMap()) { _openPending = true; }
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

        private void BuildDocument(VisualElement root)
        {
            _root = root;
            _overlay = new VisualElement { name = "expedition-loadout" };
            _overlay.style.position = Position.Absolute;
            _overlay.style.left = _overlay.style.right = _overlay.style.top = _overlay.style.bottom = 0f;
            _overlay.style.alignItems = Align.Center;
            _overlay.style.justifyContent = Justify.Center;
            _overlay.style.backgroundColor = new Color(0.02f, 0.04f, 0.06f, 0.72f);
            _overlay.RegisterCallback<NavigationCancelEvent>(evt => { evt.StopPropagation(); Close(); });

            VisualElement panel = new VisualElement();
            panel.style.width = Length.Percent(86f);
            panel.style.maxWidth = 440f;
            panel.style.maxHeight = Length.Percent(84f);
            panel.style.paddingLeft = panel.style.paddingRight = panel.style.paddingTop = panel.style.paddingBottom = 12f;
            panel.style.backgroundColor = new Color(0.10f, 0.14f, 0.20f);
            panel.style.color = Color.white;
            VisualElement heading = new VisualElement();
            heading.style.flexDirection = FlexDirection.Row;
            heading.style.justifyContent = Justify.SpaceBetween;
            Label title = new Label("戦闘準備") { pickingMode = PickingMode.Ignore };
            title.style.fontSize = 18f;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.Add(title);
            heading.Add(NewButton("loadout-close", "閉じる", Close));
            panel.Add(heading);

            VisualElement tabs = new VisualElement();
            tabs.style.flexDirection = FlexDirection.Row;
            for (int i = 0; i < s_Sections.Length; i++)
            {
                Section section = (Section)i;
                (string id, string label) = s_Sections[i];
                Button button = NewButton($"loadout-tab-{id}", label, () => Select(section));
                button.style.flexGrow = 1f;
                if (section == Section.Cooperation && loadout.CooperationOptions.Count == 0)
                {
                    button.style.display = DisplayStyle.None;
                }
                _tabs[i] = button;
                tabs.Add(button);
            }
            panel.Add(tabs);
            _body = new ScrollView();
            _body.style.flexShrink = 1f;
            panel.Add(_body);
            _status = new Label { name = "loadout-status", pickingMode = PickingMode.Ignore };
            _status.style.whiteSpace = WhiteSpace.Normal;
            panel.Add(_status);
            _overlay.Add(panel);
            root.Add(_overlay);
        }

        private void Select(Section section)
        {
            if (!CanEdit()) { return; }
            _section = section;
            ShowStatus(string.Empty);
            RenderPage();
        }

        private void RenderPage()
        {
            for (int i = 0; i < _tabs.Length; i++) { MarkSelected(_tabs[i], (int)_section == i); }
            _body.Clear();
            switch (_section)
            {
                case Section.Weapons:
                    AddChoices("weapon", loadout.Weapons, option => option.ItemId, option => option.DisplayName,
                        loadout.OwnsWeapon, loadout.CurrentWeaponId, loadout.TryEquipWeapon);
                    break;
                case Section.Cores:
                    AddChoices("core", loadout.Cores, option => option.ItemId, option => option.DisplayName,
                        loadout.OwnsCore, loadout.CurrentCoreId, loadout.TryEquipCore);
                    break;
                case Section.Magic:
                    RenderMagic();
                    break;
                case Section.Growth:
                    RenderGrowth();
                    break;
                case Section.Cooperation:
                    int equippedCooperation = loadout.CurrentCooperationId;
                    if (!loadout.OwnsCooperation(equippedCooperation))
                    {
                        _body.Add(new Label("連携が未装備です") { name = "loadout-cooperation-empty", pickingMode = PickingMode.Ignore });
                    }
                    AddChoices("cooperation", loadout.CooperationOptions, option => option.ItemId, option => option.DisplayName,
                        loadout.OwnsCooperation, equippedCooperation, loadout.TryEquipCooperation);
                    break;
            }
        }

        private void RenderMagic()
        {
            int activeSlot = loadout.ActiveSpellSlot;
            for (int i = 0; i < loadout.SpellSlotCount; i++)
            {
                int slot = i;
                int spell = loadout.GetSpellId(slot);
                string name = $"loadout-magic-slot-{slot}";
                Button button = NewButton(name, $"枠 {i + 1}　{SpellName(spell)}", () => Change(name, () => loadout.TrySelectSpellSlot(slot)));
                MarkSelected(button, slot == activeSlot);
                _body.Add(button);
            }

            const string clear = "loadout-magic-clear";
            int selectedSpell = loadout.GetSpellId(activeSlot);
            Button clearButton = NewButton(clear, "選択中の枠を空にする", () => Change(clear, () => loadout.TryClearSpell(activeSlot)));
            clearButton.SetEnabled(selectedSpell >= 0);
            _body.Add(clearButton);
            _body.Add(new Label("選択中の枠にセット") { pickingMode = PickingMode.Ignore });
            AddChoices("magic", loadout.Spells, option => option.ItemId, option => option.DisplayName,
                loadout.OwnsSpell, selectedSpell, id => loadout.TryAssignSpell(activeSlot, id));
        }

        private string SpellName(int id)
        {
            if (id < 0) { return "空き"; }
            foreach (PlayerCombatLoadout3D.SpellOption option in loadout.Spells)
            {
                if (option != null && option.ItemId == id) { return option.DisplayName; }
            }
            return "不明な魔法";
        }

        private void AddChoices<T>(string category, IReadOnlyList<T> options, Func<T, int> itemId, Func<T, string> displayName,
            Func<int, bool> owns, int equipped, Func<int, bool> change) where T : class
        {
            bool hasChoices = false;
            foreach (T option in options)
            {
                if (option == null) { continue; }
                int id = itemId(option);
                if (!owns(id)) { continue; }
                string name = $"loadout-{category}-{id}";
                bool selected = id == equipped;
                Button button = NewButton(name, displayName(option), () => Change(name, () => selected || change(id)));
                MarkSelected(button, selected);
                _body.Add(button);
                hasChoices = true;
            }
            if (!hasChoices) { _body.Add(new Label("所持している品がありません") { pickingMode = PickingMode.Ignore }); }
        }

        private void RenderGrowth()
        {
            if (_progression == null || !_progression.isActiveAndEnabled)
            {
                _body.Add(new Label("能力値を表示できません") { pickingMode = PickingMode.Ignore });
                return;
            }
            int needed = _progression.ExpForNextLevel;
            _body.Add(new Label(needed > 0
                ? $"Lv. {_progression.Level}　経験値 {_progression.CurrentExp}/{needed}"
                : $"Lv. {_progression.Level}") { pickingMode = PickingMode.Ignore });
            int points = _progression.AvailablePoints;
            _body.Add(new Label($"残り {points} pt") { name = "loadout-points", pickingMode = PickingMode.Ignore });
            StatModifier attributes = GameManager.Data.GetAttributes(_owner.Hash);
            foreach ((StatType stat, string title) in s_Attributes)
            {
                string name = $"loadout-stat-{stat}";
                Button button = NewButton(name, $"{title}　{AttributeValue(attributes, stat)}　+1",
                    () => Change(name, () => _progression.TryAllocatePoint(stat)));
                button.SetEnabled(points > 0);
                _body.Add(button);
            }
        }

        private static int AttributeValue(StatModifier attributes, StatType stat) => stat switch
        {
            StatType.Str => attributes.str, StatType.Dex => attributes.dex, StatType.Intel => attributes.intel,
            StatType.Vit => attributes.vit, StatType.Mnd => attributes.mnd, StatType.End => attributes.end,
            _ => 0,
        };

        private void Change(string focusName, Func<bool> change)
        {
            if (!CanEdit()) { return; }
            bool accepted = change();
            if (!CanEdit()) { Close(); return; }
            ShowStatus(accepted ? string.Empty : "変更できませんでした");
            RenderPage();
            Button next = _body.Q<Button>(focusName);
            Focus(next != null && next.enabledInHierarchy ? next : _tabs[(int)_section]);
        }

        private void ShowStatus(string text)
        {
            _status.text = text;
            _status.style.display = text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static void Focus(Button button) => button.schedule.Execute(() =>
        {
            if (button.panel != null) { button.Focus(); }
        });

        private static void MarkSelected(Button button, bool selected)
        {
            button.style.backgroundColor = selected ? new StyleColor(new Color(0.36f, 0.28f, 0.14f)) : new StyleColor(StyleKeyword.Null);
            button.style.unityFontStyleAndWeight = selected ? FontStyle.Bold : FontStyle.Normal;
        }

        private static Button NewButton(string name, string title, Action click) => new Button(click) { name = name, text = title };
    }
}
