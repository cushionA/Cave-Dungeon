using UnityEngine;
using Game.Core;

namespace Game.Runtime
{
    /// <summary>
    /// デバッグ用ステータスオーバーレイ。
    /// プレイヤーのHP・スタミナ・MP・アーマーをOnGUIで画面左上に表示する。
    /// DEVELOPMENT_BUILD / UNITY_EDITOR でのみ有効。
    /// </summary>
    public class DebugStatusOverlay : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private BaseCharacter _player;
        private GUIStyle _labelStyle;
        private GUIStyle _barBackStyle;
        private GUIStyle _hpBarStyle;
        private GUIStyle _staminaBarStyle;
        private GUIStyle _mpBarStyle;
        private GUIStyle _armorBarStyle;
        private bool _stylesInitialized;

        private const float k_PanelX = 10f;
        private const float k_PanelY = 10f;
        private const float k_BarWidth = 200f;
        private const float k_BarHeight = 18f;
        private const float k_Spacing = 4f;

        private void Start()
        {
            // プレイヤーキャラクターを検索
            PlayerCharacter pc = FindFirstObjectByType<PlayerCharacter>();
            if (pc != null)
            {
                _player = pc;
            }
        }

        private void InitStyles()
        {
            if (_stylesInitialized)
            {
                return;
            }

            _labelStyle = new GUIStyle(GUI.skin.label);
            _labelStyle.fontSize = 14;
            _labelStyle.fontStyle = FontStyle.Bold;
            _labelStyle.normal.textColor = Color.white;

            _barBackStyle = new GUIStyle();
            _barBackStyle.normal.background = MakeTex(1, 1, new Color(0.1f, 0.1f, 0.1f, 0.8f));

            _hpBarStyle = new GUIStyle();
            _hpBarStyle.normal.background = MakeTex(1, 1, new Color(0.2f, 0.8f, 0.2f, 0.9f));

            _staminaBarStyle = new GUIStyle();
            _staminaBarStyle.normal.background = MakeTex(1, 1, new Color(0.9f, 0.7f, 0.1f, 0.9f));

            _mpBarStyle = new GUIStyle();
            _mpBarStyle.normal.background = MakeTex(1, 1, new Color(0.2f, 0.4f, 0.9f, 0.9f));

            _armorBarStyle = new GUIStyle();
            _armorBarStyle.normal.background = MakeTex(1, 1, new Color(0.6f, 0.6f, 0.6f, 0.9f));

            _stylesInitialized = true;
        }

        private void OnGUI()
        {
            if (_player == null || !GameManager.IsCharacterValid(_player.ObjectHash))
            {
                return;
            }

            InitStyles();

            ref CharacterVitals vitals = ref GameManager.Data.GetVitals(_player.ObjectHash);

            float y = k_PanelY;

            // 背景パネル
            GUI.Box(new Rect(k_PanelX - 4, k_PanelY - 4, k_BarWidth + 80, (k_BarHeight + k_Spacing) * 5 + 8),
                "", _barBackStyle);

            // HP
            DrawBar(ref y, "HP", vitals.currentHp, vitals.maxHp, _hpBarStyle);

            // スタミナ
            DrawBar(ref y, "STA", vitals.currentStamina, vitals.maxStamina, _staminaBarStyle);

            // MP
            DrawBar(ref y, "MP", vitals.currentMp, vitals.maxMp, _mpBarStyle);

            // アーマー
            DrawBar(ref y, "ARM", vitals.currentArmor, vitals.maxArmor, _armorBarStyle);

            // 接地状態
            string groundText = _player.IsGrounded ? "GROUNDED" : "AIRBORNE";
            GUI.Label(new Rect(k_PanelX, y, k_BarWidth + 70, k_BarHeight), groundText, _labelStyle);
        }

        private void DrawBar(ref float y, string label, float current, float max, GUIStyle barStyle)
        {
            float ratio = max > 0 ? Mathf.Clamp01(current / max) : 0f;

            // ラベル
            string text = $"{label}: {current:F0}/{max:F0}";
            GUI.Label(new Rect(k_PanelX, y, k_BarWidth + 70, k_BarHeight), text, _labelStyle);

            // バー背景
            Rect barRect = new Rect(k_PanelX + 70, y + 2, k_BarWidth, k_BarHeight - 4);
            GUI.Box(barRect, "", _barBackStyle);

            // バー本体
            Rect fillRect = new Rect(barRect.x, barRect.y, barRect.width * ratio, barRect.height);
            GUI.Box(fillRect, "", barStyle);

            y += k_BarHeight + k_Spacing;
        }

        private static Texture2D MakeTex(int width, int height, Color color)
        {
            Texture2D tex = new Texture2D(width, height);
            Color[] pix = new Color[width * height];
            for (int i = 0; i < pix.Length; i++)
            {
                pix[i] = color;
            }
            tex.SetPixels(pix);
            tex.Apply();
            return tex;
        }
#endif
    }
}
