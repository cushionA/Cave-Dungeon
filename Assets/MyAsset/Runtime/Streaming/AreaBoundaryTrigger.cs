using UnityEngine;

namespace Game.Runtime
{
    /// <summary>
    /// エリア境界に配置するトリガー。
    /// プレイヤーが進入すると隣接シーンのロード/退出シーンのアンロードを要求する。
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class AreaBoundaryTrigger : MonoBehaviour
    {
        [SerializeField]
        [Header("Area Settings")]
        [Tooltip("進入時にAdditiveロードするシーン名")]
        private string sceneToLoad;

        [SerializeField]
        [Tooltip("進入時にアンロードするシーン名（空なら何もしない）")]
        private string sceneToUnload;

        [SerializeField]
        [Tooltip("プレイヤー判定用タグ")]
        private string playerTag = "Player";

        public string SceneToLoad => sceneToLoad;
        public string SceneToUnload => sceneToUnload;

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!other.CompareTag(playerTag))
            {
                return;
            }

            LevelStreamingController controller = GameManager.LevelStreaming;
            if (controller == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(sceneToLoad))
            {
                controller.RequestAreaLoad(sceneToLoad);
            }

            if (!string.IsNullOrEmpty(sceneToUnload))
            {
                controller.RequestAreaUnload(sceneToUnload);
            }
        }
    }
}
