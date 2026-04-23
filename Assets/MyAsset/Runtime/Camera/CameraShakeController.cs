using System;
using Game.Core;
using R3;
using UnityEngine;

namespace Game.Runtime
{
    /// <summary>
    /// カメラシェイクコントローラ。被弾・爆発・着地強調などのフィードバック用。
    /// GameScene (永続シーン) の Main Camera に付与される想定で、
    /// <see cref="GameEvents.OnCameraShakeRequested"/> を購読してシェイクを開始する。
    /// </summary>
    /// <remarks>
    /// 実装:
    /// <list type="bullet">
    ///   <item>Perlin ノイズ (x/y 独立 seed) を Update でサンプリングし、Camera の <c>localPosition</c> に加算。</item>
    ///   <item>時間経過で magnitude を線形減衰 (1 → 0)。</item>
    ///   <item>多重呼び出し時は「最大 magnitude / 最大残り duration で上書き」することで揺れ過ぎを防ぐ。</item>
    ///   <item>終了時は localPosition を <see cref="Vector3.zero"/> にリセット。</item>
    /// </list>
    /// TODO: ポーズ中でもフィードバックを出したい場合は <c>Time.unscaledDeltaTime</c> に切替。
    /// 現状は <c>Time.deltaTime</c> でポーズ時は停止する仕様。
    /// </remarks>
    public class CameraShakeController : MonoBehaviour
    {
        /// <summary>Perlin ノイズの x 軸サンプリング用 seed。</summary>
        private const float k_NoiseSeedX = 0f;

        /// <summary>Perlin ノイズの y 軸サンプリング用 seed (x から十分離して独立性を確保)。</summary>
        private const float k_NoiseSeedY = 100f;

        private float _currentMagnitude;
        private float _remainingDuration;
        private float _totalDuration;
        private float _currentFrequency;
        // Perlin サンプリング用の経過時間。Time.time を直接使うと長時間プレイで値が巨大化し
        // Perlin の周期性で擬似的な繰り返しや精度低下が起きるため、シェイクごとに 0 から積み上げる。
        private float _shakeElapsed;
        private bool _isShaking;
        private IDisposable _subscription;

        /// <summary>現在シェイク中か。テスト・デバッグ用。</summary>
        public bool IsShaking => _isShaking;

        private void OnEnable()
        {
            if (GameManager.Events != null)
            {
                _subscription = GameManager.Events.OnCameraShakeRequested
                    .Subscribe(p => Shake(p.Magnitude, p.Duration, p.Frequency));
            }
        }

        private void OnDisable()
        {
            _subscription?.Dispose();
            _subscription = null;

            // シェイク中に無効化された場合、localPosition を戻して視覚的なブレを残さない
            if (_isShaking)
            {
                transform.localPosition = Vector3.zero;
                _isShaking = false;
                _currentMagnitude = 0f;
                _remainingDuration = 0f;
                _totalDuration = 0f;
                _shakeElapsed = 0f;
            }
        }

        /// <summary>
        /// カメラシェイクを開始する。
        /// 既にシェイク中の場合は「最大 magnitude / 最大残り duration」で上書きする。
        /// </summary>
        /// <param name="magnitude">揺れ幅 (unit)。0 以下の場合は何もしない。</param>
        /// <param name="duration">継続時間 (秒)。0 以下の場合は何もしない。</param>
        /// <param name="frequency">Perlin ノイズのサンプリング周波数。大きいほど高速揺れ。</param>
        public void Shake(float magnitude, float duration, float frequency = CameraShakeParams.k_DefaultFrequency)
        {
            if (magnitude <= 0f || duration <= 0f)
            {
                return;
            }

            if (_isShaking)
            {
                // 既存シェイクと比較して大きい方を採用 (重ね掛けでの過剰揺れを防ぐ)
                if (magnitude > _currentMagnitude)
                {
                    _currentMagnitude = magnitude;
                }
                if (duration > _remainingDuration)
                {
                    _remainingDuration = duration;
                    _totalDuration = duration;
                }
                // frequency は最新値で上書き (揺れの質感を新しいリクエストに合わせる)
                _currentFrequency = frequency;
                return;
            }

            _currentMagnitude = magnitude;
            _remainingDuration = duration;
            _totalDuration = duration;
            _currentFrequency = frequency;
            _shakeElapsed = 0f;
            _isShaking = true;
        }

        private void Update()
        {
            if (!_isShaking)
            {
                return;
            }

            // TODO: ポーズ中でもシェイクさせたい場合は Time.unscaledDeltaTime に切替
            float dt = Time.deltaTime;
            _remainingDuration -= dt;
            _shakeElapsed += dt;

            if (_remainingDuration <= 0f)
            {
                transform.localPosition = Vector3.zero;
                _isShaking = false;
                _currentMagnitude = 0f;
                _remainingDuration = 0f;
                _totalDuration = 0f;
                _shakeElapsed = 0f;
                return;
            }

            // 時間経過で magnitude を線形減衰 (1 → 0)
            float decay = _totalDuration > 0f ? _remainingDuration / _totalDuration : 0f;
            float currentMag = _currentMagnitude * decay;

            // x/y を独立したノイズ seed でサンプリング。Perlin は [0,1] を返すので [-1,1] に変換。
            // _shakeElapsed ベースのため長時間プレイでも数値が肥大化しない。
            float t = _shakeElapsed * _currentFrequency;
            float offsetX = (Mathf.PerlinNoise(t, k_NoiseSeedX) * 2f - 1f) * currentMag;
            float offsetY = (Mathf.PerlinNoise(t, k_NoiseSeedY) * 2f - 1f) * currentMag;

            transform.localPosition = new Vector3(offsetX, offsetY, 0f);
        }
    }
}
