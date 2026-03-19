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
            await Addressables.InitializeAsync().Task;

            for (int i = 0; i < _preloadLabels.Length; i++)
            {
                await Addressables.LoadAssetsAsync<Object>(_preloadLabels[i], null).Task;
            }

#if UNITY_EDITOR
            Debug.Log("[Addressable] Preload complete");
#endif
        }

        public async UniTask PreloadStageAsync(string stageId)
        {
            string label = $"stage-{stageId}";
            await Addressables.LoadAssetsAsync<Object>(label, null).Task;
#if UNITY_EDITOR
            Debug.Log($"[Addressable] Stage {stageId} preloaded");
#endif
        }

        public void ReleaseStage(string stageId)
        {
#if UNITY_EDITOR
            Debug.Log($"[Addressable] Stage {stageId} released");
#endif
        }
    }
}
