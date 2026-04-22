using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using Game.Core;

namespace Game.Runtime
{
    /// <summary>
    /// LevelStreamingOrchestratorのMonoBehaviourラッパー。
    /// Additiveシーンのロード/アンロードをUnity SceneManager経由で実行する。
    /// GameScene（永続シーン）に配置する。
    /// IGameSubManager を実装し、GameManager から Priority 順に初期化される。
    /// </summary>
    public class LevelStreamingController : MonoBehaviour, IGameSubManager
    {
        /// <summary>InitOrder: Streaming は他のマネージャーより先に初期化する（シーン土台）。</summary>
        private const int k_InitOrder = 100;

        [SerializeField]
        [Header("Streaming Settings")]
        [Tooltip("永続シーン名（このコントローラーが属するシーン）")]
        private string persistentSceneName = "GameScene";

        [SerializeField]
        [Tooltip("シーンロード失敗時のフォールバックシーン名。空ならフォールバック処理をスキップして LogError のみ出す。指定する場合は Build Settings に登録必須。")]
        private string fallbackSceneName = "";

        private LevelStreamingOrchestrator _orchestrator;
        private AsyncOperation _loadOperation;
        private string _loadOperationScene;
        private AsyncOperation _unloadOperation;
        private string _unloadOperationScene;

        // シーン有効性チェックの注入フック（テスト用）。null時は SceneManager.GetSceneByName を使用。
        private Func<string, bool> _sceneValidityChecker;

        // フォールバックロード発動フック（テスト用）。null時は SceneManager.LoadSceneAsync を実行。
        private Action<string> _fallbackLoader;

        public LevelStreamingOrchestrator Orchestrator => _orchestrator;

        /// <summary>IGameSubManager 初期化順。数値が小さいほど先。</summary>
        public int InitOrder => k_InitOrder;

        /// <summary>
        /// IGameSubManager 実装。パラメータは GameManager.Events 経由で取得済のため未使用。
        /// 既存の no-arg Initialize() に委譲する。
        /// </summary>
        void IGameSubManager.Initialize(SoACharaDataDic data, GameEvents events)
        {
            Initialize();
        }

        /// <summary>
        /// IGameSubManager 実装。MonoBehaviour の OnDestroy 側で購読解除するため No-op。
        /// </summary>
        void IGameSubManager.Dispose()
        {
            // OnDestroy 側で購読解除するため、ここでは何もしない
        }

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

            // エリアアンロード完了時に Enemy/Projectile プールを解放（ゾンビオブジェクト残留防止）
            _orchestrator.OnAreaUnloadCompleted += HandleAreaUnloadCompleted;
        }

        private void OnDestroy()
        {
            if (_orchestrator != null)
            {
                _orchestrator.OnAreaUnloadCompleted -= HandleAreaUnloadCompleted;
            }
        }

        /// <summary>
        /// エリアアンロード完了時: 残留した Enemy/Projectile を全プールに戻す。
        /// シーン遷移後のゾンビオブジェクト残留による SoA 不整合を防止する。
        /// </summary>
        private void HandleAreaUnloadCompleted(string sceneName)
        {
            EnemySpawnerManager enemies = GameManager.EnemySpawner;
            if (enemies != null)
            {
                enemies.ClearAll();
            }

            ProjectileManager projectiles = GameManager.Projectiles;
            if (projectiles != null)
            {
                projectiles.ClearAll();
            }
        }

        private void Update()
        {
            if (_orchestrator == null)
            {
                return;
            }

            if (_loadOperation != null && _loadOperation.isDone)
            {
                HandleLoadComplete(_loadOperationScene);
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

        /// <summary>
        /// AsyncOperation 完了後のシーン有効性チェック。
        /// 無効なら必ず LogError を出し、<see cref="fallbackSceneName"/> が設定されていれば
        /// そのシーンをフォールバックロードする。未設定時はエラー通知のみ。
        /// </summary>
        private void HandleLoadComplete(string sceneName)
        {
            if (IsSceneValid(sceneName))
            {
                _orchestrator.NotifyLoadComplete(sceneName);
                return;
            }

            if (string.IsNullOrEmpty(fallbackSceneName))
            {
                Debug.LogError($"[LevelStreamingController] Scene load failed for '{sceneName}'. No fallback scene configured.");
                return;
            }

            Debug.LogError($"[LevelStreamingController] Scene load failed for '{sceneName}'. Falling back to '{fallbackSceneName}'.");
            InvokeFallbackLoad();
        }

        /// <summary>シーンがロード済みかつ有効か検証する。注入フックがあれば優先。</summary>
        private bool IsSceneValid(string sceneName)
        {
            if (_sceneValidityChecker != null)
            {
                return _sceneValidityChecker(sceneName);
            }

            Scene scene = SceneManager.GetSceneByName(sceneName);
            return scene.IsValid() && scene.isLoaded;
        }

        /// <summary>
        /// フォールバックシーンをロードする。注入フックがあれば優先。
        /// 呼び出し元 (<see cref="HandleLoadComplete"/>) が事前に fallbackSceneName の空判定を行っている前提。
        /// </summary>
        private void InvokeFallbackLoad()
        {
            if (_fallbackLoader != null)
            {
                _fallbackLoader(fallbackSceneName);
                return;
            }

            SceneManager.LoadSceneAsync(fallbackSceneName, LoadSceneMode.Additive);
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

#if UNITY_INCLUDE_TESTS
        /// <summary>テスト専用: シーン有効性チェックとフォールバックロードを差し替える。</summary>
        public void SetTestHooks(Func<string, bool> sceneValidityChecker, Action<string> fallbackLoader)
        {
            _sceneValidityChecker = sceneValidityChecker;
            _fallbackLoader = fallbackLoader;
        }

        /// <summary>テスト専用: 外部から Orchestrator を注入する（PlayMode 不要にする）。</summary>
        public void InjectOrchestratorForTest(LevelStreamingOrchestrator orchestrator)
        {
            _orchestrator = orchestrator;
        }

        /// <summary>テスト専用: HandleLoadComplete を直接呼び出し、失敗シミュレーションを実行する。</summary>
        public void InvokeHandleLoadCompleteForTest(string sceneName)
        {
            HandleLoadComplete(sceneName);
        }

        /// <summary>テスト専用: 現在の fallbackSceneName を取得。</summary>
        public string FallbackSceneNameForTest => fallbackSceneName;

        /// <summary>テスト専用: fallbackSceneName を差し替える。</summary>
        public void SetFallbackSceneNameForTest(string sceneName)
        {
            fallbackSceneName = sceneName;
        }

        /// <summary>
        /// テスト専用: シーン IO コールバックを差し替えて Orchestrator を再構築する。
        /// SceneManager を叩かないダミーコールバックでイベントフローのみ検証する目的。
        /// 既存 Orchestrator の OnAreaUnloadCompleted 購読を外し、新 Orchestrator に再購読する。
        /// </summary>
        public void SetSceneIOCallbacksForTest(
            System.Action<string> loadCallback, System.Action<string> unloadCallback)
        {
            if (_orchestrator != null)
            {
                _orchestrator.OnAreaUnloadCompleted -= HandleAreaUnloadCompleted;
            }

            _orchestrator = new LevelStreamingOrchestrator(
                persistentSceneName,
                GameManager.Events,
                loadCallback,
                unloadCallback);
            _orchestrator.OnAreaUnloadCompleted += HandleAreaUnloadCompleted;
        }
#endif
    }
}
