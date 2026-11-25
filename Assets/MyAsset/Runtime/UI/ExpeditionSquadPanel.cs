using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Game.Runtime
{
    /// <summary>解放済みの種から、出発時の編成と遭遇中の増援を編集する。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class ExpeditionSquadPanel : MonoBehaviour
    {
        private const float k_RefreshInterval = 0.2f;

        private sealed class SpeciesRow
        {
            public ExpeditionSquad.Species species;
            public int quantity;
            public Label quantityLabel;
            public Button decrease;
            public Button increase;
        }

        [SerializeField] private ExpeditionSquadManager squadManager;
        [SerializeField] private ExpeditionEncounterBudget3D encounterBudget;
        [SerializeField] private bool startInReinforcementMode;

        private readonly List<SpeciesRow> _rows = new List<SpeciesRow>();
        private readonly List<ExpeditionSquad.Species> _unlocked = new List<ExpeditionSquad.Species>();
        private readonly List<ExpeditionSquad.Selection> _sortieDraft = new List<ExpeditionSquad.Selection>();
        private readonly List<ExpeditionSquad.Selection> _reinforcementDraft = new List<ExpeditionSquad.Selection>();
        private UIDocument _document;
        private VisualElement _root;
        private VisualElement _panel;
        private ScrollView _species;
        private VisualElement _modes;
        private Button _sortieMode;
        private Button _reinforcementMode;
        private VisualElement _presetControls;
        private DropdownField _presetSlot;
        private Button _loadPreset;
        private Button _savePreset;
        private Button _close;
        private Label _count;
        private Label _cost;
        private Label _status;
        private Button _confirm;
        private ExpeditionSquad _squad;
        private ExpeditionDeckPresets _presets;
        private ExpeditionEncounterBudget3D _budget;
        private object _attempt;
        private IReadOnlyList<ExpeditionSquad.Selection> _confirmed;
        private int _presetIndex;
        private int _slot = -1;
        private ulong _loadRevision;
        private Scene _scene;
        private int _totalCount;
        private int _totalCost;
        private int _viewRevision;
        private bool _hasSnapshot;
        private bool _reinforcing;
        private bool _canDeploy;
        private bool _measured;
        private float _nextRefresh;

        private List<ExpeditionSquad.Selection> Draft => _reinforcing ? _reinforcementDraft : _sortieDraft;
        private int CountLimit => _reinforcing ? _budget.ConfiguredAdditionalCountLimit : _squad.MaxCount;
        private int CostLimit => _reinforcing ? _budget.ConfiguredAdditionalCostLimit : _squad.MaxCost;

        private void Awake() => _document = GetComponent<UIDocument>();

        private void OnEnable() => Refresh();

        private void OnDisable() => UnbindDocument();

        private void LateUpdate()
        {
            if (Time.unscaledTime >= _nextRefresh || !ReferenceEquals(CurrentRoot(), _root))
            {
                Refresh();
            }
        }

        /// <summary>編成の設定、解放、確定と保存スロットの変更を表示に反映する。</summary>
        public void Refresh()
        {
            if (!isActiveAndEnabled) { return; }
            _nextRefresh = Time.unscaledTime + k_RefreshInterval;
            if (!BindDocument()) { return; }

            ExpeditionSquadManager manager = ResolveManager();
            ExpeditionSquad squad = manager != null ? manager.Squad : null;
            SaveManager saves = GameManager.Saves;
            int slot = saves?.ActiveSlotIndex ?? -1;
            ulong loadRevision = saves?.LoadRevision ?? 0;
            Scene scene = SceneManager.GetActiveScene();
            ExpeditionEncounterBudget3D budget = encounterBudget != null && encounterBudget.SupportsReinforcements
                && encounterBudget.gameObject.scene == scene ? encounterBudget : null;
            object attempt = budget != null ? budget.CurrentAttempt : null;
            bool canDeploy = budget != null && budget.CanDeployReinforcements;
            bool scopeChanged = !_hasSnapshot || squad != _squad || slot != _slot
                || loadRevision != _loadRevision || scene != _scene;
            bool confirmedChanged = !ReferenceEquals(squad?.Confirmed, _confirmed);
            bool budgetChanged = !ReferenceEquals(budget, _budget);
            bool attemptChanged = !ReferenceEquals(attempt, _attempt);
            bool reinforcing = budget != null && (scopeChanged ? startInReinforcementMode : _reinforcing);
            bool totalsChanged = reinforcing && canDeploy != _canDeploy;

            if (scopeChanged) { _presetIndex = 0; }
            if (scopeChanged || confirmedChanged) { CopyDraft(squad?.Confirmed, _sortieDraft); }
            if (scopeChanged || budgetChanged || (attemptChanged && _attempt != null))
            {
                _reinforcementDraft.Clear();
            }

            _unlocked.Clear();
            if (squad != null) { _unlocked.AddRange(squad.GetUnlockedSpecies()); }
            bool rebuild = scopeChanged || reinforcing != _reinforcing || !RowsMatchUnlocked()
                || (reinforcing ? budgetChanged || attemptChanged : confirmedChanged);

            _hasSnapshot = true;
            _squad = squad;
            _presets = manager != null ? manager.Presets : null;
            _budget = budget;
            _attempt = attempt;
            _canDeploy = canDeploy;
            _reinforcing = reinforcing;
            _confirmed = squad?.Confirmed;
            _slot = slot;
            _loadRevision = loadRevision;
            _scene = scene;
            UpdateModeControls();
            if (rebuild)
            {
                _viewRevision++;
                RebuildRows(Draft);
            }
            if (rebuild || totalsChanged) { UpdateTotals(); }
            UpdatePresetControls();
        }

        public void Close() => enabled = false;

        private ExpeditionSquadManager ResolveManager()
        {
            ExpeditionSquadManager manager = squadManager != null
                ? squadManager : GameManager.GetSubManager<ExpeditionSquadManager>();
            return manager != null && manager.isActiveAndEnabled ? manager : null;
        }

        private VisualElement CurrentRoot() => _document != null && _document.isActiveAndEnabled
            ? _document.rootVisualElement : null;

        private bool BindDocument()
        {
            VisualElement root = CurrentRoot();
            if (ReferenceEquals(root, _root)) { return _confirm != null; }
            UnbindDocument();
            _root = root;
            _panel = root?.Q("expedition-squad");
            _species = _panel?.Q<ScrollView>("expedition-species");
            _count = _panel?.Q<Label>("expedition-count");
            _cost = _panel?.Q<Label>("expedition-cost");
            _status = _panel?.Q<Label>("expedition-status");
            _confirm = _panel?.Q<Button>("expedition-confirm");
            _close = _panel?.Q<Button>("expedition-close");
            _modes = _panel?.Q("expedition-modes");
            _sortieMode = _modes?.Q<Button>("expedition-sortie");
            _reinforcementMode = _modes?.Q<Button>("expedition-reinforcements");
            _presetControls = _panel?.Q("expedition-presets");
            _presetSlot = _presetControls?.Q<DropdownField>("expedition-preset-slot");
            _loadPreset = _presetControls?.Q<Button>("expedition-preset-load");
            _savePreset = _presetControls?.Q<Button>("expedition-preset-save");
            if (_species == null || _count == null || _cost == null || _status == null || _confirm == null)
            {
                _confirm = null;
                return false;
            }
            _panel.style.display = DisplayStyle.Flex;
            _confirm.clicked += Confirm;
            if (_close != null) { _close.clicked += Close; }
            if (_sortieMode != null) { _sortieMode.clicked += SelectSortie; }
            if (_reinforcementMode != null) { _reinforcementMode.clicked += SelectReinforcements; }
            if (_presetSlot != null && _loadPreset != null && _savePreset != null)
            {
                _presetSlot.RegisterValueChangedCallback(SelectPreset);
                _loadPreset.clicked += LoadPreset;
                _savePreset.clicked += SavePreset;
            }
            return true;
        }

        private void UnbindDocument()
        {
            if (_confirm != null) { _confirm.clicked -= Confirm; }
            if (_close != null) { _close.clicked -= Close; }
            if (_sortieMode != null) { _sortieMode.clicked -= SelectSortie; }
            if (_reinforcementMode != null) { _reinforcementMode.clicked -= SelectReinforcements; }
            _presetSlot?.UnregisterValueChangedCallback(SelectPreset);
            if (_loadPreset != null) { _loadPreset.clicked -= LoadPreset; }
            if (_savePreset != null) { _savePreset.clicked -= SavePreset; }
            if (_panel != null) { _panel.style.display = DisplayStyle.None; }
            _species?.Clear();
            _root = null;
            _panel = null;
            _species = null;
            _count = null;
            _cost = null;
            _status = null;
            _confirm = null;
            _close = null;
            _modes = null;
            _sortieMode = null;
            _reinforcementMode = null;
            _presetControls = null;
            _presetSlot = null;
            _loadPreset = null;
            _savePreset = null;
            _squad = null;
            _presets = null;
            _budget = null;
            _attempt = null;
            _confirmed = null;
            _slot = -1;
            _hasSnapshot = false;
            _viewRevision++;
            _rows.Clear();
            _sortieDraft.Clear();
            _reinforcementDraft.Clear();
        }

        private static void CopyDraft(IReadOnlyList<ExpeditionSquad.Selection> source, List<ExpeditionSquad.Selection> draft)
        {
            draft.Clear();
            if (source != null) { draft.AddRange(source); }
        }

        private bool RowsMatchUnlocked()
        {
            if (_rows.Count != _unlocked.Count) { return false; }
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].species.SpeciesId != _unlocked[i].SpeciesId
                    || _rows[i].species.CostPerUnit != _unlocked[i].CostPerUnit)
                {
                    return false;
                }
            }
            return true;
        }

        private void RebuildRows(IReadOnlyList<ExpeditionSquad.Selection> source)
        {
            _species.Clear();
            _rows.Clear();
            foreach (ExpeditionSquad.Species species in _unlocked)
            {
                SpeciesRow row = new SpeciesRow { species = species, quantity = FindCount(source, species.SpeciesId) };
                VisualElement item = new VisualElement { name = "expedition-species-row" };
                item.AddToClassList("expedition-squad__row");
                Label speciesName = new Label(species.SpeciesId) { name = "expedition-species-name" };
                speciesName.AddToClassList("expedition-squad__name");
                Label unitCost = new Label($"単価 {species.CostPerUnit}") { name = "expedition-unit-cost" };
                unitCost.AddToClassList("expedition-squad__unit-cost");
                row.quantityLabel = new Label { name = "expedition-quantity" };
                row.quantityLabel.AddToClassList("expedition-squad__quantity");
                row.decrease = CreateAdjustButton("expedition-decrease", "−", "減らす", () => ChangeQuantity(row, -1));
                row.increase = CreateAdjustButton("expedition-increase", "+", "増やす", () => ChangeQuantity(row, 1));
                item.Add(speciesName);
                item.Add(unitCost);
                item.Add(row.decrease);
                item.Add(row.quantityLabel);
                item.Add(row.increase);
                _rows.Add(row);
                _species.Add(item);
            }
        }

        private static int FindCount(IReadOnlyList<ExpeditionSquad.Selection> source, string speciesId)
        {
            if (source == null) { return 0; }
            foreach (ExpeditionSquad.Selection selection in source)
            {
                if (selection.SpeciesId == speciesId) { return selection.Count; }
            }
            return 0;
        }

        private static Button CreateAdjustButton(string name, string text, string tooltip, Action change)
        {
            Button button = new Button(change) { name = name, text = text, tooltip = tooltip };
            button.AddToClassList("expedition-squad__adjust");
            return button;
        }

        private void ChangeQuantity(SpeciesRow row, int delta)
        {
            Refresh();
            if (!isActiveAndEnabled || _squad == null || !_rows.Contains(row)
                || (delta > 0 && !CanAdd(row)) || (delta < 0 && row.quantity == 0))
            {
                return;
            }
            row.quantity += delta;
            UpdateTotals("未確定");
            UpdatePresetControls();
        }

        private bool CanAdd(SpeciesRow row) => _totalCount < CountLimit
            && _totalCost <= CostLimit - row.species.CostPerUnit;

        private void UpdateTotals(string feedback = null)
        {
            List<ExpeditionSquad.Selection> draft = Draft;
            draft.Clear();
            foreach (SpeciesRow row in _rows)
            {
                row.quantityLabel.text = row.quantity.ToString();
                if (row.quantity > 0) { draft.Add(new ExpeditionSquad.Selection(row.species.SpeciesId, row.quantity)); }
            }

            _totalCount = 0;
            _totalCost = 0;
            _measured = _squad != null && _squad.TryMeasure(draft, out _totalCount, out _totalCost);
            _count.text = _squad == null ? string.Empty : $"体数 {_totalCount} / {CountLimit}";
            _cost.text = _squad == null ? string.Empty : $"コスト {_totalCost} / {CostLimit}";
            bool fits = _measured && _totalCount <= CountLimit && _totalCost <= CostLimit;
            foreach (SpeciesRow row in _rows)
            {
                row.decrease.SetEnabled(row.quantity > 0);
                row.increase.SetEnabled(_measured && CanAdd(row));
            }
            _confirm.SetEnabled(fits && _rows.Count > 0 && (!_reinforcing || (_canDeploy && _totalCount > 0)));
            _status.text = SelectionStatus(fits, feedback);
        }

        private string SelectionStatus(bool fits, string feedback)
        {
            if (_squad == null) { return "編成設定がありません"; }
            if (_unlocked.Count == 0) { return "解放済みの種がありません"; }
            if (!fits) { return "体数またはコストが上限を超えています"; }
            if (_reinforcing && !_budget.IsOpen) { return feedback ?? "増援は遭遇開始後に投入できます"; }
            if (_reinforcing && !_canDeploy) { return feedback ?? "この遭遇の増援は投入済みです"; }
            return feedback ?? string.Empty;
        }

        private void SelectSortie() => SelectMode(false);

        private void SelectReinforcements() => SelectMode(true);

        private void SelectMode(bool reinforcing)
        {
            Refresh();
            if (!isActiveAndEnabled || _confirm == null || _budget == null || _reinforcing == reinforcing) { return; }
            _reinforcing = reinforcing;
            _viewRevision++;
            UpdateModeControls();
            RebuildRows(Draft);
            UpdateTotals();
            UpdatePresetControls();
        }

        private void UpdateModeControls()
        {
            if (_modes != null) { _modes.style.display = _budget != null ? DisplayStyle.Flex : DisplayStyle.None; }
            _sortieMode?.EnableInClassList("expedition-squad__mode--active", !_reinforcing);
            _reinforcementMode?.EnableInClassList("expedition-squad__mode--active", _reinforcing);
            _confirm.text = _reinforcing ? "増援を投入" : "編成を確定";
        }

        private void UpdatePresetControls()
        {
            if (_presetControls == null || _presetSlot == null || _loadPreset == null || _savePreset == null) { return; }
            int capacity = _presets?.Capacity ?? 0;
            _presetControls.style.display = capacity > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (capacity <= 0) { return; }

            _presetIndex = Mathf.Clamp(_presetIndex, 0, capacity - 1);
            List<string> choices = _presetSlot.choices;
            choices.Clear();
            for (int index = 0; index < capacity; index++)
            {
                choices.Add(_presets.TryGet(index, out _) ? $"枠 {index + 1}" : $"枠 {index + 1}（空）");
            }
            _presetSlot.SetValueWithoutNotify(choices[_presetIndex]);
            _loadPreset.SetEnabled(_presets.TryGet(_presetIndex, out _));
            _savePreset.SetEnabled(_measured);
        }

        private void SelectPreset(ChangeEvent<string> change)
        {
            int index = _presetSlot.choices.IndexOf(change.newValue);
            if (index < 0) { return; }
            _presetIndex = index;
            UpdatePresetControls();
        }

        private void LoadPreset()
        {
            int revision = _viewRevision;
            Refresh();
            if (revision != _viewRevision || _squad == null || _presets == null
                || !_presets.TryGet(_presetIndex, out IReadOnlyList<ExpeditionSquad.Selection> selection)) { return; }
            if (!_squad.TryMeasure(selection, out _, out _))
            {
                _status.text = "プリセットの解放済みの種を確認してください";
                return;
            }

            _viewRevision++;
            RebuildRows(selection);
            UpdateTotals($"枠 {_presetIndex + 1} を読み込みました");
            UpdatePresetControls();
        }

        private void SavePreset()
        {
            int revision = _viewRevision;
            Refresh();
            if (revision != _viewRevision || _presets == null || !_savePreset.enabledInHierarchy) { return; }
            if (!_presets.TrySave(_presetIndex, Draft))
            {
                _status.text = "プリセットを保存できませんでした";
                return;
            }

            UpdateTotals($"枠 {_presetIndex + 1} に保存しました");
            UpdatePresetControls();
        }

        private void Confirm()
        {
            int revision = _viewRevision;
            Refresh();
            if (!isActiveAndEnabled || _squad == null || revision != _viewRevision || _confirm == null
                || !_confirm.enabledInHierarchy)
            {
                return;
            }

            if (_reinforcing)
            {
                bool deployed = _budget.TryDeployReinforcements(Draft);
                Refresh();
                if (isActiveAndEnabled && revision == _viewRevision)
                {
                    _status.text = deployed ? "増援を投入しました" : "増援を投入できませんでした";
                }
                return;
            }

            bool accepted = _squad.TryConfirm(Draft);
            if (accepted) { _confirmed = _squad.Confirmed; }
            _status.text = accepted ? "編成を確定しました" : "解放済みの種と上限を確認してください";
        }
    }
}
