using System;
using System.IO;
using Game.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Game.Runtime
{
    [DefaultExecutionOrder(-50)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class ExpeditionNextPlaythroughPanel : MonoBehaviour
    {
        private readonly struct SaveContext
        {
            public readonly SaveManager Saves;
            public readonly int Slot;
            public readonly ulong LoadRevision;
            public readonly int Cycle;

            public SaveContext(SaveManager saves, ExpeditionPlaythrough playthrough)
            {
                Saves = saves;
                Slot = saves.ActiveSlotIndex;
                LoadRevision = saves.LoadRevision;
                Cycle = playthrough.CycleNumber;
            }

            public bool IsCurrent => Saves != null && GameManager.Saves == Saves && Saves.ActiveSlotIndex == Slot
                && Saves.LoadRevision == LoadRevision && GameManager.Playthrough?.CycleNumber == Cycle;
        }

        [SerializeField] private EncounterSession3D clearEncounter;
        [SerializeField] private string startScenePath;
        [SerializeField] private string saveFolder;
        [SerializeField] private bool restoreOnStart;
        [SerializeField] private string[] carryGlobalFlags = Array.Empty<string>();

        private UIDocument _document;
        private VisualElement _root;
        private VisualElement _panel;
        private Label _number;
        private Label _status;
        private Button _open;
        private Button _confirm;
        private Button _cancel;
        private SaveDataStore _store;
        private SaveContext _confirmation;
        private SaveContext _resumeContext;
        private Scene _loadOrigin;
        private string _loadingScenePath;
        private AsyncOperation _sceneLoad;
        private string _statusText = string.Empty;
        private int _shownCycle;
        private bool _confirming;
        private bool _busy;
        private bool _resumePending;

        private SaveDataStore Store => _store ??= new SaveDataStore(string.IsNullOrWhiteSpace(saveFolder)
            ? Application.persistentDataPath : Path.Combine(Application.persistentDataPath, saveFolder));

        internal void SetStoreForTest(SaveDataStore store) => _store = store;

        private void Awake() => _document = GetComponent<UIDocument>();

        private void OnEnable()
        {
            if (clearEncounter != null) { clearEncounter.Ended += HandleEncounterEnded; }
            Refresh();
        }

        private void Start()
        {
            SaveManager saves = GameManager.Saves;
            if (!restoreOnStart || gameObject.scene != SceneManager.GetActiveScene()
                || saves == null || saves.LastLoadedSlot != null || GameManager.NextPlaythrough == null) { return; }

            try
            {
                GameManager.NextPlaythrough.TryRestoreStartSlot(saves.ActiveSlotIndex, gameObject.scene.path, Store);
            }
            catch (Exception)
            {
                _statusText = "周回データを復元できませんでした。";
            }
            Refresh();
        }

        private void OnDisable()
        {
            if (clearEncounter != null) { clearEncounter.Ended -= HandleEncounterEnded; }
            if (_confirming) { _statusText = string.Empty; }
            _confirming = false;
            UnbindDocument();
        }

        private void LateUpdate()
        {
            if (_sceneLoad != null && _sceneLoad.isDone)
            {
                _sceneLoad = null;
                _busy = false;
                Scene arrived = SceneManager.GetActiveScene();
                if (arrived != _loadOrigin && arrived.isLoaded && arrived.path == _loadingScenePath)
                {
                    _resumePending = false;
                    _statusText = string.Empty;
                }
                else
                {
                    _statusText = "保存しました。開始シーンを開き直せます。";
                }
            }
            Refresh();
        }

        private void HandleEncounterEnded(ChallengeState state)
        {
            if (state == ChallengeState.Completed) { MarkCampaignCleared(); }
        }

        public void MarkCampaignCleared()
        {
            GameManager.NextPlaythrough?.MarkCleared();
            Refresh();
        }

        private void Open()
        {
            if (_busy || !TryCapture(out SaveContext context) || GameManager.NextPlaythrough == null) { return; }
            if (_resumePending && _resumeContext.IsCurrent)
            {
                LoadStartScene(GameManager.NextPlaythrough.StartScenePath);
                Refresh();
                return;
            }
            if (!GameManager.NextPlaythrough.CanBeginNext) { return; }

            _confirmation = context;
            _confirming = true;
            _statusText = "次の周回を開始しますか？";
            Refresh();
        }

        private void Cancel()
        {
            if (_busy || !_confirming) { return; }
            _confirming = false;
            _statusText = string.Empty;
            Refresh();
        }

        private void Confirm()
        {
            if (_busy || !_confirming) { return; }
            _confirming = false;
            if (!_confirmation.IsCurrent || GameManager.NextPlaythrough == null || !GameManager.NextPlaythrough.CanBeginNext)
            {
                _statusText = "保存状態が変わりました。もう一度選び直してください。";
                Refresh();
                return;
            }
            if (!TryGetBuildIndex(startScenePath, out _))
            {
                _statusText = "開始シーンを開けません。設定を確認してください。";
                Refresh();
                return;
            }

            _busy = true;
            bool committed;
            try
            {
                committed = GameManager.NextPlaythrough.TryBeginNextPlaythrough(_confirmation.Slot,
                    _confirmation.LoadRevision, _confirmation.Cycle, startScenePath, Store, carryGlobalFlags);
            }
            catch (Exception)
            {
                committed = false;
            }
            _busy = false;

            string destination = GameManager.NextPlaythrough?.StartScenePath;
            if (!TryCapture(out SaveContext next) || next.Saves != _confirmation.Saves
                || next.Slot != _confirmation.Slot || next.Cycle != (long)_confirmation.Cycle + 1
                || destination != startScenePath)
            {
                _statusText = "次の周回を保存できませんでした。もう一度お試しください。";
                Refresh();
                return;
            }

            _resumeContext = next;
            _resumePending = true;
            if (committed) { LoadStartScene(destination); }
            else { _statusText = "保存しました。開始シーンを開き直せます。"; }
            Refresh();
        }

        private void LoadStartScene(string path)
        {
            if (TryGetBuildIndex(path, out int index))
            {
                _loadOrigin = SceneManager.GetActiveScene();
                _loadingScenePath = path;
                try
                {
                    _sceneLoad = SceneManager.LoadSceneAsync(index, LoadSceneMode.Single);
                }
                catch (Exception)
                {
                    _sceneLoad = null;
                }
            }

            _busy = _sceneLoad != null;
            _statusText = _busy ? "開始シーンを読み込んでいます。" : "保存しました。開始シーンを開き直せます。";
        }

        private static bool TryGetBuildIndex(string path, out int index)
        {
            index = !string.IsNullOrWhiteSpace(path) ? SceneUtility.GetBuildIndexByScenePath(path) : -1;
            return index >= 0 && Application.CanStreamedLevelBeLoaded(index);
        }

        private static bool TryCapture(out SaveContext context)
        {
            SaveManager saves = GameManager.Saves;
            ExpeditionPlaythrough playthrough = GameManager.Playthrough;
            context = default;
            if (saves == null || playthrough == null) { return false; }
            context = new SaveContext(saves, playthrough);
            return true;
        }

        private void Refresh()
        {
            if (!isActiveAndEnabled || !BindDocument()) { return; }
            if (_confirming && !_confirmation.IsCurrent)
            {
                _confirming = false;
                _statusText = "保存状態が変わりました。もう一度選び直してください。";
            }
            if (_resumePending && !_resumeContext.IsCurrent) { _resumePending = false; }

            int cycle = GameManager.Playthrough?.CycleNumber ?? 0;
            _panel.style.display = cycle > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (cycle != _shownCycle)
            {
                _shownCycle = cycle;
                _number.text = $"第{cycle}周";
            }
            bool showOpen = !_confirming && (_resumePending || (GameManager.NextPlaythrough?.CanBeginNext ?? false));
            _open.text = _resumePending ? "開始シーンを開く" : "次の周回へ";
            _open.style.display = showOpen ? DisplayStyle.Flex : DisplayStyle.None;
            _open.SetEnabled(!_busy);
            _confirm.style.display = _confirming ? DisplayStyle.Flex : DisplayStyle.None;
            _cancel.style.display = _confirming ? DisplayStyle.Flex : DisplayStyle.None;
            _confirm.SetEnabled(!_busy);
            _cancel.SetEnabled(!_busy);
            _status.text = _statusText;
            _status.style.display = string.IsNullOrEmpty(_statusText) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private bool BindDocument()
        {
            VisualElement root = _document != null && _document.isActiveAndEnabled ? _document.rootVisualElement : null;
            if (ReferenceEquals(root, _root)) { return _panel != null; }
            UnbindDocument();
            if (root == null) { return false; }

            _root = root;
            _panel = new VisualElement { name = "next-cycle-panel" };
            _panel.style.position = Position.Absolute;
            _panel.style.top = 16f;
            _panel.style.right = 16f;
            _panel.style.minWidth = 176f;
            _panel.style.paddingTop = _panel.style.paddingBottom = 8f;
            _panel.style.paddingLeft = _panel.style.paddingRight = 10f;
            _panel.style.backgroundColor = new Color(0.10f, 0.14f, 0.20f, 0.95f);
            _panel.style.color = Color.white;
            _number = new Label { name = "next-cycle-number" };
            _number.style.fontSize = 16f;
            _number.style.unityFontStyleAndWeight = FontStyle.Bold;
            _status = new Label { name = "next-cycle-status" };
            _status.style.whiteSpace = WhiteSpace.Normal;
            _status.style.maxWidth = 248f;
            _open = new Button(Open) { name = "next-cycle-open" };
            _confirm = new Button(Confirm) { name = "next-cycle-confirm", text = "開始" };
            _cancel = new Button(Cancel) { name = "next-cycle-cancel", text = "戻る" };
            _panel.Add(_number);
            _panel.Add(_status);
            _panel.Add(_open);
            _panel.Add(_confirm);
            _panel.Add(_cancel);
            _root.Add(_panel);
            return true;
        }

        private void UnbindDocument()
        {
            if (_open != null) { _open.clicked -= Open; }
            if (_confirm != null) { _confirm.clicked -= Confirm; }
            if (_cancel != null) { _cancel.clicked -= Cancel; }
            _panel?.RemoveFromHierarchy();
            _root = _panel = null;
            _number = _status = null;
            _open = _confirm = _cancel = null;
            _shownCycle = 0;
        }
    }
}
