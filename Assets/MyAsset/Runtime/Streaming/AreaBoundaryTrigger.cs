using UnityEngine;

namespace Game.Runtime
{
    /// <summary>
    /// エリア境界に配置するトリガー。
    /// プレイヤーが進入すると隣接シーンのロード/退出シーンのアンロードを要求する。
    ///
    /// 高速移動での貫通対策:
    /// Collider2D は Continuous Collision Detection (Rigidbody2D.collisionDetectionMode = Continuous) を
    /// 推奨する。本トリガー側 Collider の isTrigger は必ず true にすること。
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class AreaBoundaryTrigger : MonoBehaviour
    {
        /// <summary>ゼロベクトル判定用の二乗許容値。</summary>
        private const float k_ZeroVelocitySqrEpsilon = 0.01f * 0.01f;

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

        [SerializeField]
        [Header("Entry Direction Filter")]
        [Tooltip("正しい進入方向（ワールド座標）。" +
                 "進入時の Rigidbody2D.linearVelocity とのドット積が正なら発火する。" +
                 "Vector2.zero を指定するとどの方向からでも発火する（既定動作）。" +
                 "高速移動での貫通を避けるため、Rigidbody2D は Continuous Collision Detection、" +
                 "トリガー Collider は isTrigger=true を推奨。")]
        private Vector2 expectedEntryDirection = Vector2.zero;

        public string SceneToLoad => sceneToLoad;
        public string SceneToUnload => sceneToUnload;

        /// <summary>
        /// 期待進入方向。ゼロベクトルなら全方向許容（既定動作）。
        /// </summary>
        public Vector2 ExpectedEntryDirection => expectedEntryDirection;

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!other.CompareTag(playerTag))
            {
                return;
            }

            if (!IsEnteringFromExpectedDirection(other))
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

        /// <summary>
        /// 侵入体の速度が <see cref="expectedEntryDirection"/> と一致する方向かを判定する。
        /// expectedEntryDirection がゼロベクトル、または速度が極小（|v|^2 &lt; k_ZeroVelocitySqrEpsilon）の場合は
        /// 方向不定として全方向許容する。
        /// </summary>
        private bool IsEnteringFromExpectedDirection(Collider2D other)
        {
            // 方向フィルタ無効（既定）
            if (expectedEntryDirection.sqrMagnitude <= Mathf.Epsilon)
            {
                return true;
            }

            Rigidbody2D rb = other.attachedRigidbody;
            if (rb == null)
            {
                // Rigidbody2D が無ければ速度判定不能 → 全方向許容にフォールバック
                return true;
            }

            Vector2 velocity = rb.linearVelocity;
            if (velocity.sqrMagnitude < k_ZeroVelocitySqrEpsilon)
            {
                // 停止時は方向不定として許容（既存挙動を壊さない）
                return true;
            }

            return Vector2.Dot(velocity, expectedEntryDirection) > 0f;
        }

#if UNITY_INCLUDE_TESTS
        /// <summary>テスト専用: expectedEntryDirection を設定する。</summary>
        public void SetExpectedEntryDirectionForTest(Vector2 direction)
        {
            expectedEntryDirection = direction;
        }

        /// <summary>テスト専用: sceneToLoad を設定する。</summary>
        public void SetSceneToLoadForTest(string sceneName)
        {
            sceneToLoad = sceneName;
        }

        /// <summary>テスト専用: sceneToUnload を設定する。</summary>
        public void SetSceneToUnloadForTest(string sceneName)
        {
            sceneToUnload = sceneName;
        }

        /// <summary>テスト専用: OnTriggerEnter2D を外部から呼ぶ。</summary>
        public void InvokeOnTriggerEnter2DForTest(Collider2D other)
        {
            OnTriggerEnter2D(other);
        }
#endif
    }
}
