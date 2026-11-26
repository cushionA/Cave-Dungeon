using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// LevelStreamingLogicとGameEventsを橋渡しするオーケストレーター。
    /// 実際のシーンロード/アンロードはコールバック経由で外部に委譲する。
    /// MonoBehaviour非依存のため、Edit Modeテストで検証可能。
    /// </summary>
    public class LevelStreamingOrchestrator
    {
        private readonly LevelStreamingLogic _logic;
        private readonly GameEvents _events;
        private readonly Action<string> _loadSceneCallback;
        private readonly Action<string> _unloadSceneCallback;
        private readonly HashSet<string> _pendingLoads;
        private readonly HashSet<string> _preloads = new();
        private readonly HashSet<string> _preloadedAreas = new();
        private FlagManager _flagManager;
        private string _previousActiveScene;

        /// <summary>
        /// エリアロード完了時に呼ばれる。引数は完了したシーン名（=マップID）。
        /// FlagManager 等がローカルフラグセットを切り替えるフックとして使用。
        /// </summary>
        public event Action<string> OnAreaLoadCompleted;

        /// <summary>
        /// エリアアンロード完了時に呼ばれる。Enemy/Projectile プール解放等のフック。
        /// </summary>
        public event Action<string> OnAreaUnloadCompleted;

        public string ActiveScene => _logic.ActiveScene;
        public string PersistentScene => _logic.PersistentScene;
        public StreamingState State => _logic.State;
        /// <summary>完了時に活動中のマップを切り替えるロードが待機中または実行中。</summary>
        public bool HasPendingAreaActivation => _pendingLoads.Count > _preloads.Count;

        public LevelStreamingOrchestrator(
            string persistentScene,
            GameEvents events,
            Action<string> loadSceneCallback,
            Action<string> unloadSceneCallback)
        {
            _logic = new LevelStreamingLogic(persistentScene);
            _events = events;
            _loadSceneCallback = loadSceneCallback;
            _unloadSceneCallback = unloadSceneCallback;
            _pendingLoads = new HashSet<string>();
            _previousActiveScene = persistentScene;
        }

        /// <summary>
        /// エリアロード完了時に自動で SwitchMap するよう FlagManager を接続する。
        /// 接続済みの場合は上書きする（nullで解除）。
        /// </summary>
        public void AttachFlagManager(FlagManager flagManager)
        {
            _flagManager = flagManager;
        }

        /// <summary>エリアシーンのロードを要求する。既にロード済みまたはキュー内ならfalse。</summary>
        public bool RequestAreaLoad(string sceneName)
        {
            if (_preloads.Remove(sceneName)) { return true; }
            bool firstArrival = _preloadedAreas.Contains(sceneName);
            if (!string.Equals(_logic.ActiveScene, sceneName, StringComparison.Ordinal) && TryActivateLoadedArea(sceneName))
            {
                if (firstArrival) { OnAreaLoadCompleted?.Invoke(sceneName); }
                return true;
            }
            return QueueLoad(sceneName);
        }

        private bool QueueLoad(string sceneName)
        {
            if (_pendingLoads.Contains(sceneName))
            {
                return false;
            }

            if (!_logic.RequestLoad(sceneName))
            {
                return false;
            }

            _pendingLoads.Add(sceneName);
            return true;
        }

        /// <summary>到着先を確認するため、マップの切替や到達報酬を発生させずに加算ロードする。</summary>
        public bool RequestAreaPreload(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName) || !QueueLoad(sceneName)) { return false; }
            _preloads.Add(sceneName);
            return true;
        }

        public bool IsAreaLoadPending(string sceneName) => _pendingLoads.Contains(sceneName);

        /// <summary>ロード済みの区画へ移動できた時点で、前回のロードを再実行せずに活動中のマップを切り替える。</summary>
        public bool TryActivateLoadedArea(string sceneName)
        {
            string previous = _logic.ActiveScene;
            if (_logic.State == StreamingState.Unloading || !_logic.TryActivateLoadedScene(sceneName)) { return false; }
            _preloadedAreas.Remove(sceneName);
            if (string.Equals(previous, sceneName, StringComparison.Ordinal)) { return true; }
            _flagManager?.SwitchMap(sceneName);
            _events.FireAreaTransition(previous, sceneName);
            return true;
        }

        /// <summary>エリアシーンのアンロードを要求する。永続シーンとアクティブシーンは維持する。</summary>
        public bool RequestAreaUnload(string sceneName)
        {
            if (!_logic.RequestUnload(sceneName))
            {
                return false;
            }

            _unloadSceneCallback(sceneName);
            return true;
        }

        /// <summary>
        /// ロードキューを処理する。毎フレーム呼ぶ。
        /// Loading中はキューを進めない（1シーンずつ順次ロード）。
        /// </summary>
        public void ProcessQueue()
        {
            if (_logic.State == StreamingState.Loading || _logic.State == StreamingState.Unloading)
            {
                return;
            }

            string nextScene = _logic.BeginNextLoad();
            if (nextScene == null)
            {
                return;
            }

            _events.FireSceneLoadStarted(nextScene);
            _loadSceneCallback(nextScene);
        }

        /// <summary>シーンロード完了を通知する。UnityのAsyncOperation完了時に呼ぶ。</summary>
        public void NotifyLoadComplete(string sceneName)
        {
            _pendingLoads.Remove(sceneName);
            bool preload = _preloads.Remove(sceneName);
            if (preload) { _preloadedAreas.Add(sceneName); }
            _previousActiveScene = _logic.ActiveScene;
            _logic.CompleteLoad(sceneName, makeActive: !preload);
            _events.FireSceneLoadCompleted(sceneName);
            if (preload) { return; }
            _events.FireAreaTransition(_previousActiveScene, sceneName);

            // FlagManager が接続されていればマップローカルフラグセットを切り替え
            if (_flagManager != null)
            {
                _flagManager.SwitchMap(sceneName);
            }

            OnAreaLoadCompleted?.Invoke(sceneName);
        }

        /// <summary>失敗したシーンの再要求と、後続のロードを許可する。</summary>
        public void NotifyLoadFailed(string sceneName)
        {
            _preloads.Remove(sceneName);
            if (_pendingLoads.Remove(sceneName)) { _logic.FailLoad(); }
        }

        /// <summary>シーンアンロード完了を通知する。</summary>
        public void NotifyUnloadComplete(string sceneName)
        {
            _preloadedAreas.Remove(sceneName);
            _logic.CompleteUnload(sceneName);
            OnAreaUnloadCompleted?.Invoke(sceneName);
        }

        /// <summary>別のシーン遷移がアンロードしたエリアをストリーミング状態から取り除く。</summary>
        public void ForgetUnloadedArea(string sceneName)
        {
            _preloadedAreas.Remove(sceneName);
            _logic.ForgetUnloadedArea(sceneName);
        }

        /// <summary>指定シーンがロード済みか。</summary>
        public bool IsLoaded(string sceneName)
        {
            return _logic.IsLoaded(sceneName);
        }
    }
}
