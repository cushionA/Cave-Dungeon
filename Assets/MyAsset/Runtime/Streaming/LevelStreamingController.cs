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
        private AsyncOperation _loadOperation;
        private string _loadOperationScene;
        private AsyncOperation _unloadOperation;
        private string _unloadOperationScene;

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

            if (_loadOperation != null && _loadOperation.isDone)
            {
                _orchestrator.NotifyLoadComplete(_loadOperationScene);
                _loadOperation = null;
                _loadOperationScene = null;
            }

            if (_unloadOperation != null && _unloadOperation.isDone)
            {
                _orchestrator.NotifyUnloadComplete(_unloadOperationScene);
                _unloadOperation = null;
                _unloadOperationScene = null;
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
            _loadOperation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            _loadOperationScene = sceneName;
        }

        private void OnUnloadSceneRequested(string sceneName)
        {
            _unloadOperation = SceneManager.UnloadSceneAsync(sceneName);
            _unloadOperationScene = sceneName;
        }
    }
}
