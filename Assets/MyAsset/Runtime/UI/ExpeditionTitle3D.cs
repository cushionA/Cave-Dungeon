using System;
using System.Collections.Generic;
using System.IO;
using Game.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Game.Runtime
{
    /// <summary>空きスロットで探索を始め、保存済みのスロットを対応するシーンから再開する。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class ExpeditionTitle3D : MonoBehaviour
    {
        [SerializeField] private string newGameScenePath;
        [SerializeField] private string[] supportedScenePaths = Array.Empty<string>();
        [SerializeField] private string saveFolder = "ExpeditionMock3D";
        [SerializeField, Tooltip("遠征のロード中に停止する、タイトル側の入力と音声のコンポーネント。")]
        private Behaviour[] transitionPresentation = Array.Empty<Behaviour>();

        private const string k_OpenFailed = "このスロットを開けませんでした。";
        private const string k_SlotChanged = "セーブが変更されました。スロットを選び直してください。";

        private readonly List<Behaviour> _suspendedPresentation = new();
        private SaveDataStore _store;
        private Label _status;
        private Button[] _buttons;
        private bool _busy;
        private bool _cancelled;
        private bool _retiring;
        private GameManager _retiringManager;
        private string _failure;
        private Scene _sourceScene;
        private int _requestedSlot;
        private string _requestedScene;
        private ExpeditionSlot _requestedSave;

        private SaveDataStore Store => _store ??= new SaveDataStore(string.IsNullOrWhiteSpace(saveFolder)
            ? Application.persistentDataPath : Path.Combine(Application.persistentDataPath, saveFolder));

        private void Start() => BuildPanel();

        private void OnEnable()
        {
            if (_buttons != null && !_busy) { BuildPanel(); }
        }

        private void OnDisable()
        {
            // 加算ロードは止められない。完了通知で読み込んだシーンを破棄する。
            if (_busy) { _cancelled = true; }
        }

        private void Update()
        {
            if (!_retiring || _retiringManager != null) { return; }
            _retiring = false;
            FinishLaunchFailure();
        }

        private void BuildPanel()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            if (root == null) { return; }
            root.Clear();
            root.style.flexGrow = 1f;
            root.style.alignItems = Align.Center;
            root.style.justifyContent = Justify.Center;
            root.style.color = new Color(0.92f, 0.94f, 0.97f);

            var panel = new VisualElement();
            panel.style.width = new Length(90f, LengthUnit.Percent);
            panel.style.maxWidth = 560;
            panel.style.paddingLeft = 28;
            panel.style.paddingRight = 28;
            panel.style.paddingTop = 24;
            panel.style.paddingBottom = 20;
            panel.style.backgroundColor = new Color(0.065f, 0.09f, 0.135f, 0.97f);
            root.Add(panel);

            var heading = new Label("Cave Dungeon");
            heading.style.fontSize = 32;
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.style.marginBottom = 20;
            panel.Add(heading);

            _buttons = new Button[SaveManager.k_MaxSlots];
            Button first = null;
            for (int slot = 0; slot < _buttons.Length; slot++)
            {
                int index = slot;
                ExpeditionSlot saved = ExpeditionLaunch.ReadSlot(Store, slot, SupportsScene);
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.justifyContent = Justify.SpaceBetween;
                row.style.marginBottom = 10;
                row.Add(new Label(SlotText(slot, saved)));

                bool empty = saved.Status == ExpeditionSlotStatus.Empty;
                var button = new Button(() => Launch(index, empty))
                {
                    name = $"expedition-slot-{slot}",
                    text = empty ? "はじめから" : "つづきから",
                };
                button.style.minWidth = 120;
                button.style.minHeight = 38;
                button.SetEnabled(empty ? SupportsScene(newGameScenePath) : saved.Status == ExpeditionSlotStatus.Ready);
                row.Add(button);
                panel.Add(row);
                _buttons[slot] = button;
                if (first == null && button.enabledSelf) { first = button; }
            }

            _status = new Label { name = "expedition-title-status" };
            _status.style.minHeight = 20;
            panel.Add(_status);
            if (first != null) { first.schedule.Execute(first.Focus); }
        }

        private static string SlotText(int index, ExpeditionSlot saved) => saved.Status switch
        {
            ExpeditionSlotStatus.Ready => $"{index + 1}　 第{saved.CycleNumber}周　{saved.Timestamp}",
            ExpeditionSlotStatus.Empty => $"{index + 1}　 空きスロット",
            _ => $"{index + 1}　 再開できないセーブ",
        };

        private void Launch(int slot, bool newGame)
        {
            if (_busy || gameObject.scene != SceneManager.GetActiveScene()) { return; }
            if (GameManager.Instance != null)
            {
                _status.text = "探索中のゲームを終了してから開いてください。";
                return;
            }

            ExpeditionSlot saved = ExpeditionLaunch.ReadSlot(Store, slot, SupportsScene);
            ExpeditionSlotStatus expected = newGame ? ExpeditionSlotStatus.Empty : ExpeditionSlotStatus.Ready;
            string scenePath = newGame ? newGameScenePath : saved.ScenePath;
            if (saved.Status != expected || !SupportsScene(scenePath) || SceneManager.GetSceneByPath(scenePath).isLoaded)
            {
                _failure = k_OpenFailed;
                FinishLaunchFailure();
                return;
            }

            _busy = true;
            _cancelled = false;
            _failure = k_OpenFailed;
            _sourceScene = gameObject.scene;
            _requestedSlot = slot;
            _requestedScene = scenePath;
            _requestedSave = saved;
            foreach (Button button in _buttons) { button.SetEnabled(false); }
            _status.text = "読み込み中…";
            SuspendPresentation();
            SceneManager.sceneLoaded += EnterLoadedWorld;
            try
            {
                if (SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Additive) != null) { return; }
            }
            catch (Exception exception) { Debug.LogError($"[ExpeditionTitle3D] シーンの読み込みに失敗しました: {exception.Message}", this); }
            SceneManager.sceneLoaded -= EnterLoadedWorld;
            _requestedScene = null;
            FinishLaunchFailure();
        }

        private void EnterLoadedWorld(Scene world, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Additive || !string.Equals(world.path, _requestedScene, StringComparison.Ordinal)) { return; }
            SceneManager.sceneLoaded -= EnterLoadedWorld;
            _requestedScene = null;

            GameManager manager = GameManager.Instance;
            try
            {
                if (this != null && !_cancelled && _sourceScene.isLoaded && TryEnterWorld(world))
                {
                    SceneManager.UnloadSceneAsync(_sourceScene);
                    return;
                }
            }
            catch (Exception exception) { Debug.LogError($"[ExpeditionTitle3D] 遠征を開始できませんでした: {exception.Message}", this); }

            if (_sourceScene.isLoaded) { SceneManager.SetActiveScene(_sourceScene); }
            AsyncOperation unload = SceneManager.UnloadSceneAsync(world);
            if (unload != null) { unload.completed += _ => RetireFailedWorld(manager); }
            else { RetireFailedWorld(manager); }
        }

        private bool TryEnterWorld(Scene world)
        {
            SaveManager saves = GameManager.Saves;
            if (saves == null || saves.LoadRevision != 0 || !SceneManager.SetActiveScene(world)
                || _cancelled || GameManager.Saves != saves || saves.LoadRevision != 0) { return false; }
            if (!ExpeditionLaunch.TryReadUnchangedSlot(Store, _requestedSlot, SupportsScene, _requestedSave, out ExpeditionSlot current))
            {
                _failure = k_SlotChanged;
                return false;
            }

            // 遭遇の Start より前に、選ばれたスロットを新しいシーンへ適用する。
            return ApplyRequestedSlot(world, saves, current);
        }

        private bool ApplyRequestedSlot(Scene world, SaveManager saves, ExpeditionSlot current)
        {
            ExpeditionSavePoint3D matchingPoint = null;
            foreach (GameObject root in world.GetRootGameObjects())
            {
                foreach (ExpeditionSavePoint3D point in root.GetComponentsInChildren<ExpeditionSavePoint3D>(true))
                {
                    point.UseSaveStore(Store);
                    if (current.SavePointId == null
                        || !string.Equals(point.SavePointId, current.SavePointId, StringComparison.Ordinal)) { continue; }
                    if (matchingPoint != null) { return false; }
                    matchingPoint = point;
                }
                foreach (ExpeditionNextPlaythroughPanel panel in root.GetComponentsInChildren<ExpeditionNextPlaythroughPanel>(true))
                {
                    panel.UseSaveStore(Store);
                }
            }

            saves.SetActiveSlot(_requestedSlot);
            if (saves.ActiveSlotIndex != _requestedSlot) { return false; }
            if (current.Status == ExpeditionSlotStatus.Empty) { return true; }
            if (current.SavePointId == null) { return ExpeditionLaunch.TryRestoreStart(saves, current, world.path); }
            return matchingPoint != null && matchingPoint.TryRestoreFromTitle(saves, current);
        }

        private void RetireFailedWorld(GameManager manager)
        {
            if (manager != null) { Destroy(manager.gameObject); }
            if (this == null) { RestorePresentation(); }
            else
            {
                _retiring = true;
                _retiringManager = manager;
            }
        }

        private void FinishLaunchFailure()
        {
            RestorePresentation();
            _busy = false;
            _requestedSave = default;
            BuildPanel();
            if (_status != null) { _status.text = _cancelled ? string.Empty : _failure; }
        }

        private void SuspendPresentation()
        {
            foreach (Behaviour behaviour in transitionPresentation)
            {
                if (behaviour == null || behaviour == this || !behaviour.isActiveAndEnabled
                    || behaviour.gameObject.scene != _sourceScene) { continue; }
                _suspendedPresentation.Add(behaviour);
                behaviour.enabled = false;
            }
        }

        private void RestorePresentation()
        {
            for (int i = _suspendedPresentation.Count - 1; i >= 0; i--)
            {
                Behaviour behaviour = _suspendedPresentation[i];
                if (behaviour != null && behaviour.gameObject.scene == _sourceScene && _sourceScene.isLoaded)
                {
                    behaviour.enabled = true;
                }
            }
            _suspendedPresentation.Clear();
        }

        private bool SupportsScene(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || supportedScenePaths == null
                || !Array.Exists(supportedScenePaths, candidate => string.Equals(candidate, path, StringComparison.Ordinal)))
            {
                return false;
            }
            int index = SceneUtility.GetBuildIndexByScenePath(path);
            return index >= 0 && Application.CanStreamedLevelBeLoaded(index);
        }

#if UNITY_INCLUDE_TESTS
        internal void SetStoreForTest(SaveDataStore store) => _store = store;
#endif
    }
}
