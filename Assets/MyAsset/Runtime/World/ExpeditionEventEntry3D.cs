using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Game.Runtime
{
    /// <summary>探索側の騎士を残したまま、加算ロードするイベントとの入退場を管理する。</summary>
    [DisallowMultipleComponent]
    public sealed class ExpeditionEventEntry3D : MonoBehaviour
    {
        private static ExpeditionEventEntry3D s_Active;

        [SerializeField] private CharacterActor3D knight;
        [SerializeField] private Camera gameplayCamera;
        [SerializeField] private string eventScenePath;
        [SerializeField, Tooltip("周回内で再生済みかを保存するキー。空なら現在のシーン内だけ保持する。")]
        private string completionFlag;

        private readonly List<InputAction> _enabledActions = new List<InputAction>();
        private PlayerInput _input;
        private InputActionAsset _inputAsset;
        private InputActionMap _priorMap;
        private ActorRef _owner;
        private SaveManager _saves;
        private AsyncOperation _load;
        private AsyncOperation _unload;
        private Scene _eventScene;
        private ExpeditionEventPlayback3D _playback;
        private NpcInteraction3D _hiddenInteraction;
        private bool _completedInScene;
        private bool _cameraWasEnabled;
        private bool _cameraSuspended;
        private bool _closing;
        private string _loadingPath;

        public bool IsPlaying => ReferenceEquals(s_Active, this);
        public bool HasCompleted => !string.IsNullOrWhiteSpace(completionFlag) && GameManager.Flags != null
            ? GameManager.Flags.GetGlobalFlag(completionFlag) : _completedInScene;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetActive() => s_Active = null;

        private void OnEnable() => BindSaveState();

        private void Start() => BindSaveState();

        public void Play() => TryPlay();

        public bool TryPlay()
        {
            if (!isActiveAndEnabled || !ReferenceEquals(s_Active, null) || HasCompleted || knight == null || !knight.IsAlive
                || knight.gameObject.scene != gameObject.scene || gameplayCamera == null
                || gameplayCamera.gameObject.scene != gameObject.scene || !knight.TryGetComponent(out PlayerInput input)
                || !input.isActiveAndEnabled || !input.inputIsActive || input.actions == null
                || input.currentActionMap == null || input.currentActionMap.name != "Player" || !input.currentActionMap.enabled
                || string.IsNullOrWhiteSpace(eventScenePath))
            {
                return false;
            }

            int index = SceneUtility.GetBuildIndexByScenePath(eventScenePath);
            if (index < 0 || !Application.CanStreamedLevelBeLoaded(index)
                || SceneManager.GetSceneByPath(eventScenePath).isLoaded || gameObject.scene.path == eventScenePath)
            {
                return false;
            }

            s_Active = this;
            _loadingPath = eventScenePath;
            _owner = knight.Actor;
            _input = input;
            _inputAsset = input.actions;
            _priorMap = input.currentActionMap;
            _cameraWasEnabled = gameplayCamera.enabled;
            foreach (InputAction action in _priorMap.actions)
            {
                if (action.enabled) { _enabledActions.Add(action); }
            }
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            BindSaveState();
            input.DeactivateInput();

            try { _load = SceneManager.LoadSceneAsync(index, LoadSceneMode.Additive); }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            if (_load == null)
            {
                ReturnToExploration();
                return false;
            }

            _load.completed += OnLoaded;
            return true;
        }

        public void Skip()
        {
            if (_playback != null && !_closing) { _playback.Finish(); }
        }

        private void Update()
        {
            if (IsPlaying && !_closing && (knight == null || !knight.IsAlive || knight.Actor != _owner))
            {
                RequestReturn(false);
            }
        }

        private void OnDisable()
        {
            if (IsPlaying) { RequestReturn(false); }
            if (_saves != null) { _saves.Loaded -= OnSaveLoaded; }
            _saves = null;
        }

        private void BindSaveState()
        {
            SaveManager saves = GameManager.Saves;
            if (_saves != saves)
            {
                if (_saves != null) { _saves.Loaded -= OnSaveLoaded; }
                _saves = saves;
                if (_saves != null) { _saves.Loaded += OnSaveLoaded; }
            }
            UpdateInteraction();
        }

        private void OnSaveLoaded(int _)
        {
            RequestReturn(false);
            UpdateInteraction();
        }

        private void UpdateInteraction()
        {
            if (HasCompleted)
            {
                if (TryGetComponent(out NpcInteraction3D interaction) && interaction.enabled)
                {
                    _hiddenInteraction = interaction;
                    interaction.enabled = false;
                }
            }
            else if (_hiddenInteraction != null)
            {
                _hiddenInteraction.enabled = true;
                _hiddenInteraction = null;
            }
        }

        private void OnLoaded(AsyncOperation operation)
        {
            operation.completed -= OnLoaded;
            _load = null;
            _eventScene = SceneManager.GetSceneByPath(_loadingPath);
            if (_closing || this == null || !isActiveAndEnabled || !_eventScene.isLoaded
                || knight == null || !knight.IsAlive || knight.Actor != _owner || gameplayCamera == null)
            {
                RequestReturn(false);
                BeginUnload();
                return;
            }

            foreach (GameObject root in _eventScene.GetRootGameObjects())
            {
                if (!root.TryGetComponent(out ExpeditionEventPlayback3D candidate)) { continue; }
                if (_playback != null)
                {
                    RequestReturn(false);
                    return;
                }
                _playback = candidate;
            }
            if (_playback == null)
            {
                RequestReturn(false);
                return;
            }

            _cameraSuspended = true;
            gameplayCamera.enabled = false;
            _playback.Finished += OnPlaybackFinished;
            if (!_playback.TryBegin()) { RequestReturn(false); }
        }

        private void OnPlaybackFinished() => RequestReturn(true);

        private void RequestReturn(bool completed)
        {
            if (!IsPlaying || _closing) { return; }
            _closing = true;
            if (completed)
            {
                _completedInScene = true;
                if (!string.IsNullOrWhiteSpace(completionFlag)) { GameManager.Flags?.SetGlobalFlag(completionFlag, true); }
                UpdateInteraction();
            }
            EndPresentation();
            if (_load == null) { BeginUnload(); }
        }

        private void EndPresentation()
        {
            if (_playback != null)
            {
                _playback.Finished -= OnPlaybackFinished;
                _playback.EndPresentation();
                _playback = null;
            }
            if (_cameraSuspended && gameplayCamera != null) { gameplayCamera.enabled = _cameraWasEnabled; }
            _cameraSuspended = false;
        }

        private void BeginUnload()
        {
            if (!IsPlaying || _unload != null) { return; }
            if (_eventScene.isLoaded && SceneManager.sceneCount > 1)
            {
                try { _unload = SceneManager.UnloadSceneAsync(_eventScene); }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
            }
            if (_unload == null) { ReturnToExploration(); }
            else { _unload.completed += OnUnloaded; }
        }

        private void OnSceneUnloaded(Scene scene)
        {
            if (scene == _eventScene && !_closing)
            {
                RequestReturn(false);
            }
        }

        private void OnUnloaded(AsyncOperation operation)
        {
            operation.completed -= OnUnloaded;
            _unload = null;
            ReturnToExploration();
        }

        private void ReturnToExploration()
        {
            if (!IsPlaying) { return; }
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            EndPresentation();
            RestoreInput();
            _enabledActions.Clear();
            _input = null;
            _inputAsset = null;
            _priorMap = null;
            _owner = default;
            _eventScene = default;
            _loadingPath = null;
            _closing = false;
            s_Active = null;
        }

        private void RestoreInput()
        {
            if (_input == null || !_input.isActiveAndEnabled || _input.actions != _inputAsset
                || knight == null || knight.Actor != _owner) { return; }

            if (_input.currentActionMap != _priorMap) { _input.currentActionMap = _priorMap; }
            _input.ActivateInput();
            foreach (InputAction action in _priorMap.actions)
            {
                bool enabled = _enabledActions.Contains(action);
                if (enabled && !action.enabled) { action.Enable(); }
                if (!enabled && action.enabled) { action.Disable(); }
            }
        }
    }
}
