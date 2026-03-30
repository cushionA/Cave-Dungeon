using UnityEngine;
using UnityEngine.SceneManagement;
using Game.Core;

namespace Game.Runtime
{
    /// <summary>
    /// LevelStreamingOrchestratorのMonoBehaviourラッパー。
    /// Additiveシーンのロード/アンロードをUnity SceneManager経由で実行する。
    /// GameScene（永続シーン）に配置する。
    /// </summary>
    public class LevelStreamingController : MonoBehaviour
    {
        [SerializeField]
        [Header("Streaming Settings")]
        [Tooltip("永続シーン名（このコントローラーが属するシーン）")]
        private string persistentSceneName = "GameScene";

        private LevelStreamingOrchestrator _orchestrator;
        private AsyncOperation _currentOperation;
        private string _currentOperationScene;
        private bool _isUnloading;

        public LevelStreamingOrchestrator Orchestrator => _orchestrator;

        public void Initialize()
        {
            GameEvents events = GameManager.Events;
            if (events == null)
            {
                return;
            }

            _orchestrator = new LevelStreamingOrchestrator(
                persistentSceneName,
                events,
                OnLoadSceneRequested,
                OnUnloadSceneRequested
            );
        }

        private void Update()
        {
            if (_orchestrator == null)
            {
                return;
            }

            if (_currentOperation != null && _currentOperation.isDone)
            {
                if (_isUnloading)
                {
                    _orchestrator.NotifyUnloadComplete(_currentOperationScene);
                }
                else
                {
                    _orchestrator.NotifyLoadComplete(_currentOperationScene);
                }

                _currentOperation = null;
                _currentOperationScene = null;
                _isUnloading = false;
            }

            _orchestrator.ProcessQueue();
        }

        /// <summary>外部からエリアロードを要求する。AreaBoundaryTriggerから呼ばれる。</summary>
        public bool RequestAreaLoad(string sceneName)
        {
            if (_orchestrator == null)
            {
                return false;
            }

            return _orchestrator.RequestAreaLoad(sceneName);
        }

        /// <summary>外部からエリアアンロードを要求する。</summary>
        public bool RequestAreaUnload(string sceneName)
        {
            if (_orchestrator == null)
            {
                return false;
            }

            return _orchestrator.RequestAreaUnload(sceneName);
        }

        private void OnLoadSceneRequested(string sceneName)
        {
            _currentOperation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            _currentOperationScene = sceneName;
            _isUnloading = false;
        }

        private void OnUnloadSceneRequested(string sceneName)
        {
            _currentOperation = SceneManager.UnloadSceneAsync(sceneName);
            _currentOperationScene = sceneName;
            _isUnloading = true;
        }
    }
}
