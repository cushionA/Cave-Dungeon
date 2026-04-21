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

        /// <summary>AI追尾時のデフォルト攻撃範囲（この距離以内で停止）。</summary>
        public const float k_DefaultAttackRange = 1.5f;

        /// <summary>AI移動時のフォールバック速度。</summary>
        public const float k_FallbackMoveSpeed = 3f;

        // --- 物理レイヤー（Architect/08_物理レイヤー定義.md 参照） ---

        /// <summary>キャラクター通常状態（すり抜け）。</summary>
        public const int k_LayerCharaPassThrough = 12;

        /// <summary>キャラクター衝突状態（物理衝突有効）。</summary>
        public const int k_LayerCharaCollide = 13;

        /// <summary>キャラクター無敵状態（すり抜け＋ダメージ無効）。</summary>
        public const int k_LayerCharaInvincible = 14;
    }
}
