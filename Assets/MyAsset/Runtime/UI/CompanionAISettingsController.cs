using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Game.Core;

namespace Game.Runtime
{
    /// <summary>
    /// 仲間AI設定画面のMonoBehaviourコントローラ。
    /// CompanionAISettingsLogic をラップし、UXML要素のバインドとイベントハンドリングを担う。
    /// ToolTip はカスタム実装（パッド操作時のフォーカスにも反応する）。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public partial class CompanionAISettingsController : MonoBehaviour
    {
        [Header("Style Sheet (explicit load)")]
        [SerializeField] private StyleSheet _styleSheet;

        [Header("Registries (optional injection)")]
        [SerializeField] private bool _useSharedRegistries = false;

        [Header("Attack Catalog (Attack/Cast の候補元)")]
        [Tooltip("仲間が使える AttackInfo の一覧。Attack/Cast の行動ピッカーで参照される。")]
        [SerializeField] private AttackInfo[] _attackCatalog;

        [Header("Item Catalog (アイテム使用 タブの候補元)")]
        [Tooltip("仲間が使えるアイテムの一覧（UI用プレースホルダー）。将来 ItemInfo テーブルに差し替え。")]
        [SerializeField] private CompanionItemEntry[] _itemCatalog;

        [Header("Action Type Registry (任意注入)")]
        [Tooltip("解放済みアクションを参照するレジストリ。null の場合はデフォルト（全 Instant/Sustained を許可）で動作")]
        [SerializeField] private bool _useActionTypeRegistry = false;

        // 純ロジック
        private CompanionAISettingsLogic _logic;
        private ModePresetRegistry _modeRegistry;
        private TacticalPresetRegistry _tacticalRegistry;
        private ActionTypeRegistry _actionTypeRegistry;

        // UI参照
        private UIDocument _uiDocument;
        private VisualElement _root;
        private Button _backButton;
        private Label _dirtyIndicator;
        private Button _tacticTabButton;
        private Button _shortcutTabButton;
        private VisualElement _tacticTab;
        private VisualElement _shortcutTab;

        // 戦術リスト
        private VisualElement _currentTacticContainer;
        private Label _presetSectionLabel;
        private ScrollView _presetScroll;
        private Button _addPresetButton;

        // エディタ
        private TextField _configNameField;
        private Button _saveButton;
        private Button _saveAsPresetButton;
        private VisualElement _modeSlotsContainer;
        private VisualElement _transitionList;

        // ショートカット
        private DropdownField[] _shortcutDropdowns = new DropdownField[k_ShortcutSlotCount];

        // ダイアログ・ツールチップ
        private VisualElement _dialogLayer;
        private VisualElement _tooltipPanel;
        private Label _tooltipText;

        // 動的ハンドラ退避用（購読解除のため）
        private readonly List<Action> _unsubscribeActions = new List<Action>();

        private const int k_ShortcutSlotCount = 4;

        // =========================================================================
        // Lifecycle
        // =========================================================================

        private void Awake()
        {
            _uiDocument = GetComponent<UIDocument>();

            if (!_useSharedRegistries || _modeRegistry == null || _tacticalRegistry == null)
            {
                _modeRegistry = new ModePresetRegistry();
                _tacticalRegistry = new TacticalPresetRegistry(_modeRegistry);
            }
            _logic = new CompanionAISettingsLogic(_modeRegistry, _tacticalRegistry);

            if (_useActionTypeRegistry && _actionTypeRegistry == null)
            {
                _actionTypeRegistry = new ActionTypeRegistry();
            }
        }

        private void OnEnable()
        {
            _root = _uiDocument.rootVisualElement;
            if (_root == null)
            {
                return;
            }

            // StyleSheet を明示ロード（UXML の <Style src> に依存しない）
            if (_styleSheet != null && !_root.styleSheets.Contains(_styleSheet))
            {
                _root.styleSheets.Add(_styleSheet);
            }

            // UXML のルート要素が画面全体を覆うよう保険を入れる（USS が効かない場合の fallback）
            VisualElement uxmlRoot = _root.Q<VisualElement>("companion-ai-settings-root");
            if (uxmlRoot != null)
            {
                uxmlRoot.style.position = Position.Absolute;
                uxmlRoot.style.left = 0;
                uxmlRoot.style.top = 0;
                uxmlRoot.style.right = 0;
                uxmlRoot.style.bottom = 0;
                uxmlRoot.style.flexDirection = FlexDirection.Column;
            }

            QueryElements();
            RegisterEventHandlers();
            RefreshAll();
        }

        private void OnDisable()
        {
            UnregisterEventHandlers();
        }

        private void OnDestroy()
        {
            _tacticalRegistry?.Dispose();
        }

        // =========================================================================
        // Query / Register
        // =========================================================================

        private void QueryElements()
        {
            _backButton = _root.Q<Button>("back-button");
            _dirtyIndicator = _root.Q<Label>("dirty-indicator");
            _tacticTabButton = _root.Q<Button>("tactic-tab-button");
            _shortcutTabButton = _root.Q<Button>("shortcut-tab-button");
            _tacticTab = _root.Q<VisualElement>("tactic-tab");
            _shortcutTab = _root.Q<VisualElement>("shortcut-tab");

            _currentTacticContainer = _root.Q<VisualElement>("current-tactic-container");
            _presetSectionLabel = _root.Q<Label>("preset-section-label");
            _presetScroll = _root.Q<ScrollView>("preset-scroll");
            _addPresetButton = _root.Q<Button>("add-preset-button");

            _configNameField = _root.Q<TextField>("config-name-field");
            _saveButton = _root.Q<Button>("save-button");
            _saveAsPresetButton = _root.Q<Button>("save-as-preset-button");
            _modeSlotsContainer = _root.Q<VisualElement>("mode-slots-container");
            _transitionList = _root.Q<VisualElement>("transition-list");

            for (int i = 0; i < k_ShortcutSlotCount; i++)
            {
                _shortcutDropdowns[i] = _root.Q<DropdownField>("shortcut-dropdown-" + i);
            }

            _dialogLayer = _root.Q<VisualElement>("dialog-layer");
            _tooltipPanel = _root.Q<VisualElement>("tooltip-panel");
            _tooltipText = _root.Q<Label>("tooltip-text");
        }

        private void RegisterEventHandlers()
        {
            RegisterClick(_backButton, OnBackClicked);
            RegisterClick(_tacticTabButton, () => SwitchTab(CompanionAISettingsLogic.TabId.TacticEdit));
            RegisterClick(_shortcutTabButton, () => SwitchTab(CompanionAISettingsLogic.TabId.Shortcut));
            RegisterClick(_addPresetButton, OnAddPresetClicked);
            RegisterClick(_saveButton, OnSaveClicked);
            RegisterClick(_saveAsPresetButton, OnSaveAsPresetClicked);

            if (_configNameField != null)
            {
                EventCallback<ChangeEvent<string>> handler = OnConfigNameChanged;
                _configNameField.RegisterValueChangedCallback(handler);
                _unsubscribeActions.Add(() => _configNameField.UnregisterValueChangedCallback(handler));
            }

            for (int i = 0; i < k_ShortcutSlotCount; i++)
            {
                int slotIndex = i;
                DropdownField dropdown = _shortcutDropdowns[i];
                if (dropdown == null)
                {
                    continue;
                }
                EventCallback<ChangeEvent<string>> handler = evt => OnShortcutChanged(slotIndex, evt.newValue);
                dropdown.RegisterValueChangedCallback(handler);
                _unsubscribeActions.Add(() => dropdown.UnregisterValueChangedCallback(handler));
            }

            // ツールチップ: 全ての tooltip 属性付き要素にフック
            AttachTooltipHandlers(_root);
        }

        private void UnregisterEventHandlers()
        {
            for (int i = 0; i < _unsubscribeActions.Count; i++)
            {
                _unsubscribeActions[i]?.Invoke();
            }
            _unsubscribeActions.Clear();
        }

        private void RegisterClick(Button button, Action action)
        {
            if (button == null || action == null)
            {
                return;
            }
            button.clicked += action;
            _unsubscribeActions.Add(() => button.clicked -= action);
        }

        // =========================================================================
        // Tooltip Handling
        // =========================================================================

        private void AttachTooltipHandlers(VisualElement root)
        {
            root.Query<VisualElement>().ForEach(element =>
            {
                if (string.IsNullOrEmpty(element.tooltip))
                {
                    return;
                }

                EventCallback<MouseEnterEvent> onMouseEnter = evt => ShowTooltip(element, element.tooltip);
                EventCallback<MouseLeaveEvent> onMouseLeave = evt => HideTooltip();
                EventCallback<FocusInEvent> onFocusIn = evt => ShowTooltip(element, element.tooltip);
                EventCallback<FocusOutEvent> onFocusOut = evt => HideTooltip();

                element.RegisterCallback(onMouseEnter);
                element.RegisterCallback(onMouseLeave);
                element.RegisterCallback(onFocusIn);
                element.RegisterCallback(onFocusOut);

                _unsubscribeActions.Add(() =>
                {
                    element.UnregisterCallback(onMouseEnter);
                    element.UnregisterCallback(onMouseLeave);
                    element.UnregisterCallback(onFocusIn);
                    element.UnregisterCallback(onFocusOut);
                });
            });
        }

        private void ShowTooltip(VisualElement anchor, string text)
        {
            if (_tooltipPanel == null || _tooltipText == null || string.IsNullOrEmpty(text))
            {
                return;
            }

            _tooltipText.text = text;
            _tooltipPanel.AddToClassList("tooltip-panel--visible");

            // アンカーの直下に表示する
            Vector2 anchorPos = anchor.worldBound.position;
            float anchorBottom = anchor.worldBound.yMax;
            _tooltipPanel.style.left = anchorPos.x;
            _tooltipPanel.style.top = anchorBottom + 4f;
        }

        private void HideTooltip()
        {
            if (_tooltipPanel == null)
            {
                return;
            }
            _tooltipPanel.RemoveFromClassList("tooltip-panel--visible");
        }

        // =========================================================================
        // Refresh
        // =========================================================================

        private void RefreshAll()
        {
            RefreshDirtyIndicator();
            RefreshTabVisibility();
            RefreshTacticList();
            RefreshEditor();
            RefreshShortcutDropdowns();
        }

        private void RefreshDirtyIndicator()
        {
            if (_dirtyIndicator == null)
            {
                return;
            }
            if (_logic.IsDirty)
            {
                _dirtyIndicator.AddToClassList("dirty-indicator--visible");
            }
            else
            {
                _dirtyIndicator.RemoveFromClassList("dirty-indicator--visible");
            }
        }

        private void RefreshTabVisibility()
        {
            bool isTactic = _logic.ActiveTab == CompanionAISettingsLogic.TabId.TacticEdit;
            if (_tacticTab != null)
            {
                _tacticTab.style.display = isTactic ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (_shortcutTab != null)
            {
                _shortcutTab.style.display = isTactic ? DisplayStyle.None : DisplayStyle.Flex;
            }
            if (_tacticTabButton != null)
            {
                if (isTactic)
                {
                    _tacticTabButton.AddToClassList("tab-button--active");
                }
                else
                {
                    _tacticTabButton.RemoveFromClassList("tab-button--active");
                }
            }
            if (_shortcutTabButton != null)
            {
                if (!isTactic)
                {
                    _shortcutTabButton.AddToClassList("tab-button--active");
                }
                else
                {
                    _shortcutTabButton.RemoveFromClassList("tab-button--active");
                }
            }
        }

        private void RefreshTacticList()
        {
            // 現在の戦術項目
            if (_currentTacticContainer != null)
            {
                _currentTacticContainer.Clear();
                Button currentItem = CreateTacticListItem(
                    string.IsNullOrEmpty(_logic.CurrentTactic.configName) ? "現在の戦術" : _logic.CurrentTactic.configName,
                    isCurrent: true,
                    isSelected: string.IsNullOrEmpty(_logic.EditingConfigId));
                currentItem.clicked += OnCurrentTacticClicked;
                _currentTacticContainer.Add(currentItem);
            }

            // プリセット一覧
            if (_presetScroll != null)
            {
                _presetScroll.Clear();
                CompanionAIConfig[] presets = _tacticalRegistry.GetAll();
                for (int i = 0; i < presets.Length; i++)
                {
                    CompanionAIConfig preset = presets[i];
                    string configId = preset.configId;
                    bool isSelected = configId == _logic.EditingConfigId;
                    Button item = CreateTacticListItem(
                        string.IsNullOrEmpty(preset.configName) ? "(無名)" : preset.configName,
                        isCurrent: false,
                        isSelected: isSelected);
                    item.clicked += () => OnPresetClicked(configId);
                    _presetScroll.Add(item);
                }
            }

            if (_presetSectionLabel != null)
            {
                _presetSectionLabel.text = $"プリセット ({_tacticalRegistry.Count}/20)";
            }
        }

        private Button CreateTacticListItem(string name, bool isCurrent, bool isSelected)
        {
            Button item = new Button { text = name };
            item.AddToClassList("tactic-list-item");
            if (isCurrent)
            {
                item.AddToClassList("tactic-list-item--current");
            }
            if (isSelected)
            {
                item.AddToClassList("tactic-list-item--selected");
            }
            item.focusable = true;
            return item;
        }

        private void RefreshEditor()
        {
            CompanionAIConfig buffer = _logic.EditingBuffer;

            // 戦術名
            if (_configNameField != null)
            {
                _configNameField.SetValueWithoutNotify(buffer.configName ?? "");
            }

            // モードスロット
            if (_modeSlotsContainer != null)
            {
                _modeSlotsContainer.Clear();
                int modeCount = buffer.modes != null ? buffer.modes.Length : 0;
                for (int i = 0; i < modeCount; i++)
                {
                    int slotIndex = i;
                    AIMode mode = buffer.modes[i];
                    Button slot = new Button { text = string.IsNullOrEmpty(mode.modeName) ? "(無名)" : mode.modeName };
                    slot.AddToClassList("mode-slot");
                    if (!string.IsNullOrEmpty(mode.modeId))
                    {
                        slot.AddToClassList("mode-slot--linked");
                        slot.tooltip = "プリセット参照あり - このモードを上書きすると参照元も全て更新されます";
                    }
                    else
                    {
                        slot.AddToClassList("mode-slot--independent");
                        slot.tooltip = "独立コピー（参照なし）";
                    }
                    slot.clicked += () => OnModeSlotClicked(slotIndex);
                    _modeSlotsContainer.Add(slot);
                }

                if (modeCount < 4)
                {
                    Button addButton = new Button { text = "＋" };
                    addButton.AddToClassList("mode-slot");
                    addButton.AddToClassList("mode-slot--add");
                    addButton.tooltip = "モードを追加";
                    addButton.clicked += OnAddModeClicked;
                    _modeSlotsContainer.Add(addButton);
                }

                AttachTooltipHandlers(_modeSlotsContainer);
            }

            // 遷移ルール
            if (_transitionList != null)
            {
                _transitionList.Clear();
                int ruleCount = buffer.modeTransitionRules != null ? buffer.modeTransitionRules.Length : 0;
                for (int i = 0; i < ruleCount; i++)
                {
                    int ruleIndex = i;
                    ModeTransitionRule rule = buffer.modeTransitionRules[i];
                    Button row = new Button(() => ShowModeTransitionDialog(ruleIndex));
                    row.text = FormatTransitionRuleSummary(rule, buffer.modes);
                    row.tooltip = "クリックで遷移ルールを編集";
                    row.AddToClassList("transition-row");
                    row.AddToClassList("transition-row--clickable");
                    _transitionList.Add(row);
                }

                Button addRuleButton = new Button(() => ShowModeTransitionDialog(-1));
                addRuleButton.text = "＋ 遷移ルールを追加";
                addRuleButton.tooltip = "新しいモード遷移ルールを追加";
                addRuleButton.AddToClassList("transition-row");
                addRuleButton.AddToClassList("transition-row--add");
                _transitionList.Add(addRuleButton);

                AttachTooltipHandlers(_transitionList);
            }
        }

        /// <summary>
        /// 遷移ルール行のサマリー表示を組み立てる。
        /// 例: "[警戒] → [攻撃] (HP < 30% AND 距離 < 5)"
        /// </summary>
        private string FormatTransitionRuleSummary(ModeTransitionRule rule, AIMode[] modes)
        {
            string sourceName = rule.sourceModeIndex < 0
                ? "任意"
                : (modes != null && rule.sourceModeIndex < modes.Length
                    ? (string.IsNullOrEmpty(modes[rule.sourceModeIndex].modeName) ? $"Mode{rule.sourceModeIndex}" : modes[rule.sourceModeIndex].modeName)
                    : $"Mode{rule.sourceModeIndex}");
            string targetName = modes != null && rule.targetModeIndex >= 0 && rule.targetModeIndex < modes.Length
                ? (string.IsNullOrEmpty(modes[rule.targetModeIndex].modeName) ? $"Mode{rule.targetModeIndex}" : modes[rule.targetModeIndex].modeName)
                : $"Mode{rule.targetModeIndex}";
            int condCount = rule.conditions != null ? rule.conditions.Length : 0;
            string condText = condCount > 0 ? $"{condCount}個の条件" : "常時";
            return $"[{sourceName}] → [{targetName}] ({condText})";
        }

        private void RefreshShortcutDropdowns()
        {
            List<string> choices = new List<string>();
            choices.Add("(未割当)");
            CompanionAIConfig[] presets = _tacticalRegistry.GetAll();
            for (int i = 0; i < presets.Length; i++)
            {
                choices.Add(string.IsNullOrEmpty(presets[i].configName) ? "(無名)" : presets[i].configName);
            }

            int[] bindings = _logic.EditingBuffer.shortcutModeBindings;
            for (int i = 0; i < k_ShortcutSlotCount; i++)
            {
                DropdownField dropdown = _shortcutDropdowns[i];
                if (dropdown == null)
                {
                    continue;
                }
                dropdown.choices = choices;
                int boundIndex = bindings != null && i < bindings.Length ? bindings[i] : 0;
                int clampedIndex = Mathf.Clamp(boundIndex + 1, 0, choices.Count - 1);
                dropdown.SetValueWithoutNotify(choices[clampedIndex]);
            }
        }

        // =========================================================================
        // Event Handlers
        // =========================================================================

        private void OnBackClicked()
        {
            if (_logic.IsDirty)
            {
                ShowUnsavedDiscardDialog(() => CloseScreen());
                return;
            }
            CloseScreen();
        }

        private void CloseScreen()
        {
            gameObject.SetActive(false);
        }

        private void SwitchTab(CompanionAISettingsLogic.TabId tab)
        {
            _logic.SwitchTab(tab);
            RefreshTabVisibility();
        }

        private void OnCurrentTacticClicked()
        {
            ShowCurrentTacticActionDialog();
        }

        private void OnPresetClicked(string configId)
        {
            ShowPresetActionDialog(configId);
        }

        private void OnAddPresetClicked()
        {
            CompanionAIConfig empty = new CompanionAIConfig
            {
                configName = "新規戦術",
                modes = new AIMode[0],
                modeTransitionRules = new ModeTransitionRule[0],
                shortcutModeBindings = new int[k_ShortcutSlotCount],
            };
            string newId = _tacticalRegistry.Save("新規戦術", empty);
            if (newId == null)
            {
                // 上限超過: ダイアログで告知
                ShowInfoDialog("戦術プリセットが上限（20個）に達しています。");
                return;
            }
            _logic.SwitchEditingTarget(newId, force: true);
            RefreshAll();
        }

        private void OnSaveClicked()
        {
            bool ok = _logic.SaveBufferToEditingPreset();
            if (!ok)
            {
                ShowInfoDialog("保存に失敗しました。");
                return;
            }
            RefreshAll();
        }

        private void OnSaveAsPresetClicked()
        {
            ShowNamingDialog(
                title: "プリセットとして保存",
                description: "この戦術を新しいプリセットとして保存します。",
                defaultValue: _logic.EditingBuffer.configName,
                onConfirm: name =>
                {
                    string id = _logic.SaveBufferAsNewPreset(name);
                    if (id == null)
                    {
                        ShowInfoDialog("戦術プリセットが上限（20個）に達しています。");
                        return;
                    }
                    RefreshAll();
                });
        }

        private void OnConfigNameChanged(ChangeEvent<string> evt)
        {
            _logic.SetEditingName(evt.newValue);
            RefreshDirtyIndicator();
        }

        private void OnShortcutChanged(int slotIndex, string value)
        {
            // choices[0] が "(未割当)" なので、-1 相当として 0 を bindingに保存する運用
            int choiceIndex = Mathf.Max(0, _shortcutDropdowns[slotIndex].index);
            int bindingValue = choiceIndex - 1; // "(未割当)" → -1
            _logic.SetShortcutBinding(slotIndex, bindingValue);
            RefreshDirtyIndicator();
        }

        private void OnAddModeClicked()
        {
            // モードプリセット一覧から選ぶダイアログを表示
            AIMode[] available = _modeRegistry.GetAll();
            if (available.Length == 0)
            {
                // 空のモードを追加
                bool ok = _logic.AddModeToBuffer(new AIMode { modeName = "新規モード", modeId = "" });
                if (!ok)
                {
                    ShowInfoDialog("モードは最大4個までです。");
                    return;
                }
                RefreshEditor();
                RefreshDirtyIndicator();
                return;
            }

            ShowModeSelectDialog("モードを追加", available, modeId =>
            {
                AIMode? selected = _modeRegistry.GetById(modeId);
                if (selected.HasValue)
                {
                    bool ok = _logic.AddModeToBuffer(selected.Value);
                    if (!ok)
                    {
                        ShowInfoDialog("モードは最大4個までです。");
                        return;
                    }
                    RefreshEditor();
                    RefreshDirtyIndicator();
                }
            });
        }

        private void OnModeSlotClicked(int slotIndex)
        {
            ShowModeSlotActionDialog(slotIndex);
        }

        // =========================================================================
        // Dialogs
        // =========================================================================

        /// <summary>
        /// ダイアログを表示する。ネスト対応: フレームでラップしてスタック追加するため、
        /// モード詳細ダイアログから行動ピッカーを開いた時に親が消えない。
        /// </summary>
        private void ShowDialog(VisualElement content)
        {
            if (_dialogLayer == null)
            {
                return;
            }
            _dialogLayer.pickingMode = PickingMode.Position;

            VisualElement frame = new VisualElement();
            frame.AddToClassList("dialog-frame");
            frame.Add(content);
            _dialogLayer.Add(frame);
            _dialogLayer.AddToClassList("dialog-layer--visible");
        }

        /// <summary>
        /// 直近のダイアログを閉じる。スタック上の前のダイアログが再びフォーカスされる。
        /// </summary>
        private void CloseDialog()
        {
            if (_dialogLayer == null)
            {
                return;
            }
            int count = _dialogLayer.childCount;
            if (count > 0)
            {
                _dialogLayer.RemoveAt(count - 1);
            }
            if (_dialogLayer.childCount == 0)
            {
                _dialogLayer.pickingMode = PickingMode.Ignore;
                _dialogLayer.RemoveFromClassList("dialog-layer--visible");
            }
        }

        private VisualElement BuildModalDialog(string title, string body)
        {
            VisualElement dialog = new VisualElement();
            dialog.AddToClassList("modal-dialog");

            Label titleLabel = new Label(title);
            titleLabel.AddToClassList("modal-dialog__title");
            dialog.Add(titleLabel);

            if (!string.IsNullOrEmpty(body))
            {
                Label bodyLabel = new Label(body);
                bodyLabel.AddToClassList("modal-dialog__body");
                dialog.Add(bodyLabel);
            }

            return dialog;
        }

        private VisualElement BuildButtonRow()
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("modal-dialog__buttons");
            return row;
        }

        private Button BuildDialogButton(string text, string classListItem, Action onClick)
        {
            Button button = new Button(() =>
            {
                CloseDialog();
                onClick?.Invoke();
            });
            button.text = text;
            button.AddToClassList(classListItem);
            return button;
        }

        private Button BuildDialogButtonNoClose(string text, string classListItem, Action onClick)
        {
            Button button = new Button(onClick);
            button.text = text;
            button.AddToClassList(classListItem);
            return button;
        }

        // --- Dialog 1: 戦術プリセット選択 ---
        private void ShowPresetActionDialog(string configId)
        {
            CompanionAIConfig? preset = _tacticalRegistry.GetById(configId);
            if (!preset.HasValue)
            {
                return;
            }

            VisualElement dialog = BuildModalDialog(preset.Value.configName ?? "プリセット", "このプリセットに対する操作を選択してください。");

            VisualElement buttons = BuildButtonRow();
            buttons.Add(BuildDialogButton("現在の戦術に適用", "primary-button", () =>
            {
                // 現在の戦術にこのプリセットの内容をコピー
                _logic.SwitchEditingTarget(configId, force: true);
                _logic.ApplyBufferToCurrentTactic();
                RefreshAll();
            }));
            buttons.Add(BuildDialogButton("編集する", "secondary-button", () =>
            {
                if (_logic.IsDirty)
                {
                    ShowUnsavedDiscardDialog(() =>
                    {
                        _logic.SwitchEditingTarget(configId, force: true);
                        RefreshAll();
                    });
                    return;
                }
                _logic.SwitchEditingTarget(configId, force: true);
                RefreshAll();
            }));
            buttons.Add(BuildDialogButton("複製", "secondary-button", () =>
            {
                string newId = _logic.DuplicatePreset(configId);
                if (newId == null)
                {
                    ShowInfoDialog("戦術プリセットが上限（20個）に達しています。");
                    return;
                }
                RefreshAll();
            }));
            buttons.Add(BuildDialogButton("削除", "danger-button", () =>
            {
                ShowDeleteConfirmDialog(preset.Value.configName, () =>
                {
                    bool ok = _logic.DeletePreset(configId);
                    if (!ok)
                    {
                        ShowInfoDialog("最後のプリセットは削除できません。");
                        return;
                    }
                    RefreshAll();
                });
            }));
            buttons.Add(BuildDialogButton("キャンセル", "secondary-button", null));

            dialog.Add(buttons);
            ShowDialog(dialog);
        }

        // --- Dialog 2: 現在の戦術アクション ---
        private void ShowCurrentTacticActionDialog()
        {
            VisualElement dialog = BuildModalDialog("現在の戦術", "現在の戦術に対する操作を選択してください。");
            VisualElement buttons = BuildButtonRow();

            buttons.Add(BuildDialogButton("編集する", "primary-button", () =>
            {
                if (_logic.IsDirty)
                {
                    ShowUnsavedDiscardDialog(() =>
                    {
                        _logic.SwitchEditingTarget(null, force: true);
                        RefreshAll();
                    });
                    return;
                }
                _logic.SwitchEditingTarget(null, force: true);
                RefreshAll();
            }));
            buttons.Add(BuildDialogButton("プリセットとして保存", "secondary-button", () =>
            {
                ShowNamingDialog(
                    title: "プリセットとして保存",
                    description: "現在の戦術を新しいプリセットとして保存します。",
                    defaultValue: "新規プリセット",
                    onConfirm: name =>
                    {
                        string id = _logic.SaveBufferAsNewPreset(name);
                        if (id == null)
                        {
                            ShowInfoDialog("戦術プリセットが上限（20個）に達しています。");
                            return;
                        }
                        RefreshAll();
                    });
            }));
            buttons.Add(BuildDialogButton("キャンセル", "secondary-button", null));

            dialog.Add(buttons);
            ShowDialog(dialog);
        }

        // --- Dialog 3: モードスロットアクション ---
        private void ShowModeSlotActionDialog(int slotIndex)
        {
            AIMode[] modes = _logic.EditingBuffer.modes;
            if (modes == null || slotIndex < 0 || slotIndex >= modes.Length)
            {
                return;
            }
            AIMode mode = modes[slotIndex];
            bool isLinked = !string.IsNullOrEmpty(mode.modeId);

            VisualElement dialog = BuildModalDialog(
                $"Mode {slotIndex + 1}: {mode.modeName}",
                isLinked ? "プリセット参照中のモードです。" : "独立コピーのモードです。");
            dialog.AddToClassList("slot-action-dialog");
            VisualElement buttons = BuildButtonRow();
            buttons.AddToClassList("slot-action-dialog__buttons");

            buttons.Add(BuildDialogButton("モード詳細を編集", "primary-button", () =>
            {
                ShowModeDetailDialog(slotIndex);
            }));
            buttons.Add(BuildDialogButton("プリセットから選択", "secondary-button", () =>
            {
                AIMode[] available = _modeRegistry.GetAll();
                if (available.Length == 0)
                {
                    ShowInfoDialog("モードプリセットが1個もありません。先に「モードプリセットとして保存」でプリセット化してください。");
                    return;
                }
                ShowModeSelectDialog("モードを選択", available, newModeId =>
                {
                    _logic.ReplaceModeFromPreset(slotIndex, newModeId);
                    RefreshEditor();
                    RefreshDirtyIndicator();
                });
            }));
            buttons.Add(BuildDialogButton("このスロットから削除", "secondary-button", () =>
            {
                _logic.RemoveModeFromBuffer(slotIndex);
                RefreshEditor();
                RefreshDirtyIndicator();
            }));
            buttons.Add(BuildDialogButton("モードプリセットとして保存", "secondary-button", () =>
            {
                ShowNamingDialog(
                    title: "モードプリセットとして保存",
                    description: "このモードを新しいモードプリセットとして保存します。",
                    defaultValue: string.IsNullOrEmpty(mode.modeName) ? "新規モード" : mode.modeName,
                    onConfirm: name =>
                    {
                        AIMode toSave = mode;
                        toSave.modeName = name;
                        string newId = _modeRegistry.Save(toSave);
                        if (newId == null)
                        {
                            ShowInfoDialog("モードプリセットが上限（40個）に達しています。");
                            return;
                        }
                        // 参照リンクを新しいプリセットに更新
                        _logic.ReplaceModeFromPreset(slotIndex, newId);
                        RefreshEditor();
                        RefreshDirtyIndicator();
                    });
            }));
            if (isLinked)
            {
                buttons.Add(BuildDialogButton("このモードだけ変更", "secondary-button", () =>
                {
                    _logic.ConvertModeToIndependent(slotIndex);
                    RefreshEditor();
                    RefreshDirtyIndicator();
                }));
            }
            buttons.Add(BuildDialogButton("キャンセル", "secondary-button", null));

            dialog.Add(buttons);
            ShowDialog(dialog);
        }

        // --- Dialog 5: 未保存変更破棄確認 ---
        private void ShowUnsavedDiscardDialog(Action onDiscard)
        {
            VisualElement dialog = BuildModalDialog(
                "未保存の変更があります",
                "現在の編集内容を破棄しますか？");
            VisualElement buttons = BuildButtonRow();

            buttons.Add(BuildDialogButton("破棄する", "danger-button", () =>
            {
                _logic.ClearDirty();
                onDiscard?.Invoke();
            }));
            Button cancel = BuildDialogButton("キャンセル", "primary-button", null);
            buttons.Add(cancel);

            dialog.Add(buttons);
            ShowDialog(dialog);

            // デフォルトフォーカスは「キャンセル」
            cancel.schedule.Execute(() => cancel.Focus()).ExecuteLater(50);
        }

        // --- Dialog 6: 削除確認 ---
        private void ShowDeleteConfirmDialog(string itemName, Action onDelete)
        {
            VisualElement dialog = BuildModalDialog(
                $"「{itemName}」を削除しますか？",
                "この操作は取り消せません。");
            VisualElement buttons = BuildButtonRow();

            buttons.Add(BuildDialogButton("削除", "danger-button", onDelete));
            Button cancel = BuildDialogButton("キャンセル", "primary-button", null);
            buttons.Add(cancel);

            dialog.Add(buttons);
            ShowDialog(dialog);

            cancel.schedule.Execute(() => cancel.Focus()).ExecuteLater(50);
        }

        // --- 汎用: 命名入力ダイアログ ---
        private void ShowNamingDialog(string title, string description, string defaultValue, Action<string> onConfirm)
        {
            VisualElement dialog = BuildModalDialog(title, description);

            TextField input = new TextField("名前");
            input.value = defaultValue ?? "";
            input.AddToClassList("modal-dialog__input");
            dialog.Add(input);

            VisualElement buttons = BuildButtonRow();
            buttons.Add(BuildDialogButton("保存", "primary-button", () =>
            {
                string inputValue = string.IsNullOrWhiteSpace(input.value) ? defaultValue : input.value;
                onConfirm?.Invoke(inputValue);
            }));
            buttons.Add(BuildDialogButton("キャンセル", "secondary-button", null));

            dialog.Add(buttons);
            ShowDialog(dialog);

            input.schedule.Execute(() => input.Focus()).ExecuteLater(50);
        }

        // --- 汎用: モード選択ダイアログ ---
        private void ShowModeSelectDialog(string title, AIMode[] modes, Action<string> onSelect)
        {
            VisualElement dialog = BuildModalDialog(title, "モードプリセットから選択してください。");

            VisualElement list = new VisualElement();
            list.AddToClassList("modal-dialog__reference-list");
            for (int i = 0; i < modes.Length; i++)
            {
                AIMode mode = modes[i];
                string modeId = mode.modeId;
                Button item = new Button(() =>
                {
                    CloseDialog();
                    onSelect?.Invoke(modeId);
                })
                {
                    text = string.IsNullOrEmpty(mode.modeName) ? "(無名)" : mode.modeName,
                };
                item.AddToClassList("secondary-button");
                list.Add(item);
            }
            dialog.Add(list);

            VisualElement buttons = BuildButtonRow();
            buttons.Add(BuildDialogButton("キャンセル", "secondary-button", null));
            dialog.Add(buttons);

            ShowDialog(dialog);
        }

        // --- 汎用: 情報ダイアログ ---
        private void ShowInfoDialog(string message)
        {
            VisualElement dialog = BuildModalDialog("お知らせ", message);
            VisualElement buttons = BuildButtonRow();
            Button ok = BuildDialogButton("OK", "primary-button", null);
            buttons.Add(ok);
            dialog.Add(buttons);
            ShowDialog(dialog);
            ok.schedule.Execute(() => ok.Focus()).ExecuteLater(50);
        }

        // =========================================================================
        // Public API (外部からの初期化・テスト用)
        // =========================================================================

        /// <summary>
        /// 外部で作成したレジストリを注入する。Awake前に呼ぶ必要がある。
        /// </summary>
        public void InjectRegistries(ModePresetRegistry modeRegistry, TacticalPresetRegistry tacticalRegistry)
        {
            _modeRegistry = modeRegistry;
            _tacticalRegistry = tacticalRegistry;
            _useSharedRegistries = true;
        }

        /// <summary>編集中のロジックインスタンス（テスト・外部参照用）。</summary>
        public CompanionAISettingsLogic Logic => _logic;
    }
}
