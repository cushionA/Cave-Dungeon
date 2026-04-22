namespace Game.Core
{
    /// <summary>
    /// カメラシェイクリクエストのパラメータ。
    /// GameEvents.OnCameraShakeRequested のペイロードとして使用する。
    /// </summary>
    /// <remarks>
    /// 被弾・爆発・着地強調などのフィードバック用。
    /// magnitude が大きいほど揺れ幅が大きく、frequency が大きいほど速く揺れる。
    /// 多重リクエストは CameraShakeController 側で「最大 magnitude / duration で上書き」される。
    /// </remarks>
    public readonly struct CameraShakeParams
    {
        /// <summary>揺れ幅 (unit)。</summary>
        public readonly float Magnitude;

        /// <summary>継続時間 (秒)。</summary>
        public readonly float Duration;

        /// <summary>Perlin ノイズのサンプリング周波数 (Hz 相当。大きいほど高速揺れ)。</summary>
        public readonly float Frequency;

        /// <summary>CameraShakeController のデフォルト周波数。</summary>
        public const float k_DefaultFrequency = 25f;

        public CameraShakeParams(float magnitude, float duration, float frequency = k_DefaultFrequency)
        {
            Magnitude = magnitude;
            Duration = duration;
            Frequency = frequency;
        }
    }
}
