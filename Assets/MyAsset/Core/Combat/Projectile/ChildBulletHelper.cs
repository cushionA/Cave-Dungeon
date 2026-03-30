namespace Game.Core
{
    /// <summary>
    /// 子弾生成のピュアロジック。
    /// MagicDefinition差し替え、タイマーチェック、子弾有無判定を提供する。
    /// </summary>
    public static class ChildBulletHelper
    {
        /// <summary>
        /// 親のMagicDefinitionを基に子弾用MagicDefinitionを生成する。
        /// BulletProfileは子弾設定のものに差し替え、childBulletはnullにして無限再帰を防止する。
        /// </summary>
        public static MagicDefinition CreateChildMagic(MagicDefinition parentMagic, ChildBulletConfig config)
        {
            MagicDefinition childMagic = parentMagic;
            childMagic.bulletProfile = config.profile;
            childMagic.childBullet = null;
            return childMagic;
        }

        /// <summary>
        /// MagicDefinitionに有効な子弾設定があるか判定する。
        /// </summary>
        public static bool HasChildBullet(MagicDefinition magic)
        {
            return magic.childBullet != null
                && magic.childBullet.trigger != ChildBulletTrigger.None
                && magic.childBullet.count > 0;
        }

        /// <summary>
        /// OnTimerトリガーの発射タイミングを判定する。
        /// </summary>
        public static bool ShouldEmitOnTimer(float elapsedTime, float lastEmitTime, float emitInterval)
        {
            if (emitInterval <= 0f)
            {
                return false;
            }

            return elapsedTime - lastEmitTime >= emitInterval;
        }
    }
}
