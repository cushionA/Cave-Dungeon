using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Runtime
{
    internal sealed class AttackWarningPresentation : IDisposable
    {
        private const int k_GroundSpriteSize = 128;
        private const int k_GroundSortingOrder = 5;
        private const int k_MeleeSampleRate = 22050;
        private const float k_MeleeDuration = 0.35f;

        private Sprite _groundSprite;
        private Texture2D _groundTexture;
        private AudioSource _meleeSource;
        private AudioClip _fallbackClip;

        internal GameObject ShowGroundImpact(Vector2 center, float radius)
        {
            EnsureGroundSprite();
            GameObject warning = new GameObject("[Ground Impact Warning]");
            warning.transform.SetParent(GameManager.Instance.transform, false);
            warning.transform.position = center;
            warning.transform.localScale = Vector3.one * (2f * radius);
            SpriteRenderer renderer = warning.AddComponent<SpriteRenderer>();
            renderer.sprite = _groundSprite;
            renderer.sortingOrder = k_GroundSortingOrder;
            return warning;
        }

        internal void PlayDangerousMelee(AudioClip clip)
        {
            if (_meleeSource == null)
            {
                GameObject warning = new GameObject("[Dangerous Melee Warning]");
                warning.transform.SetParent(GameManager.Instance.transform, false);
                _meleeSource = warning.AddComponent<AudioSource>();
                _meleeSource.playOnAwake = false;
            }
            if (clip == null)
            {
                if (_fallbackClip == null) { _fallbackClip = CreateMeleeWarningClip(); }
                clip = _fallbackClip;
            }
            _meleeSource.clip = clip;
            _meleeSource.Play();
        }

        internal static void HideGroundImpact(GameObject warning)
        {
            if (warning == null) { return; }
            warning.SetActive(false);
            Object.Destroy(warning);
        }

        private void EnsureGroundSprite()
        {
            if (_groundSprite != null) { return; }
            _groundTexture = new Texture2D(k_GroundSpriteSize, k_GroundSpriteSize, TextureFormat.RGBA32, false)
            {
                name = "Ground Impact Warning",
                wrapMode = TextureWrapMode.Clamp,
            };
            Color32[] pixels = new Color32[k_GroundSpriteSize * k_GroundSpriteSize];
            const float pixelRadius = k_GroundSpriteSize * 0.5f;
            const float outlineWidth = 3f;
            Color32 outline = new Color32(255, 37, 34, 215);
            Color32 fill = new Color32(255, 37, 34, 55);
            for (int y = 0; y < k_GroundSpriteSize; y++)
            {
                float dy = y + 0.5f - pixelRadius;
                for (int x = 0; x < k_GroundSpriteSize; x++)
                {
                    float dx = x + 0.5f - pixelRadius;
                    float squaredDistance = dx * dx + dy * dy;
                    if (squaredDistance >= pixelRadius * pixelRadius) { continue; }
                    pixels[y * k_GroundSpriteSize + x] = squaredDistance >= (pixelRadius - outlineWidth) * (pixelRadius - outlineWidth)
                        ? outline : fill;
                }
            }
            _groundTexture.SetPixels32(pixels);
            _groundTexture.Apply(false, true);
            _groundSprite = Sprite.Create(_groundTexture, new Rect(0f, 0f, k_GroundSpriteSize, k_GroundSpriteSize),
                new Vector2(0.5f, 0.5f), k_GroundSpriteSize, 0, SpriteMeshType.FullRect);
        }

        private static AudioClip CreateMeleeWarningClip()
        {
            // Placeholder until a warning clip is authored on the attack. Fade both ends to avoid clicks.
            int count = Mathf.RoundToInt(k_MeleeSampleRate * k_MeleeDuration);
            float[] samples = new float[count];
            for (int i = 0; i < count; i++)
            {
                float time = (float)i / k_MeleeSampleRate;
                float envelope = Mathf.Min(1f, time / 0.025f) * Mathf.Pow(1f - time / k_MeleeDuration, 1.4f);
                float phase = 2f * Mathf.PI * time;
                samples[i] = envelope * (0.28f * Mathf.Sin(96f * phase) + 0.14f * Mathf.Sin(143f * phase));
            }
            AudioClip clip = AudioClip.Create("Dangerous melee warning", count, 1, k_MeleeSampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        public void Dispose()
        {
            if (_meleeSource != null) { Object.Destroy(_meleeSource.gameObject); }
            if (_fallbackClip != null) { Object.Destroy(_fallbackClip); }
            if (_groundSprite != null) { Object.Destroy(_groundSprite); }
            if (_groundTexture != null) { Object.Destroy(_groundTexture); }
        }
    }
}
