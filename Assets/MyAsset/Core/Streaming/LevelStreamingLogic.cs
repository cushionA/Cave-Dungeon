using System.Collections.Generic;

namespace Game.Core
{
    public enum StreamingState : byte
    {
        Idle,
        Loading,
        Loaded,
        Unloading,
    }

    /// <summary>
    /// シーンロード管理のピュアロジック。
    /// ロード状態管理、ロードキュー、アクティブシーン追跡を担当。
    /// 実際のUnityシーンロード（SceneManager.LoadSceneAsync）は別途MonoBehaviourで行う。
    /// </summary>
    public class LevelStreamingLogic
    {
        private string _activeScene;
        private HashSet<string> _loadedScenes;
        private Queue<string> _loadQueue;
        private StreamingState _state;

        public string ActiveScene => _activeScene;
        public StreamingState State => _state;
        public int LoadedSceneCount => _loadedScenes.Count;

        public LevelStreamingLogic(string initialScene)
        {
            _activeScene = initialScene;
            _loadedScenes = new HashSet<string> { initialScene };
            _loadQueue = new Queue<string>();
            _state = StreamingState.Idle;
        }

        /// <summary>シーンロード要求をキューに追加。既にロード済みならfalse。</summary>
        public bool RequestLoad(string sceneName)
        {
            if (_loadedScenes.Contains(sceneName))
            {
                return false;
            }

            _loadQueue.Enqueue(sceneName);
            return true;
        }

        /// <summary>キューの次のシーンを取得してLoading状態に遷移。キュー空ならnull。</summary>
        public string BeginNextLoad()
        {
            if (_loadQueue.Count == 0)
            {
                return null;
            }

            string sceneName = _loadQueue.Dequeue();
            _state = StreamingState.Loading;
            return sceneName;
        }

        /// <summary>ロード完了を通知。Loaded状態に遷移、loadedScenesに追加、アクティブシーンを更新。</summary>
        public void CompleteLoad(string sceneName)
        {
            _loadedScenes.Add(sceneName);
            _activeScene = sceneName;
            _state = StreamingState.Loaded;
        }

        /// <summary>シーンアンロード要求。アクティブシーンはアンロード不可。</summary>
        public bool RequestUnload(string sceneName)
        {
            if (!_loadedScenes.Contains(sceneName))
            {
                return false;
            }

            if (_activeScene == sceneName)
            {
                return false;
            }

            _state = StreamingState.Unloading;
            return true;
        }

        /// <summary>アンロード完了通知。</summary>
        public void CompleteUnload(string sceneName)
        {
            _loadedScenes.Remove(sceneName);
            _state = StreamingState.Idle;
        }

        /// <summary>指定シーンがロード済みか</summary>
        public bool IsLoaded(string sceneName)
        {
            return _loadedScenes.Contains(sceneName);
        }
    }
}
