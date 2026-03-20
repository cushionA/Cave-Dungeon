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
        private static readonly int s_HitEffectBlend = Shader.PropertyToID("_HitEffectBlend");
        private static readonly int s_OutlineAlpha = Shader.PropertyToID("_OutlineAlpha");
        private static readonly int s_OutlineColor = Shader.PropertyToID("_OutlineColor");
        private static readonly int s_OutlineWidth = Shader.PropertyToID("_OutlineWidth");

        private static MaterialPropertyBlock s_PropertyBlock;

        private static MaterialPropertyBlock GetPropertyBlock()
        {
            if (s_PropertyBlock == null)
            {
                s_PropertyBlock = new MaterialPropertyBlock();
            }
            return s_PropertyBlock;
        }

        // === Sprite Shaders Ultimate ===

        /// <summary>
        /// SpriteRendererにヒットフラッシュエフェクトを適用する。
        /// Sprite Shaders UltimateのマテリアルプロパティをAnimateして白フラッシュ。
        /// </summary>
        public static void ApplyHitFlash(SpriteRenderer renderer, float duration = 0.1f)
        {
            if (renderer == null)
            {
                return;
            }

            LitMotion.LMotion.Create(1f, 0f, duration)
                .WithEase(LitMotion.Ease.OutQuad)
                .Bind(renderer, (value, r) =>
                {
                    MaterialPropertyBlock block = GetPropertyBlock();
                    r.GetPropertyBlock(block);
                    block.SetFloat(s_HitEffectBlend, value);
                    r.SetPropertyBlock(block);
                });
        }

        /// <summary>
        /// SpriteRendererにアウトラインを設定する。
        /// ターゲットロックオン等の視覚フィードバック用。
        /// </summary>
        public static void SetOutline(SpriteRenderer renderer, Color color, float width = 1f)
        {
            if (renderer == null)
            {
                return;
            }

            MaterialPropertyBlock block = GetPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetFloat(s_OutlineAlpha, 1f);
            block.SetColor(s_OutlineColor, color);
            block.SetFloat(s_OutlineWidth, width);
            renderer.SetPropertyBlock(block);
        }

        /// <summary>
        /// アウトラインを解除する。
        /// </summary>
        public static void ClearOutline(SpriteRenderer renderer)
        {
            if (renderer == null)
            {
                return;
            }

            MaterialPropertyBlock block = GetPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetFloat(s_OutlineAlpha, 0f);
            renderer.SetPropertyBlock(block);
        }

        // === Destructible 2D ===

        /// <summary>
        /// Destructible 2Dオブジェクトにダメージを与える（破壊エフェクト）。
        /// 地形や破壊可能オブジェクトに使用。
        /// CW.Destructible2D.D2dDestructible コンポーネント経由で破壊。
        /// </summary>
        public static void ApplyDestruction(GameObject target, Vector2 hitPoint, float radius)
        {
            // TODO: Destructible 2DのAPIを実装時に接続
#if UNITY_EDITOR
            Debug.Log($"[VFX] Destruction at {hitPoint} radius={radius}");
#endif
        }
    }
}
