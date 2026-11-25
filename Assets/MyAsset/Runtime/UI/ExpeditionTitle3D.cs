using System;
using System.Collections;
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

        private SaveDataStore _store;
        private Label _status;
        private Button[] _buttons;
        private bool _busy;
        private int _requestedSlot;
        private string _requestedScene;
        private ExpeditionSlot _requestedSave;

        private SaveDataStore Store => _store ??= new SaveDataStore(string.IsNullOrWhiteSpace(saveFolder)
            ? Application.persistentDataPath : Path.Combine(Application.persistentDataPath, saveFolder));

        private void Start() => BuildPanel();

        private void OnDisable() => SceneManager.sceneLoaded -= EnterLoadedWorld;

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
                ShowFailure();
                return;
            }

            _busy = true;
            _requestedSlot = slot;
            _requestedScene = scenePath;
            _requestedSave = saved;
            foreach (Button button in _buttons) { button.SetEnabled(false); }
            _status.text = "読み込み中…";
            SceneManager.sceneLoaded += EnterLoadedWorld;
            try
            {
                if (SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Additive) != null) { return; }
            }
            catch (Exception exception) { Debug.LogError($"[ExpeditionTitle3D] シーンの読み込みに失敗しました: {exception.Message}", this); }
            SceneManager.sceneLoaded -= EnterLoadedWorld;
            ShowFailure();
        }

        private void EnterLoadedWorld(Scene world, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Additive || !string.Equals(world.path, _requestedScene, StringComparison.Ordinal)) { return; }
            SceneManager.sceneLoaded -= EnterLoadedWorld;

            Scene title = gameObject.scene;
            GameManager manager = GameManager.Instance;
            SaveManager saves = GameManager.Saves;
            if (saves != null && saves.LoadRevision == 0 && SceneManager.SetActiveScene(world) && ApplyRequestedSlot(world, saves))
            {
                // 遭遇の Start より前に、選ばれたスロットを新しいシーンへ適用する。
                SceneManager.UnloadSceneAsync(title);
                return;
            }

            SceneManager.SetActiveScene(title);
            StartCoroutine(UnloadFailedWorld(world, manager));
        }

        private bool ApplyRequestedSlot(Scene world, SaveManager saves)
        {
            ExpeditionSavePoint3D matchingPoint = null;
            foreach (GameObject root in world.GetRootGameObjects())
            {
                foreach (ExpeditionSavePoint3D point in root.GetComponentsInChildren<ExpeditionSavePoint3D>(true))
                {
                    point.UseSaveStore(Store);
                    if (_requestedSave.SavePointId == null
                        || !string.Equals(point.SavePointId, _requestedSave.SavePointId, StringComparison.Ordinal)) { continue; }
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
            if (_requestedSave.Status == ExpeditionSlotStatus.Empty)
            {
                return ExpeditionLaunch.ReadSlot(Store, _requestedSlot, SupportsScene).Status == ExpeditionSlotStatus.Empty;
            }
            if (_requestedSave.SavePointId == null) { return ExpeditionLaunch.TryRestoreStart(saves, _requestedSave, world.path); }
            return matchingPoint != null && matchingPoint.TryRestoreFromTitle(saves, _requestedSave);
        }

        private IEnumerator UnloadFailedWorld(Scene world, GameManager manager)
        {
            yield return SceneManager.UnloadSceneAsync(world);
            if (manager != null)
            {
                Destroy(manager.gameObject);
                yield return null;
            }
            ShowFailure();
        }

        private void ShowFailure()
        {
            _busy = false;
            BuildPanel();
            _status.text = "このスロットを開けませんでした。";
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
