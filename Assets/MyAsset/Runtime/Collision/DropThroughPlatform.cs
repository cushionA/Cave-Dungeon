using UnityEngine;

namespace Game.Runtime
{
    /// <summary>
    /// 一方通行プラットフォーム (drop-through) のマーカー MonoBehaviour。
    /// <see cref="PlatformEffector2D"/> が付いたプラットフォームに付与し、
    /// プレイヤー側が真下の Collider に本コンポーネントが付いているかを判定して
    /// drop-through (床すり抜け) を発動する。
    ///
    /// 使い方:
    ///   1. プラットフォームの GameObject に <see cref="BoxCollider2D"/> + <see cref="PlatformEffector2D"/> を付与。
    ///      Collider2D.usedByEffector = true、PlatformEffector2D.useOneWay = true 推奨。
    ///   2. 本コンポーネントを同じ GameObject に付与。
    ///   3. 層は Ground (Layer 6) を想定。
    /// </summary>
    [DisallowMultipleComponent]
    public class DropThroughPlatform : MonoBehaviour
    {
        [Tooltip("drop-through 中にプレイヤーがこのプラットフォームをすり抜ける時間（秒）。\n" +
                 "0 以下なら DropThroughLogic.k_DropThroughDurationSeconds を使用する。")]
        [SerializeField] private float _dropThroughDurationOverride = 0f;

        /// <summary>
        /// プレイヤー側で参照する持続時間。0 以下なら既定値を使うよう呼び出し側に通知する。
        /// </summary>
        public float DropThroughDurationOverride => _dropThroughDurationOverride;
    }
}
