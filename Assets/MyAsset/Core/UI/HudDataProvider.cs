namespace Game.Core
{
    /// <summary>
    /// HUD表示用データの計算・フォーマット。
    /// 実際のUI描画は別レイヤーで行い、ここはデータバインディング用ロジックのみ。
    /// </summary>
    public static class HudDataProvider
    {
        /// <summary>バー表示率を計算。0除算防止、0~1にクランプ。</summary>
        public static float CalculateBarRatio(float current, float max)
        {
            if (max <= 0f)
            {
                return 0f;
            }

            float ratio = current / max;

            if (ratio > 1f)
            {
                return 1f;
            }

            if (ratio < 0f)
            {
                return 0f;
            }

            return ratio;
        }

        /// <summary>HP/MP/スタミナのバー率をまとめて返す（アロケーション回避版）。</summary>
        public static void GetVitalsRatios(in CharacterVitals vitals,
            out float hpRatio, out float mpRatio, out float staminaRatio)
        {
            hpRatio = CalculateBarRatio(vitals.currentHp, vitals.maxHp);
            mpRatio = CalculateBarRatio(vitals.currentMp, vitals.maxMp);
            staminaRatio = CalculateBarRatio(vitals.currentStamina, vitals.maxStamina);
        }
    }
}
