namespace Game.Core
{
    /// <summary>
    /// プロジェクト共通の定数。複数ファイルで重複していたマジックナンバーを一元管理する。
    /// </summary>
    public static class GameConstants
    {
        /// <summary>キャラクター共通の重力スケール。</summary>
        public const float k_GravityScale = 3.0f;

        /// <summary>ダッシュのデフォルト持続時間（秒）。</summary>
        public const float k_DefaultDashDuration = 0.25f;

        /// <summary>クリティカル率の初期値。</summary>
        public const float k_DefaultCriticalRate = 0.05f;

        /// <summary>クリティカル倍率の初期値。</summary>
        public const float k_DefaultCriticalMultiplier = 1.5f;
    }
}
