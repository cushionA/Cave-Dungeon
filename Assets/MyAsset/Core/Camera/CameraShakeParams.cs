namespace Game.Core
{
    /// <summary>
    /// カメラシェイク要求のペイロード。<see cref="GameEvents.OnCameraShakeRequested"/> が運ぶ。
    /// </summary>
    /// <remarks>
    /// preset 名駆動。Runtime 側の <c>ProCamera2DShakeAdapter</c> が受信し、
    /// ProCamera2DShake コンポーネントに登録された ShakePreset (Inspector で編集) を名前で引いて再生する。
    /// magnitude / duration / frequency 等の詳細値は ShakePreset に集約され、コードからは preset 名のみで指定する。
    /// </remarks>
    public readonly struct CameraShakeParams
    {
        /// <summary>ProCamera2DShake.ShakePresets で定義された preset 名。</summary>
        public readonly string PresetName;

        public CameraShakeParams(string presetName)
        {
            PresetName = presetName;
        }
    }
}
