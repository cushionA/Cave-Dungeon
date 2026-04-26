using System;
using Com.LuisPedroFonseca.ProCamera2D;
using Game.Core;
using R3;
using UnityEngine;

namespace Game.Runtime
{
    /// <summary>
    /// <see cref="GameEvents.OnCameraShakeRequested"/> を購読し、
    /// 同シーンに存在する <see cref="ProCamera2DShake"/> の preset 駆動 Shake API へ転送するアダプタ。
    /// </summary>
    /// <remarks>
    /// 配置先: ProCamera2DShake と同じ GameObject、または別 GameObject (シーン内で
    /// <see cref="ProCamera2DShake.Exists"/> が true ならどこでも可)。
    ///
    /// 設計判断:
    /// <list type="bullet">
    ///   <item>ProCamera2D 未配置の環境 (テスト・最小シーン) でも例外を出さない。
    ///         <see cref="ProCamera2DShake.Exists"/> false 時は no-op。</item>
    ///   <item>preset 名空文字も no-op。発火元のミス検出は <see cref="LastShakeWasNoop"/> で確認可。</item>
    ///   <item>R3 購読は <see cref="OnEnable"/> / <see cref="OnDisable"/> で対称管理。
    ///         オブジェクトプール / DontDestroyOnLoad 下でもリーク無し。</item>
    /// </list>
    /// </remarks>
    public class ProCamera2DShakeAdapter : MonoBehaviour
    {
        private IDisposable _subscription;

        /// <summary>
        /// 直近の <see cref="HandleShake"/> が ProCamera2D 不在 / preset 名空 / 解除済みなどで no-op に終わったか。
        /// テスト・デバッグ向けのフラグ。
        /// </summary>
        public bool LastShakeWasNoop { get; private set; }

        private void OnEnable()
        {
            if (GameManager.Events != null)
            {
                _subscription = GameManager.Events.OnCameraShakeRequested
                    .Subscribe(HandleShake);
            }
        }

        private void OnDisable()
        {
            _subscription?.Dispose();
            _subscription = null;
        }

        /// <summary>テスト専用: <see cref="LastShakeWasNoop"/> を初期状態に戻す。</summary>
        public void ResetLastShakeFlagForTest()
        {
            LastShakeWasNoop = false;
        }

        private void HandleShake(CameraShakeParams shakeParams)
        {
            if (!ProCamera2DShake.Exists)
            {
                LastShakeWasNoop = true;
                return;
            }

            if (string.IsNullOrEmpty(shakeParams.PresetName))
            {
                LastShakeWasNoop = true;
                return;
            }

            ProCamera2DShake.Instance.Shake(shakeParams.PresetName);
            LastShakeWasNoop = false;
        }
    }
}
