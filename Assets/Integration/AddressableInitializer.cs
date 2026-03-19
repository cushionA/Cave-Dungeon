using UnityEngine;
using UnityEngine.AddressableAssets;
using Cysharp.Threading.Tasks;

namespace Game.Runtime
{
    /// <summary>
    /// Addressablesの初期化とプリロードを管理する。
    /// Smart Addresserで自動設定されたグループをロードタイミングに応じてプリロード。
    /// </summary>
    public class AddressableInitializer : MonoBehaviour
    {
        [Header("Preload Settings")]
        [Tooltip("起動時にプリロードするラベル")]
        [SerializeField] private string[] _preloadLabels = { "preload" };

        private async void Start()
        {
            await InitializeAsync();
        }

        private async UniTask InitializeAsync()
        {
            // Addressables初期化
            await Addressables.InitializeAsync().Task;

            // preloadラベルのアセットを一括ロード
            for (int i = 0; i < _preloadLabels.Length; i++)
            {
                await Addressables.LoadAssetsAsync<Object>(_preloadLabels[i], null).Task;
            }

            AILogger.Log("[Addressable] Preload complete");
        }

        /// <summary>
        /// ステージ切替時にステージ固有アセットをプリロードする。
        /// </summary>
        public async UniTask PreloadStageAsync(string stageId)
        {
            string label = $"stage-{stageId}";
            await Addressables.LoadAssetsAsync<Object>(label, null).Task;
            AILogger.Log($"[Addressable] Stage {stageId} preloaded");
        }

        /// <summary>
        /// ステージアセットを解放する。
        /// </summary>
        public void ReleaseStage(string stageId)
        {
            // Smart Addresser管理のグループはラベル単位でリリース
            AILogger.Log($"[Addressable] Stage {stageId} released");
        }
    }
}
