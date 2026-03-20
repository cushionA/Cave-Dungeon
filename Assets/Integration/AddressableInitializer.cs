using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
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

        private List<AsyncOperationHandle> _preloadHandles = new List<AsyncOperationHandle>();
        private Dictionary<string, AsyncOperationHandle> _stageHandles = new Dictionary<string, AsyncOperationHandle>();

        private async void Start()
        {
            try
            {
                await InitializeAsync();
            }
            catch (System.Exception e)
            {
                Debug.LogException(e, this);
            }
        }

        private async UniTask InitializeAsync()
        {
            await Addressables.InitializeAsync().Task;

            UniTask[] tasks = new UniTask[_preloadLabels.Length];
            for (int i = 0; i < _preloadLabels.Length; i++)
            {
                tasks[i] = PreloadLabelAsync(i);
            }
            await UniTask.WhenAll(tasks);

#if UNITY_EDITOR
            Debug.Log("[Addressable] Preload complete");
#endif
        }

        private async UniTask PreloadLabelAsync(int index)
        {
            AsyncOperationHandle<IList<Object>> handle =
                Addressables.LoadAssetsAsync<Object>(_preloadLabels[index], null);
            _preloadHandles.Add(handle);
            await handle.Task;
        }

        public async UniTask PreloadStageAsync(string stageId)
        {
            string label = $"stage-{stageId}";

            if (_stageHandles.ContainsKey(stageId))
            {
                return;
            }

            AsyncOperationHandle<IList<Object>> handle =
                Addressables.LoadAssetsAsync<Object>(label, null);
            _stageHandles[stageId] = handle;
            await handle.Task;

#if UNITY_EDITOR
            Debug.Log($"[Addressable] Stage {stageId} preloaded");
#endif
        }

        public void ReleaseStage(string stageId)
        {
            if (_stageHandles.TryGetValue(stageId, out AsyncOperationHandle handle))
            {
                Addressables.Release(handle);
                _stageHandles.Remove(stageId);
#if UNITY_EDITOR
                Debug.Log($"[Addressable] Stage {stageId} released");
#endif
            }
        }

        private void OnDestroy()
        {
            foreach (AsyncOperationHandle handle in _preloadHandles)
            {
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }
            }
            _preloadHandles.Clear();

            foreach (KeyValuePair<string, AsyncOperationHandle> kvp in _stageHandles)
            {
                if (kvp.Value.IsValid())
                {
                    Addressables.Release(kvp.Value);
                }
            }
            _stageHandles.Clear();
        }
    }
}
