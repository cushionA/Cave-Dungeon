using UnityEngine;

namespace Game.Runtime
{
    /// <summary>
    /// ビジュアルエフェクト系アセットのヘルパー。
    /// Smart Lighting 2D / Sprite Shaders Ultimate / Destructible 2D / All In 1 VFX Toolkit の
    /// 共通的な呼び出しを簡易化するユーティリティ。
    /// 各アセットは直接コンポーネントとして使うが、ゲームロジックからの制御はここを経由する。
    /// </summary>
    public static class VisualEffectHelper
    {
        // === Sprite Shaders Ultimate ===

        /// <summary>
        /// SpriteRendererにヒットフラッシュエフェクトを適用する。
        /// Sprite Shaders UltimateのマテリアルプロパティをAnimateして白フラッシュ。
        /// </summary>
        public static void ApplyHitFlash(SpriteRenderer renderer, float duration = 0.1f)
        {
            if (renderer == null || renderer.material == null)
            {
                return;
            }

            // Sprite Shaders Ultimateの _HitEffectBlend プロパティを制御
            // LitMotionでアニメーション
            LitMotion.LMotion.Create(1f, 0f, duration)
                .WithEase(LitMotion.Ease.OutQuad)
                .BindWithState(renderer.material, (value, mat) =>
                {
                    mat.SetFloat("_HitEffectBlend", value);
                });
        }

        /// <summary>
        /// SpriteRendererにアウトラインを設定する。
        /// ターゲットロックオン等の視覚フィードバック用。
        /// </summary>
        public static void SetOutline(SpriteRenderer renderer, Color color, float width = 1f)
        {
            if (renderer == null || renderer.material == null)
            {
                return;
            }

            renderer.material.SetFloat("_OutlineAlpha", 1f);
            renderer.material.SetColor("_OutlineColor", color);
            renderer.material.SetFloat("_OutlineWidth", width);
        }

        /// <summary>
        /// アウトラインを解除する。
        /// </summary>
        public static void ClearOutline(SpriteRenderer renderer)
        {
            if (renderer == null || renderer.material == null)
            {
                return;
            }

            renderer.material.SetFloat("_OutlineAlpha", 0f);
        }

        // === Destructible 2D ===

        /// <summary>
        /// Destructible 2Dオブジェクトにダメージを与える（破壊エフェクト）。
        /// 地形や破壊可能オブジェクトに使用。
        /// CW.Destructible2D.D2dDestructible コンポーネント経由で破壊。
        /// </summary>
        public static void ApplyDestruction(GameObject target, Vector2 hitPoint, float radius)
        {
            // Destructible 2DのAPIを直接呼ぶ
            // CW.Destructible2D.D2dDestructible.StampAll(hitPoint, ...) 等
            // 具体的なパラメータは実装時にアセットのAPIに合わせて調整
            AILogger.Log($"[VFX] Destruction at {hitPoint} radius={radius}");
        }
    }
}
